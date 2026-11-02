using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using Survival.Core;
using Survival.Gameplay;

namespace Survival.Networking
{
    public enum ClientState
    {
        Idle,
        Resolving,
        Connecting,
        AwaitingWelcome,
        InGame,
        Reconnecting,
        Disconnected,
    }

    /// <summary>Who the player is. Generated once per install and stored on the device.</summary>
    public sealed class ClientIdentity
    {
        public string PlayerId;
        public string Secret;
        public string Nickname;
    }

    /// <summary>
    /// Connection to a dedicated (or local) server: handshake, message dispatch, input upload and
    /// automatic reconnect with the session token after network loss (e.g. switching Wi-Fi/LTE or
    /// the app being backgrounded). Engine independent: used by the Unity client and by the
    /// headless load-test bots. Call <see cref="Update"/> every frame on the game thread.
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        public const int MaxReconnectAttempts = 6;
        private static readonly float[] ReconnectDelays = { 1f, 2f, 3f, 5f, 8f, 12f };

        private readonly ClientIdentity _identity;
        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly NetManager _net;
        private readonly NetDataWriter _writer = new NetDataWriter();
        private readonly List<EntityState> _entityScratch = new List<EntityState>(64);

        private NetPeer _peer;
        private string _host;
        private int _port;
        private IPEndPoint _endPoint;
        private Task<IPAddress[]> _dns;
        private float _stateTime;
        private float _reconnectWait;
        private bool _userDisconnect;

        public GameClient(ClientIdentity identity, string clientVersion)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            ClientVersion = clientVersion;
            _listener.PeerConnectedEvent += OnConnected;
            _listener.PeerDisconnectedEvent += OnDisconnected;
            _listener.NetworkReceiveEvent += OnReceive;
            _net = new NetManager(_listener)
            {
                ChannelsCount = NetProtocol.ChannelCount,
                DisconnectTimeout = 10000,
                PingInterval = 1000,
                IPv6Enabled = false,
                EnableStatistics = true,
                AutoRecycle = false,
            };
            _net.Start();
        }

        /// <summary>
        /// Emulates a bad mobile network on this client (load tests). Only effective in builds that
        /// define SIMULATE_NETWORK (the .NET test tools do; the game does not).
        /// </summary>
        public void SimulateNetwork(int minLatencyMs, int maxLatencyMs, int lossPercent)
        {
            _net.SimulateLatency = maxLatencyMs > 0;
            _net.SimulationMinLatency = minLatencyMs;
            _net.SimulationMaxLatency = maxLatencyMs;
            _net.SimulatePacketLoss = lossPercent > 0;
            _net.SimulationPacketLossChance = lossPercent;
        }

        public string ClientVersion { get; }
        public ClientState State { get; private set; } = ClientState.Idle;
        public string SessionToken { get; private set; }
        public uint LocalEntityId { get; private set; }
        public WelcomeMsg LastWelcome { get; private set; }
        public int ReconnectAttempt { get; private set; }
        public int RttMs => _peer != null ? _peer.RoundTripTime : -1;
        public long BytesReceived => _net.Statistics.BytesReceived;
        public long BytesSent => _net.Statistics.BytesSent;
        public string LastError { get; private set; }
        public RejectReason LastRejectReason { get; private set; }
        public string Host => _host;
        public int Port => _port;

        // ---- events (raised on the thread calling Update) ----
        public event Action<ClientState> StateChanged;
        public event Action<WelcomeMsg> WelcomeReceived;
        public event Action<SnapshotHeader, List<EntityState>> SnapshotReceived;
        public event Action<EntitySpawnMsg> EntitySpawned;
        public event Action<EntityDespawnMsg> EntityDespawned;
        public event Action<NoticeMsg> NoticeReceived;
        public event Action<TeleportMsg> Teleported;
        public event Action<DiedMsg> Died;
        public event Action<ChatMsg> ChatReceived;
        /// <summary>Connection attempt failed or reconnect gave up. Argument is a human readable reason.</summary>
        public event Action<string> Failed;
        /// <summary>The connection dropped while playing; automatic reconnect is starting.</summary>
        public event Action ConnectionLost;

        public void Connect(string host, int port)
        {
            _host = host;
            _port = port;
            _userDisconnect = false;
            ReconnectAttempt = 0;
            SessionToken = null;
            LastError = null;
            BeginConnect();
        }

        private void BeginConnect()
        {
            _endPoint = null;
            _dns = null;
            if (IPAddress.TryParse(_host, out IPAddress ip))
            {
                _endPoint = new IPEndPoint(ip, _port);
                SendConnect();
            }
            else
            {
                try
                {
                    _dns = Dns.GetHostAddressesAsync(_host);
                    SetState(ClientState.Resolving);
                }
                catch (Exception e)
                {
                    Fail("DNS error: " + e.Message);
                }
            }
        }

        private void SendConnect()
        {
            _writer.Reset();
            new ConnectRequestData
            {
                ProtocolVersion = NetProtocol.Version,
                PlayerId = _identity.PlayerId,
                PlayerSecret = _identity.Secret,
                Nickname = _identity.Nickname,
                SessionToken = SessionToken,
                ClientVersion = ClientVersion,
                Platform = 0,
            }.Write(_writer);
            _peer = (NetPeer)_net.Connect(_endPoint, _writer);
            if (_peer == null)
            {
                Fail("Could not start connection");
                return;
            }
            if (State != ClientState.Reconnecting) SetState(ClientState.Connecting);
            _stateTime = 0f;
        }

        public void Update(float dt)
        {
            _net.PollEvents();
            _stateTime += dt;

            if (State == ClientState.Resolving && _dns != null && _dns.IsCompleted)
            {
                IPAddress addr = null;
                if (_dns.Status == TaskStatus.RanToCompletion && _dns.Result != null)
                {
                    foreach (IPAddress a in _dns.Result)
                    {
                        if (a.AddressFamily == AddressFamily.InterNetwork)
                        {
                            addr = a;
                            break;
                        }
                    }
                }
                _dns = null;
                if (addr == null)
                {
                    Fail("Server address could not be resolved");
                    return;
                }
                _endPoint = new IPEndPoint(addr, _port);
                SendConnect();
            }

            if (State == ClientState.AwaitingWelcome && _stateTime > 15f)
            {
                Fail("Server did not answer the handshake");
            }

            if (State == ClientState.Reconnecting && _peer == null)
            {
                _reconnectWait -= dt;
                if (_reconnectWait <= 0f)
                {
                    ReconnectAttempt++;
                    Log.Info("Net", $"Reconnect attempt {ReconnectAttempt}/{MaxReconnectAttempts} to {_host}:{_port}");
                    if (_endPoint != null) SendConnect();
                    else BeginConnect();
                }
            }
        }

        public void SendReady()
        {
            if (_peer == null) return;
            _writer.Reset();
            _writer.Put((byte)MsgId.ClientReady);
            _peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }

        public void SendInputs(IReadOnlyList<PlayerInput> newestFirst)
        {
            if (_peer == null || State != ClientState.InGame || newestFirst.Count == 0) return;
            _writer.Reset();
            InputPacket.Write(_writer, newestFirst, NetProtocol.InputRedundancy);
            _peer.Send(_writer, NetProtocol.ChannelUnreliable, DeliveryMethod.Unreliable);
        }

        public void RequestRespawn()
        {
            if (_peer == null) return;
            _writer.Reset();
            _writer.Put((byte)MsgId.ClientRespawn);
            _peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }

        public void SendChat(string text)
        {
            if (_peer == null || string.IsNullOrWhiteSpace(text)) return;
            _writer.Reset();
            new ChatMsg { From = string.Empty, Text = text }.Write(_writer, MsgId.ClientChat);
            _peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }

        public void Disconnect()
        {
            _userDisconnect = true;
            if (_peer != null) _peer.Disconnect();
            _peer = null;
            SetState(ClientState.Disconnected);
        }

        private void OnConnected(NetPeer peer)
        {
            _peer = peer;
            if (State != ClientState.Reconnecting) SetState(ClientState.AwaitingWelcome);
            _stateTime = 0f;
        }

        private void OnDisconnected(NetPeer peer, DisconnectInfo info)
        {
            _peer = null;
            if (_userDisconnect) return;

            if (info.Reason == DisconnectReason.ConnectionRejected)
            {
                RejectData reject = RejectData.Read(info.AdditionalData);
                LastRejectReason = reject.Reason;
                Fail(string.IsNullOrEmpty(reject.Message) ? "Connection rejected (" + reject.Reason + ")" : reject.Message);
                return;
            }

            bool wasPlaying = State == ClientState.InGame || State == ClientState.Reconnecting;
            bool kicked = info.Reason == DisconnectReason.RemoteConnectionClose && State == ClientState.InGame;
            if (wasPlaying && !kicked && !string.IsNullOrEmpty(SessionToken) && ReconnectAttempt < MaxReconnectAttempts)
            {
                if (State != ClientState.Reconnecting)
                {
                    SetState(ClientState.Reconnecting);
                    ConnectionLost?.Invoke();
                }
                _reconnectWait = ReconnectDelays[Math.Min(ReconnectAttempt, ReconnectDelays.Length - 1)];
                return;
            }

            string reason;
            switch (info.Reason)
            {
                case DisconnectReason.ConnectionFailed: reason = "Server is offline or unreachable"; break;
                case DisconnectReason.Timeout: reason = "Connection timed out"; break;
                case DisconnectReason.HostUnreachable:
                case DisconnectReason.NetworkUnreachable: reason = "No network connection"; break;
                case DisconnectReason.RemoteConnectionClose: reason = "Disconnected by server"; break;
                default: reason = "Disconnected (" + info.Reason + ")"; break;
            }
            if (wasPlaying && ReconnectAttempt >= MaxReconnectAttempts) reason = "Reconnect failed: " + reason;
            Fail(reason);
        }

        private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            try
            {
                if (reader.AvailableBytes < 1) return;
                var id = (MsgId)reader.GetByte();
                switch (id)
                {
                    case MsgId.Welcome:
                        WelcomeMsg welcome = WelcomeMsg.Read(reader);
                        LastWelcome = welcome;
                        SessionToken = welcome.SessionToken;
                        LocalEntityId = welcome.EntityId;
                        ReconnectAttempt = 0;
                        SetState(ClientState.InGame);
                        WelcomeReceived?.Invoke(welcome);
                        break;
                    case MsgId.Snapshot:
                        SnapshotHeader header = SnapshotHeader.Read(reader);
                        int count = (int)reader.GetVarUInt();
                        _entityScratch.Clear();
                        for (int i = 0; i < count; i++) _entityScratch.Add(EntityState.Read(reader));
                        SnapshotReceived?.Invoke(header, _entityScratch);
                        break;
                    case MsgId.EntitySpawn:
                        EntitySpawned?.Invoke(EntitySpawnMsg.Read(reader));
                        break;
                    case MsgId.EntityDespawn:
                        EntityDespawned?.Invoke(EntityDespawnMsg.Read(reader));
                        break;
                    case MsgId.Notice:
                        NoticeReceived?.Invoke(NoticeMsg.Read(reader));
                        break;
                    case MsgId.Teleport:
                        Teleported?.Invoke(TeleportMsg.Read(reader));
                        break;
                    case MsgId.Died:
                        Died?.Invoke(DiedMsg.Read(reader));
                        break;
                    case MsgId.Chat:
                        ChatReceived?.Invoke(ChatMsg.Read(reader));
                        break;
                    default:
                        Log.Warn("Net", "Unknown message " + id);
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Exception("Net", e);
            }
            finally
            {
                reader.Recycle();
            }
        }

        private void Fail(string reason)
        {
            LastError = reason;
            _peer = null;
            SetState(ClientState.Disconnected);
            Log.Warn("Net", reason);
            Failed?.Invoke(reason);
        }

        private void SetState(ClientState s)
        {
            if (State == s) return;
            State = s;
            _stateTime = 0f;
            StateChanged?.Invoke(s);
        }

        public void Dispose()
        {
            _userDisconnect = true;
            _net.Stop(true);
        }
    }
}
