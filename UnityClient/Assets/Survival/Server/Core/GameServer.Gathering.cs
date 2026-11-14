using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using Survival.Core;
using Survival.Gameplay;
using Survival.Networking;
using Survival.World;

namespace Survival.Server
{
    /// <summary>Resource gathering, inventory, crafting and consumables (all server-authoritative).</summary>
    public sealed partial class GameServer
    {
        private struct NodeState
        {
            public float Health;
            /// <summary>Server time when the node is restored (depleted) or forgotten (damaged).</summary>
            public double RestoreAt;
        }

        private readonly Dictionary<uint, NodeState> _nodeStates = new Dictionary<uint, NodeState>(256);
        private readonly List<uint> _nodeScratch = new List<uint>(64);
        private double _lastNodeSweep;

        public int DepletedNodeCount
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<uint, NodeState> kv in _nodeStates) if (kv.Value.Health <= 0f) n++;
                return n;
            }
        }

        public bool IsNodeAlive(uint id) => !_nodeStates.TryGetValue(id, out NodeState st) || st.Health > 0f;

        private void InitGathering()
        {
            World.IsNodeAlive = IsNodeAlive;
        }

        /// <summary>Restores depleted nodes whose timer ran out (called every tick, sweeps once a second).</summary>
        private void TickNodes(double now)
        {
            if (now - _lastNodeSweep < 1.0 || _nodeStates.Count == 0) return;
            _lastNodeSweep = now;
            _nodeScratch.Clear();
            int respawned = 0;
            foreach (KeyValuePair<uint, NodeState> kv in _nodeStates)
            {
                if (now < kv.Value.RestoreAt) continue;
                // Do not regrow a tree on top of somebody standing in its place
                if (kv.Value.Health <= 0f)
                {
                    if (World.Nodes.TryGetNode(kv.Key, out WorldNode n) && n.Radius > 0f &&
                        AnyPlayerWithin(new System.Numerics.Vector3(n.X, n.Y, n.Z), n.Radius + 1.2f, _queryScratch)) continue;
                    _nodeScratch.Insert(respawned++, kv.Key);
                }
                else
                {
                    _nodeScratch.Add(kv.Key);
                }
            }
            if (_nodeScratch.Count == 0) return;
            for (int i = 0; i < _nodeScratch.Count; i++) _nodeStates.Remove(_nodeScratch[i]);
            if (respawned > 0)
            {
                _writer.Reset();
                NodeStateMsg.Write(_writer, false, false, _nodeScratch, 0, Math.Min(respawned, NodeStateMsg.MaxPerMessage));
                SendToAllReady(_writer, DeliveryMethod.ReliableOrdered);
            }
        }

        /// <summary>Sends everything a freshly loaded client needs: inventory and the depleted node set.</summary>
        private void SendInitialState(PlayerSession s)
        {
            SendInventory(s);
            _nodeScratch.Clear();
            foreach (KeyValuePair<uint, NodeState> kv in _nodeStates) if (kv.Value.Health <= 0f) _nodeScratch.Add(kv.Key);
            int sent = 0;
            do
            {
                int count = Math.Min(NodeStateMsg.MaxPerMessage, _nodeScratch.Count - sent);
                _writer.Reset();
                NodeStateMsg.Write(_writer, sent == 0, true, _nodeScratch, sent, count);
                s.Peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
                sent += count;
            }
            while (sent < _nodeScratch.Count);
        }

        private void SendInventory(PlayerSession s)
        {
            if (s?.Entity?.Items == null || !s.IsConnected) return;
            _writer.Reset();
            InventoryMsg.Write(_writer, s.Entity.Items);
            s.Peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }

        private static bool CanAct(PlayerSession s) => s.Ready && s.Entity != null && !s.Entity.Removed && !s.Entity.IsDead && s.Entity.Items != null;

        private void HandleHit(PlayerSession s, uint nodeId)
        {
            if (!CanAct(s)) return;
            double now = Time;
            if (now - s.LastSwingAt < GatherRules.SwingSeconds * 0.8) return; // swing rate cannot be cheated
            s.LastSwingAt = now;

            ServerEntity e = s.Entity;
            if (!World.Nodes.TryGetNode(nodeId, out WorldNode node) || !GatherRules.CanGather(node.Kind)) return;
            float dx = node.X - e.Move.Position.X, dz = node.Z - e.Move.Position.Z;
            float reach = GatherRules.Reach + node.Radius + 0.6f;
            if (dx * dx + dz * dz > reach * reach || Math.Abs(node.Y - e.Move.Position.Y) > 5f) return;

            float max = GatherRules.MaxHealth(node);
            float health = _nodeStates.TryGetValue(nodeId, out NodeState st) ? st.Health : max;
            if (health <= 0f) return;

            int slot = Math.Min((int)s.LastInput.HotbarSlot, Inventory.HotbarSlots - 1);
            ItemDef tool = ItemDb.ToolOf(e.Items.Slots[slot]);
            float power = GatherRules.HitPower(tool, node.Kind);
            if (power <= 0f) return;

            float damage = Math.Min(power, health);
            health -= damage;
            bool finished = health <= 0.01f;
            if (finished) health = 0f;

            GatherRules.Yield(node, damage, finished, out ItemId main, out int mainCount, out ItemId extra, out int extraCount);
            int lost = e.Items.Add(main, mainCount);
            if (extra != ItemId.None && extraCount > 0) lost += e.Items.Add(extra, extraCount);

            _nodeStates[nodeId] = new NodeState { Health = health, RestoreAt = now + (finished ? Config.NodeRespawnSeconds : 600.0) };

            _writer.Reset();
            new HitResultMsg
            {
                NodeId = nodeId,
                Health = finished ? (byte)0 : (byte)Math.Max(1, (int)(health / max * 255f)),
                Item = main,
                Count = (ushort)mainCount,
                Extra = extraCount > 0 ? extra : ItemId.None,
                ExtraCount = (ushort)extraCount,
                Full = lost > 0,
            }.Write(_writer);
            s.Peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
            SendInventory(s);

            if (finished)
            {
                _nodeScratch.Clear();
                _nodeScratch.Add(nodeId);
                _writer.Reset();
                NodeStateMsg.Write(_writer, false, true, _nodeScratch, 0, 1);
                SendToAllReady(_writer, DeliveryMethod.ReliableOrdered);
            }
        }

        private void HandleCraft(PlayerSession s, byte recipeId)
        {
            if (!CanAct(s)) return;
            Recipe r = ItemDb.FindRecipe(recipeId);
            if (r == null) return;
            if (s.Entity.Items.Craft(r)) SendInventory(s);
            else SendNotice(s, NoticeKind.Warning, "craft_failed", 2f);
        }

        private void HandleUse(PlayerSession s, byte slot)
        {
            if (!CanAct(s) || slot >= Inventory.Size) return;
            Inventory inv = s.Entity.Items;
            ItemDef def = ItemDb.Get(inv.Slots[slot].Id);
            if (def == null || inv.Slots[slot].IsEmpty) return;
            if (def.Kind != ItemKind.Food && def.Kind != ItemKind.Medical) return;
            double now = Time;
            if (now - s.LastUseAt < 0.4) return;
            s.LastUseAt = now;
            if (!inv.RemoveAt(slot, 1)) return;
            SurvivalRules.Eat(ref s.Entity.Stats, def.Food, def.Water, def.Heal);
            if (def.StopBleed > 0f) s.Entity.Stats.Bleeding = 0f;
            SendInventory(s);
        }

        private void HandleMove(PlayerSession s, byte from, byte to)
        {
            if (!CanAct(s)) return;
            if (s.Entity.Items.Move(from, to)) SendInventory(s);
        }

        private void HandleDrink(PlayerSession s)
        {
            if (!CanAct(s)) return;
            double now = Time;
            if (now - s.LastUseAt < 0.8) return;
            ServerEntity e = s.Entity;
            if (e.Move.Position.Y > World.WaterLevel + 0.45f) return; // must stand in water
            s.LastUseAt = now;
            SurvivalRules.Eat(ref e.Stats, 0f, 22f, 0f);
        }

        private void SendNotice(PlayerSession s, NoticeKind kind, string text, float duration)
        {
            if (!s.IsConnected) return;
            _writer.Reset();
            new NoticeMsg { Kind = kind, Text = text, Duration = duration }.Write(_writer);
            s.Peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }
    }
}
