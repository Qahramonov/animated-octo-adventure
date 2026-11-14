using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    public class InventoryTests
    {
        [Fact]
        public void AddStacksAndReportsOverflow()
        {
            var inv = new Inventory();
            Assert.Equal(0, inv.Add(ItemId.Wood, 1500));
            Assert.Equal(1500, inv.CountOf(ItemId.Wood));
            // Resources go to the backpack first, tools to the hotbar
            Assert.True(inv.Slots[0].IsEmpty);
            Assert.Equal(0, inv.Add(ItemId.StoneHatchet, 1));
            Assert.Equal(ItemId.StoneHatchet, inv.Slots[0].Id);

            var full = new Inventory();
            for (int i = 0; i < Inventory.Size; i++) full.Slots[i] = new ItemStack { Id = ItemId.Rock, Count = 1 };
            Assert.Equal(5, full.Add(ItemId.Wood, 5));
        }

        [Fact]
        public void RemoveMoveAndMerge()
        {
            var inv = new Inventory();
            inv.Add(ItemId.Stone, 300);
            Assert.False(inv.Remove(ItemId.Stone, 301));
            Assert.True(inv.Remove(ItemId.Stone, 120));
            Assert.Equal(180, inv.CountOf(ItemId.Stone));

            inv.Clear();
            inv.Slots[6] = new ItemStack { Id = ItemId.Fiber, Count = 400 };
            inv.Slots[7] = new ItemStack { Id = ItemId.Fiber, Count = 200 };
            Assert.True(inv.Move(7, 6)); // merge up to the stack limit (500)
            Assert.Equal(500, inv.Slots[6].Count);
            Assert.Equal(100, inv.Slots[7].Count);
            Assert.True(inv.Move(6, 0)); // move into an empty slot
            Assert.True(inv.Slots[6].IsEmpty);
            Assert.False(inv.Move(6, 3)); // nothing to move
        }

        [Fact]
        public void CraftConsumesIngredientsOnlyOnSuccess()
        {
            Recipe hatchet = ItemDb.FindRecipe(1);
            var inv = new Inventory();
            inv.Add(ItemId.Wood, 59);
            inv.Add(ItemId.Stone, 30);
            inv.Add(ItemId.Fiber, 10);
            Assert.False(inv.Craft(hatchet));
            Assert.Equal(59, inv.CountOf(ItemId.Wood));
            inv.Add(ItemId.Wood, 1);
            Assert.True(inv.Craft(hatchet));
            Assert.Equal(1, inv.CountOf(ItemId.StoneHatchet));
            Assert.Equal(0, inv.CountOf(ItemId.Wood));
            foreach (Recipe r in ItemDb.Recipes)
            {
                Assert.NotNull(ItemDb.Get(r.Result));
                foreach (Ingredient c in r.Cost) Assert.NotNull(ItemDb.Get(c.Id));
            }
        }

        [Fact]
        public void BetterToolsGatherFaster()
        {
            ItemDef rock = ItemDb.Get(ItemId.Rock), hatchet = ItemDb.Get(ItemId.StoneHatchet), pick = ItemDb.Get(ItemId.StonePickaxe);
            Assert.True(GatherRules.HitPower(hatchet, NodeKind.TreePine) > GatherRules.HitPower(rock, NodeKind.TreePine));
            Assert.True(GatherRules.HitPower(pick, NodeKind.MetalNode) > GatherRules.HitPower(rock, NodeKind.MetalNode));
            Assert.True(GatherRules.HitPower(ItemDb.Hands, NodeKind.TreePine) > 0f);
        }

        [Fact]
        public void PlayerRecordKeepsInventoryAndReadsVersion1()
        {
            var rec = new PlayerRecord { PlayerId = "abcdefgh", SecretHash = "h", Nickname = "n" };
            rec.Items.Add(ItemId.MetalOre, 77);
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) rec.Write(w);
            ms.Position = 0;
            PlayerRecord back = PlayerRecord.Read(new BinaryReader(ms));
            Assert.Equal(77, back.Items.CountOf(ItemId.MetalOre));
            Assert.Equal(1, back.Items.CountOf(ItemId.Rock));

            // A version 1 file (no inventory block) still loads and gets the starter kit
            byte[] v2 = ms.ToArray();
            byte[] v1 = new byte[v2.Length - Inventory.Size * 3];
            Array.Copy(v2, v1, v1.Length);
            BitConverter.GetBytes(1).CopyTo(v1, 0);
            PlayerRecord old = PlayerRecord.Read(new BinaryReader(new MemoryStream(v1)));
            Assert.Equal("abcdefgh", old.PlayerId);
            Assert.Equal(1, old.Items.CountOf(ItemId.Rock));
        }
    }

    /// <summary>Gathering and crafting over real UDP, with the same client code the phone runs.</summary>
    public class GatheringIntegrationTests
    {
        private static bool Pump(GameServer server, GameClient client, Func<bool> until, double seconds, Action step = null)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                server.RunTick();
                client.Update(1f / 30f);
                step?.Invoke();
                if (until()) return true;
                Thread.Sleep(5);
            }
            return until();
        }

        private static WorldNode FindNode(GameServer server, Func<NodeKind, bool> match)
        {
            NodeLayout nodes = server.World.Nodes;
            for (int cz = 0; cz < nodes.CellsPerSide; cz++)
            {
                for (int cx = 0; cx < nodes.CellsPerSide; cx++)
                {
                    foreach (WorldNode n in nodes.GetCell(cx, cz)) if (match(n.Kind)) return n;
                }
            }
            throw new InvalidOperationException("no node of that kind on the test island");
        }

        [Fact]
        public void HitTreeUntilFelledThenCraft()
        {
            var cfg = new ServerConfig { Port = 0, BotCount = 0, WorldSize = 2000f, StatusHttpPort = 0, NodeRespawnSeconds = 2f };
            var server = new GameServer(cfg, new MemoryPlayerStore());
            Assert.True(server.Start());
            using var client = new GameClient(new ClientIdentity { PlayerId = "gatherer-0001", Secret = "s", Nickname = "Gatherer" }, "test");
            bool welcome = false, inventory = false;
            var results = new List<HitResultMsg>();
            var depleted = new HashSet<uint>();
            client.WelcomeReceived += w =>
            {
                welcome = true;
                client.SendReady();
            };
            client.InventoryReceived += () => inventory = true;
            client.HitResultReceived += h => results.Add(h);
            client.NodeStateReceived += (reset, dep, ids) =>
            {
                if (reset) depleted.Clear();
                foreach (uint id in ids)
                {
                    if (dep) depleted.Add(id);
                    else depleted.Remove(id);
                }
            };
            try
            {
                client.Connect("127.0.0.1", server.LocalPort);
                Assert.True(Pump(server, client, () => welcome && inventory, 8), "no welcome/inventory");
                Assert.Equal(1, client.Inventory.CountOf(ItemId.Rock)); // starter kit

                WorldNode tree = FindNode(server, NodeKinds.IsTree);
                ServerEntity e = server.Sessions[0].Entity;

                // Too far away: the server ignores the hit
                client.SendHit(tree.Id);
                Pump(server, client, () => results.Count > 0, 0.6);
                Assert.Empty(results);

                e.Move = MoveState.At(new Vector3(tree.X + tree.Radius + 1f, tree.Y, tree.Z));
                var sw = Stopwatch.StartNew();
                double next = 0;
                Assert.True(Pump(server, client, () => depleted.Contains(tree.Id), 25, () =>
                {
                    e.Move = MoveState.At(new Vector3(tree.X + tree.Radius + 1f, tree.Y, tree.Z)); // stay put (no client inputs)
                    if (sw.Elapsed.TotalSeconds >= next)
                    {
                        next = sw.Elapsed.TotalSeconds + 0.5;
                        client.SendHit(tree.Id);
                    }
                }), "tree was never felled");
                Assert.False(server.IsNodeAlive(tree.Id));
                Assert.True(results.Count >= 2);
                Assert.Equal(0, results[results.Count - 1].Health);
                int wood = client.Inventory.CountOf(ItemId.Wood);
                Assert.True(wood >= 100, "expected a decent amount of wood, got " + wood);
                Assert.Equal(wood, e.Items.CountOf(ItemId.Wood)); // client mirror == server truth

                // Hitting a felled tree gives nothing more
                int before = results.Count;
                Pump(server, client, () => false, 0.6);
                client.SendHit(tree.Id);
                Pump(server, client, () => results.Count > before, 0.6);
                Assert.Equal(before, results.Count);

                // Crafting: not enough stone/fiber yet -> refused, inventory unchanged
                client.SendCraft(1);
                Pump(server, client, () => false, 0.3);
                Assert.Equal(0, client.Inventory.CountOf(ItemId.StoneHatchet));
                e.Items.Add(ItemId.Stone, 30);
                e.Items.Add(ItemId.Fiber, 10);
                client.SendCraft(1);
                Assert.True(Pump(server, client, () => client.Inventory.CountOf(ItemId.StoneHatchet) == 1, 3), "hatchet was not crafted");
                Assert.Equal(wood - 60, client.Inventory.CountOf(ItemId.Wood));

                // Eating restores hunger
                e.Items.Add(ItemId.Berries, 3);
                e.Stats.Hunger = 40f;
                int slot = Array.FindIndex(e.Items.Slots, s => s.Id == ItemId.Berries);
                client.SendUseItem(slot);
                Assert.True(Pump(server, client, () => client.Inventory.CountOf(ItemId.Berries) == 2, 3), "berries were not eaten");
                Assert.True(e.Stats.Hunger > 40f);

                // The tree grows back after the respawn time (2 s in this test) once the player has left
                e.Move = MoveState.At(new Vector3(tree.X + 30f, tree.Y, tree.Z));
                Assert.True(Pump(server, client, () => !depleted.Contains(tree.Id), 8, () =>
                    e.Move = MoveState.At(new Vector3(tree.X + 30f, tree.Y, tree.Z))), "tree did not respawn");
                Assert.True(server.IsNodeAlive(tree.Id));
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
