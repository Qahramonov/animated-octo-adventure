using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using Survival.Core;
using Survival.Gameplay;
using Survival.Networking;
using Survival.Server.AI;
using Survival.Server.Persistence;
using Survival.World;

namespace Survival.Server
{
    /// <summary>Immutable status snapshot, safe to read from any thread (HTTP status, console).</summary>
    public sealed class ServerStatus
    {
        public string Name;
        public string Region;
        public int Players;
        public int PlayersLoading;
        public int Bots;
        public int BotsFullSim;
        public int MaxPlayers;
        public int Entities;
        public uint Tick;
        public int TickRate;
        public double AvgTickMs;
        public double MaxTickMs;
        public double UptimeSeconds;
        public float TimeOfDay01;
        public long BytesSentPerSecond;
        public long BytesReceivedPerSecond;
        public int TerrainChunksGenerated;
        public long ManagedMemoryBytes;
        public string WorldInfo;
    }

    /// <summary>
    /// Authoritative game server: connections, handshake/auth, player spawning, server-side
    /// movement from client inputs, vitals, death/respawn, bots, interest-managed replication,
    /// reconnect grace and persistence. Single-threaded: every method must be called on the server
    /// thread (use <see cref="EnqueueCommand"/> from other threads).
    /// </summary>
    public sealed partial class GameServer
    {
        private struct PendingJoin
        {
            public ConnectRequestData Data;
            public PlayerRecord Record;
        }

        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly IPlayerStore _store;
        private readonly Dictionary<int, PlayerSession> _sessionsByPeer = new Dictionary<int, PlayerSession>();
        private readonly Dictionary<string, PlayerSession> _sessionsByPlayer = new Dictionary<string, PlayerSession>();
        private readonly Dictionary<int, PendingJoin> _pendingJoins = new Dictionary<int, PendingJoin>();
        private readonly Dictionary<uint, ServerEntity> _entities = new Dictionary<uint, ServerEntity>(256);
        private readonly List<ServerEntity> _entityList = new List<ServerEntity>(256);
        private readonly List<PlayerSession> _sessionList = new List<PlayerSession>(128);
        private readonly List<PlayerSession> _sessionScratch = new List<PlayerSession>(16);
        private readonly List<ServerEntity> _queryScratch = new List<ServerEntity>(128);
        private readonly List<Vector3> _occupiedScratch = new List<Vector3>(256);
        private readonly List<Vector3> _dangerScratch = new List<Vector3>(16);
        private readonly PlayerInput[] _inputBuffer = new PlayerInput[16];
        private readonly NetDataWriter _writer = new NetDataWriter(true, 1400);
        private readonly ConcurrentQueue<KeyValuePair<string, TaskCompletionSource<string>>> _commands =
            new ConcurrentQueue<KeyValuePair<string, TaskCompletionSource<string>>>();
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly Stopwatch _tickWatch = new Stopwatch();

        private NetManager _net;
        private BotManager _bots;
        private Replication _replication;
        private Rng _rng;
        private uint _nextEntityId = 1;
        private double _lastAutosave;
        private double _statsWindowStart;
        private long _statsBytesSent;
        private long _statsBytesReceived;
        private double _tickMsAccum;
        private int _tickMsCount;
        private double _tickMsMax;
        private volatile ServerStatus _status = new ServerStatus();

        public GameServer(ServerConfig config, IPlayerStore store)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            _store = store ?? new MemoryPlayerStore();
            string warnings = config.Validate();
            if (!string.IsNullOrEmpty(warnings)) Log.Warn("Server", warnings.Trim());

            World = new WorldRuntime(config.ToWorldConfig());
            Movement = MovementSettings.Default;
            Survival = SurvivalSettings.Default;
            Grid = new SpatialGrid<ServerEntity>(64f);
            TickDt = 1f / config.TickRate;
            _rng = new Rng((uint)config.WorldSeed ^ 0xC0FFEEu);
            _bots = new BotManager(this, (uint)config.WorldSeed);
            _replication = new Replication(this);

            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnReceive;
            _listener.NetworkReceiveUnconnectedEvent += OnUnconnected;
            _listener.NetworkErrorEvent += (ep, err) => Log.Warn("Net", $"Socket error {err} from {ep}");
        }

        public ServerConfig Config { get; }
        public WorldRuntime World { get; }
        public MovementSettings Movement { get; }
        public SurvivalSettings Survival { get; }
        public SpatialGrid<ServerEntity> Grid { get; }
        public float TickDt { get; }
        public uint CurrentTick { get; private set; }
        public double Time => _clock.Elapsed.TotalSeconds;
        public float TimeOfDay01 { get; private set; }
        public bool IsRunning => _net != null && _net.IsRunning;
        public int LocalPort => _net != null ? _net.LocalPort : 0;
        public IReadOnlyList<ServerEntity> Entities => _entityList;
        public IReadOnlyList<PlayerSession> Sessions => _sessionList;
        public BotManager Bots => _bots;
        public ServerStatus Status => _status;
        public uint WorldFingerprintValue { get; private set; }

        internal NetDataWriter Writer => _writer;

        public bool Start()
        {
            _net = new NetManager(_listener)
            {
                ChannelsCount = NetProtocol.ChannelCount,
                UnconnectedMessagesEnabled = true,
                BroadcastReceiveEnabled = Config.AnswerBroadcast,
                DisconnectTimeout = Config.DisconnectTimeoutMs,
                PingInterval = 1000,
                AutoRecycle = false,
                EnableStatistics = true,
                IPv6Enabled = false,
            };

            IPAddress bind = IPAddress.Any;
            if (!string.IsNullOrEmpty(Config.BindAddress) && !IPAddress.TryParse(Config.BindAddress, out bind)) bind = IPAddress.Any;
            if (!_net.Start(bind, IPAddress.IPv6Any, Config.Port))
            {
                Log.Error("Server", $"Could not bind UDP port {Config.Port} (already in use?)");
                return false;
            }

            _clock.Restart();
            TimeOfDay01 = Config.StartTimeOfDay01;
            Log.Info("Server", $"'{Config.ServerName}' listening on UDP {bind}:{_net.LocalPort} | tick {Config.TickRate} Hz | " +
                               $"max {Config.MaxPlayers} | bots {Config.BotCount} | world {World.Config}");

            // Pre-generate terrain around the island centre so the first spawns are fast
            var sw = Stopwatch.StartNew();
            float c = (float)World.Generator.Center;
            if (Config.PrewarmRadius > 0f) World.Heights.Prewarm(c, c, Config.PrewarmRadius);
            WorldFingerprintValue = WorldFingerprint.Compute(World);
            Log.Info("World", $"Terrain prewarm: {World.Heights.GeneratedChunkCount} chunks in {sw.ElapsedMilliseconds} ms | fingerprint {WorldFingerprintValue:x8}");
            UpdateStatus();
            return true;
        }

        public void Stop()
        {
            if (_net == null) return;
            Log.Info("Server", "Stopping: saving players...");
            SaveAll();
            _net.Stop(true);
            _net = null;
        }

        // ------------------------------------------------------------------ tick

        /// <summary>Runs one fixed simulation tick.</summary>
        public void RunTick()
        {
            if (_net == null) return;
            _tickWatch.Restart();
            CurrentTick++;
            double now = Time;

            ProcessCommands();
            _net.PollEvents();

            TimeOfDay01 = (float)((Config.StartTimeOfDay01 + now / (Config.DayLengthMinutes * 60.0)) % 1.0);

            // Players
            for (int i = 0; i < _sessionList.Count; i++)
            {
                PlayerSession s = _sessionList[i];
                ServerEntity e = s.Entity;
                if (e == null || e.Removed) continue;
                if (s.IsConnected && s.Ready) ProcessInputs(s, e);
                else if (!s.IsConnected) ApplyGravityOnly(e);
            }

            // Vitals (players only; bots get a full survival model in Phase 5)
            if (CurrentTick % 10 == 0)
            {
                float dt = TickDt * 10f;
                for (int i = 0; i < _sessionList.Count; i++)
                {
                    ServerEntity e = _sessionList[i].Entity;
                    if (e == null || e.Removed || e.IsDead || !_sessionList[i].Ready) continue;
                    SurvivalRules.Tick(ref e.Stats, dt, e.Move.Sprinting, Survival, out DamageType cause);
                    if (e.Stats.Health <= 0f) Kill(e, cause, null);
                }
            }

            // Bots
            int realPlayers = ConnectedPlayerCount();
            _bots.Balance(realPlayers);
            _bots.Tick(CurrentTick, TickDt);

            // Lag compensation history
            for (int i = 0; i < _entityList.Count; i++) _entityList[i].RecordHistory(CurrentTick);

            // Dead players count their respawn timer
            for (int i = 0; i < _sessionList.Count; i++)
            {
                ServerEntity e = _sessionList[i].Entity;
                if (e != null && e.IsDead) e.DeadTime += TickDt;
            }

            // Replication
            if (CurrentTick % (uint)Config.SnapshotInterval == 0) _replication.SendSnapshots(CurrentTick);

            ExpireDisconnectedSessions(now);

            if (now - _lastAutosave > Config.AutosaveSeconds)
            {
                _lastAutosave = now;
                SaveAll();
            }

            double ms = _tickWatch.Elapsed.TotalMilliseconds;
            _tickMsAccum += ms;
            _tickMsCount++;
            if (ms > _tickMsMax) _tickMsMax = ms;
            if (now - _statsWindowStart >= 1.0) UpdateStatus();
        }

        private void ProcessInputs(PlayerSession s, ServerEntity e)
        {
            s.InputTokens = Math.Min(s.InputTokens + 1f, 3f);
            int processed = 0;
            List<PlayerInput> pending = s.PendingInputs;
            while (pending.Count > 0 && s.InputTokens >= 1f && processed < 3)
            {
                PlayerInput input = pending[0];
                pending.RemoveAt(0);
                if (input.Sequence <= s.LastProcessedInput) continue;
                if (s.LastProcessedInput != 0 && input.Sequence != s.LastProcessedInput + 1) s.SequenceGaps++;
                StepCharacter(e, input);
                s.LastProcessedInput = input.Sequence;
                s.LastInput = input;
                s.InputTokens -= 1f;
                s.TicksWithoutInput = 0;
                processed++;
            }

            if (processed == 0 && ++s.TicksWithoutInput > Config.TickRate)
            {
                // Client stalled for over a second (app in background, connection hiccup): keep physics
                // running with a neutral input. Shorter gaps are normal jitter and are simply caught up
                // when the delayed inputs arrive, so they never cause a prediction error.
                PlayerInput neutral = s.LastInput;
                neutral.MoveX = 0;
                neutral.MoveY = 0;
                neutral.Buttons = InputButtons.None;
                StepCharacter(e, neutral);
                s.NeutralSteps++;
            }
        }

        private void StepCharacter(ServerEntity e, in PlayerInput input)
        {
            if (e.IsDead) return;
            bool wasGrounded = e.Move.Grounded;
            float vy = e.Move.Velocity.Y;
            MovementSim.Step(ref e.Move, input, TickDt, World, Movement);
            e.Yaw = input.YawDegrees;
            e.Pitch = input.PitchDegrees;

            if (!wasGrounded && e.Move.Grounded)
            {
                float dmg = SurvivalRules.FallDamage(-vy, Survival);
                if (dmg > 0f) ApplyDamage(e, dmg, DamageType.Fall, null);
            }
            OnEntityMoved(e);
        }

        private void ApplyGravityOnly(ServerEntity e)
        {
            if (e.IsDead || e.Move.Grounded) return;
            var neutral = new PlayerInput();
            neutral.YawDegrees = e.Yaw;
            StepCharacter(e, neutral);
        }

        // ------------------------------------------------------------------ entities

        public ServerEntity CreateEntity(EntityType type, string name, Vector3 position, float yaw)
        {
            var e = new ServerEntity
            {
                Id = _nextEntityId++,
                Type = type,
                Name = name,
                Move = MoveState.At(position),
                Yaw = yaw,
                Stats = SurvivalStats.Fresh,
            };
            e.Move.Position = new Vector3(position.X, World.GroundHeight(position.X, position.Z), position.Z);
            _entities.Add(e.Id, e);
            _entityList.Add(e);
            Grid.Insert(e, e.Move.Position);
            return e;
        }

        public void RemoveEntity(ServerEntity e, DespawnReason reason)
        {
            if (e == null || e.Removed) return;
            e.Removed = true;
            _entities.Remove(e.Id);
            _entityList.Remove(e);
            Grid.Remove(e);
            _replication.OnEntityRemoved(e, reason);
        }

        public void OnEntityMoved(ServerEntity e)
        {
            if (!e.Removed) Grid.Move(e, e.Move.Position);
        }

        public bool TryGetEntity(uint id, out ServerEntity e) => _entities.TryGetValue(id, out e);

        public bool AnyPlayerWithin(Vector3 pos, float radius, List<ServerEntity> scratch)
        {
            scratch.Clear();
            Grid.QueryCells(pos, radius, scratch);
            float r2 = radius * radius;
            for (int i = 0; i < scratch.Count; i++)
            {
                ServerEntity e = scratch[i];
                if (e.Type == EntityType.Player && e.Session != null && e.Session.IsConnected && SMath.DistanceSqXZ(e.Move.Position, pos) <= r2) return true;
            }
            return false;
        }

        public Vector3 FindSpawnPoint()
        {
            _occupiedScratch.Clear();
            _dangerScratch.Clear();
            for (int i = 0; i < _entityList.Count; i++)
            {
                ServerEntity e = _entityList[i];
                if (e.IsDead) continue;
                _occupiedScratch.Add(e.Move.Position);
            }
            if (SpawnFinder.TryFind(World, ref _rng, _occupiedScratch, _dangerScratch, out Vector3 p)) return p;
            // Extremely crowded/unlucky: relax the separation rule
            if (SpawnFinder.TryFind(World, ref _rng, null, null, out p)) return p;
            float c = (float)World.Generator.Center;
            return new Vector3(c, World.GroundHeight(c, c), c);
        }

        public void ApplyDamage(ServerEntity e, float amount, DamageType type, ServerEntity attacker)
        {
            if (e.IsDead || amount <= 0f) return;
            e.Stats.Health = Math.Max(0f, e.Stats.Health - amount);
            if (e.Stats.Health <= 0f) Kill(e, type, attacker);
        }

        public void Kill(ServerEntity e, DamageType cause, ServerEntity killer)
        {
            e.Stats.Health = 0f;
            e.Move.Flags = MoveFlags.Dead;
            e.Move.Velocity = Vector3.Zero;
            e.DeadTime = 0f;
            e.LastKiller = killer?.Name;
            if (e.Record != null)
            {
                e.Record.Deaths++;
                e.Record.Alive = false;
            }
            if (killer?.Record != null) killer.Record.Kills++;

            if (e.Session != null && e.Session.IsConnected)
            {
                _writer.Reset();
                new DiedMsg { Cause = cause, KillerName = killer?.Name ?? string.Empty, RespawnDelay = Config.RespawnDelaySeconds }.Write(_writer);
                e.Session.Peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
            }
            if (e.Type == EntityType.Player)
            {
                string text = killer != null ? $"{killer.Name} killed {e.Name}" : $"{e.Name} died ({cause})";
                Broadcast(NoticeKind.Kill, text, 4f);
            }
        }

        public void Respawn(ServerEntity e)
        {
            Vector3 p = FindSpawnPoint();
            e.Move = MoveState.At(p);
            e.Stats = SurvivalStats.Fresh;
            e.DeadTime = 0f;
            e.Yaw = _rng.Range(0f, 360f);
            e.History.Clear();
            OnEntityMoved(e);
            if (e.Record != null)
            {
                e.Record.Alive = true;
                e.Record.Stats = e.Stats;
            }
            if (e.Session != null && e.Session.IsConnected)
            {
                e.Session.PendingInputs.Clear();
                _writer.Reset();
                new TeleportMsg { State = e.Move, Yaw = e.Yaw, Stats = e.Stats }.Write(_writer);
                e.Session.Peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
            }
        }

        // ------------------------------------------------------------------ connections

        public int ConnectedPlayerCount()
        {
            int n = 0;
            for (int i = 0; i < _sessionList.Count; i++) if (_sessionList[i].IsConnected) n++;
            return n;
        }

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (!ConnectRequestData.TryRead(request.Data, out ConnectRequestData data, out string error))
            {
                Reject(request, RejectReason.BadRequest, "Invalid connection request");
                Log.Debug("Net", $"Bad request from {request.RemoteEndPoint}: {error}");
                return;
            }
            if (data.ProtocolVersion != NetProtocol.Version)
            {
                Reject(request, RejectReason.VersionMismatch,
                    $"Version mismatch (server v{NetProtocol.Version}, game v{data.ProtocolVersion}). Please update the game.");
                return;
            }
            if (!Secrets.IsValidPlayerId(data.PlayerId) || string.IsNullOrEmpty(data.PlayerSecret))
            {
                Reject(request, RejectReason.BadRequest, "Invalid player identity");
                return;
            }

            _sessionsByPlayer.TryGetValue(data.PlayerId, out PlayerSession existing);
            PlayerRecord record = existing?.Entity?.Record ?? _store.Load(data.PlayerId);
            if (record != null && record.SecretHash != Secrets.Hash(data.PlayerSecret))
            {
                Reject(request, RejectReason.AuthFailed, "Authentication failed for this player id");
                return;
            }

            bool isReturning = existing != null;
            if (!isReturning && ConnectedPlayerCount() + _pendingJoins.Count >= Config.MaxPlayers)
            {
                Reject(request, RejectReason.ServerFull, $"Server is full ({Config.MaxPlayers}/{Config.MaxPlayers})");
                return;
            }

            // Same player connecting again (app restart / network switch): drop the stale connection
            if (existing != null && existing.IsConnected)
            {
                NetPeer old = existing.Peer;
                _sessionsByPeer.Remove(old.Id);
                existing.Peer = null;
                existing.DisconnectedAt = Time;
                old.Disconnect();
            }

            NetPeer peer = (NetPeer)request.Accept();
            _pendingJoins[peer.Id] = new PendingJoin { Data = data, Record = record };
        }

        private static void Reject(ConnectionRequest request, RejectReason reason, string message)
        {
            request.Reject(new RejectData { Reason = reason, Message = message }.ToBytes());
        }

        private void OnPeerConnected(NetPeer peer)
        {
            if (!_pendingJoins.TryGetValue(peer.Id, out PendingJoin join))
            {
                peer.Disconnect();
                return;
            }
            _pendingJoins.Remove(peer.Id);
            ConnectRequestData data = join.Data;
            string nickname = NetCodec.SanitizeName(data.Nickname);

            bool resumed = false;
            if (_sessionsByPlayer.TryGetValue(data.PlayerId, out PlayerSession session) && session.Entity != null && !session.Entity.Removed)
            {
                resumed = true;
            }
            else
            {
                PlayerRecord record = join.Record ?? new PlayerRecord
                {
                    PlayerId = data.PlayerId,
                    SecretHash = Secrets.Hash(data.PlayerSecret),
                    CreatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Stats = SurvivalStats.Fresh,
                };

                // Make room: bots yield their slot so players + bots never exceed MaxPlayers
                _bots.Balance(ConnectedPlayerCount() + 1);

                Vector3 pos;
                bool restore = record.HasPosition && record.Alive && SpawnFinder.IsSafe(World, record.Position.X, record.Position.Z, out _);
                pos = restore ? record.Position : FindSpawnPoint();
                ServerEntity entity = CreateEntity(EntityType.Player, nickname, pos, restore ? record.Yaw : _rng.Range(0f, 360f));
                entity.Stats = record.Alive ? record.Stats : SurvivalStats.Fresh;
                entity.Record = record;
                record.Alive = true;

                session = new PlayerSession { PlayerId = data.PlayerId, Entity = entity };
                entity.Session = session;
                _sessionsByPlayer[data.PlayerId] = session;
                _sessionList.Add(session);
            }

            session.Peer = peer;
            session.Nickname = nickname;
            session.Entity.Name = nickname;
            session.Entity.Record.Nickname = nickname;
            session.SessionToken = Secrets.NewToken();
            session.Ready = false;
            session.ConnectedAt = Time;
            session.DisconnectedAt = -1;
            session.PendingInputs.Clear();
            session.LastProcessedInput = 0;
            session.Known.Clear();
            session.InputTokens = 2f;
            _sessionsByPeer[peer.Id] = session;

            ServerEntity ent = session.Entity;
            _writer.Reset();
            new WelcomeMsg
            {
                ProtocolVersion = NetProtocol.Version,
                EntityId = ent.Id,
                SessionToken = session.SessionToken,
                ServerName = Config.ServerName,
                ServerTick = CurrentTick,
                TickRate = (byte)Config.TickRate,
                SnapshotRate = (byte)(Config.TickRate / Config.SnapshotInterval),
                MaxPlayers = (ushort)Config.MaxPlayers,
                Resumed = resumed,
                World = World.Config,
                Spawn = ent.Move,
                SpawnYaw = ent.Yaw,
                Stats = ent.Stats,
                TimeOfDay01 = TimeOfDay01,
                DayLengthSeconds = Config.DayLengthMinutes * 60f,
                WorldFingerprint = WorldFingerprintValue,
            }.Write(_writer);
            peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

            Log.Info("Net", $"{(resumed ? "Rejoined" : "Joined")}: {nickname} ({Short(data.PlayerId)}) from {peer} | players {ConnectedPlayerCount()}/{Config.MaxPlayers}");
            if (!resumed) Broadcast(NoticeKind.Info, nickname + " joined the island", 3f);
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            _pendingJoins.Remove(peer.Id);
            if (!_sessionsByPeer.TryGetValue(peer.Id, out PlayerSession s)) return;
            _sessionsByPeer.Remove(peer.Id);
            if (s.Peer != peer) return; // replaced by a newer connection
            s.Peer = null;
            s.Ready = false;
            s.DisconnectedAt = Time;
            SaveRecord(s.Entity);
            Log.Info("Net", $"Disconnected: {s.Nickname} ({info.Reason}); character kept for {Config.ReconnectGraceSeconds:0}s");
        }

        private void ExpireDisconnectedSessions(double now)
        {
            _sessionScratch.Clear();
            for (int i = 0; i < _sessionList.Count; i++)
            {
                PlayerSession s = _sessionList[i];
                if (!s.IsConnected && s.DisconnectedAt >= 0 && now - s.DisconnectedAt > Config.ReconnectGraceSeconds) _sessionScratch.Add(s);
            }
            foreach (PlayerSession s in _sessionScratch)
            {
                SaveRecord(s.Entity);
                RemoveEntity(s.Entity, DespawnReason.Disconnected);
                _sessionList.Remove(s);
                _sessionsByPlayer.Remove(s.PlayerId);
                Broadcast(NoticeKind.Info, s.Nickname + " left the island", 3f);
            }
        }

        private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            try
            {
                if (!_sessionsByPeer.TryGetValue(peer.Id, out PlayerSession s) || reader.AvailableBytes < 1) return;
                var id = (MsgId)reader.GetByte();
                switch (id)
                {
                    case MsgId.ClientInput:
                        int n = InputPacket.Read(reader, _inputBuffer);
                        for (int i = n - 1; i >= 0; i--) EnqueueInput(s, _inputBuffer[i]);
                        break;
                    case MsgId.ClientReady:
                        if (!s.Ready)
                        {
                            s.Ready = true;
                            Log.Info("Net", $"{s.Nickname} finished loading ({(Time - s.ConnectedAt):0.0}s)");
                        }
                        break;
                    case MsgId.ClientRespawn:
                        if (s.Entity != null && s.Entity.IsDead && s.Entity.DeadTime >= Config.RespawnDelaySeconds - 0.25f) Respawn(s.Entity);
                        break;
                    case MsgId.ClientChat:
                        ChatMsg chat = ChatMsg.Read(reader);
                        string text = chat.Text?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (text.Length > 120) text = text.Substring(0, 120);
                            _writer.Reset();
                            new ChatMsg { From = s.Nickname, Text = text }.Write(_writer, MsgId.Chat);
                            SendToAllReady(_writer, DeliveryMethod.ReliableOrdered);
                        }
                        break;
                    default:
                        s.RejectedInputs++;
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Warn("Net", $"Malformed packet from {peer}: {e.Message}");
            }
            finally
            {
                reader.Recycle();
            }
        }

        private static void EnqueueInput(PlayerSession s, in PlayerInput input)
        {
            if (input.Sequence <= s.LastProcessedInput) return;
            List<PlayerInput> list = s.PendingInputs;
            int idx = list.Count;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Sequence == input.Sequence) return; // duplicate (redundancy)
                if (list[i].Sequence < input.Sequence) break;
                idx = i;
            }
            list.Insert(idx, input);
            if (list.Count > 32)
            {
                list.RemoveAt(0);
                s.InputsDropped++;
            }
        }

        private void OnUnconnected(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type)
        {
            try
            {
                if (type == UnconnectedMessageType.Broadcast && !Config.AnswerBroadcast) return;
                if (!ServerInfo.TryReadRequest(reader, out long ts)) return;
                int players = ConnectedPlayerCount();
                var flags = ServerStatusFlags.Open;
                if (players >= Config.MaxPlayers) flags = ServerStatusFlags.Full;
                _writer.Reset();
                new ServerInfo
                {
                    ProtocolVersion = NetProtocol.Version,
                    Name = Config.ServerName,
                    Region = Config.Region,
                    MapName = Config.MapName,
                    GameVersion = GameInfo.Version,
                    Players = (ushort)players,
                    Bots = (ushort)_bots.Count,
                    MaxPlayers = (ushort)Config.MaxPlayers,
                    Flags = flags,
                    UptimeSeconds = (int)Time,
                    WorldSize = Config.WorldSize,
                }.WriteResponse(_writer, ts);
                _net.SendUnconnectedMessage(_writer, remote);
            }
            catch (Exception e)
            {
                Log.Debug("Net", "Bad query: " + e.Message);
            }
            finally
            {
                reader.Recycle();
            }
        }

        public void Broadcast(NoticeKind kind, string text, float duration)
        {
            _writer.Reset();
            new NoticeMsg { Kind = kind, Text = text, Duration = duration }.Write(_writer);
            SendToAllReady(_writer, DeliveryMethod.ReliableOrdered);
        }

        private void SendToAllReady(NetDataWriter w, DeliveryMethod method)
        {
            for (int i = 0; i < _sessionList.Count; i++)
            {
                PlayerSession s = _sessionList[i];
                if (s.IsConnected) s.Peer.Send(w, method == DeliveryMethod.Unreliable ? NetProtocol.ChannelUnreliable : NetProtocol.ChannelReliable, method);
            }
        }

        // ------------------------------------------------------------------ persistence

        private void SaveRecord(ServerEntity e)
        {
            if (e?.Record == null) return;
            PlayerRecord r = e.Record;
            r.HasPosition = true;
            r.Position = e.Move.Position;
            r.Yaw = e.Yaw;
            r.Stats = e.Stats;
            r.Alive = !e.IsDead;
            r.LastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try
            {
                _store.Save(r);
            }
            catch (Exception ex)
            {
                Log.Error("Store", $"Saving {r.PlayerId} failed: {ex.Message}");
            }
        }

        public void SaveAll()
        {
            for (int i = 0; i < _sessionList.Count; i++)
            {
                PlayerSession s = _sessionList[i];
                if (s.Entity?.Record != null && s.IsConnected) s.Entity.Record.PlayTimeSeconds += Math.Min(Config.AutosaveSeconds, Time - s.ConnectedAt);
                SaveRecord(s.Entity);
            }
        }

        // ------------------------------------------------------------------ status

        private void UpdateStatus()
        {
            double now = Time;
            double window = Math.Max(0.001, now - _statsWindowStart);
            long sent = _net?.Statistics.BytesSent ?? 0;
            long recv = _net?.Statistics.BytesReceived ?? 0;
            int loading = 0;
            for (int i = 0; i < _sessionList.Count; i++) if (_sessionList[i].IsConnected && !_sessionList[i].Ready) loading++;
            _status = new ServerStatus
            {
                Name = Config.ServerName,
                Region = Config.Region,
                Players = ConnectedPlayerCount(),
                PlayersLoading = loading,
                Bots = _bots.Count,
                BotsFullSim = _bots.FullSimCount,
                MaxPlayers = Config.MaxPlayers,
                Entities = _entityList.Count,
                Tick = CurrentTick,
                TickRate = Config.TickRate,
                AvgTickMs = _tickMsCount > 0 ? _tickMsAccum / _tickMsCount : 0,
                MaxTickMs = _tickMsMax,
                UptimeSeconds = now,
                TimeOfDay01 = TimeOfDay01,
                BytesSentPerSecond = (long)((sent - _statsBytesSent) / window),
                BytesReceivedPerSecond = (long)((recv - _statsBytesReceived) / window),
                TerrainChunksGenerated = World.Heights.GeneratedChunkCount,
                ManagedMemoryBytes = GC.GetTotalMemory(false),
                WorldInfo = World.Config.ToString(),
            };
            _statsBytesSent = sent;
            _statsBytesReceived = recv;
            _statsWindowStart = now;
            _tickMsAccum = 0;
            _tickMsCount = 0;
            _tickMsMax = 0;
        }

        private static string Short(string id) => id.Length > 8 ? id.Substring(0, 8) : id;
    }
}
