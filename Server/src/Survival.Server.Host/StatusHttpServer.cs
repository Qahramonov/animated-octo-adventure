using System;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Survival.Core;

namespace Survival.Server.Host
{
    /// <summary>
    /// Tiny HTTP endpoint for monitoring and administration (TCP, default port 8080):
    ///   GET  /health                        -> "ok" (for Docker/uptime checks)
    ///   GET  /status                        -> JSON (players, bots, tick time, bandwidth, memory)
    ///   POST /admin?token=...&amp;cmd=bots%2075  -> runs an admin command (only when adminToken is set)
    /// Admin requests are compared in constant time and refused when no token is configured.
    /// </summary>
    public sealed class StatusHttpServer
    {
        private readonly GameServer _server;
        private readonly HttpListener _listener = new HttpListener();
        private readonly string _adminToken;
        private readonly int _port;
        private CancellationTokenSource _cts;

        public StatusHttpServer(GameServer server, int port, string adminToken)
        {
            _server = server;
            _port = port;
            _adminToken = adminToken ?? string.Empty;
            _listener.Prefixes.Add($"http://*:{port}/");
        }

        public void Start()
        {
            try
            {
                _listener.Start();
            }
            catch (Exception e)
            {
                Log.Warn("Http", $"Status endpoint disabled (cannot listen on TCP {_port}: {e.Message})");
                return;
            }
            _cts = new CancellationTokenSource();
            Task.Run(() => Loop(_cts.Token));
            Log.Info("Http", $"Status endpoint on http://0.0.0.0:{_port}/status" + (_adminToken.Length > 0 ? " (admin enabled)" : ""));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener.Stop(); } catch { /* already stopped */ }
        }

        private async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private async Task Handle(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url?.AbsolutePath ?? "/";
                if (path == "/health")
                {
                    await Write(ctx, 200, "text/plain", "ok");
                }
                else if (path == "/status" || path == "/")
                {
                    await Write(ctx, 200, "application/json", StatusJson(_server.Status));
                }
                else if (path == "/admin" && ctx.Request.HttpMethod == "POST")
                {
                    string token = ctx.Request.QueryString["token"] ?? ctx.Request.Headers["X-Admin-Token"] ?? string.Empty;
                    if (_adminToken.Length == 0 || !FixedTimeEquals(token, _adminToken))
                    {
                        await Write(ctx, 403, "text/plain", "forbidden");
                        return;
                    }
                    string cmd = ctx.Request.QueryString["cmd"] ?? string.Empty;
                    Task<string> result = _server.EnqueueCommand(cmd);
                    string text = await Task.WhenAny(result, Task.Delay(3000)) == result ? result.Result : "timeout";
                    await Write(ctx, 200, "text/plain", text);
                }
                else
                {
                    await Write(ctx, 404, "text/plain", "not found");
                }
            }
            catch (Exception e)
            {
                Log.Debug("Http", e.Message);
            }
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            byte[] x = Encoding.UTF8.GetBytes(a);
            byte[] y = Encoding.UTF8.GetBytes(b);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(x, y);
        }

        public static string StatusJson(ServerStatus s)
        {
            var o = new
            {
                name = s.Name,
                region = s.Region,
                players = s.Players,
                playersLoading = s.PlayersLoading,
                bots = s.Bots,
                botsNearPlayers = s.BotsFullSim,
                maxPlayers = s.MaxPlayers,
                activeParticipants = s.Players + s.Bots,
                entities = s.Entities,
                tick = s.Tick,
                tickRate = s.TickRate,
                avgTickMs = Math.Round(s.AvgTickMs, 3),
                maxTickMs = Math.Round(s.MaxTickMs, 3),
                tickBudgetMs = Math.Round(1000.0 / Math.Max(1, s.TickRate), 2),
                uptimeSeconds = Math.Round(s.UptimeSeconds),
                timeOfDay = Math.Round(s.TimeOfDay01, 3),
                bytesSentPerSecond = s.BytesSentPerSecond,
                bytesReceivedPerSecond = s.BytesReceivedPerSecond,
                terrainChunks = s.TerrainChunksGenerated,
                managedMemoryMB = Math.Round(s.ManagedMemoryBytes / 1048576.0, 1),
                world = s.WorldInfo,
                version = GameInfo.Version,
                protocol = Survival.Networking.NetProtocol.Version,
            };
            return JsonSerializer.Serialize(o);
        }

        private static async Task Write(HttpListenerContext ctx, int code, string type, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = type + "; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            ctx.Response.Close();
        }
    }
}
