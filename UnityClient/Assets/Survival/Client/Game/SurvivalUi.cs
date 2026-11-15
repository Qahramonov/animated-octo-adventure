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

    /// <summary>One inventory cell: item picture, stack count, selection frame.</summary>
    public sealed class SlotView
    {
        public RectTransform Root;
        private Image _bg, _icon, _frame;
        private Text _code, _count;
        private Sprite _tile;
        private float _size;

        public static SlotView Create(Transform parent, string name, float size, Action onClick, string corner = null)
        {
            var v = new SlotView { _size = size };
            v._bg = UIFactory.Image(parent, name, Theme.SlotEmpty, UISprites.Rounded(12));
            v._bg.raycastTarget = onClick != null;
            v.Root = v._bg.rectTransform;
            v.Root.sizeDelta = new Vector2(size, size);
            Image line = UIFactory.Image(v.Root, "Line", Theme.Line, UISprites.Rounded(12, 2));
            UIFactory.Stretch(line.rectTransform);
            v._frame = UIFactory.Image(v.Root, "Frame", Theme.Accent, UISprites.Rounded(12, 3));
            UIFactory.Stretch(v._frame.rectTransform);
            v._icon = UIFactory.Image(v.Root, "Icon", Color.white, UISprites.Rounded(18));
            UIFactory.Place(v._icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(size * 0.56f, size * 0.56f), new Vector2(0, size * 0.03f));
            v._code = UIFactory.Label(v._icon.transform, "Code", "", Mathf.RoundToInt(size * 0.24f), new Color(0.05f, 0.05f, 0.07f), TextAnchor.MiddleCenter, UIFactory.Bold);
            UIFactory.Stretch(v._code.rectTransform);
            v._count = UIFactory.Label(v.Root, "Count", "", Mathf.RoundToInt(Mathf.Max(16f, size * 0.19f)), Theme.Text, TextAnchor.LowerRight, UIFactory.Bold);
            UIFactory.Stretch(v._count.rectTransform, 7);
            UIFactory.AddShadow(v._count, 0.9f, 1.5f);
            if (corner != null)
            {
                Text c = UIFactory.Label(v.Root, "Corner", corner, Mathf.RoundToInt(Mathf.Max(13f, size * 0.15f)), Theme.TextDim, TextAnchor.UpperLeft);
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

        public void Set(ItemStack s, bool selected, bool dimmed = false)
        {
            bool has = !s.IsEmpty;
            _icon.enabled = has;
            _code.enabled = has;
            _frame.enabled = selected;
            _bg.color = has ? Theme.Slot : Theme.SlotEmpty;
            if (!has)
            {
                _count.text = string.Empty;
                return;
            }
            float alpha = dimmed ? 0.28f : 1f;
            // Real picture of the item when it could be rendered, otherwise a coloured tile with letters
            Sprite pic = Survival.Client.Rendering.IconBaker.Get(s.Id);
            if (pic != null)
            {
                if (_tile == null) _tile = _icon.sprite;
                _icon.sprite = pic;
                _icon.color = new Color(1, 1, 1, alpha);
                _icon.rectTransform.sizeDelta = new Vector2(_size, _size) * 0.84f;
                _code.enabled = false;
            }
            else
            {
                if (_tile != null) _icon.sprite = _tile;
                _icon.rectTransform.sizeDelta = new Vector2(_size, _size) * 0.56f;
                _icon.color = ItemVisuals.ColorOf(s.Id).WithAlpha(alpha);
                _code.text = ItemVisuals.Code(s.Id);
            }
            _count.text = s.Count > 1 ? s.Count.ToString() : string.Empty;
            _count.color = Theme.Text.WithAlpha(alpha);
        }
    }

    /// <summary>
    /// Survival HUD layer: hotbar, gather feed, target info, context prompts and the full-screen
    /// inventory / crafting screen. Purely presentational: every action is a request to the server
    /// through the session, and the screen redraws from the server's inventory.
    /// </summary>
    public sealed class SurvivalUi : MonoBehaviour
    {
        private const float HotSize = 96f;
        private const float BagSize = 118f;
        private const int Cols = 6;

        private static readonly string[] FilterKeys = { "cat_all", "cat_tools", "cat_resources", "cat_food", "cat_medical", "cat_build" };
        private static readonly ItemKind[] FilterKinds = { ItemKind.Tool, ItemKind.Tool, ItemKind.Resource, ItemKind.Food, ItemKind.Medical, ItemKind.Placeable };

        private Session _session;
        private readonly SlotView[] _hot = new SlotView[Inventory.HotbarSlots];
        private readonly RectTransform[] _hotRoots = new RectTransform[Inventory.HotbarSlots];
        private readonly SlotView[] _bag = new SlotView[Inventory.Size];
        private readonly List<KeyValuePair<Text, float>> _feed = new List<KeyValuePair<Text, float>>();
        private RectTransform _feedRoot;
        private GameObject _target;
        private Text _targetName;
        private Image _targetBar;
        private AnimatedButton _drink, _cook;

        // full-screen panel
        private GameObject _panel;
        private CanvasGroup _panelGroup;
        private RectTransform _panelBody;
        private float _panelAnim;
        private GameObject _invPage, _craftPage;
        private AnimatedButton _tabInv, _tabCraft;
        private bool _craftTab;
        private readonly List<AnimatedButton> _filterButtons = new List<AnimatedButton>();
        private int _filter;
        private int _bagSelected = -1;
        // item details
        private SlotView _detailIcon;
        private Text _detailName, _detailKind, _detailDesc, _detailStats;
        private AnimatedButton _useButton, _splitButton, _dropButton;
        private float _dropConfirm;
        // crafting
        private readonly List<AnimatedButton> _craftFilterButtons = new List<AnimatedButton>();
        private int _craftFilter;
        private RectTransform _recipeList;
        private readonly List<KeyValuePair<Recipe, GameObject>> _recipeRows = new List<KeyValuePair<Recipe, GameObject>>();
        private readonly Dictionary<byte, Image> _recipeDots = new Dictionary<byte, Image>();
        private readonly Dictionary<byte, Image> _recipeFrames = new Dictionary<byte, Image>();
        private Recipe _recipe;
        private SlotView _recipeIcon;
        private Text _recipeName, _recipeDesc, _recipeState;
        private readonly List<SlotView> _costIcons = new List<SlotView>();
        private readonly List<Text> _costTexts = new List<Text>();
        private AnimatedButton _craftButton;

        public int SelectedHotbar { get; private set; }
        public bool InventoryOpen => _panel != null && _panel.activeSelf;
        public Action DrinkRequested;
        public Action CookRequested;

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

        // ------------------------------------------------------------------ HUD

        private void Build(RectTransform root, Transform canvas)
        {
            // Hotbar (bottom centre) on a glass strip
            float width = Inventory.HotbarSlots * HotSize + (Inventory.HotbarSlots - 1) * 8f;
            var bar = UIFactory.Rect(root, "Hotbar");
            UIFactory.Place(bar, new Vector2(0.5f, 0), new Vector2(width + 20, HotSize + 20), new Vector2(0, 14), new Vector2(0.5f, 0));
            Image strip = UIFactory.Image(bar, "Strip", Theme.GlassSoft, UISprites.Rounded(18));
            UIFactory.Stretch(strip.rectTransform);
            for (int i = 0; i < Inventory.HotbarSlots; i++)
            {
                int slot = i;
                _hot[i] = SlotView.Create(bar, "Hot" + i, HotSize, () => OnHotbarTap(slot), (i + 1).ToString());
                _hotRoots[i] = _hot[i].Root;
                UIFactory.Place(_hot[i].Root, new Vector2(0, 0), new Vector2(HotSize, HotSize), new Vector2(10 + i * (HotSize + 8f), 10), new Vector2(0, 0));
            }

            // Gather feed (left, above the joystick area)
            _feedRoot = UIFactory.Rect(root, "Feed");
            UIFactory.Place(_feedRoot, new Vector2(0, 0.5f), new Vector2(520, 260), new Vector2(34, 70), new Vector2(0, 0.5f));

            // Target info (under the crosshair)
            var target = UIFactory.Rect(canvas, "Target");
            UIFactory.Place(target, new Vector2(0.5f, 0.5f), new Vector2(360, 56), new Vector2(0, -64));
            _targetName = UIFactory.Label(target, "Name", "", 22, Theme.Text, TextAnchor.UpperCenter, UIFactory.Bold);
            UIFactory.Place(_targetName.rectTransform, new Vector2(0.5f, 1), new Vector2(360, 32), Vector2.zero, new Vector2(0.5f, 1));
            UIFactory.AddShadow(_targetName, 0.85f, 2f);
            _targetBar = UIFactory.Bar(target, "Bar", Theme.Accent, new Vector2(180, 8));
            UIFactory.Place((RectTransform)_targetBar.transform.parent, new Vector2(0.5f, 0), new Vector2(180, 8), new Vector2(0, 8), new Vector2(0.5f, 0));
            _target = target.gameObject;
            _target.SetActive(false);

            // Context prompts above the hotbar
            _drink = UIFactory.Button(root, "Drink", Loc.T("drink"), () => DrinkRequested?.Invoke(), 26, true, new Vector2(220, 68));
            UIFactory.Place(_drink.Background.rectTransform, new Vector2(0.5f, 0), new Vector2(220, 68), new Vector2(0, HotSize + 54), new Vector2(0.5f, 0));
            _drink.gameObject.SetActive(false);
            _cook = UIFactory.Button(root, "Cook", Loc.T("cook"), () => CookRequested?.Invoke(), 26, true, new Vector2(220, 68));
            UIFactory.Place(_cook.Background.rectTransform, new Vector2(0.5f, 0), new Vector2(220, 68), new Vector2(0, HotSize + 134), new Vector2(0.5f, 0));
            _cook.gameObject.SetActive(false);

            BuildPanel(canvas);
        }

        // ------------------------------------------------------------------ full-screen panel

        private static Image Card(Transform parent, string name)
        {
            Image card = UIFactory.Image(parent, name, Theme.PanelSolid, UISprites.Rounded(18));
            Image line = UIFactory.Image(card.transform, "Line", Theme.Line, UISprites.Rounded(18, 2));
            UIFactory.Stretch(line.rectTransform);
            return card;
        }

        private void BuildPanel(Transform canvas)
        {
            Image dim = UIFactory.Image(canvas, "InventoryPanel", Theme.Backdrop);
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            _panelGroup = dim.gameObject.AddComponent<CanvasGroup>();
            var safe = UIFactory.Rect(dim.transform, "Safe");
            UIFactory.Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();
            _panelBody = UIFactory.Rect(safe, "Body");
            UIFactory.Stretch(_panelBody);

            // Header: two tabs + close
            _tabInv = UIFactory.Button(_panelBody, "TabInv", Loc.T("inventory"), () => ShowTab(false), 28, true, new Vector2(300, 70));
            UIFactory.Place(_tabInv.Background.rectTransform, new Vector2(0, 1), new Vector2(300, 70), new Vector2(48, -26), new Vector2(0, 1));
            _tabCraft = UIFactory.Button(_panelBody, "TabCraft", Loc.T("crafting"), () => ShowTab(true), 28, false, new Vector2(300, 70));
            UIFactory.Place(_tabCraft.Background.rectTransform, new Vector2(0, 1), new Vector2(300, 70), new Vector2(360, -26), new Vector2(0, 1));
            AnimatedButton close = UIFactory.Button(_panelBody, "Close", string.Empty, () => SetInventoryOpen(false), 30, false, new Vector2(70, 70));
            UIFactory.Place(close.Background.rectTransform, new Vector2(1, 1), new Vector2(70, 70), new Vector2(-48, -26), new Vector2(1, 1));
            Image x = UIFactory.Image(close.transform, "X", Theme.Text, UISprites.Icon("close", 64));
            UIFactory.Place(x.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(30, 30));
            x.raycastTarget = false;

            _invPage = UIFactory.Rect(_panelBody, "Inventory").gameObject;
            UIFactory.Stretch((RectTransform)_invPage.transform);
            _craftPage = UIFactory.Rect(_panelBody, "Crafting").gameObject;
            UIFactory.Stretch((RectTransform)_craftPage.transform);
            BuildInventoryPage((RectTransform)_invPage.transform);
            BuildCraftPage((RectTransform)_craftPage.transform);

            _panel = dim.gameObject;
            _panel.SetActive(false);
        }

        private void BuildInventoryPage(RectTransform page)
        {
            float step = BagSize + 8f;
            float gridW = Cols * step - 8f;

            // Category chips
            var chips = UIFactory.Rect(page, "Filters");
            UIFactory.Place(chips, new Vector2(0, 1), new Vector2(gridW, 52), new Vector2(48, -118), new Vector2(0, 1));
            float chipW = (gridW - (FilterKeys.Length - 1) * 6f) / FilterKeys.Length;
            for (int i = 0; i < FilterKeys.Length; i++)
            {
                int idx = i;
                AnimatedButton b = UIFactory.Button(chips, "F" + i, Loc.T(FilterKeys[i]), () =>
                {
                    _filter = idx;
                    App.Audio.PlayUi(UiSound.Tick);
                    Refresh();
                }, 17, false, new Vector2(chipW, 48));
                UIFactory.Place(b.Background.rectTransform, new Vector2(0, 0.5f), new Vector2(chipW, 48), new Vector2(i * (chipW + 6f), 0), new Vector2(0, 0.5f));
                _filterButtons.Add(b);
            }

            // Backpack (3 rows) and, separated below, the hotbar row
            Image card = Card(page, "GridCard");
            UIFactory.Place(card.rectTransform, new Vector2(0, 1), new Vector2(gridW + 32, 4 * step + 86), new Vector2(32, -180), new Vector2(0, 1));
            Text bagLabel = UIFactory.Label(card.transform, "BagLabel", Loc.T("backpack"), 17, Theme.TextDim, TextAnchor.MiddleLeft, UIFactory.Bold);
            UIFactory.Place(bagLabel.rectTransform, new Vector2(0, 1), new Vector2(300, 26), new Vector2(18, -8), new Vector2(0, 1));
            Text hotLabel = UIFactory.Label(card.transform, "HotLabel", Loc.T("hotbar"), 17, Theme.TextDim, TextAnchor.MiddleLeft, UIFactory.Bold);
            UIFactory.Place(hotLabel.rectTransform, new Vector2(0, 1), new Vector2(300, 26), new Vector2(18, -(40 + 3 * step)), new Vector2(0, 1));
            for (int i = 0; i < Inventory.Size; i++)
            {
                int slot = i;
                bool hot = i < Inventory.HotbarSlots;
                int row = hot ? 3 : (i - Inventory.HotbarSlots) / Cols;
                int col = hot ? i : (i - Inventory.HotbarSlots) % Cols;
                _bag[i] = SlotView.Create(card.transform, "Slot" + i, BagSize, () => OnBagTap(slot), hot ? (i + 1).ToString() : null);
                float y = -38 - row * step - (hot ? 34f : 0f);
                UIFactory.Place(_bag[i].Root, new Vector2(0, 1), new Vector2(BagSize, BagSize), new Vector2(16 + col * step, y), new Vector2(0, 1));
            }

            // Details card (right)
            Image info = Card(page, "Details");
            info.rectTransform.anchorMin = new Vector2(0, 0);
            info.rectTransform.anchorMax = new Vector2(1, 1);
            info.rectTransform.offsetMin = new Vector2(gridW + 96, 40);
            info.rectTransform.offsetMax = new Vector2(-48, -118);
            _detailIcon = SlotView.Create(info.transform, "Icon", 190, null);
            UIFactory.Place(_detailIcon.Root, new Vector2(0, 1), new Vector2(190, 190), new Vector2(28, -28), new Vector2(0, 1));
            _detailName = UIFactory.Label(info.transform, "Name", "", 40, Theme.Text, TextAnchor.UpperLeft, UIFactory.Display);
            Anchor(_detailName.rectTransform, 244, -30, -28, 56);
            _detailKind = UIFactory.Label(info.transform, "Kind", "", 22, Theme.Accent, TextAnchor.UpperLeft, UIFactory.Bold);
            Anchor(_detailKind.rectTransform, 244, -88, -28, 32);
            _detailStats = UIFactory.Label(info.transform, "Stats", "", 23, Theme.Text, TextAnchor.UpperLeft);
            Anchor(_detailStats.rectTransform, 244, -126, -28, 100);
            _detailStats.supportRichText = true;
            _detailDesc = UIFactory.Label(info.transform, "Desc", "", 25, Theme.TextDim, TextAnchor.UpperLeft);
            Anchor(_detailDesc.rectTransform, 28, -244, -28, 230);
            _detailDesc.horizontalOverflow = HorizontalWrapMode.Wrap;

            _useButton = UIFactory.Button(info.transform, "Use", Loc.T("use"), UseSelected, 26, true, new Vector2(250, 74));
            UIFactory.Place(_useButton.Background.rectTransform, new Vector2(0, 0), new Vector2(250, 74), new Vector2(28, 28), new Vector2(0, 0));
            _splitButton = UIFactory.Button(info.transform, "Split", Loc.T("split"), SplitSelected, 26, false, new Vector2(220, 74));
            UIFactory.Place(_splitButton.Background.rectTransform, new Vector2(0, 0), new Vector2(220, 74), new Vector2(292, 28), new Vector2(0, 0));
            _dropButton = UIFactory.Button(info.transform, "Drop", Loc.T("drop"), DropSelected, 26, false, new Vector2(220, 74));
            UIFactory.Place(_dropButton.Background.rectTransform, new Vector2(1, 0), new Vector2(220, 74), new Vector2(-28, 28), new Vector2(1, 0));
        }

        /// <summary>Stretches a rect horizontally inside its parent with left/right insets, at a top offset.</summary>
        private static void Anchor(RectTransform rt, float left, float top, float right, float height)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0, 1);
            rt.offsetMin = new Vector2(left, top - height);
            rt.offsetMax = new Vector2(right, top);
        }

        private void BuildCraftPage(RectTransform page)
        {
            const float listW = 760f;
            var chips = UIFactory.Rect(page, "Filters");
            UIFactory.Place(chips, new Vector2(0, 1), new Vector2(listW, 52), new Vector2(48, -118), new Vector2(0, 1));
            int[] cats = { 0, 1, 4, 5 };
            float chipW = (listW - (cats.Length - 1) * 6f) / cats.Length;
            for (int i = 0; i < cats.Length; i++)
            {
                int cat = cats[i];
                AnimatedButton b = UIFactory.Button(chips, "F" + i, Loc.T(FilterKeys[cat]), () =>
                {
                    _craftFilter = cat;
                    App.Audio.PlayUi(UiSound.Tick);
                    LayoutRecipes();
                    Refresh();
                }, 18, false, new Vector2(chipW, 48));
                UIFactory.Place(b.Background.rectTransform, new Vector2(0, 0.5f), new Vector2(chipW, 48), new Vector2(i * (chipW + 6f), 0), new Vector2(0, 0.5f));
                _craftFilterButtons.Add(b);
            }

            Image listCard = Card(page, "Recipes");
            listCard.rectTransform.anchorMin = new Vector2(0, 0);
            listCard.rectTransform.anchorMax = new Vector2(0, 1);
            listCard.rectTransform.pivot = new Vector2(0, 0.5f);
            listCard.rectTransform.offsetMin = new Vector2(32, 40);
            listCard.rectTransform.offsetMax = new Vector2(32 + listW + 32, -180);
            _recipeList = UIFactory.Rect(listCard.transform, "List");
            UIFactory.Stretch(_recipeList, 14);
            foreach (Recipe r in ItemDb.Recipes)
            {
                Recipe recipe = r;
                Image row = UIFactory.Image(_recipeList, "Row" + r.Id, Theme.Slot, UISprites.Rounded(12));
                row.raycastTarget = true;
                row.rectTransform.anchorMin = new Vector2(0, 1);
                row.rectTransform.anchorMax = new Vector2(1, 1);
                row.rectTransform.pivot = new Vector2(0.5f, 1);
                row.rectTransform.sizeDelta = new Vector2(0, 88);
                Image frame = UIFactory.Image(row.transform, "Frame", Theme.Accent, UISprites.Rounded(12, 3));
                UIFactory.Stretch(frame.rectTransform);
                _recipeFrames[r.Id] = frame;
                SlotView icon = SlotView.Create(row.transform, "Icon", 72, null);
                UIFactory.Place(icon.Root, new Vector2(0, 0.5f), new Vector2(72, 72), new Vector2(8, 0), new Vector2(0, 0.5f));
                icon.Set(new ItemStack { Id = r.Result, Count = r.Count }, false);
                Text name = UIFactory.Label(row.transform, "Name", ItemVisuals.Name(r.Result), 26, Theme.Text, TextAnchor.MiddleLeft, UIFactory.Bold);
                Anchor(name.rectTransform, 96, -22, -60, 44);
                Image dot = UIFactory.Image(row.transform, "Dot", Theme.Good, UISprites.Circle(32));
                UIFactory.Place(dot.rectTransform, new Vector2(1, 0.5f), new Vector2(18, 18), new Vector2(-24, 0), new Vector2(1, 0.5f));
                _recipeDots[r.Id] = dot;
                var btn = row.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() =>
                {
                    _recipe = recipe;
                    App.Audio.PlayUi(UiSound.Tick);
                    Refresh();
                });
                _recipeRows.Add(new KeyValuePair<Recipe, GameObject>(r, row.gameObject));
            }
            _recipe = ItemDb.Recipes.Length > 0 ? ItemDb.Recipes[0] : null;

            // Recipe details (right)
            Image info = Card(page, "RecipeDetails");
            info.rectTransform.anchorMin = new Vector2(0, 0);
            info.rectTransform.anchorMax = new Vector2(1, 1);
            info.rectTransform.offsetMin = new Vector2(listW + 96, 40);
            info.rectTransform.offsetMax = new Vector2(-48, -118);
            _recipeIcon = SlotView.Create(info.transform, "Icon", 190, null);
            UIFactory.Place(_recipeIcon.Root, new Vector2(0, 1), new Vector2(190, 190), new Vector2(28, -28), new Vector2(0, 1));
            _recipeName = UIFactory.Label(info.transform, "Name", "", 40, Theme.Text, TextAnchor.UpperLeft, UIFactory.Display);
            Anchor(_recipeName.rectTransform, 244, -30, -28, 56);
            _recipeState = UIFactory.Label(info.transform, "State", "", 22, Theme.Good, TextAnchor.UpperLeft, UIFactory.Bold);
            Anchor(_recipeState.rectTransform, 244, -88, -28, 32);
            _recipeDesc = UIFactory.Label(info.transform, "Desc", "", 24, Theme.TextDim, TextAnchor.UpperLeft);
            Anchor(_recipeDesc.rectTransform, 244, -126, -28, 100);
            _recipeDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
            Text need = UIFactory.Label(info.transform, "Need", Loc.T("materials"), 19, Theme.TextDim, TextAnchor.MiddleLeft, UIFactory.Bold);
            Anchor(need.rectTransform, 28, -240, -28, 30);
            for (int i = 0; i < 4; i++)
            {
                SlotView c = SlotView.Create(info.transform, "Cost" + i, 76, null);
                UIFactory.Place(c.Root, new Vector2(0, 1), new Vector2(76, 76), new Vector2(28, -280 - i * 86), new Vector2(0, 1));
                Text t = UIFactory.Label(info.transform, "CostText" + i, "", 26, Theme.Text, TextAnchor.MiddleLeft, UIFactory.Bold);
                Anchor(t.rectTransform, 122, -280 - i * 86 - 14, -28, 48);
                t.supportRichText = true;
                _costIcons.Add(c);
                _costTexts.Add(t);
            }
            _craftButton = UIFactory.Button(info.transform, "Craft", Loc.T("craft"), () =>
            {
                if (_recipe != null) _session.SendCraft(_recipe.Id);
                App.Audio.PlayUi(UiSound.Confirm);
            }, 30, true, new Vector2(320, 84));
            UIFactory.Place(_craftButton.Background.rectTransform, new Vector2(1, 0), new Vector2(320, 84), new Vector2(-28, 28), new Vector2(1, 0));
            LayoutRecipes();
        }

        private static bool RecipeInCategory(Recipe r, int cat)
        {
            if (cat == 0) return true;
            ItemDef d = ItemDb.Get(r.Result);
            return d != null && d.Kind == FilterKinds[cat];
        }

        private void LayoutRecipes()
        {
            float y = 0f;
            bool selectedVisible = false;
            Recipe first = null;
            foreach (KeyValuePair<Recipe, GameObject> kv in _recipeRows)
            {
                bool show = RecipeInCategory(kv.Key, _craftFilter);
                kv.Value.SetActive(show);
                if (!show) continue;
                if (first == null) first = kv.Key;
                if (kv.Key == _recipe) selectedVisible = true;
                ((RectTransform)kv.Value.transform).anchoredPosition = new Vector2(0, y);
                y -= 96f;
            }
            if (!selectedVisible) _recipe = first;
        }

        private void ShowTab(bool craft)
        {
            _craftTab = craft;
            App.Audio.PlayUi(UiSound.Tick);
            Refresh();
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
            _dropConfirm = 0f;
            if (_bagSelected < 0)
            {
                if (!inv.Slots[slot].IsEmpty) _bagSelected = slot;
            }
            else if (_bagSelected == slot)
            {
                _bagSelected = -1;
            }
            else if (inv.Slots[slot].IsEmpty || inv.Slots[slot].Id == inv.Slots[_bagSelected].Id)
            {
                // Tapping an empty cell (or the same kind of item) moves / merges the selected stack
                _session.SendMoveItem(_bagSelected, slot);
                _bagSelected = slot;
            }
            else
            {
                _bagSelected = slot; // another item: just select it (use two taps on an empty cell to swap places)
            }
            Refresh();
        }

        private void UseSelected()
        {
            Inventory inv = _session.Inventory;
            if (inv == null || _bagSelected < 0 || !IsConsumable(inv.Slots[_bagSelected])) return;
            _session.SendUseItem(_bagSelected);
            App.Audio.Play(SfxSound.Eat, 0.7f);
        }

        private void SplitSelected()
        {
            if (_bagSelected < 0) return;
            _session.SendSplit(_bagSelected);
            App.Audio.PlayUi(UiSound.Tick);
        }

        private void DropSelected()
        {
            if (_bagSelected < 0) return;
            if (_dropConfirm <= 0f)
            {
                // First tap arms the button, the second one within 3 s discards the stack
                _dropConfirm = 3f;
                _dropButton.SetText(Loc.T("drop_confirm"));
                App.Audio.PlayUi(UiSound.Error);
                return;
            }
            _dropConfirm = 0f;
            _session.SendDrop(_bagSelected);
            _bagSelected = -1;
            App.Audio.PlayUi(UiSound.Back);
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
            _dropConfirm = 0f;
            _panelAnim = 0f;
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

        private static string KindName(ItemKind k)
        {
            switch (k)
            {
                case ItemKind.Tool: return Loc.T("cat_tools");
                case ItemKind.Food: return Loc.T("cat_food");
                case ItemKind.Medical: return Loc.T("cat_medical");
                case ItemKind.Placeable: return Loc.T("cat_build");
                default: return Loc.T("cat_resources");
            }
        }

        private static string Stats(ItemDef d)
        {
            var sb = new System.Text.StringBuilder(96);
            void Line(string key, string value) => sb.Append("<color=#9AA3AD>").Append(Loc.T(key)).Append("</color>  ").Append(value).Append('\n');
            if (d.Kind == ItemKind.Tool)
            {
                Line("stat_damage", d.Damage.ToString("0"));
                Line("stat_wood", "x" + d.TreePower.ToString("0.0"));
                Line("stat_ore", "x" + d.OrePower.ToString("0.0"));
            }
            if (d.Food != 0f) Line("stat_food", "+" + d.Food.ToString("0"));
            if (d.Water != 0f) Line("stat_water", "+" + d.Water.ToString("0"));
            if (d.Heal != 0f) Line("stat_health", (d.Heal > 0f ? "+" : string.Empty) + d.Heal.ToString("0"));
            if (d.MaxStack > 1) Line("stat_stack", d.MaxStack.ToString());
            return sb.ToString();
        }

        public void Refresh()
        {
            Inventory inv = _session?.Inventory;
            if (inv == null || _hot[0] == null) return;
            for (int i = 0; i < Inventory.HotbarSlots; i++) _hot[i].Set(inv.Slots[i], i == SelectedHotbar);
            if (!InventoryOpen) return;

            _invPage.SetActive(!_craftTab);
            _craftPage.SetActive(_craftTab);
            _tabInv.SetColor(_craftTab ? Theme.Button : Theme.Accent);
            _tabInv.Label.color = _craftTab ? Theme.Text : new Color(0.1f, 0.06f, 0.02f);
            _tabCraft.SetColor(_craftTab ? Theme.Accent : Theme.Button);
            _tabCraft.Label.color = _craftTab ? new Color(0.1f, 0.06f, 0.02f) : Theme.Text;

            if (!_craftTab)
            {
                for (int i = 0; i < _filterButtons.Count; i++)
                {
                    _filterButtons[i].SetColor(i == _filter ? Theme.Accent.WithAlpha(0.9f) : Theme.Button);
                    _filterButtons[i].Label.color = i == _filter ? new Color(0.1f, 0.06f, 0.02f) : Theme.TextDim;
                }
                if (_bagSelected >= 0 && inv.Slots[_bagSelected].IsEmpty) _bagSelected = -1;
                for (int i = 0; i < Inventory.Size; i++)
                {
                    ItemDef def = inv.Slots[i].IsEmpty ? null : ItemDb.Get(inv.Slots[i].Id);
                    bool dim = _filter != 0 && def != null && def.Kind != FilterKinds[_filter];
                    _bag[i].Set(inv.Slots[i], i == _bagSelected, dim);
                }
                ItemStack sel = _bagSelected >= 0 ? inv.Slots[_bagSelected] : default;
                ItemDef d = sel.IsEmpty ? null : ItemDb.Get(sel.Id);
                _detailIcon.Set(sel, false);
                _detailName.text = d != null ? ItemVisuals.Name(sel.Id) : Loc.T("inv_empty_title");
                _detailKind.text = d != null ? KindName(d.Kind).ToUpperInvariant() + "   x" + sel.Count : string.Empty;
                _detailStats.text = d != null ? Stats(d) : string.Empty;
                _detailDesc.text = d != null ? Loc.T("desc_" + d.Key) : Loc.T("inv_hint");
                _useButton.gameObject.SetActive(IsConsumable(sel));
                _splitButton.gameObject.SetActive(d != null && sel.Count > 1);
                _dropButton.gameObject.SetActive(d != null);
                if (_dropConfirm <= 0f) _dropButton.SetText(Loc.T("drop"));
                return;
            }

            for (int i = 0; i < _craftFilterButtons.Count; i++)
            {
                int[] cats = { 0, 1, 4, 5 };
                bool on = cats[i] == _craftFilter;
                _craftFilterButtons[i].SetColor(on ? Theme.Accent.WithAlpha(0.9f) : Theme.Button);
                _craftFilterButtons[i].Label.color = on ? new Color(0.1f, 0.06f, 0.02f) : Theme.TextDim;
            }
            foreach (KeyValuePair<Recipe, GameObject> kv in _recipeRows)
            {
                bool can = inv.CanCraft(kv.Key);
                _recipeDots[kv.Key.Id].color = can ? Theme.Good : Theme.Neutral.WithAlpha(0.5f);
                _recipeFrames[kv.Key.Id].enabled = kv.Key == _recipe;
            }
            bool has = _recipe != null;
            _recipeIcon.Set(has ? new ItemStack { Id = _recipe.Result, Count = _recipe.Count } : default, false);
            _recipeName.text = has ? ItemVisuals.Name(_recipe.Result) : string.Empty;
            _recipeDesc.text = has ? Loc.T("desc_" + ItemDb.Get(_recipe.Result).Key) : string.Empty;
            bool craftable = has && inv.CanCraft(_recipe);
            _recipeState.text = !has ? string.Empty : craftable ? Loc.T("craft_ready") : Loc.T("craft_missing");
            _recipeState.color = craftable ? Theme.Good : Theme.Bad;
            _craftButton.Interactable = craftable;
            for (int i = 0; i < _costIcons.Count; i++)
            {
                bool used = has && i < _recipe.Cost.Length;
                _costIcons[i].Root.gameObject.SetActive(used);
                _costTexts[i].gameObject.SetActive(used);
                if (!used) continue;
                Ingredient c = _recipe.Cost[i];
                int have = inv.CountOf(c.Id);
                _costIcons[i].Set(new ItemStack { Id = c.Id, Count = 1 }, false);
                _costTexts[i].text = $"{ItemVisuals.Name(c.Id)}   <color={(have >= c.Count ? "#8FE08F" : "#FF7A70")}>{have} / {c.Count}</color>";
            }
        }

        public void ShowGain(ItemId id, int count)
        {
            if (id == ItemId.None || count <= 0) return;
            AddFeed($"+{count}  {ItemVisuals.Name(id)}", ItemVisuals.ColorOf(id));
        }

        public void ShowMessage(string text, Color color) => AddFeed(text, color);

        private void AddFeed(string text, Color color)
        {
            Text t = UIFactory.Label(_feedRoot, "Feed", text, 26, color, TextAnchor.MiddleLeft, UIFactory.Bold);
            UIFactory.AddShadow(t, 0.85f, 2f);
            UIFactory.Place(t.rectTransform, new Vector2(0, 0), new Vector2(520, 36), new Vector2(-30, 0), new Vector2(0, 0));
            _feed.Add(new KeyValuePair<Text, float>(t, 2.6f));
            if (_feed.Count > 6)
            {
                Destroy(_feed[0].Key.gameObject);
                _feed.RemoveAt(0);
            }
        }

        public void SetCookVisible(bool visible)
        {
            if (_cook.gameObject.activeSelf != visible) _cook.gameObject.SetActive(visible);
        }

        public void SetTarget(bool visible, string name, float health01)
        {
            if (_target.activeSelf != visible) _target.SetActive(visible);
            if (!visible) return;
            _targetName.text = name;
            _targetBar.fillAmount = Mathf.Clamp01(health01);
        }

        public void SetDrinkVisible(bool visible)
        {
            if (_drink.gameObject.activeSelf != visible) _drink.gameObject.SetActive(visible);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // Gain feed: newest at the bottom, slides in from the left, fades out
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
            for (int i = 0; i < _feed.Count; i++)
            {
                RectTransform rt = _feed[i].Key.rectTransform;
                float y = (_feed.Count - 1 - i) * 38f;
                float k = 1f - Mathf.Exp(-dt * 14f);
                rt.anchoredPosition = new Vector2(Mathf.Lerp(rt.anchoredPosition.x, 0f, k), Mathf.Lerp(rt.anchoredPosition.y, y, k));
            }

            // Selected hotbar slot rises slightly
            for (int i = 0; i < _hotRoots.Length; i++)
            {
                if (_hotRoots[i] == null) continue;
                Vector2 p = _hotRoots[i].anchoredPosition;
                p.y = Mathf.Lerp(p.y, i == SelectedHotbar ? 20f : 10f, 1f - Mathf.Exp(-dt * 16f));
                _hotRoots[i].anchoredPosition = p;
            }

            // Panel open animation: fade + slight rise
            if (InventoryOpen)
            {
                _panelAnim = Mathf.Min(1f, _panelAnim + dt / 0.16f);
                float e = 1f - (1f - _panelAnim) * (1f - _panelAnim);
                _panelGroup.alpha = e;
                _panelBody.anchoredPosition = new Vector2(0, (1f - e) * -28f);
                if (_dropConfirm > 0f)
                {
                    _dropConfirm -= dt;
                    if (_dropConfirm <= 0f) _dropButton.SetText(Loc.T("drop"));
                }
            }
        }
    }
}
