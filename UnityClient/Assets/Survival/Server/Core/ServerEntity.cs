using System.Collections.Generic;
using System.Numerics;
using LiteNetLib;
using Survival.Core;
using Survival.Gameplay;
using Survival.Networking;
using Survival.Server.AI;
using Survival.Server.Persistence;

namespace Survival.Server
{
    /// <summary>A simulated character on the server (real player or AI bot).</summary>
    public sealed class ServerEntity
    {
        /// <summary>Position history for lag-compensated hit detection (used by combat in Phase 3).</summary>
        public struct HistorySample
        {
            public uint Tick;
            public Vector3 Position;
        }

        public uint Id;
        public EntityType Type;
        public byte SubType;
        public string Name;
        public MoveState Move;
        public float Yaw;
        public float Pitch;
        public SurvivalStats Stats;
        public float DeadTime;
        public string LastKiller;
        public bool Removed;
        public readonly RingBuffer<HistorySample> History = new RingBuffer<HistorySample>(32);

        /// <summary>Owning connection for players (null for bots or while disconnected).</summary>
        public PlayerSession Session;
        public PlayerRecord Record;
        public BotBrain Bot;

        public bool IsDead => Stats.Health <= 0f;
        public Vector3 Position => Move.Position;

        public void RecordHistory(uint tick)
        {
            History.Add(new HistorySample { Tick = tick, Position = Move.Position });
        }

        /// <summary>Position at a past tick (linear between samples) for lag compensation.</summary>
        public Vector3 PositionAt(float tick)
        {
            if (History.Count == 0) return Move.Position;
            if (tick <= History.Oldest.Tick) return History.Oldest.Position;
            if (tick >= History.Newest.Tick) return Move.Position;
            for (int i = History.Count - 2; i >= 0; i--)
            {
                HistorySample a = History[i];
                if (a.Tick <= tick)
                {
                    HistorySample b = History[i + 1];
                    float t = (tick - a.Tick) / System.Math.Max(1f, b.Tick - a.Tick);
                    return Vector3.Lerp(a.Position, b.Position, t);
                }
            }
            return Move.Position;
        }
    }

    /// <summary>Server-side state of one connected client.</summary>
    public sealed class PlayerSession
    {
        public NetPeer Peer;
        public string PlayerId;
        public string Nickname;
        public string SessionToken;
        public ServerEntity Entity;
        public bool Ready;
        public double ConnectedAt;
        public double DisconnectedAt = -1;

        // ---- input processing ----
        public uint LastProcessedInput;
        public readonly List<PlayerInput> PendingInputs = new List<PlayerInput>(32);
        public float InputTokens = 2f;
        public int TicksWithoutInput;
        public PlayerInput LastInput;
        public int RejectedInputs;
        // Diagnostics (shown by the 'players' admin command)
        public int InputsDropped;
        public int NeutralSteps;
        public int SequenceGaps;

        // ---- replication ----
        public readonly HashSet<uint> Known = new HashSet<uint>();
        public readonly List<ServerEntity> Relevant = new List<ServerEntity>(96);
        public long BytesSent;

        public bool IsConnected => Peer != null;
    }
}
