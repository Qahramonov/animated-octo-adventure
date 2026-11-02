using System;
using System.Diagnostics;
using System.Threading;
using Survival.Core;

namespace Survival.Server
{
    /// <summary>
    /// Runs a <see cref="GameServer"/> at a fixed tick rate on a dedicated thread.
    /// Used by the Linux dedicated server host and by the Unity client's offline/LAN host mode.
    /// </summary>
    public sealed class ServerRunner : IDisposable
    {
        private readonly GameServer _server;
        private Thread _thread;
        private volatile bool _running;

        public ServerRunner(GameServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
        }

        public GameServer Server => _server;
        public bool IsRunning => _running;
        public Exception Fault { get; private set; }

        public bool Start()
        {
            if (_running) return true;
            if (!_server.Start()) return false;
            _running = true;
            _thread = new Thread(Loop) { Name = "GameServer", IsBackground = true };
            _thread.Start();
            return true;
        }

        private void Loop()
        {
            var sw = Stopwatch.StartNew();
            double tickSeconds = 1.0 / _server.Config.TickRate;
            double next = sw.Elapsed.TotalSeconds;
            try
            {
                while (_running)
                {
                    double now = sw.Elapsed.TotalSeconds;
                    if (now < next)
                    {
                        double wait = next - now;
                        if (wait > 0.002) Thread.Sleep((int)Math.Max(1, wait * 1000 - 1));
                        else Thread.Yield();
                        continue;
                    }

                    _server.RunTick();
                    next += tickSeconds;
                    // If we fell far behind (debugger pause, overloaded host) do not spiral
                    if (sw.Elapsed.TotalSeconds - next > 0.5) next = sw.Elapsed.TotalSeconds;
                }
            }
            catch (Exception e)
            {
                Fault = e;
                Log.Exception("Server", e);
            }
            finally
            {
                _running = false;
            }
        }

        public void Stop()
        {
            if (_thread == null) return;
            _running = false;
            _thread.Join(3000);
            _thread = null;
            _server.Stop();
        }

        public void Dispose() => Stop();
    }
}
