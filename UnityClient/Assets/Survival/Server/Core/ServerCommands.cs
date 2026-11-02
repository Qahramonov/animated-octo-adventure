using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Survival.Networking;

namespace Survival.Server
{
    /// <summary>Admin commands (console stdin, HTTP admin endpoint, in-game host menu).</summary>
    public sealed partial class GameServer
    {
        /// <summary>Thread-safe: queues a command for execution on the next tick.</summary>
        public Task<string> EnqueueCommand(string line)
        {
            var tcs = new TaskCompletionSource<string>();
            _commands.Enqueue(new KeyValuePair<string, TaskCompletionSource<string>>(line, tcs));
            return tcs.Task;
        }

        private void ProcessCommands()
        {
            int budget = 16;
            while (budget-- > 0 && _commands.TryDequeue(out var cmd))
            {
                string result;
                try
                {
                    result = ExecuteCommand(cmd.Key);
                }
                catch (Exception e)
                {
                    result = "error: " + e.Message;
                }
                cmd.Value.TrySetResult(result);
            }
        }

        /// <summary>Executes a command immediately (server thread only).</summary>
        public string ExecuteCommand(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return string.Empty;
            string[] parts = line.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            switch (cmd)
            {
                case "help":
                    return "commands: status | players | bots <count> | kick <name> | say <text> | save | time <0..1> | config";
                case "status":
                    ServerStatus st = _status;
                    return $"{st.Name} | players {ConnectedPlayerCount()}/{Config.MaxPlayers} | bots {_bots.Count} (near players {_bots.FullSimCount}) | " +
                           $"entities {_entityList.Count} | tick {CurrentTick} avg {st.AvgTickMs:0.00}ms max {st.MaxTickMs:0.00}ms | " +
                           $"out {st.BytesSentPerSecond / 1024.0:0.0} KB/s | uptime {TimeSpan.FromSeconds(Time):d\\.hh\\:mm\\:ss}";
                case "players":
                {
                    var sb = new StringBuilder();
                    foreach (PlayerSession s in _sessionList)
                    {
                        sb.Append(s.Nickname).Append(s.IsConnected ? (s.Ready ? " [in game]" : " [loading]") : " [disconnected]")
                          .Append(" ping ").Append(s.Peer != null ? s.Peer.RoundTripTime : -1).Append("ms pos ")
                          .Append(s.Entity.Move.Position.X.ToString("0", CultureInfo.InvariantCulture)).Append(',')
                          .Append(s.Entity.Move.Position.Z.ToString("0", CultureInfo.InvariantCulture))
                          .Append(" hp ").Append(s.Entity.Stats.Health.ToString("0", CultureInfo.InvariantCulture))
                          .Append(" | input backlog ").Append(s.PendingInputs.Count).Append(" gaps ").Append(s.SequenceGaps)
                          .Append(" dropped ").Append(s.InputsDropped).Append(" neutral ").Append(s.NeutralSteps).AppendLine();
                    }
                    return sb.Length == 0 ? "no players" : sb.ToString().TrimEnd();
                }
                case "bots":
                    if (int.TryParse(arg, out int n))
                    {
                        Config.BotCount = Math.Max(0, Math.Min(Config.MaxPlayers, n));
                        return $"bot target set to {Config.BotCount} (active participants capped at {Config.MaxPlayers})";
                    }
                    return $"bots: {_bots.Count} active, target {Config.BotCount}";
                case "kick":
                    foreach (PlayerSession s in _sessionList)
                    {
                        if (s.IsConnected && string.Equals(s.Nickname, arg, StringComparison.OrdinalIgnoreCase))
                        {
                            s.Peer.Disconnect();
                            return "kicked " + s.Nickname;
                        }
                    }
                    return "player not found: " + arg;
                case "say":
                    Broadcast(NoticeKind.Admin, arg, 5f);
                    return "sent";
                case "save":
                    SaveAll();
                    return "saved " + _sessionList.Count + " player(s)";
                case "time":
                    if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float t))
                    {
                        double elapsedDays = Time / (Config.DayLengthMinutes * 60.0);
                        Config.StartTimeOfDay01 = (float)(((t - elapsedDays) % 1.0 + 1.0) % 1.0);
                        return "time of day set to " + t.ToString("0.00", CultureInfo.InvariantCulture);
                    }
                    return "time of day: " + TimeOfDay01.ToString("0.00", CultureInfo.InvariantCulture);
                case "config":
                    return $"name={Config.ServerName} region={Config.Region} port={Config.Port} maxPlayers={Config.MaxPlayers} bots={Config.BotCount} " +
                           $"tickRate={Config.TickRate} seed={Config.WorldSeed} size={Config.WorldSize} interest={Config.InterestRadius}";
                default:
                    return "unknown command, type 'help'";
            }
        }
    }
}
