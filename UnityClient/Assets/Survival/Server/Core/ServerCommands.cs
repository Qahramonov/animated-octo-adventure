using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Survival.Core;
using Survival.Gameplay;
using Survival.Networking;
using Survival.World;

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

        private PlayerSession FindSession(string name)
        {
            foreach (PlayerSession s in _sessionList)
            {
                if (string.Equals(s.Nickname, name, StringComparison.OrdinalIgnoreCase)) return s;
            }
            return _sessionList.Count == 1 && name == "*" ? _sessionList[0] : null;
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
                    return "commands: status | players | bots <count> | kick <name> | say <text> | save | time <0..1> | config | give <player> <item> [count] | spawn <deer|boar|wolf|campfire> <player> [distance]";
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
                case "give":
                {
                    // give <player> <item> [count]
                    string[] a = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (a.Length < 2 || !Enum.TryParse(a[1], true, out ItemId item) || ItemDb.Get(item) == null) return "usage: give <player> <item> [count]";
                    int count = a.Length > 2 && int.TryParse(a[2], out int c) ? Math.Max(1, c) : 1;
                    PlayerSession ps = FindSession(a[0]);
                    if (ps?.Entity?.Items == null) return "player not found: " + a[0];
                    int lost = ps.Entity.Items.Add(item, count);
                    SendInventory(ps);
                    return $"gave {count - lost} x {item} to {ps.Nickname}";
                }
                case "spawn":
                {
                    // spawn <deer|boar|wolf|campfire> <player> [distance]
                    string[] a = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    PlayerSession ps = a.Length > 1 ? FindSession(a[1]) : null;
                    if (ps?.Entity == null) return "usage: spawn <deer|boar|wolf|campfire> <player> [distance]";
                    float dist = a.Length > 2 && float.TryParse(a[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : 6f;
                    System.Numerics.Vector3 pos = ps.Entity.Move.Position + SMath.YawForward(ps.Entity.Yaw) * dist;
                    if (string.Equals(a[0], "campfire", StringComparison.OrdinalIgnoreCase))
                    {
                        ServerEntity fire = CreateEntity(EntityType.Structure, string.Empty, pos, ps.Entity.Yaw);
                        fire.SubType = (byte)StructureKind.Campfire;
                        _structures.Add(fire);
                        return "campfire placed";
                    }
                    if (!Enum.TryParse(a[0], true, out AnimalKind kind)) return "unknown kind: " + a[0];
                    SpawnAnimal(kind, pos);
                    return "spawned " + kind;
                }
                case "goto":
                {
                    // goto <player> <tree|bush|boulder|stone|metal|sulfur> : teleport next to the nearest such node
                    string[] a = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    PlayerSession ps = a.Length > 1 ? FindSession(a[0]) : null;
                    if (ps?.Entity == null) return "usage: goto <player> <tree|bush|boulder|stone|metal|sulfur>";
                    NodeKind want;
                    switch (a[1].ToLowerInvariant())
                    {
                        case "tree": want = NodeKind.TreePine; break;
                        case "bush": want = NodeKind.Bush; break;
                        case "boulder": want = NodeKind.Boulder; break;
                        case "stone": want = NodeKind.StoneNode; break;
                        case "metal": want = NodeKind.MetalNode; break;
                        case "sulfur": want = NodeKind.SulfurNode; break;
                        default: return "unknown node kind: " + a[1];
                    }
                    var nodes = new List<WorldNode>(256);
                    System.Numerics.Vector3 from = ps.Entity.Move.Position;
                    for (float radius = 64f; radius <= 2048f; radius *= 2f)
                    {
                        nodes.Clear();
                        World.Nodes.Query(from.X, from.Z, radius, nodes);
                        float best = float.MaxValue;
                        int pick = -1;
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            if (nodes[i].Kind != want || !IsNodeAlive(nodes[i].Id)) continue;
                            float d = (nodes[i].X - from.X) * (nodes[i].X - from.X) + (nodes[i].Z - from.Z) * (nodes[i].Z - from.Z);
                            if (d < best)
                            {
                                best = d;
                                pick = i;
                            }
                        }
                        if (pick < 0) continue;
                        WorldNode node = nodes[pick];
                        float x = node.X, z = node.Z - node.Radius - 4.5f; // stand south of it, facing north (+Z)
                        ps.Entity.Move = MoveState.At(new System.Numerics.Vector3(x, World.GroundHeight(x, z), z));
                        ps.Entity.Yaw = 0f;
                        ps.Entity.History.Clear();
                        ps.PendingInputs.Clear();
                        OnEntityMoved(ps.Entity);
                        _writer.Reset();
                        new TeleportMsg { State = ps.Entity.Move, Yaw = 0f, Stats = ps.Entity.Stats }.Write(_writer);
                        ps.Peer?.Send(_writer, NetProtocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
                        return $"moved {ps.Nickname} to {want} at {node.X:0},{node.Z:0}";
                    }
                    return "no such node found";
                }
                default:
                    return "unknown command, type 'help'";
            }
        }
    }
}
