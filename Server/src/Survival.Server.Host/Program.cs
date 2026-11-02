using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Survival.Core;
using Survival.Networking;
using Survival.Server;
using Survival.Server.Persistence;

namespace Survival.Server.Host
{
    /// <summary>
    /// OROL Survival dedicated server (Linux headless, .NET 8).
    /// Configuration precedence: defaults &lt; server.json &lt; environment (OROL_*) &lt; command line (--key value).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Log.MinLevel = LogLevel.Info;

            ServerConfig config;
            try
            {
                config = ConfigLoader.Load(args, out string source);
                Log.Info("Host", "Configuration: " + source);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Invalid configuration: " + e.Message);
                return 2;
            }

            if (ConfigLoader.HasFlag(args, "--help") || ConfigLoader.HasFlag(args, "-h"))
            {
                Console.WriteLine(ConfigLoader.HelpText);
                return 0;
            }

            Log.Info("Host", $"{GameInfo.Title} {GameInfo.Subtitle} dedicated server v{GameInfo.Version} | protocol v{NetProtocol.Version} | .NET {Environment.Version} | {RuntimeInformation.OSDescription}");
            Directory.CreateDirectory(config.DataDirectory);
            var store = new FilePlayerStore(config.DataDirectory);
            var server = new GameServer(config, store);
            var runner = new ServerRunner(server);
            if (!runner.Start())
            {
                Log.Error("Host", "Server failed to start");
                return 1;
            }

            StatusHttpServer http = null;
            if (config.StatusHttpPort > 0)
            {
                http = new StatusHttpServer(server, config.StatusHttpPort, config.AdminToken);
                http.Start();
            }

            var quit = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                quit.Set();
            };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => quit.Set();
            using (PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
            {
                ctx.Cancel = true;
                quit.Set();
            }))
            {
                bool interactive = !Console.IsInputRedirected && !ConfigLoader.HasFlag(args, "--no-console");
                if (interactive) StartConsoleThread(server, quit);
                Log.Info("Host", interactive ? "Type 'help' for admin commands. Ctrl+C to stop." : "Running (non-interactive). Send SIGTERM to stop.");

                while (!quit.Wait(1000))
                {
                    if (!runner.IsRunning)
                    {
                        Log.Error("Host", "Server loop stopped unexpectedly: " + runner.Fault?.Message);
                        break;
                    }
                }
            }

            Log.Info("Host", "Shutting down...");
            http?.Stop();
            runner.Stop();
            Log.Info("Host", "Bye.");
            return runner.Fault == null ? 0 : 1;
        }

        private static void StartConsoleThread(GameServer server, ManualResetEventSlim quit)
        {
            var t = new Thread(() =>
            {
                while (!quit.IsSet)
                {
                    string line = Console.ReadLine();
                    if (line == null) break;
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    if (line == "quit" || line == "exit" || line == "stop")
                    {
                        quit.Set();
                        break;
                    }
                    string result = server.EnqueueCommand(line).GetAwaiter().GetResult();
                    Console.WriteLine(result);
                }
            })
            { IsBackground = true, Name = "Console" };
            t.Start();
        }
    }
}
