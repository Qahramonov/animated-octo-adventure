using System;
using System.Collections.Generic;
using Survival.Client.UI;
using Survival.Gameplay;
using Survival.World;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.Client.Game
{
    /// <summary>Colours, short codes and localized names of items for the procedural icons.</summary>
    public static class ItemVisuals
    {
        public static Color ColorOf(ItemId id)
        {
            switch (id)
            {
                case ItemId.Wood: return new Color(0.62f, 0.42f, 0.22f);
                case ItemId.Stone: return new Color(0.62f, 0.63f, 0.65f);
                case ItemId.MetalOre: return new Color(0.78f, 0.50f, 0.36f);
                case ItemId.SulfurOre: return new Color(0.93f, 0.82f, 0.24f);
                case ItemId.Fiber: return new Color(0.50f, 0.74f, 0.36f);
                case ItemId.Berries: return new Color(0.86f, 0.22f, 0.34f);
                case ItemId.Rock: return new Color(0.50f, 0.52f, 0.55f);
                case ItemId.StoneHatchet: return new Color(0.95f, 0.62f, 0.25f);
                case ItemId.StonePickaxe: return new Color(0.36f, 0.72f, 0.92f);
                case ItemId.Spear: return new Color(0.86f, 0.80f, 0.62f);
                case ItemId.Bandage: return new Color(0.95f, 0.93f, 0.88f);
                default: return Theme.Neutral;
            }
        }

        public static string Name(ItemId id)
        {
            ItemDef d = ItemDb.Get(id);
            return d == null ? string.Empty : Loc.T("item_" + d.Key);
        }

        /// <summary>Two-letter code drawn on the icon (taken from the localized name).</summary>
        public static string Code(ItemId id)
        {
            string n = Name(id);
            if (string.IsNullOrEmpty(n)) return string.Empty;
            int space = n.IndexOf(' ');
            string code = space > 0 && space + 1 < n.Length ? n.Substring(0, 1) + n.Substring(space + 1, 1) : n.Substring(0, Math.Min(2, n.Length));
            return code.ToUpperInvariant();
        }

        public static string NodeName(NodeKind k)
        {
            switch (k)
            {
                case NodeKind.Bush: return Loc.T("node_bush");
                case NodeKind.StoneNode: return Loc.T("node_stone");
                case NodeKind.MetalNode: return Loc.T("node_metal");
                case NodeKind.SulfurNode: return Loc.T("node_sulfur");
                default: return Loc.T("node_tree");
            }
        }
    }

    /// <summary>One inventory cell: procedural icon, stack count, selection frame.</summary>
    public sealed class SlotView
    {
        public RectTransform Root;
        private Image _bg, _icon, _frame;
        private Text _code, _count;

        public static SlotView Create(Transform parent, string name, float size, Action onClick, string corner = null)
        {
            var v = new SlotView();
            v._bg = UIFactory.Image(parent, name, new Color(0.05f, 0.06f, 0.08f, 0.72f), UISprites.Rounded(14));
            v._bg.raycastTarget = true;
            v.Root = v._bg.rectTransform;
            v.Root.sizeDelta = new Vector2(size, size);
            v._frame = UIFactory.Image(v.Root, "Frame", Theme.Accent, UISprites.Rounded(14, 4));
            UIFactory.Stretch(v._frame.rectTransform);
            v._frame.raycastTarget = false;
            v._icon = UIFactory.Image(v.Root, "Icon", Color.white, UISprites.Rounded(18));
            UIFactory.Place(v._icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(size * 0.56f, size * 0.56f), new Vector2(0, size * 0.04f));
            v._icon.raycastTarget = false;
            v._code = UIFactory.Label(v._icon.transform, "Code", "", Mathf.RoundToInt(size * 0.24f), new Color(0.05f, 0.05f, 0.07f), TextAnchor.MiddleCenter, UIFactory.Bold);
            UIFactory.Stretch(v._code.rectTransform);
            v._count = UIFactory.Label(v.Root, "Count", "", Mathf.RoundToInt(size * 0.2f), Theme.Text, TextAnchor.LowerRight, UIFactory.Bold);
            UIFactory.Stretch(v._count.rectTransform, 6);
            UIFactory.AddShadow(v._count, 0.9f, 1.5f);
            if (corner != null)
            {
                Text c = UIFactory.Label(v.Root, "Corner", corner, Mathf.RoundToInt(size * 0.17f), Theme.TextDim, TextAnchor.UpperLeft);
                UIFactory.Stretch(c.rectTransform, 7);
            }
            if (onClick != null)
            {
                var b = v._bg.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => onClick());
            }
            v.Set(default, false);
            return v;
        }

        public void Set(ItemStack s, bool selected)
        {
            bool has = !s.IsEmpty;
            _icon.enabled = has;
            _code.enabled = has;
            _frame.enabled = selected;
            if (has)
            {
                _icon.color = ItemVisuals.ColorOf(s.Id);
                _code.text = ItemVisuals.Code(s.Id);
                _count.text = s.Count > 1 ? s.Count.ToString() : string.Empty;
            }
            else
            {
                _count.text = string.Empty;
            }
        }
    }

    /// <summary>
    /// Survival HUD layer: hotbar, gather feed, target info, drink prompt and the inventory/crafting
    /// screen. Purely presentational: every action is a request to the server through the session.
    /// </summary>
    public sealed class SurvivalUi : MonoBehaviour
    {
        private const float HotSize = 104f;
        private const float BagSize = 112f;

        private Session _session;
        private readonly SlotView[] _hot = new SlotView[Inventory.HotbarSlots];
        private readonly SlotView[] _bag = new SlotView[Inventory.Size];
        private readonly List<KeyValuePair<Text, float>> _feed = new List<KeyValuePair<Text, float>>();
        private RectTransform _feedRoot;
        private GameObject _target;
        private Text _targetName;
        private Image _targetBar;
        private AnimatedButton _drink;
        private GameObject _panel;
        private Text _selName;
        private AnimatedButton _useButton;
        private readonly List<KeyValuePair<Recipe, AnimatedButton>> _recipes = new List<KeyValuePair<Recipe, AnimatedButton>>();
        private readonly List<Text> _recipeCosts = new List<Text>();
        private int _bagSelected = -1;

        public int SelectedHotbar { get; private set; }
        public bool InventoryOpen => _panel != null && _panel.activeSelf;
        public Action DrinkRequested;

        public static SurvivalUi Create(RectTransform safe, Transform canvas, Session session)
        {
            var go = new GameObject("SurvivalUi", typeof(RectTransform));
            go.transform.SetParent(safe, false);
            UIFactory.Stretch((RectTransform)go.transform);
            var ui = go.AddComponent<SurvivalUi>();
            ui._session = session;
            ui.Build((RectTransform)go.transform, canvas);
            session.InventoryChanged += ui.Refresh;
            ui.Refresh();
            return ui;
        }

        private void OnDestroy()
        {
            if (_session != null) _session.InventoryChanged -= Refresh;
        }

        private void Build(RectTransform root, Transform canvas)
        {
            // Hotbar (bottom centre)
            float width = Inventory.HotbarSlots * HotSize + (Inventory.HotbarSlots - 1) * 10f;
            var bar = UIFactory.Rect(root, "Hotbar");
            UIFactory.Place(bar, new Vector2(0.5f, 0), new Vector2(width, HotSize), new Vector2(0, 18), new Vector2(0.5f, 0));
            for (int i = 0; i < Inventory.HotbarSlots; i++)
            {
                int slot = i;
                _hot[i] = SlotView.Create(bar, "Hot" + i, HotSize, () => OnHotbarTap(slot), (i + 1).ToString());
                UIFactory.Place(_hot[i].Root, new Vector2(0, 0), new Vector2(HotSize, HotSize), new Vector2(i * (HotSize + 10f), 0), new Vector2(0, 0));
            }

            // Gather feed (left, above the joystick area)
            _feedRoot = UIFactory.Rect(root, "Feed");
            UIFactory.Place(_feedRoot, new Vector2(0, 0.5f), new Vector2(520, 260), new Vector2(34, 60), new Vector2(0, 0.5f));

            // Target info (under the crosshair)
            var target = UIFactory.Rect(canvas, "Target");
            UIFactory.Place(target, new Vector2(0.5f, 0.5f), new Vector2(360, 64), new Vector2(0, -70));
            _targetName = UIFactory.Label(target, "Name", "", 26, Theme.Text, TextAnchor.UpperCenter, UIFactory.Bold);
            UIFactory.Place(_targetName.rectTransform, new Vector2(0.5f, 1), new Vector2(360, 36), Vector2.zero, new Vector2(0.5f, 1));
            UIFactory.AddShadow(_targetName, 0.85f, 2f);
            _targetBar = UIFactory.Bar(target, "Bar", Theme.Accent, new Vector2(220, 10));
            UIFactory.Place((RectTransform)_targetBar.transform.parent, new Vector2(0.5f, 0), new Vector2(220, 10), new Vector2(0, 8), new Vector2(0.5f, 0));
            _target = target.gameObject;
            _target.SetActive(false);

            // Drink prompt (appears while standing in water)
            _drink = UIFactory.Button(root, "Drink", Loc.T("drink"), () => DrinkRequested?.Invoke(), 28, true, new Vector2(240, 76));
            UIFactory.Place(_drink.Background.rectTransform, new Vector2(0.5f, 0), new Vector2(240, 76), new Vector2(0, HotSize + 40), new Vector2(0.5f, 0));
            _drink.gameObject.SetActive(false);

            BuildPanel(canvas);
        }

        private void BuildPanel(Transform canvas)
        {
            Image dim = UIFactory.Image(canvas, "InventoryPanel", new Color(0.02f, 0.025f, 0.035f, 0.93f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var safe = UIFactory.Rect(dim.transform, "Safe");
            UIFactory.Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();

            Text title = UIFactory.Label(safe, "Title", Loc.T("inventory"), 44, Theme.Text, TextAnchor.MiddleLeft, UIFactory.Display);
            UIFactory.Place(title.rectTransform, new Vector2(0, 1), new Vector2(600, 70), new Vector2(60, -34), new Vector2(0, 1));
            AnimatedButton close = UIFactory.Button(safe, "Close", "X", () => SetInventoryOpen(false), 34, false, new Vector2(90, 80));
            UIFactory.Place(close.Background.rectTransform, new Vector2(1, 1), new Vector2(90, 80), new Vector2(-40, -30), new Vector2(1, 1));

            // Backpack grid: hotbar row on top, 3 backpack rows below
            const int cols = 6;
            float step = BagSize + 10f;
            var grid = UIFactory.Rect(safe, "Grid");
            UIFactory.Place(grid, new Vector2(0, 1), new Vector2(cols * step, 4 * step + 30), new Vector2(60, -130), new Vector2(0, 1));
            for (int i = 0; i < Inventory.Size; i++)
            {
                int slot = i;
                int row = i / cols, col = i % cols;
                _bag[i] = SlotView.Create(grid, "Slot" + i, BagSize, () => OnBagTap(slot), i < Inventory.HotbarSlots ? (i + 1).ToString() : null);
                float y = -row * step - (row > 0 ? 30f : 0f);
                UIFactory.Place(_bag[i].Root, new Vector2(0, 1), new Vector2(BagSize, BagSize), new Vector2(col * step, y), new Vector2(0, 1));
            }
            _selName = UIFactory.Label(safe, "Selected", "", 28, Theme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(_selName.rectTransform, new Vector2(0, 0), new Vector2(520, 60), new Vector2(60, 46), new Vector2(0, 0));
            _useButton = UIFactory.Button(safe, "Use", Loc.T("use"), UseSelected, 28, true, new Vector2(200, 72));
            UIFactory.Place(_useButton.Background.rectTransform, new Vector2(0, 0), new Vector2(200, 72), new Vector2(60 + cols * step - 200, 40), new Vector2(0, 0));

            // Crafting list (right)
            Image craft = UIFactory.Panel(safe, "Craft");
            craft.rectTransform.anchorMin = new Vector2(1, 0);
            craft.rectTransform.anchorMax = new Vector2(1, 1);
            craft.rectTransform.pivot = new Vector2(1, 0.5f);
            craft.rectTransform.offsetMin = new Vector2(-860, 40);
            craft.rectTransform.offsetMax = new Vector2(-40, -130);
            Text ct = UIFactory.Label(craft.transform, "Title", Loc.T("crafting"), 34, Theme.Accent, TextAnchor.MiddleLeft, UIFactory.Bold);
            UIFactory.Place(ct.rectTransform, new Vector2(0, 1), new Vector2(500, 56), new Vector2(30, -18), new Vector2(0, 1));
            for (int i = 0; i < ItemDb.Recipes.Length; i++)
            {
                Recipe r = ItemDb.Recipes[i];
                float y = -90 - i * 128;
                Image row = UIFactory.Image(craft.transform, "Row" + i, new Color(1, 1, 1, 0.05f), UISprites.Rounded(14));
                UIFactory.Place(row.rectTransform, new Vector2(0, 1), new Vector2(760, 116), new Vector2(30, y), new Vector2(0, 1));
                SlotView icon = SlotView.Create(row.transform, "Icon", 92, null);
                UIFactory.Place(icon.Root, new Vector2(0, 0.5f), new Vector2(92, 92), new Vector2(12, 0), new Vector2(0, 0.5f));
                icon.Set(new ItemStack { Id = r.Result, Count = r.Count }, false);
                Text name = UIFactory.Label(row.transform, "Name", ItemVisuals.Name(r.Result), 28, Theme.Text, TextAnchor.UpperLeft, UIFactory.Bold);
                UIFactory.Place(name.rectTransform, new Vector2(0, 1), new Vector2(420, 40), new Vector2(120, -14), new Vector2(0, 1));
                Text cost = UIFactory.Label(row.transform, "Cost", "", 22, Theme.TextDim, TextAnchor.UpperLeft);
                UIFactory.Place(cost.rectTransform, new Vector2(0, 1), new Vector2(440, 56), new Vector2(120, -54), new Vector2(0, 1));
                cost.supportRichText = true;
                _recipeCosts.Add(cost);
                AnimatedButton make = UIFactory.Button(row.transform, "Make", Loc.T("craft"), () => _session.SendCraft(r.Id), 24, true, new Vector2(190, 72));
                UIFactory.Place(make.Background.rectTransform, new Vector2(1, 0.5f), new Vector2(190, 72), new Vector2(-14, 0), new Vector2(1, 0.5f));
                _recipes.Add(new KeyValuePair<Recipe, AnimatedButton>(r, make));
            }

            _panel = dim.gameObject;
            _panel.SetActive(false);
        }

        // ------------------------------------------------------------------ interaction

        public void SelectHotbar(int slot)
        {
            SelectedHotbar = Mathf.Clamp(slot, 0, Inventory.HotbarSlots - 1);
            Refresh();
        }

        private void OnHotbarTap(int slot)
        {
            Inventory inv = _session.Inventory;
            if (slot == SelectedHotbar && inv != null && IsConsumable(inv.Slots[slot]))
            {
                _session.SendUseItem(slot); // second tap on a selected consumable uses it
                App.Audio.PlayUi(UiSound.Confirm);
                return;
            }
            App.Audio.PlayUi(UiSound.Tick);
            SelectHotbar(slot);
        }

        private void OnBagTap(int slot)
        {
            Inventory inv = _session.Inventory;
            if (inv == null) return;
            App.Audio.PlayUi(UiSound.Tick);
            if (_bagSelected < 0)
            {
                if (!inv.Slots[slot].IsEmpty) _bagSelected = slot;
            }
            else if (_bagSelected == slot)
            {
                _bagSelected = -1;
            }
            else
            {
                _session.SendMoveItem(_bagSelected, slot);
                _bagSelected = -1;
            }
            Refresh();
        }

        private void UseSelected()
        {
            Inventory inv = _session.Inventory;
            if (inv == null || _bagSelected < 0 || !IsConsumable(inv.Slots[_bagSelected])) return;
            _session.SendUseItem(_bagSelected);
            App.Audio.PlayUi(UiSound.Confirm);
        }

        private static bool IsConsumable(ItemStack s)
        {
            ItemDef d = s.IsEmpty ? null : ItemDb.Get(s.Id);
            return d != null && (d.Kind == ItemKind.Food || d.Kind == ItemKind.Medical);
        }

        public void SetInventoryOpen(bool open)
        {
            if (_panel.activeSelf == open) return;
            _panel.SetActive(open);
            _bagSelected = -1;
            App.Audio.PlayUi(open ? UiSound.Whoosh : UiSound.Back);
            Refresh();
        }

        /// <summary>Use the consumable in the selected hotbar slot (keyboard / gamepad shortcut).</summary>
        public void UseHotbarItem()
        {
            Inventory inv = _session.Inventory;
            if (inv != null && IsConsumable(inv.Slots[SelectedHotbar])) _session.SendUseItem(SelectedHotbar);
        }

        // ------------------------------------------------------------------ display

        public void Refresh()
        {
            Inventory inv = _session?.Inventory;
            if (inv == null || _hot[0] == null) return;
            for (int i = 0; i < Inventory.HotbarSlots; i++) _hot[i].Set(inv.Slots[i], i == SelectedHotbar);
            if (!InventoryOpen) return;
            if (_bagSelected >= 0 && inv.Slots[_bagSelected].IsEmpty) _bagSelected = -1;
            for (int i = 0; i < Inventory.Size; i++) _bag[i].Set(inv.Slots[i], i == _bagSelected);
            ItemStack sel = _bagSelected >= 0 ? inv.Slots[_bagSelected] : default;
            _selName.text = sel.IsEmpty ? Loc.T("inv_hint") : $"{ItemVisuals.Name(sel.Id)}  x{sel.Count}";
            _useButton.gameObject.SetActive(IsConsumable(sel));
            for (int i = 0; i < _recipes.Count; i++)
            {
                Recipe r = _recipes[i].Key;
                _recipes[i].Value.Interactable = inv.CanCraft(r);
                var sb = new System.Text.StringBuilder(96);
                for (int k = 0; k < r.Cost.Length; k++)
                {
                    int have = inv.CountOf(r.Cost[k].Id);
                    bool ok = have >= r.Cost[k].Count;
                    if (k > 0) sb.Append("   ");
                    sb.Append(ok ? "<color=#8FE08F>" : "<color=#FF7A70>").Append(have).Append('/').Append(r.Cost[k].Count).Append("</color> ")
                        .Append(ItemVisuals.Name(r.Cost[k].Id));
                }
                _recipeCosts[i].text = sb.ToString();
            }
        }

        public void ShowGain(ItemId id, int count)
        {
            if (id == ItemId.None || count <= 0) return;
            Text t = UIFactory.Label(_feedRoot, "Gain", $"+{count}  {ItemVisuals.Name(id)}", 30, ItemVisuals.ColorOf(id), TextAnchor.MiddleLeft, UIFactory.Bold);
            UIFactory.AddShadow(t, 0.85f, 2f);
            UIFactory.Place(t.rectTransform, new Vector2(0, 0), new Vector2(520, 40), Vector2.zero, new Vector2(0, 0));
            _feed.Add(new KeyValuePair<Text, float>(t, 2.6f));
            if (_feed.Count > 6)
            {
                Destroy(_feed[0].Key.gameObject);
                _feed.RemoveAt(0);
            }
        }

        public void ShowMessage(string text, Color color)
        {
            Text t = UIFactory.Label(_feedRoot, "Msg", text, 28, color, TextAnchor.MiddleLeft, UIFactory.Bold);
            UIFactory.AddShadow(t, 0.85f, 2f);
            UIFactory.Place(t.rectTransform, new Vector2(0, 0), new Vector2(520, 40), Vector2.zero, new Vector2(0, 0));
            _feed.Add(new KeyValuePair<Text, float>(t, 2.6f));
        }

        public void SetTarget(bool visible, NodeKind kind, float health01)
        {
            if (_target.activeSelf != visible) _target.SetActive(visible);
            if (!visible) return;
            _targetName.text = ItemVisuals.NodeName(kind);
            _targetBar.fillAmount = Mathf.Clamp01(health01);
        }

        public void SetDrinkVisible(bool visible)
        {
            if (_drink.gameObject.activeSelf != visible) _drink.gameObject.SetActive(visible);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = _feed.Count - 1; i >= 0; i--)
            {
                float left = _feed[i].Value - dt;
                Text t = _feed[i].Key;
                if (left <= 0f)
                {
                    Destroy(t.gameObject);
                    _feed.RemoveAt(i);
                    continue;
                }
                _feed[i] = new KeyValuePair<Text, float>(t, left);
                Color c = t.color;
                c.a = Mathf.Clamp01(left / 0.6f);
                t.color = c;
            }
            // Newest entry at the bottom, older ones stacked above it
            for (int i = 0; i < _feed.Count; i++)
            {
                RectTransform rt = _feed[i].Key.rectTransform;
                float y = (_feed.Count - 1 - i) * 42f;
                rt.anchoredPosition = new Vector2(0, Mathf.Lerp(rt.anchoredPosition.y, y, 1f - Mathf.Exp(-dt * 14f)));
            }
        }
    }
}
