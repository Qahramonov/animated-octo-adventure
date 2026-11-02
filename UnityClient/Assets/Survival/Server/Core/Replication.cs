using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using Survival.Core;
using Survival.Networking;

namespace Survival.Server
{
    /// <summary>
    /// Interest management + snapshot replication.
    /// For every ready client: gather entities inside the relevancy radius from the spatial grid,
    /// keep the N nearest, send reliable spawn/despawn events when the relevant set changes
    /// (with hysteresis), and send unreliable snapshots whose update rate drops with distance
    /// (near: every snapshot, mid: every 2nd, far: every 4th). Snapshots are split to stay
    /// below the MTU.
    /// </summary>
    internal sealed class Replication
    {
        private const float NearDistance = 120f;
        private const float MidDistance = 220f;
        private const float Hysteresis = 30f;
        private const int MaxEntriesPerPacket = 60;

        private readonly GameServer _server;
        private readonly List<ServerEntity> _candidates = new List<ServerEntity>(256);
        private readonly List<KeyValuePair<float, ServerEntity>> _sorted = new List<KeyValuePair<float, ServerEntity>>(256);
        private readonly HashSet<uint> _relevantIds = new HashSet<uint>();
        private readonly List<uint> _toRemove = new List<uint>(32);
        private readonly List<ServerEntity> _due = new List<ServerEntity>(128);
        private static readonly Comparison<KeyValuePair<float, ServerEntity>> ByDistance = (a, b) => a.Key.CompareTo(b.Key);

        public Replication(GameServer server)
        {
            _server = server;
        }

        public void SendSnapshots(uint tick)
        {
            IReadOnlyList<PlayerSession> sessions = _server.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                PlayerSession s = sessions[i];
                if (!s.IsConnected || !s.Ready || s.Entity == null || s.Entity.Removed) continue;
                UpdateRelevancy(s);
                SendSnapshot(s, tick);
            }
        }

        private void UpdateRelevancy(PlayerSession s)
        {
            ServerConfig cfg = _server.Config;
            ServerEntity self = s.Entity;
            float radius = cfg.InterestRadius;
            float keepRadius = radius + Hysteresis;

            _candidates.Clear();
            _server.Grid.QueryCells(self.Move.Position, keepRadius, _candidates);
            _sorted.Clear();
            float keep2 = keepRadius * keepRadius;
            float r2 = radius * radius;
            for (int i = 0; i < _candidates.Count; i++)
            {
                ServerEntity e = _candidates[i];
                if (e == self || e.Removed) continue;
                float d2 = SMath.DistanceSqXZ(e.Move.Position, self.Move.Position);
                // New entities must be inside the radius; known ones may stay until radius + hysteresis
                if (d2 <= r2 || (d2 <= keep2 && s.Known.Contains(e.Id))) _sorted.Add(new KeyValuePair<float, ServerEntity>(d2, e));
            }
            _sorted.Sort(ByDistance);
            int limit = Math.Min(_sorted.Count, cfg.MaxEntitiesPerSnapshot);

            _relevantIds.Clear();
            s.Relevant.Clear();
            NetDataWriter w = _server.Writer;
            for (int i = 0; i < limit; i++)
            {
                ServerEntity e = _sorted[i].Value;
                _relevantIds.Add(e.Id);
                s.Relevant.Add(e);
                if (s.Known.Add(e.Id))
                {
                    w.Reset();
                    new EntitySpawnMsg
                    {
                        Id = e.Id,
                        Type = e.Type,
                        SubType = e.SubType,
                        Name = e.Name,
                        Position = e.Move.Position,
                        Yaw = e.Yaw,
                        Flags = e.Move.Flags,
                        Health = NetCodec.EncodePercent(e.Stats.Health),
                    }.Write(w);
                    Send(s, w, DeliveryMethod.ReliableOrdered);
                }
            }

            _toRemove.Clear();
            foreach (uint id in s.Known)
            {
                if (!_relevantIds.Contains(id)) _toRemove.Add(id);
            }
            for (int i = 0; i < _toRemove.Count; i++)
            {
                s.Known.Remove(_toRemove[i]);
                w.Reset();
                new EntityDespawnMsg { Id = _toRemove[i], Reason = DespawnReason.OutOfRange }.Write(w);
                Send(s, w, DeliveryMethod.ReliableOrdered);
            }
        }

        private void SendSnapshot(PlayerSession s, uint tick)
        {
            ServerEntity self = s.Entity;
            uint snapIndex = tick / (uint)_server.Config.SnapshotInterval;

            _due.Clear();
            for (int i = 0; i < s.Relevant.Count; i++)
            {
                ServerEntity e = s.Relevant[i];
                float d = SMath.DistanceXZ(e.Move.Position, self.Move.Position);
                uint every = d < NearDistance ? 1u : d < MidDistance ? 2u : 4u;
                if ((snapIndex + e.Id) % every == 0) _due.Add(e);
            }

            var header = new SnapshotHeader
            {
                Tick = tick,
                AckInputSequence = s.LastProcessedInput,
                TimeOfDay01 = _server.TimeOfDay01,
                Weather = 0,
                Own = self.Move,
                Stats = self.Stats,
            };

            NetDataWriter w = _server.Writer;
            int index = 0;
            do
            {
                int count = Math.Min(MaxEntriesPerPacket, _due.Count - index);
                w.Reset();
                header.Write(w);
                w.PutVarUInt((uint)count);
                for (int i = 0; i < count; i++)
                {
                    ServerEntity e = _due[index + i];
                    new EntityState
                    {
                        Id = e.Id,
                        Position = e.Move.Position,
                        Yaw = e.Yaw,
                        Flags = e.Move.Flags,
                        Health = NetCodec.EncodePercent(e.Stats.Health),
                    }.Write(w);
                }
                index += count;
                Send(s, w, DeliveryMethod.Unreliable);
            }
            while (index < _due.Count);
        }

        /// <summary>Tells every client that knows the entity that it is gone.</summary>
        public void OnEntityRemoved(ServerEntity e, DespawnReason reason)
        {
            IReadOnlyList<PlayerSession> sessions = _server.Sessions;
            NetDataWriter w = _server.Writer;
            for (int i = 0; i < sessions.Count; i++)
            {
                PlayerSession s = sessions[i];
                if (!s.Known.Remove(e.Id)) continue;
                s.Relevant.Remove(e);
                if (!s.IsConnected) continue;
                w.Reset();
                new EntityDespawnMsg { Id = e.Id, Reason = reason }.Write(w);
                Send(s, w, DeliveryMethod.ReliableOrdered);
            }
        }

        private static void Send(PlayerSession s, NetDataWriter w, DeliveryMethod method)
        {
            byte channel = method == DeliveryMethod.Unreliable ? NetProtocol.ChannelUnreliable : NetProtocol.ChannelReliable;
            s.Peer.Send(w, channel, method);
            s.BytesSent += w.Length;
        }
    }
}
