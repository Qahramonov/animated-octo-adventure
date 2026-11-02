using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Survival.Core;
using Survival.Gameplay;
using Survival.Networking;
using Survival.Server;
using Survival.Server.Persistence;
using Survival.World;
using UnityEngine;

namespace Survival.Client
{
    public enum SessionMode
    {
        None,
        Online,
        OfflineLocal,
        LanHost,
    }

    public enum SessionPhase
    {
        Idle,
        Connecting,
        Authenticating,
        LoadingWorld,
        LoadingScene,
        Spawning,
        Playing,
        Reconnecting,
        Failed,
    }

    /// <summary>
    /// The connection context shared by the lobby and the game scene: target server, network
    /// client, prediction/interpolation, generated world and - in offline/LAN mode - the embedded
    /// server running on a background thread. Offline mode is clearly labelled in the UI and is
    /// never presented as online multiplayer.
    /// </summary>
    public sealed class Session
    {
        private GameClient _client;
        private ServerRunner _localServer;
        private Task _worldTask;
        private bool _readySent;
        private bool _sceneRequested;
        private double _sessionStart;

        public SessionMode Mode { get; private set; }
        public SessionPhase Phase { get; private set; }
        public string ServerName { get; private set; } = string.Empty;
        public string Host { get; private set; } = string.Empty;
        public int Port { get; private set; }
        public string Error { get; private set; } = string.Empty;
        public float Progress { get; private set; }
        public WorldRuntime World { get; private set; }
        public ClientSimulation Sim { get; private set; }
        public WelcomeMsg Welcome { get; private set; }
        public bool WorldMismatch { get; private set; }
        public GameClient Client => _client;
        public bool IsLocalServer => _localServer != null;
        public GameServer LocalServer => _localServer?.Server;
        public int RttMs => _client != null ? _client.RttMs : -1;
        public bool InGame => Phase == SessionPhase.Playing || Phase == SessionPhase.Reconnecting || Phase == SessionPhase.Spawning;

        // Forwarded to the game scene / HUD
        public event Action<NoticeMsg> Notice;
        public event Action<DiedMsg> Died;
        public event Action<ChatMsg> Chat;
        public event Action Respawned;
        public event Action<SessionPhase> PhaseChanged;

        public void ConnectOnline(ServerEntry entry)
        {
            Shutdown();
            Mode = SessionMode.Online;
            ServerName = entry.Name;
            Host = entry.Host;
            Port = entry.Port;
            App.Settings.LastServerKey = entry.Key;
            App.Settings.Save();
            StartClient(entry.Host, entry.Port);
        }

        /// <summary>Starts the embedded server on this phone. lan=true lets other phones on the Wi-Fi join.</summary>
        public void StartLocal(bool lan)
        {
            Shutdown();
            Mode = lan ? SessionMode.LanHost : SessionMode.OfflineLocal;
            var cfg = new ServerConfig
            {
                ServerName = lan ? App.Profile.Nickname + "'s LAN game" : "Offline practice",
                Region = lan ? "LAN" : "Local",
                MapName = "Demo Island",
                BindAddress = lan ? "0.0.0.0" : "127.0.0.1",
                Port = lan ? NetProtocol.DefaultPort : 0,
                MaxPlayers = lan ? 16 : 1 + App.Settings.OfflineBotCount,
                BotCount = App.Settings.OfflineBotCount,
                AnswerBroadcast = lan,
                // Phones are not servers: lighter bot simulation budget
                MaxBotThinksPerTick = 6,
                DistantBotTickDivider = 8,
                DataDirectory = System.IO.Path.Combine(Application.persistentDataPath, "localserver"),
                ReconnectGraceSeconds = 30f,
            };
            var server = new GameServer(cfg, new FilePlayerStore(cfg.DataDirectory));
            _localServer = new ServerRunner(server);
            if (!_localServer.Start())
            {
                _localServer = null;
                Fail(lan ? "Could not open UDP port 7777 for the LAN game" : "Local server failed to start");
                return;
            }
            ServerName = cfg.ServerName;
            Host = "127.0.0.1";
            Port = server.LocalPort;
            StartClient(Host, Port);
        }

        private void StartClient(string host, int port)
        {
            Error = string.Empty;
            WorldMismatch = false;
            _readySent = false;
            _sceneRequested = false;
            _client = new GameClient(App.Profile.ToIdentity(), GameInfo.Version);
            _client.StateChanged += OnClientState;
            _client.WelcomeReceived += OnWelcome;
            _client.SnapshotReceived += (h, states) =>
            {
                if (Sim == null) return;
                Sim.ApplyServerState(h);
                Sim.ApplyEntityStates(h.Tick, states);
            };
            _client.EntitySpawned += m => Sim?.OnEntitySpawn(m);
            _client.EntityDespawned += m => Sim?.OnEntityDespawn(m);
            _client.Teleported += t =>
            {
                Sim?.Teleport(t);
                Respawned?.Invoke();
            };
            _client.Died += d =>
            {
                App.Profile.Stats.Deaths++;
                Died?.Invoke(d);
            };
            _client.NoticeReceived += n => Notice?.Invoke(n);
            _client.ChatReceived += c => Chat?.Invoke(c);
            _client.ConnectionLost += () => SetPhase(SessionPhase.Reconnecting);
            _client.Failed += reason => Fail(Localize(reason));
            SetPhase(SessionPhase.Connecting);
            _client.Connect(host, port);
        }

        private static string Localize(string reason)
        {
            if (reason.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0) return Loc.T("server_full_msg");
            if (reason.IndexOf("offline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("unreachable", StringComparison.OrdinalIgnoreCase) >= 0) return Loc.T("server_offline_msg");
            return reason;
        }

        private void OnClientState(ClientState s)
        {
            if (s == ClientState.AwaitingWelcome && Phase == SessionPhase.Connecting) SetPhase(SessionPhase.Authenticating);
        }

        private void OnWelcome(WelcomeMsg w)
        {
            bool reconnect = Phase == SessionPhase.Reconnecting && World != null && World.Config.Seed == w.World.Seed;
            Welcome = w;
            if (w.World.GeneratorVersion != WorldConfig.CurrentGeneratorVersion)
            {
                Fail(Loc.T("version_mismatch") + $" (world generator v{w.World.GeneratorVersion}, game v{WorldConfig.CurrentGeneratorVersion})");
                return;
            }

            if (reconnect)
            {
                Sim.ClearRemotes();
                Sim.Initialize(w);
                _readySent = false;
                SendReadyIfInGame();
                SetPhase(SessionPhase.Playing);
                Notice?.Invoke(new NoticeMsg { Kind = NoticeKind.Info, Text = "Reconnected", Duration = 2f });
                return;
            }

            World = new WorldRuntime(w.World);
            Sim = new ClientSimulation(World, MovementSettings.Default, w.TickRate);
            Sim.Initialize(w);
            _sessionStart = Time.realtimeSinceStartupAsDouble;
            App.Profile.Stats.Sessions++;
            App.Profile.Stats.LastServer = ServerName;

            SetPhase(SessionPhase.LoadingWorld);
            Vector3 spawn = new Vector3(w.Spawn.Position.X, 0, w.Spawn.Position.Z);
            WorldRuntime world = World;
            uint expected = w.WorldFingerprint;
            _worldTask = Task.Run(() =>
            {
                world.Heights.Prewarm(spawn.x, spawn.z, 420f);
                uint fp = WorldFingerprint.Compute(world);
                if (expected != 0 && fp != expected)
                {
                    WorldMismatch = true;
                    Log.Error("World", $"World fingerprint mismatch: client {fp:x8} server {expected:x8}");
                }
            });
        }

        public void Update(float dt)
        {
            if (_client == null) return;
            _client.Update(dt);

            if (Phase == SessionPhase.LoadingWorld && _worldTask != null)
            {
                Progress = Mathf.Clamp01(World.Heights.GeneratedChunkCount / 49f) * 0.6f;
                if (_worldTask.IsCompleted)
                {
                    if (_worldTask.IsFaulted) Log.Exception("World", _worldTask.Exception);
                    _worldTask = null;
                    if (!_sceneRequested)
                    {
                        _sceneRequested = true;
                        SetPhase(SessionPhase.LoadingScene);
                        App.Scenes.Load(App.SceneGame);
                    }
                }
            }
        }

        /// <summary>Called by the game scene when the terrain around the spawn is visible.</summary>
        public void MarkSceneReady()
        {
            SetPhase(SessionPhase.Spawning);
            SendReadyIfInGame();
            SetPhase(SessionPhase.Playing);
        }

        private void SendReadyIfInGame()
        {
            if (_readySent || _client == null) return;
            _readySent = true;
            _client.SendReady();
        }

        public void SendInputs(IReadOnlyList<PlayerInput> inputs) => _client?.SendInputs(inputs);
        public void RequestRespawn() => _client?.RequestRespawn();

        public void SetLoadingProgress(float p) => Progress = Mathf.Clamp01(p);

        public void Leave()
        {
            SaveLastState();
            Shutdown();
            App.Scenes.Load(App.SceneLobby);
        }

        public void OnAppPaused()
        {
            SaveLastState();
        }

        private void SaveLastState()
        {
            if (Sim == null) return;
            PlayerProfile.LocalStats st = App.Profile.Stats;
            st.LastX = Sim.Predicted.Position.X;
            st.LastZ = Sim.Predicted.Position.Z;
            st.LastHealth = Sim.Stats.Health;
            st.LastHunger = Sim.Stats.Hunger;
            st.LastThirst = Sim.Stats.Thirst;
            st.LastPlayedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_sessionStart > 0) st.PlaySeconds += Time.realtimeSinceStartupAsDouble - _sessionStart;
            _sessionStart = Time.realtimeSinceStartupAsDouble;
            App.Profile.SaveStats();
        }

        public void Cancel()
        {
            Shutdown();
            SetPhase(SessionPhase.Idle);
        }

        public void Shutdown()
        {
            _worldTask = null;
            if (_client != null)
            {
                _client.Disconnect();
                _client.Dispose();
                _client = null;
            }
            if (_localServer != null)
            {
                _localServer.Stop();
                _localServer = null;
            }
            Sim = null;
            World = null;
            Mode = SessionMode.None;
            Phase = SessionPhase.Idle;
            Progress = 0f;
        }

        private void Fail(string reason)
        {
            Error = reason;
            bool wasInGame = InGame;
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
            if (_localServer != null)
            {
                _localServer.Stop();
                _localServer = null;
            }
            SetPhase(SessionPhase.Failed);
            if (wasInGame)
            {
                SaveLastState();
                Sim = null;
                World = null;
                App.Scenes.Load(App.SceneLobby);
            }
        }

        private void SetPhase(SessionPhase p)
        {
            if (Phase == p) return;
            Phase = p;
            PhaseChanged?.Invoke(p);
        }

        /// <summary>Best-effort local IPv4 address for "join my LAN game" instructions.</summary>
        public static string LocalIPv4()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("10.255.255.255", 9);
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch
            {
                return "?";
            }
        }
    }
}
