using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Survival.Core;
using Survival.Gameplay;
using Survival.Networking;
using Survival.World;

namespace Survival.LoadTest
{
    /// <summary>
    /// Network stress test: connects N headless clients over real UDP to a running server. Every
    /// client runs the same prediction/reconciliation code as the phone (ClientSimulation), walks
    /// around with random inputs and reports bandwidth, RTT, snapshot rate and prediction errors.
    ///
    ///   orol-loadtest --host 127.0.0.1 --port 7777 --clients 100 --duration 60 --ramp 20
    /// </summary>
    public static class Program
    {
        private sealed class Bot
        {
            public int Index;
            public GameClient Client;
            public ClientSimulation Sim;
            public Rng Rng;
            public float Yaw;
            public float TurnTimer;
            public bool Sprint;
            public int Snapshots;
            public int SnapshotsWindow;
            public bool Welcomed;
            public string Error;
        }

        private static bool debug;
        private static int debugPrinted;

        public static int Main(string[] args)
        {
            string host = Arg(args, "--host", "127.0.0.1");
            int port = int.Parse(Arg(args, "--port", NetProtocol.DefaultPort.ToString()), CultureInfo.InvariantCulture);
            int clients = int.Parse(Arg(args, "--clients", "50"), CultureInfo.InvariantCulture);
            double duration = double.Parse(Arg(args, "--duration", "30"), CultureInfo.InvariantCulture);
            double ramp = double.Parse(Arg(args, "--ramp", "20"), CultureInfo.InvariantCulture);
            int failBelowPercent = int.Parse(Arg(args, "--min-connected-percent", "95"), CultureInfo.InvariantCulture);
            // e.g. --latency 40-140 --loss 3 : one-way delay range in ms and packet loss % per client
            string[] lat = Arg(args, "--latency", "0-0").Split('-');
            int latMin = int.Parse(lat[0], CultureInfo.InvariantCulture), latMax = int.Parse(lat[lat.Length - 1], CultureInfo.InvariantCulture);
            int loss = int.Parse(Arg(args, "--loss", "0"), CultureInfo.InvariantCulture);
            Log.MinLevel = LogLevel.Warning;
            debug = Array.IndexOf(args, "--debug") >= 0;

            Console.WriteLine($"Load test: {clients} clients -> {host}:{port} for {duration}s (ramp {ramp}/s)" +
                              (latMax > 0 || loss > 0 ? $" | emulated latency {latMin}-{latMax} ms, loss {loss}%" : ""));
            var bots = new List<Bot>(clients);
            WorldRuntime sharedWorld = null;
            var clock = Stopwatch.StartNew();
            double last = 0, nextReport = 2, nextSpawn = 0;
            int spawned = 0;
            const float frameDt = 1f / 30f;
            var rttSamples = new List<int>();

            while (clock.Elapsed.TotalSeconds < duration)
            {
                double now = clock.Elapsed.TotalSeconds;
                float dt = (float)(now - last);
                last = now;

                while (spawned < clients && now >= nextSpawn)
                {
                    var b = new Bot { Index = spawned, Rng = new Rng((uint)(spawned * 7919 + 13)) };
                    var identity = new ClientIdentity
                    {
                        PlayerId = "loadtest-" + spawned.ToString("0000", CultureInfo.InvariantCulture) + "-0000-0000",
                        Secret = "loadtest-secret-" + spawned,
                        Nickname = "LoadBot" + spawned,
                    };
                    b.Client = new GameClient(identity, GameInfo.Version);
                    if (latMax > 0 || loss > 0) b.Client.SimulateNetwork(latMin, latMax, loss);
                    Bot captured = b;
                    b.Client.WelcomeReceived += w =>
                    {
                        if (sharedWorld == null || sharedWorld.Config.Seed != w.World.Seed)
                        {
                            sharedWorld = new WorldRuntime(w.World);
                            uint fp = WorldFingerprint.Compute(sharedWorld);
                            if (fp != w.WorldFingerprint)
                                Console.WriteLine($"WARNING: world fingerprint mismatch (client {fp:x8}, server {w.WorldFingerprint:x8}) - rebuild the load test with the server's code");
                        }
                        captured.Sim = new ClientSimulation(sharedWorld, MovementSettings.Default, w.TickRate);
                        captured.Sim.Initialize(w);
                        captured.Welcomed = true;
                        captured.Client.SendReady();
                    };
                    b.Client.SnapshotReceived += (h, states) =>
                    {
                        captured.Snapshots++;
                        captured.SnapshotsWindow++;
                        int before = captured.Sim?.Corrections ?? 0;
                        captured.Sim?.ApplyServerState(h);
                        if (debug && captured.Sim != null && captured.Sim.Corrections > before && debugPrinted < 12)
                        {
                            debugPrinted++;
                            MoveState p = captured.Sim.LastCorrectionPredicted, s = captured.Sim.LastCorrectionServer;
                            Console.WriteLine($"  [bot {captured.Index}] correction seq {captured.Sim.LastCorrectionSequence} err {captured.Sim.LastCorrectionDistance:0.000} | " +
                                              $"pred {p.Position} {p.Flags} st {p.Stamina:0.00} | srv {s.Position} {s.Flags} st {s.Stamina:0.00}");
                        }
                        captured.Sim?.ApplyEntityStates(h.Tick, states);
                    };
                    b.Client.EntitySpawned += m => captured.Sim?.OnEntitySpawn(m);
                    b.Client.EntityDespawned += m => captured.Sim?.OnEntityDespawn(m);
                    b.Client.Teleported += t => captured.Sim?.Teleport(t);
                    b.Client.Failed += reason => captured.Error = reason;
                    b.Client.Connect(host, port);
                    bots.Add(b);
                    spawned++;
                    nextSpawn += 1.0 / Math.Max(0.1, ramp);
                }

                foreach (Bot b in bots)
                {
                    b.Client.Update(dt);
                    if (b.Sim == null || b.Client.State != ClientState.InGame) continue;

                    b.TurnTimer -= dt;
                    if (b.TurnTimer <= 0f)
                    {
                        b.TurnTimer = b.Rng.Range(1.5f, 5f);
                        b.Yaw = b.Rng.Range(0f, 360f);
                        b.Sprint = b.Rng.Chance(0.3f);
                    }
                    var input = new PlayerInput { MoveY = 127, Buttons = b.Sprint ? InputButtons.Sprint : InputButtons.None };
                    if (b.Rng.Chance(0.01f)) input.Buttons |= InputButtons.Jump;
                    input.YawDegrees = b.Yaw;
                    b.Sim.Advance(dt, input);
                    b.Client.SendInputs(b.Sim.PendingSend);
                    b.Sim.UpdateRemotes();
                }

                if (now >= nextReport)
                {
                    nextReport = now + 5;
                    Report(bots, now, rttSamples, false);
                }

                int sleep = (int)((frameDt - (clock.Elapsed.TotalSeconds - now)) * 1000);
                if (sleep > 0) Thread.Sleep(sleep);
            }

            bool ok = Report(bots, clock.Elapsed.TotalSeconds, rttSamples, true, failBelowPercent);
            foreach (Bot b in bots) b.Client.Dispose();
            return ok ? 0 : 1;
        }

        private static bool Report(List<Bot> bots, double now, List<int> rtts, bool final, int minPercent = 0)
        {
            int inGame = 0, failed = 0, snaps = 0, remotes = 0, corrections = 0;
            long rx = 0, tx = 0;
            float maxCorr = 0;
            rtts.Clear();
            var errors = new Dictionary<string, int>();
            foreach (Bot b in bots)
            {
                if (b.Client.State == ClientState.InGame && b.Welcomed) inGame++;
                if (b.Error != null)
                {
                    failed++;
                    errors.TryGetValue(b.Error, out int c);
                    errors[b.Error] = c + 1;
                }
                snaps += b.SnapshotsWindow;
                b.SnapshotsWindow = 0;
                rx += b.Client.BytesReceived;
                tx += b.Client.BytesSent;
                if (b.Client.RttMs >= 0) rtts.Add(b.Client.RttMs);
                if (b.Sim != null)
                {
                    remotes += b.Sim.Remotes.Count;
                    corrections += b.Sim.Corrections;
                    maxCorr = Math.Max(maxCorr, b.Sim.LastCorrectionDistance);
                }
            }
            rtts.Sort();
            int p50 = rtts.Count > 0 ? rtts[rtts.Count / 2] : -1;
            int p95 = rtts.Count > 0 ? rtts[Math.Min(rtts.Count - 1, (int)(rtts.Count * 0.95))] : -1;
            double perClientKBs = inGame > 0 ? rx / 1024.0 / Math.Max(1, now) / inGame : 0;
            Console.WriteLine($"[{now,5:0}s] in-game {inGame}/{bots.Count} failed {failed} | snapshots/s (all) {snaps / 5.0,7:0.0} | " +
                              $"avg visible entities {(inGame > 0 ? remotes / (double)inGame : 0),5:0.0} | RTT p50 {p50}ms p95 {p95}ms | " +
                              $"down {perClientKBs:0.0} KB/s/client | corrections {corrections} (last max {maxCorr:0.00} m)");
            foreach (var kv in errors) Console.WriteLine($"   error x{kv.Value}: {kv.Key}");
            if (!final) return true;
            int percent = bots.Count == 0 ? 0 : inGame * 100 / bots.Count;
            bool ok = percent >= minPercent;
            Console.WriteLine(ok ? $"RESULT: PASS ({percent}% connected)" : $"RESULT: FAIL ({percent}% connected, required {minPercent}%)");
            return ok;
        }

        private static string Arg(string[] args, string name, string def)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return def;
        }
    }
}
