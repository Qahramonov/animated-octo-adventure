using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Survival.Gameplay;
using Survival.Networking;
using Survival.Server;
using Survival.Server.Persistence;
using Survival.World;
using Xunit;

namespace Survival.Tests
{
    /// <summary>
    /// End-to-end tests over real UDP sockets on loopback: the server and the clients use exactly the
    /// code that runs on the dedicated server and on the phone.
    /// </summary>
    public class ServerIntegrationTests
    {
        private sealed class TestClient : IDisposable
        {
            public readonly GameClient Client;
            public ClientSimulation Sim;
            public WelcomeMsg? Welcome;
            public string Error;
            public int Snapshots;
            public readonly HashSet<uint> Seen = new HashSet<uint>();
            public bool Lost;
            public bool Teleported;
            public DiedMsg? Died;

            public TestClient(string id, string secret, string name)
            {
                Client = new GameClient(new ClientIdentity { PlayerId = id, Secret = secret, Nickname = name }, "test");
                Client.WelcomeReceived += w =>
                {
                    Welcome = w;
                    Sim = new ClientSimulation(new WorldRuntime(w.World), MovementSettings.Default, w.TickRate);
                    Sim.Initialize(w);
                    Client.SendReady();
                };
                Client.SnapshotReceived += (h, states) =>
                {
                    Snapshots++;
                    Sim?.ApplyServerState(h);
                    Sim?.ApplyEntityStates(h.Tick, states);
                };
                Client.EntitySpawned += m =>
                {
                    Seen.Add(m.Id);
                    Sim?.OnEntitySpawn(m);
                };
                Client.EntityDespawned += m => Sim?.OnEntityDespawn(m);
                Client.Failed += r => Error = r;
                Client.ConnectionLost += () => Lost = true;
                Client.Teleported += t =>
                {
                    Teleported = true;
                    Sim?.Teleport(t);
                };
                Client.Died += d => Died = d;
            }

            public void Dispose() => Client.Dispose();
        }

        private static GameServer StartServer(Action<ServerConfig> configure, IPlayerStore store = null)
        {
            var cfg = new ServerConfig
            {
                Port = 0,
                BotCount = 0,
                WorldSize = 2000f,
                StatusHttpPort = 0,
                ReconnectGraceSeconds = 30f,
                DisconnectTimeoutMs = 2000,
            };
            configure?.Invoke(cfg);
            var server = new GameServer(cfg, store ?? new MemoryPlayerStore());
            Assert.True(server.Start());
            return server;
        }

        private static bool Pump(GameServer server, IEnumerable<TestClient> clients, Func<bool> until, double seconds, Action<TestClient> perClient = null)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                server.RunTick();
                foreach (TestClient c in clients)
                {
                    c.Client.Update(1f / 30f);
                    perClient?.Invoke(c);
                }
                if (until()) return true;
                Thread.Sleep(5);
            }
            return until();
        }

        private static void Walk(TestClient c, float yaw)
        {
            if (c.Sim == null || c.Client.State != ClientState.InGame) return;
            var input = new PlayerInput { MoveY = 127 };
            input.YawDegrees = yaw;
            c.Sim.Advance(1f / 30f, input);
            c.Client.SendInputs(c.Sim.PendingSend);
            c.Sim.UpdateRemotes();
        }

        [Fact]
        public void Query_ReportsRealInfo_AndPing()
        {
            GameServer server = StartServer(c => { c.ServerName = "TEST #1"; c.BotCount = 5; });
            try
            {
                using (var q = new ServerQueryClient())
                {
                    ServerQueryResult result = null;
                    for (int i = 0; i < 5; i++) server.RunTick(); // spawn some bots
                    q.Query("127.0.0.1", server.LocalPort, r => result = r);
                    var sw = Stopwatch.StartNew();
                    while (result == null && sw.Elapsed.TotalSeconds < 5)
                    {
                        server.RunTick();
                        q.Update();
                        Thread.Sleep(5);
                    }
                    Assert.NotNull(result);
                    Assert.Equal(QueryStatus.Online, result.Status);
                    Assert.Equal("TEST #1", result.Info.Name);
                    Assert.Equal(NetProtocol.Version, result.Info.ProtocolVersion);
                    Assert.Equal(100, result.Info.MaxPlayers);
                    Assert.True(result.Info.Bots > 0);
                    Assert.InRange(result.PingMs, 0, 1000);
                }
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void Query_UnreachableServer_IsOffline()
        {
            using (var q = new ServerQueryClient())
            {
                ServerQueryResult result = null;
                q.Query("127.0.0.1", 1, r => result = r, 1.5);
                var sw = Stopwatch.StartNew();
                while (result == null && sw.Elapsed.TotalSeconds < 4)
                {
                    q.Update();
                    Thread.Sleep(10);
                }
                Assert.NotNull(result);
                Assert.Equal(QueryStatus.Offline, result.Status);
            }
        }

        [Fact]
        public void TwoClients_SeeEachOther_AndMovementReplicates()
        {
            // Small island so both spawns are within the interest radius
            GameServer server = StartServer(c => { c.WorldSize = 1000f; c.InterestRadius = 600f; });
            var a = new TestClient("aaaaaaaa-1111", "secret-a", "Alice");
            var b = new TestClient("bbbbbbbb-2222", "secret-b", "Bob");
            var clients = new[] { a, b };
            try
            {
                a.Client.Connect("127.0.0.1", server.LocalPort);
                b.Client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, clients, () => a.Welcome != null && b.Welcome != null, 5), "welcome: " + a.Error + b.Error);
                uint aId = a.Welcome.Value.EntityId, bId = b.Welcome.Value.EntityId;
                Assert.NotEqual(aId, bId);

                Assert.True(Pump(server, clients, () => a.Seen.Contains(bId) && b.Seen.Contains(aId) && a.Snapshots > 5, 5), "players must see each other");

                Vector3 bStartOnA = a.Sim.Remotes[bId].Position;
                Pump(server, clients, () => false, 2.0, c => Walk(c, c == b ? 90f : 0f));
                Vector3 bNowOnA = a.Sim.Remotes[bId].Position;
                ServerEntity bServer = server.Entities.First(e => e.Id == bId);
                Assert.True(SmathDist(bStartOnA, bNowOnA) > 2f, "B's movement must reach A");
                Assert.True(SmathDist(bNowOnA, bServer.Move.Position) < 2.5f, "interpolated position must track the server");
                Assert.True(SmathDist(b.Sim.Predicted.Position, bServer.Move.Position) < 1.5f, "prediction must track the server");
            }
            finally
            {
                a.Dispose();
                b.Dispose();
                server.Stop();
            }
        }

        [Fact]
        public void ServerFull_IsRejectedWithMessage()
        {
            GameServer server = StartServer(c => c.MaxPlayers = 1);
            var a = new TestClient("aaaaaaaa-0001", "s1", "One");
            var b = new TestClient("bbbbbbbb-0002", "s2", "Two");
            try
            {
                a.Client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, new[] { a }, () => a.Welcome != null, 5));
                b.Client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, new[] { a, b }, () => b.Error != null, 5));
                Assert.Equal(RejectReason.ServerFull, b.Client.LastRejectReason);
                Assert.Contains("full", b.Error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                a.Dispose();
                b.Dispose();
                server.Stop();
            }
        }

        [Fact]
        public void BotsYieldSlots_TotalNeverExceedsMax()
        {
            GameServer server = StartServer(c => { c.MaxPlayers = 10; c.BotCount = 10; });
            var clients = Enumerable.Range(0, 4).Select(i => new TestClient($"player0{i}-abcd", "s" + i, "P" + i)).ToArray();
            try
            {
                for (int i = 0; i < 30; i++) server.RunTick();
                Assert.Equal(10, server.Bots.Count);
                foreach (TestClient c in clients) c.Client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, clients, () => clients.All(c => c.Welcome != null), 5));
                for (int i = 0; i < 10; i++) server.RunTick();
                Assert.Equal(4, server.ConnectedPlayerCount());
                Assert.Equal(6, server.Bots.Count);
                Assert.True(server.ConnectedPlayerCount() + server.Bots.Count <= 10);
            }
            finally
            {
                foreach (TestClient c in clients) c.Dispose();
                server.Stop();
            }
        }

        [Fact]
        public void WrongSecret_IsRejected_AndCharacterPersists()
        {
            string dir = Path.Combine(Path.GetTempPath(), "orol-test-" + Guid.NewGuid().ToString("N"));
            var store = new FilePlayerStore(dir);
            GameServer server = StartServer(c => c.ReconnectGraceSeconds = 0f, store);
            try
            {
                var a = new TestClient("persist-0001", "good-secret", "Keeper");
                a.Client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, new[] { a }, () => a.Welcome != null && a.Snapshots > 3, 5));
                Pump(server, new[] { a }, () => false, 1.5, c => Walk(c, 45f));
                a.Client.Disconnect();
                a.Dispose();
                Pump(server, Array.Empty<TestClient>(), () => server.Sessions.Count == 0, 5);
                PlayerRecord rec = store.Load("persist-0001");
                Assert.NotNull(rec);
                Assert.True(rec.HasPosition);

                var thief = new TestClient("persist-0001", "wrong-secret", "Thief");
                thief.Client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, new[] { thief }, () => thief.Error != null, 5));
                Assert.Equal(RejectReason.AuthFailed, thief.Client.LastRejectReason);
                thief.Dispose();

                var back = new TestClient("persist-0001", "good-secret", "Keeper");
                back.Client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, new[] { back }, () => back.Welcome != null, 5));
                Assert.True(SmathDist(back.Welcome.Value.Spawn.Position, rec.Position) < 0.5f, "must resume at the saved position");
                back.Dispose();
            }
            finally
            {
                server.Stop();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void Reconnect_WithinGrace_ResumesSameCharacter()
        {
            GameServer server = StartServer(null);
            var a = new TestClient("reconn-0001", "secret", "Phone");
            try
            {
                a.Client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, new[] { a }, () => a.Welcome != null && a.Snapshots > 2, 5));
                uint id = a.Welcome.Value.EntityId;
                a.Client.Disconnect();
                a.Dispose();
                Pump(server, Array.Empty<TestClient>(), () => false, 0.5);

                var again = new TestClient("reconn-0001", "secret", "Phone");
                again.Client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, new[] { again }, () => again.Welcome != null, 5));
                Assert.True(again.Welcome.Value.Resumed);
                Assert.Equal(id, again.Welcome.Value.EntityId);
                again.Dispose();
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact]
        public void Death_AndRespawn_Flow()
        {
            GameServer server = StartServer(c => c.RespawnDelaySeconds = 0.2f);
            var a = new TestClient("death-00001", "secret", "Mortal");
            try
            {
                a.Client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, new[] { a }, () => a.Welcome != null && a.Snapshots > 2, 5));
                ServerEntity e = server.Entities.First(x => x.Type == EntityType.Player);
                server.ApplyDamage(e, 500f, DamageType.Fall, null);
                Assert.True(Pump(server, new[] { a }, () => a.Died != null, 3));
                Assert.True(Pump(server, new[] { a }, () => e.DeadTime > 0.3f, 3));
                a.Client.RequestRespawn();
                Assert.True(Pump(server, new[] { a }, () => a.Teleported, 3));
                Assert.False(e.IsDead);
                Assert.Equal(100f, e.Stats.Health);
            }
            finally
            {
                a.Dispose();
                server.Stop();
            }
        }

        [Fact]
        public void VersionMismatch_IsRejected()
        {
            GameServer server = StartServer(null);
            try
            {
                var listener = new LiteNetLib.EventBasedNetListener();
                RejectReason? reason = null;
                listener.PeerDisconnectedEvent += (p, info) => reason = RejectData.Read(info.AdditionalData).Reason;
                var mgr = new LiteNetLib.NetManager(listener) { ChannelsCount = NetProtocol.ChannelCount };
                mgr.Start();
                try
                {
                    var w = new LiteNetLib.Utils.NetDataWriter();
                    new ConnectRequestData { ProtocolVersion = NetProtocol.Version + 99, PlayerId = "version-0001", PlayerSecret = "x", Nickname = "Old" }.Write(w);
                    mgr.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, server.LocalPort), w);
                    var sw = Stopwatch.StartNew();
                    while (reason == null && sw.Elapsed.TotalSeconds < 5)
                    {
                        server.RunTick();
                        mgr.PollEvents();
                        Thread.Sleep(5);
                    }
                    Assert.Equal(RejectReason.VersionMismatch, reason);
                }
                finally
                {
                    mgr.Stop();
                }
            }
            finally
            {
                server.Stop();
            }
        }

        private static float SmathDist(Vector3 a, Vector3 b) => Survival.Core.SMath.DistanceXZ(a, b);
    }
}
