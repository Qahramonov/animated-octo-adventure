using System;
using Survival.World;

namespace Survival.Gameplay
{
    public enum ItemId : byte
    {
        None = 0,
        // resources
        Wood = 1,
        Stone = 2,
        MetalOre = 3,
        SulfurOre = 4,
        Fiber = 5,
        Berries = 6,
        // tools
        Rock = 10,
        StoneHatchet = 11,
        StonePickaxe = 12,
        Spear = 13,
        // consumables
        Bandage = 20,
    }

    public enum ItemKind : byte
    {
        Resource,
        Tool,
        Food,
        Medical,
    }

    public sealed class ItemDef
    {
        public ItemId Id;
        public string Key;
        public ItemKind Kind;
        public int MaxStack;
        /// <summary>Gather multiplier against trees / ore nodes (0 = cannot gather that).</summary>
        public float TreePower;
        public float OrePower;
        public float Damage;
        public float Food;
        public float Water;
        public float Heal;
        public float StopBleed;
    }

    public struct ItemStack
    {
        public ItemId Id;
        public ushort Count;
        public bool IsEmpty => Id == ItemId.None || Count == 0;
    }

    public struct Ingredient
    {
        public ItemId Id;
        public ushort Count;
        public Ingredient(ItemId id, int count)
        {
            Id = id;
            Count = (ushort)count;
        }
    }

    public sealed class Recipe
    {
        public byte Id;
        public ItemId Result;
        public ushort Count;
        public Ingredient[] Cost;
    }

    /// <summary>Static item and recipe tables shared by the server (rules) and the client (UI).</summary>
    public static class ItemDb
    {
        private static readonly ItemDef[] Defs = new ItemDef[32];
        public static readonly Recipe[] Recipes;

        /// <summary>Bare hands: slow gathering of anything, no item required.</summary>
        public static readonly ItemDef Hands = new ItemDef { Id = ItemId.None, Key = "hands", Kind = ItemKind.Tool, MaxStack = 1, TreePower = 0.35f, OrePower = 0.2f, Damage = 4f };

        static ItemDb()
        {
            Add(new ItemDef { Id = ItemId.Wood, Key = "wood", Kind = ItemKind.Resource, MaxStack = 1000 });
            Add(new ItemDef { Id = ItemId.Stone, Key = "stone", Kind = ItemKind.Resource, MaxStack = 1000 });
            Add(new ItemDef { Id = ItemId.MetalOre, Key = "metal_ore", Kind = ItemKind.Resource, MaxStack = 1000 });
            Add(new ItemDef { Id = ItemId.SulfurOre, Key = "sulfur_ore", Kind = ItemKind.Resource, MaxStack = 1000 });
            Add(new ItemDef { Id = ItemId.Fiber, Key = "fiber", Kind = ItemKind.Resource, MaxStack = 500 });
            Add(new ItemDef { Id = ItemId.Berries, Key = "berries", Kind = ItemKind.Food, MaxStack = 50, Food = 7f, Water = 5f, Heal = 1f });
            Add(new ItemDef { Id = ItemId.Rock, Key = "rock", Kind = ItemKind.Tool, MaxStack = 1, TreePower = 1f, OrePower = 1f, Damage = 10f });
            Add(new ItemDef { Id = ItemId.StoneHatchet, Key = "stone_hatchet", Kind = ItemKind.Tool, MaxStack = 1, TreePower = 2.6f, OrePower = 0.5f, Damage = 18f });
            Add(new ItemDef { Id = ItemId.StonePickaxe, Key = "stone_pickaxe", Kind = ItemKind.Tool, MaxStack = 1, TreePower = 0.5f, OrePower = 2.6f, Damage = 16f });
            Add(new ItemDef { Id = ItemId.Spear, Key = "spear", Kind = ItemKind.Tool, MaxStack = 1, TreePower = 0.3f, OrePower = 0.2f, Damage = 30f });
            Add(new ItemDef { Id = ItemId.Bandage, Key = "bandage", Kind = ItemKind.Medical, MaxStack = 10, Heal = 18f, StopBleed = 1f });

            Recipes = new[]
            {
                new Recipe { Id = 1, Result = ItemId.StoneHatchet, Count = 1, Cost = new[] { new Ingredient(ItemId.Wood, 60), new Ingredient(ItemId.Stone, 30), new Ingredient(ItemId.Fiber, 10) } },
                new Recipe { Id = 2, Result = ItemId.StonePickaxe, Count = 1, Cost = new[] { new Ingredient(ItemId.Wood, 60), new Ingredient(ItemId.Stone, 40), new Ingredient(ItemId.Fiber, 10) } },
                new Recipe { Id = 3, Result = ItemId.Spear, Count = 1, Cost = new[] { new Ingredient(ItemId.Wood, 90), new Ingredient(ItemId.Stone, 15) } },
                new Recipe { Id = 4, Result = ItemId.Bandage, Count = 1, Cost = new[] { new Ingredient(ItemId.Fiber, 12) } },
                new Recipe { Id = 5, Result = ItemId.Rock, Count = 1, Cost = new[] { new Ingredient(ItemId.Stone, 10) } },
            };
        }

        private static void Add(ItemDef d) => Defs[(int)d.Id] = d;

        public static ItemDef Get(ItemId id)
        {
            int i = (int)id;
            return i > 0 && i < Defs.Length ? Defs[i] : null;
        }

        /// <summary>Definition of the tool in a stack; empty or non-tool stacks act as bare hands.</summary>
        public static ItemDef ToolOf(ItemStack s)
        {
            if (s.IsEmpty) return Hands;
            ItemDef d = Get(s.Id);
            return d != null && d.Kind == ItemKind.Tool ? d : Hands;
        }

        public static Recipe FindRecipe(byte id)
        {
            for (int i = 0; i < Recipes.Length; i++) if (Recipes[i].Id == id) return Recipes[i];
            return null;
        }
    }

    /// <summary>Fixed-size inventory; the first <see cref="HotbarSlots"/> slots are the hotbar.</summary>
    public sealed class Inventory
    {
        public const int HotbarSlots = 6;
        public const int Size = 24;

        public readonly ItemStack[] Slots = new ItemStack[Size];

        public int CountOf(ItemId id)
        {
            int n = 0;
            for (int i = 0; i < Size; i++) if (Slots[i].Id == id) n += Slots[i].Count;
            return n;
        }

        /// <summary>Adds items, filling existing stacks first. Returns how many did not fit.</summary>
        public int Add(ItemId id, int count)
        {
            ItemDef def = ItemDb.Get(id);
            if (def == null || count <= 0) return count;
            for (int i = 0; i < Size && count > 0; i++)
            {
                if (Slots[i].Id != id || Slots[i].Count >= def.MaxStack) continue;
                int take = Math.Min(count, def.MaxStack - Slots[i].Count);
                Slots[i].Count += (ushort)take;
                count -= take;
            }
            // Tools prefer the hotbar, everything else the backpack (falls back to any free slot)
            bool tool = def.Kind == ItemKind.Tool;
            for (int pass = 0; pass < 2 && count > 0; pass++)
            {
                int from = (pass == 0) == tool ? 0 : HotbarSlots;
                int to = (pass == 0) == tool ? HotbarSlots : Size;
                for (int i = from; i < to && count > 0; i++)
                {
                    if (!Slots[i].IsEmpty) continue;
                    int take = Math.Min(count, def.MaxStack);
                    Slots[i] = new ItemStack { Id = id, Count = (ushort)take };
                    count -= take;
                }
            }
            return count;
        }

        public bool Remove(ItemId id, int count)
        {
            if (CountOf(id) < count) return false;
            for (int i = Size - 1; i >= 0 && count > 0; i--)
            {
                if (Slots[i].Id != id) continue;
                int take = Math.Min(count, Slots[i].Count);
                Slots[i].Count -= (ushort)take;
                count -= take;
                if (Slots[i].Count == 0) Slots[i] = default;
            }
            return true;
        }

        public bool RemoveAt(int slot, int count)
        {
            if ((uint)slot >= Size || Slots[slot].IsEmpty || Slots[slot].Count < count) return false;
            Slots[slot].Count -= (ushort)count;
            if (Slots[slot].Count == 0) Slots[slot] = default;
            return true;
        }

        /// <summary>Moves or swaps two slots; identical stackable items are merged.</summary>
        public bool Move(int from, int to)
        {
            if ((uint)from >= Size || (uint)to >= Size || from == to || Slots[from].IsEmpty) return false;
            ItemStack a = Slots[from], b = Slots[to];
            ItemDef def = ItemDb.Get(a.Id);
            if (!b.IsEmpty && b.Id == a.Id && def != null && b.Count < def.MaxStack)
            {
                int take = Math.Min(a.Count, def.MaxStack - b.Count);
                b.Count += (ushort)take;
                a.Count -= (ushort)take;
                Slots[to] = b;
                Slots[from] = a.Count == 0 ? default : a;
                return true;
            }
            Slots[from] = b.IsEmpty ? default : b;
            Slots[to] = a;
            return true;
        }

        public bool CanCraft(Recipe r)
        {
            if (r == null) return false;
            for (int i = 0; i < r.Cost.Length; i++) if (CountOf(r.Cost[i].Id) < r.Cost[i].Count) return false;
            return true;
        }

        /// <summary>Consumes the ingredients and adds the result; fails without changes if it cannot fit.</summary>
        public bool Craft(Recipe r)
        {
            if (!CanCraft(r)) return false;
            var backup = (ItemStack[])Slots.Clone();
            for (int i = 0; i < r.Cost.Length; i++) Remove(r.Cost[i].Id, r.Cost[i].Count);
            if (Add(r.Result, r.Count) > 0)
            {
                Array.Copy(backup, Slots, Size);
                return false;
            }
            return true;
        }

        public void Clear() => Array.Clear(Slots, 0, Size);

        public void CopyFrom(Inventory other) => Array.Copy(other.Slots, Slots, Size);

        public static Inventory Starter()
        {
            var inv = new Inventory();
            inv.Slots[0] = new ItemStack { Id = ItemId.Rock, Count = 1 };
            inv.Slots[1] = new ItemStack { Id = ItemId.Bandage, Count = 2 };
            return inv;
        }
    }

    /// <summary>Health and yield of harvestable world nodes (trees, ore, bushes).</summary>
    public static class GatherRules
    {
        public const float Reach = 3.2f;
        public const float SwingSeconds = 0.55f;
        public const float RespawnSeconds = 420f;

        public static bool CanGather(NodeKind k) => NodeKinds.IsTree(k) || NodeKinds.IsOre(k) || k == NodeKind.Bush;

        /// <summary>Total "hit points" of a node; one hit removes 10 x tool power.</summary>
        public static float MaxHealth(in WorldNode n)
        {
            switch (n.Kind)
            {
                case NodeKind.TreePine:
                case NodeKind.TreeBroadleaf: return 100f * n.Scale;
                case NodeKind.TreeDead: return 60f * n.Scale;
                case NodeKind.Bush: return 1f;
                default: return 100f * n.Scale;
            }
        }

        public static float HitPower(ItemDef tool, NodeKind k)
        {
            if (k == NodeKind.Bush) return 10f;
            return 10f * (NodeKinds.IsTree(k) ? tool.TreePower : tool.OrePower);
        }

        /// <summary>Items for removing <paramref name="damage"/> health from a node (bonus when it is finished).</summary>
        public static void Yield(in WorldNode n, float damage, bool finished, out ItemId main, out int mainCount, out ItemId extra, out int extraCount)
        {
            extra = ItemId.None;
            extraCount = 0;
            switch (n.Kind)
            {
                case NodeKind.Bush:
                    main = ItemId.Fiber;
                    mainCount = 6 + (int)(n.Scale * 4f);
                    extra = ItemId.Berries;
                    extraCount = 2 + (int)(n.Id % 3);
                    return;
                case NodeKind.StoneNode:
                    main = ItemId.Stone;
                    mainCount = (int)Math.Ceiling(damage * 1.6f) + (finished ? 15 : 0);
                    return;
                case NodeKind.MetalNode:
                    main = ItemId.MetalOre;
                    mainCount = (int)Math.Ceiling(damage * 0.9f) + (finished ? 10 : 0);
                    extra = ItemId.Stone;
                    extraCount = (int)(damage * 0.3f);
                    return;
                case NodeKind.SulfurNode:
                    main = ItemId.SulfurOre;
                    mainCount = (int)Math.Ceiling(damage * 0.8f) + (finished ? 10 : 0);
                    extra = ItemId.Stone;
                    extraCount = (int)(damage * 0.2f);
                    return;
                default:
                    main = ItemId.Wood;
                    mainCount = (int)Math.Ceiling(damage * 1.5f) + (finished ? 20 : 0);
                    return;
            }
        }
    }
}
