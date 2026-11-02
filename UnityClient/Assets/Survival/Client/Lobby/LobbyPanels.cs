using System;
using System.Collections;
using Survival.Client.UI;
using Survival.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.Client.Lobby
{
    /// <summary>Glass side panel sliding in from the right with a title and BACK button.</summary>
    public abstract class LobbyPanel : MonoBehaviour
    {
        protected LobbyController Lobby;
        protected RectTransform Body;
        protected Image Frame;
        public bool IsOpen { get; private set; }

        protected abstract string Title { get; }
        protected virtual float Width => 1000f;

        public void Init(LobbyController lobby)
        {
            Lobby = lobby;
            var rt = (RectTransform)transform;
            UIFactory.Stretch(rt);
            Frame = UIFactory.Panel(rt, "Frame");
            UIFactory.Place(Frame.rectTransform, new Vector2(1, 0.5f), new Vector2(Width, 900), new Vector2(-40, -20), new Vector2(1, 0.5f));
            Frame.rectTransform.anchorMin = new Vector2(1, 0);
            Frame.rectTransform.anchorMax = new Vector2(1, 1);
            Frame.rectTransform.offsetMin = new Vector2(-Width - 40, 40);
            Frame.rectTransform.offsetMax = new Vector2(-40, -140);
            Text title = UIFactory.Label(Frame.transform, "Title", Title, 46, Theme.Text, TextAnchor.MiddleLeft, UIFactory.Display);
            UIFactory.Place(title.rectTransform, new Vector2(0, 1), new Vector2(Width - 300, 80), new Vector2(40, -20), new Vector2(0, 1));
            AnimatedButton back = UIFactory.Button(Frame.transform, "Back", "‹ " + Loc.T("back"), Close, 28, false, new Vector2(200, 70));
            UIFactory.Place(back.Background.rectTransform, new Vector2(1, 1), new Vector2(200, 70), new Vector2(-30, -25), new Vector2(1, 1));
            Body = UIFactory.Rect(Frame.transform, "Body");
            Body.anchorMin = Vector2.zero;
            Body.anchorMax = Vector2.one;
            Body.offsetMin = new Vector2(40, 30);
            Body.offsetMax = new Vector2(-40, -120);
            Build();
            IsOpen = true;
            StartCoroutine(UIAnim.PopIn(Frame.rectTransform, UIAnim.Group(Frame), new Vector2(120, 0), 0.35f));
        }

        protected abstract void Build();

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            App.Audio.PlayUi(UiSound.Back);
            StartCoroutine(CloseRoutine());
        }

        private IEnumerator CloseRoutine()
        {
            CanvasGroup g = UIAnim.Group(Frame);
            Vector2 start = Frame.rectTransform.anchoredPosition;
            for (float t = 0; t < 0.22f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.22f;
                g.alpha = 1f - k;
                Frame.rectTransform.anchoredPosition = start + new Vector2(120 * k, 0);
                yield return null;
            }
            Lobby.OnPanelClosed(this);
            Destroy(gameObject);
        }

        protected Text Section(Transform parent, string text)
        {
            Text t = UIFactory.Label(parent, "Section", text, 28, Theme.Accent, TextAnchor.LowerLeft, UIFactory.Bold);
            UIFactory.Layout(t, 52);
            return t;
        }

        /// <summary>Label + control row for settings lists.</summary>
        protected RectTransform Row(Transform parent, string label, out RectTransform controlSlot, float height = 76f)
        {
            var row = UIFactory.Rect(parent, label);
            UIFactory.Layout(row, height);
            Text l = UIFactory.Label(row, "Label", label, 28, Theme.Text, TextAnchor.MiddleLeft);
            l.rectTransform.anchorMin = new Vector2(0, 0);
            l.rectTransform.anchorMax = new Vector2(0.45f, 1);
            l.rectTransform.offsetMin = l.rectTransform.offsetMax = Vector2.zero;
            controlSlot = UIFactory.Rect(row, "Control");
            controlSlot.anchorMin = new Vector2(0.45f, 0);
            controlSlot.anchorMax = new Vector2(1, 1);
            controlSlot.offsetMin = controlSlot.offsetMax = Vector2.zero;
            return row;
        }
    }

    // ====================================================================== servers

    public sealed class ServerBrowserPanel : LobbyPanel
    {
        private RectTransform _list;
        private float _autoRefresh;
        private InputField _address;

        protected override string Title => Loc.T("servers");
        protected override float Width => 1080f;

        protected override void Build()
        {
            var top = UIFactory.Rect(Body, "Top");
            top.anchorMin = new Vector2(0, 1);
            top.anchorMax = new Vector2(1, 1);
            top.pivot = new Vector2(0.5f, 1);
            top.sizeDelta = new Vector2(0, 80);
            AnimatedButton refresh = UIFactory.Button(top, "Refresh", Loc.T("refresh"), () => App.Servers.RefreshAll(), 26, false, new Vector2(260, 70));
            UIFactory.Place(refresh.Background.rectTransform, new Vector2(0, 0.5f), new Vector2(260, 70), Vector2.zero, new Vector2(0, 0.5f));
            AnimatedButton lan = UIFactory.Button(top, "Lan", Loc.T("find_lan"), () =>
            {
                App.Servers.DiscoverLan();
                Lobby.Toaster.Show(Loc.T("find_lan") + "...", Theme.Secondary, 2f);
            }, 26, false, new Vector2(330, 70));
            UIFactory.Place(lan.Background.rectTransform, new Vector2(0, 0.5f), new Vector2(330, 70), new Vector2(280, 0), new Vector2(0, 0.5f));

            var listRoot = UIFactory.Rect(Body, "ListRoot");
            listRoot.anchorMin = new Vector2(0, 0);
            listRoot.anchorMax = new Vector2(1, 1);
            listRoot.offsetMin = new Vector2(0, 300);
            listRoot.offsetMax = new Vector2(0, -95);
            _list = UIFactory.ScrollList(listRoot, "List", out _);
            UIFactory.Stretch((RectTransform)_list.parent);

            // Bottom: add server + local modes
            var bottom = UIFactory.Rect(Body, "Bottom");
            bottom.anchorMin = new Vector2(0, 0);
            bottom.anchorMax = new Vector2(1, 0);
            bottom.pivot = new Vector2(0.5f, 0);
            bottom.sizeDelta = new Vector2(0, 290);
            _address = UIFactory.Input(bottom, "Address", "", 28, "IP:port  (e.g. 203.0.113.5:7777)");
            UIFactory.Place(_address.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(640, 70), Vector2.zero, new Vector2(0, 1));
            AnimatedButton add = UIFactory.Button(bottom, "Add", "+ " + Loc.T("add_server"), AddServer, 26, false, new Vector2(330, 70));
            UIFactory.Place(add.Background.rectTransform, new Vector2(1, 1), new Vector2(330, 70), Vector2.zero, new Vector2(1, 1));

            AnimatedButton offline = UIFactory.Button(bottom, "Offline", Loc.T("offline_practice"), () => Lobby.StartLocal(false), 26, false, new Vector2(490, 74));
            UIFactory.Place(offline.Background.rectTransform, new Vector2(0, 0), new Vector2(490, 74), new Vector2(0, 80), new Vector2(0, 0));
            Text offNote = UIFactory.Label(bottom, "OffNote", Loc.T("offline_note"), 20, Theme.TextDim, TextAnchor.UpperLeft);
            UIFactory.Place(offNote.rectTransform, new Vector2(0, 0), new Vector2(490, 70), new Vector2(0, 5), new Vector2(0, 0));
            AnimatedButton host = UIFactory.Button(bottom, "Host", Loc.T("host_lan"), () => Lobby.StartLocal(true), 26, false, new Vector2(490, 74));
            UIFactory.Place(host.Background.rectTransform, new Vector2(1, 0), new Vector2(490, 74), new Vector2(0, 80), new Vector2(1, 0));
            Text hostNote = UIFactory.Label(bottom, "HostNote", Loc.T("lan_note") + "  IP: " + Session.LocalIPv4(), 20, Theme.TextDim, TextAnchor.UpperLeft);
            UIFactory.Place(hostNote.rectTransform, new Vector2(1, 0), new Vector2(490, 70), new Vector2(0, 5), new Vector2(1, 0));

            App.Servers.Changed += Rebuild;
            App.Servers.FetchRemote();
            App.Servers.RefreshAll();
            Rebuild();
        }

        private void OnDestroy() => App.Servers.Changed -= Rebuild;

        private void AddServer()
        {
            ServerEntry e = App.Servers.AddCustom(_address.text);
            if (e == null)
            {
                App.Audio.PlayUi(UiSound.Error);
                return;
            }
            _address.text = string.Empty;
            App.Servers.Refresh(e);
        }

        private void Update()
        {
            _autoRefresh += Time.unscaledDeltaTime;
            if (_autoRefresh > 10f)
            {
                _autoRefresh = 0f;
                App.Servers.RefreshAll();
            }
        }

        private void Rebuild()
        {
            if (_list == null) return;
            UIFactory.Clear(_list);
            foreach (ServerEntry e in App.Servers.Entries) Card(e);
        }

        private void Card(ServerEntry e)
        {
            Image card = UIFactory.Panel(_list, "Card", Theme.PanelLight, 18);
            UIFactory.Layout(card, 150);
            LobbyController.Describe(e, out string status, out Color dot);

            Image d = UIFactory.Image(card.transform, "Dot", dot, UISprites.Circle(32));
            UIFactory.Place(d.rectTransform, new Vector2(0, 0.5f), new Vector2(24, 24), new Vector2(28, 22), new Vector2(0, 0.5f));
            Text name = UIFactory.Label(card.transform, "Name", e.Name, 34, Theme.Text, TextAnchor.MiddleLeft, UIFactory.Bold);
            UIFactory.Place(name.rectTransform, new Vector2(0, 0.5f), new Vector2(560, 50), new Vector2(66, 26), new Vector2(0, 0.5f));
            string region = e.Result != null && e.Result.Status == QueryStatus.Online ? e.Result.Info.Region : e.Region;
            string map = e.Result != null && e.Result.Status == QueryStatus.Online ? e.Result.Info.MapName : e.Map;
            Text meta = UIFactory.Label(card.transform, "Meta", $"{map} · {Loc.T("region")}: {region}", 24, Theme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(meta.rectTransform, new Vector2(0, 0.5f), new Vector2(560, 36), new Vector2(66, -12), new Vector2(0, 0.5f));
            Text st = UIFactory.Label(card.transform, "Status", status, 24, dot, TextAnchor.MiddleLeft);
            UIFactory.Place(st.rectTransform, new Vector2(0, 0.5f), new Vector2(640, 36), new Vector2(66, -46), new Vector2(0, 0.5f));

            if (e.Result != null && e.Result.Status == QueryStatus.Online)
            {
                ServerInfo i = e.Result.Info;
                Image bar = UIFactory.Bar(card.transform, "Load", e.Result.IsFull ? Theme.Warn : Theme.Secondary, new Vector2(220, 16));
                UIFactory.Place(((RectTransform)bar.transform.parent), new Vector2(1, 0.5f), new Vector2(220, 16), new Vector2(-260, 30), new Vector2(1, 0.5f));
                bar.fillAmount = i.MaxPlayers > 0 ? (i.Players + i.Bots) / (float)i.MaxPlayers : 0f;
                Text pc = UIFactory.Label(card.transform, "Players", $"{Loc.T("players")}: {i.Players}/{i.MaxPlayers}  (+{i.Bots} {Loc.T("bots")})", 22, Theme.TextDim, TextAnchor.MiddleRight);
                UIFactory.Place(pc.rectTransform, new Vector2(1, 0.5f), new Vector2(420, 30), new Vector2(-260, -4), new Vector2(1, 0.5f));
                Text ping = UIFactory.Label(card.transform, "Ping", $"{Loc.T("ping")}: {e.Result.PingMs} ms", 22, Theme.PingColor(e.Result.PingMs), TextAnchor.MiddleRight);
                UIFactory.Place(ping.rectTransform, new Vector2(1, 0.5f), new Vector2(300, 30), new Vector2(-260, -38), new Vector2(1, 0.5f));
            }
            else if (!e.IsConfigured)
            {
                Text hint = UIFactory.Label(card.transform, "Hint", "config/serverlist.json: \"host\"", 20, Theme.TextDim, TextAnchor.MiddleRight);
                UIFactory.Place(hint.rectTransform, new Vector2(1, 0.5f), new Vector2(420, 30), new Vector2(-260, 0), new Vector2(1, 0.5f));
            }

            bool joinable = e.IsConfigured && e.Result != null && e.Result.Status == QueryStatus.Online && !e.Result.VersionMismatch;
            AnimatedButton join = UIFactory.Button(card.transform, "Join", Loc.T("join"), () => Lobby.Join(e), 30, joinable, new Vector2(220, 96));
            UIFactory.Place(join.Background.rectTransform, new Vector2(1, 0.5f), new Vector2(220, 96), new Vector2(-24, 0), new Vector2(1, 0.5f));
            join.Interactable = joinable;
        }
    }

    // ====================================================================== settings

    public sealed class SettingsPanel : LobbyPanel
    {
        private RectTransform _content;
        private int _tab;
        private readonly AnimatedButton[] _tabs = new AnimatedButton[4];

        protected override string Title => Loc.T("settings");

        public static void BuildInto(RectTransform body, int tab, Action rebuild, Action editLayout, RectTransform content)
        {
            GameSettings s = App.Settings;
            UIFactory.Clear(content);
            void Apply()
            {
                s.Save();
                QualityManager.Apply(s);
            }

            Text Section(string t)
            {
                Text x = UIFactory.Label(content, "Section", t, 28, Theme.Accent, TextAnchor.LowerLeft, UIFactory.Bold);
                UIFactory.Layout(x, 52);
                return x;
            }

            RectTransform Row(string label)
            {
                var row = UIFactory.Rect(content, label);
                UIFactory.Layout(row, 76);
                Text l = UIFactory.Label(row, "Label", label, 28, Theme.Text, TextAnchor.MiddleLeft);
                l.rectTransform.anchorMin = new Vector2(0, 0);
                l.rectTransform.anchorMax = new Vector2(0.45f, 1);
                l.rectTransform.offsetMin = l.rectTransform.offsetMax = Vector2.zero;
                var slot = UIFactory.Rect(row, "Control");
                slot.anchorMin = new Vector2(0.45f, 0);
                slot.anchorMax = new Vector2(1, 1);
                slot.offsetMin = slot.offsetMax = Vector2.zero;
                return slot;
            }

            void SliderRow(string label, float min, float max, float value, Action<float> set, Func<float, string> fmt, bool whole = false)
            {
                RectTransform slot = Row(label);
                Text val = UIFactory.Label(slot, "Value", fmt(value), 26, Theme.TextDim, TextAnchor.MiddleRight);
                UIFactory.Place(val.rectTransform, new Vector2(1, 0.5f), new Vector2(120, 50), Vector2.zero, new Vector2(1, 0.5f));
                Slider sl = UIFactory.Slider(slot, "Slider", min, max, value, v =>
                {
                    set(v);
                    val.text = fmt(v);
                }, whole);
                UIFactory.Place((RectTransform)sl.transform, new Vector2(0, 0.5f), new Vector2(380, 44), Vector2.zero, new Vector2(0, 0.5f));
            }

            void ToggleRow(string label, bool value, Action<bool> set)
            {
                RectTransform slot = Row(label);
                Toggle t = UIFactory.Toggle(slot, "Toggle", value, set);
                UIFactory.Place((RectTransform)t.transform, new Vector2(0, 0.5f), new Vector2(96, 48), Vector2.zero, new Vector2(0, 0.5f));
            }

            void Choice(string label, string[] options, int selected, Action<int> pick)
            {
                RectTransform slot = Row(label);
                float w = Mathf.Min(150f, 560f / options.Length);
                for (int i = 0; i < options.Length; i++)
                {
                    int idx = i;
                    AnimatedButton b = UIFactory.Button(slot, options[i], options[i], () => pick(idx), 24, i == selected, new Vector2(w - 8, 60));
                    UIFactory.Place(b.Background.rectTransform, new Vector2(0, 0.5f), new Vector2(w - 8, 60), new Vector2(i * w, 0), new Vector2(0, 0.5f));
                }
            }

            switch (tab)
            {
                case 0:
                    Section(Loc.T("graphics"));
                    Choice(Loc.T("quality"), new[] { "LOW", "MEDIUM", "HIGH", "CUSTOM" }, (int)s.Preset, i =>
                    {
                        s.Preset = (QualityPreset)i;
                        Apply();
                        rebuild();
                    });
                    int maxFps = QualityManager.MaxSupportedFps();
                    Choice(Loc.T("fps_limit"), new[] { "30", "45", maxFps >= 60 ? "60" : "60*" }, s.TargetFps >= 60 ? 2 : s.TargetFps >= 45 ? 1 : 0, i =>
                    {
                        s.TargetFps = i == 0 ? 30 : i == 1 ? 45 : 60;
                        Apply();
                        rebuild();
                    });
                    void Custom()
                    {
                        s.Preset = QualityPreset.Custom;
                    }
                    SliderRow(Loc.T("render_scale"), 0.5f, 1f, s.RenderScale, v => { Custom(); s.RenderScale = v; Apply(); }, v => Mathf.RoundToInt(v * 100) + "%");
                    SliderRow(Loc.T("view_distance"), 400f, 2000f, s.ViewDistance, v => { Custom(); s.ViewDistance = v; Apply(); }, v => Mathf.RoundToInt(v) + " m");
                    ToggleRow(Loc.T("shadows"), s.Shadows, v => { Custom(); s.Shadows = v; if (v && s.ShadowDistance < 20) s.ShadowDistance = 45; Apply(); });
                    ToggleRow(Loc.T("post_fx"), s.PostProcessing, v => { Custom(); s.PostProcessing = v; Apply(); });
                    SliderRow(Loc.T("vegetation"), 0.2f, 1f, s.VegetationDensity, v => { Custom(); s.VegetationDensity = v; Apply(); }, v => Mathf.RoundToInt(v * 100) + "%");
                    break;
                case 1:
                    Section(Loc.T("controls"));
                    SliderRow(Loc.T("sensitivity"), 0.2f, 3f, s.LookSensitivity, v => { s.LookSensitivity = v; s.Save(); }, v => v.ToString("0.0"));
                    ToggleRow(Loc.T("invert_y"), s.InvertY, v => { s.InvertY = v; s.Save(); });
                    SliderRow(Loc.T("joystick_size"), 0.6f, 1.6f, s.JoystickSize, v => { s.JoystickSize = v; s.Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
                    SliderRow(Loc.T("button_opacity"), 0.2f, 1f, s.ButtonOpacity, v => { s.ButtonOpacity = v; s.Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
                    ToggleRow(Loc.T("auto_sprint"), s.AutoSprintOnStickEdge, v => { s.AutoSprintOnStickEdge = v; s.Save(); });
                    if (editLayout != null)
                    {
                        AnimatedButton edit = UIFactory.Button(content, "Edit", Loc.T("edit_layout"), editLayout, 28, false, new Vector2(500, 80));
                        UIFactory.Layout(edit.Background, 84);
                    }
                    break;
                case 2:
                    Section(Loc.T("audio"));
                    SliderRow(Loc.T("master_volume"), 0f, 1f, s.MasterVolume, v => { s.MasterVolume = v; s.Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
                    SliderRow(Loc.T("music_volume"), 0f, 1f, s.MusicVolume, v => { s.MusicVolume = v; s.Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
                    SliderRow(Loc.T("sfx_volume"), 0f, 1f, s.SfxVolume, v => { s.SfxVolume = v; s.Save(); }, v => Mathf.RoundToInt(v * 100) + "%");
                    break;
                default:
                    Section(Loc.T("general"));
                    Choice(Loc.T("language"), new[] { "O'ZBEK", "ENGLISH", "РУССКИЙ" }, s.Language == "uz" ? 0 : s.Language == "en" ? 1 : 2, i =>
                    {
                        s.Language = i == 0 ? "uz" : i == 1 ? "en" : "ru";
                        Loc.Language = s.Language;
                        s.Save();
                        rebuild();
                    });
                    ToggleRow(Loc.T("show_fps"), s.ShowFps, v => { s.ShowFps = v; s.Save(); });
                    SliderRow(Loc.T("offline_bots"), 0f, 60f, s.OfflineBotCount, v => { s.OfflineBotCount = Mathf.RoundToInt(v); s.Save(); }, v => Mathf.RoundToInt(v).ToString(), true);
                    break;
            }
        }

        protected override void Build()
        {
            string[] names = { Loc.T("graphics"), Loc.T("controls"), Loc.T("audio"), Loc.T("general") };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                AnimatedButton b = UIFactory.Button(Body, "Tab" + i, names[i], () => SelectTab(idx), 24, i == _tab, new Vector2(220, 64));
                UIFactory.Place(b.Background.rectTransform, new Vector2(0, 1), new Vector2(220, 64), new Vector2(i * 230, 0), new Vector2(0, 1));
                _tabs[i] = b;
            }
            var listRoot = UIFactory.Rect(Body, "ListRoot");
            listRoot.anchorMin = Vector2.zero;
            listRoot.anchorMax = Vector2.one;
            listRoot.offsetMin = Vector2.zero;
            listRoot.offsetMax = new Vector2(0, -84);
            _content = UIFactory.ScrollList(listRoot, "List", out _);
            UIFactory.Stretch((RectTransform)_content.parent);
            Refresh();
        }

        private void SelectTab(int i)
        {
            _tab = i;
            for (int k = 0; k < _tabs.Length; k++) _tabs[k].SetColor(k == i ? Theme.Accent : Theme.Button);
            Refresh();
        }

        private void Refresh()
        {
            BuildInto(Body, _tab, Refresh, () => Game.LayoutEditor.Open(Lobby.Safe), _content);
        }
    }

    // ====================================================================== profile

    public sealed class ProfilePanel : LobbyPanel
    {
        protected override string Title => Loc.T("profile");
        protected override float Width => 880f;

        protected override void Build()
        {
            UIFactory.VStack(Body, 18);
            Text l = UIFactory.Label(Body, "NickLabel", Loc.T("nickname"), 26, Theme.TextDim);
            UIFactory.Layout(l, 40);
            var row = UIFactory.Rect(Body, "NickRow");
            UIFactory.Layout(row, 76);
            InputField nick = UIFactory.Input(row, "Nick", App.Profile.Nickname, 32);
            UIFactory.Place(nick.GetComponent<RectTransform>(), new Vector2(0, 0.5f), new Vector2(520, 72), Vector2.zero, new Vector2(0, 0.5f));
            nick.characterLimit = NetProtocol.MaxNameLength;
            AnimatedButton save = UIFactory.Button(row, "Save", Loc.T("save"), () =>
            {
                App.Profile.SetNickname(nick.text);
                nick.text = App.Profile.Nickname;
                Lobby.RefreshProfile();
                Lobby.Toaster.Show(Loc.T("save") + ": " + App.Profile.Nickname, Theme.Good, 2f);
            }, 26, true, new Vector2(220, 72));
            UIFactory.Place(save.Background.rectTransform, new Vector2(1, 0.5f), new Vector2(220, 72), Vector2.zero, new Vector2(1, 0.5f));

            PlayerProfile.LocalStats st = App.Profile.Stats;
            Stat(Loc.T("player_id"), App.Profile.PlayerId.Substring(0, 8).ToUpperInvariant() + "...");
            Stat(Loc.T("sessions"), st.Sessions.ToString());
            Stat(Loc.T("play_time"), TimeSpan.FromSeconds(st.PlaySeconds).ToString(@"hh\:mm\:ss"));
            Stat(Loc.T("deaths"), st.Deaths.ToString());
            Stat(Loc.T("servers"), string.IsNullOrEmpty(st.LastServer) ? "—" : st.LastServer);
        }

        private void Stat(string k, string v)
        {
            var row = UIFactory.Rect(Body, k);
            UIFactory.Layout(row, 56);
            Text a = UIFactory.Label(row, "K", k, 28, Theme.TextDim);
            a.rectTransform.anchorMin = Vector2.zero;
            a.rectTransform.anchorMax = new Vector2(0.5f, 1);
            a.rectTransform.offsetMin = a.rectTransform.offsetMax = Vector2.zero;
            Text b = UIFactory.Label(row, "V", v, 30, Theme.Text, TextAnchor.MiddleRight, UIFactory.Bold);
            b.rectTransform.anchorMin = new Vector2(0.5f, 0);
            b.rectTransform.anchorMax = Vector2.one;
            b.rectTransform.offsetMin = b.rectTransform.offsetMax = Vector2.zero;
        }
    }

    // ====================================================================== inventory

    public sealed class InventoryPanel : LobbyPanel
    {
        protected override string Title => Loc.T("inventory");

        protected override void Build()
        {
            PlayerProfile.LocalStats st = App.Profile.Stats;
            Text title = UIFactory.Label(Body, "Last", Loc.T("last_state") + (string.IsNullOrEmpty(st.LastServer) ? "" : " · " + st.LastServer), 28, Theme.Accent, TextAnchor.UpperLeft, UIFactory.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0, 1), new Vector2(900, 50), Vector2.zero, new Vector2(0, 1));
            Vital(Loc.T("health"), st.LastHealth, Theme.Health, 0);
            Vital(Loc.T("hunger"), st.LastHunger, Theme.Hunger, 1);
            Vital(Loc.T("thirst"), st.LastThirst, Theme.Thirst, 2);

            // 6 x 4 slot grid (server-side inventory arrives with Phase 2)
            var grid = UIFactory.Rect(Body, "Grid");
            UIFactory.Place(grid, new Vector2(0, 1), new Vector2(900, 380), new Vector2(0, -250), new Vector2(0, 1));
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 6; x++)
                {
                    Image slot = UIFactory.Image(grid, "Slot", new Color(1, 1, 1, 0.06f), UISprites.Rounded(12));
                    UIFactory.Place(slot.rectTransform, new Vector2(0, 1), new Vector2(130, 84), new Vector2(x * 145, -y * 95), new Vector2(0, 1));
                    Image border = UIFactory.Image(slot.transform, "B", new Color(1, 1, 1, 0.1f), UISprites.Rounded(12, 2));
                    UIFactory.Stretch(border.rectTransform);
                }
            }
            Text note = UIFactory.Label(Body, "Note", Loc.T("inventory_empty"), 24, Theme.TextDim, TextAnchor.UpperLeft);
            UIFactory.Place(note.rectTransform, new Vector2(0, 0), new Vector2(900, 110), Vector2.zero, new Vector2(0, 0));
        }

        private void Vital(string name, float value, Color c, int i)
        {
            Text l = UIFactory.Label(Body, name, name, 24, Theme.TextDim);
            UIFactory.Place(l.rectTransform, new Vector2(0, 1), new Vector2(200, 40), new Vector2(0, -60 - i * 56), new Vector2(0, 1));
            Image fill = UIFactory.Bar(Body, name + "Bar", c, new Vector2(560, 22));
            UIFactory.Place((RectTransform)fill.transform.parent, new Vector2(0, 1), new Vector2(560, 22), new Vector2(210, -70 - i * 56), new Vector2(0, 1));
            fill.fillAmount = Mathf.Clamp01(value / 100f);
            Text v = UIFactory.Label(Body, name + "V", Mathf.RoundToInt(value).ToString(), 24, Theme.Text, TextAnchor.MiddleLeft, UIFactory.Bold);
            UIFactory.Place(v.rectTransform, new Vector2(0, 1), new Vector2(100, 40), new Vector2(790, -60 - i * 56), new Vector2(0, 1));
        }
    }

    // ====================================================================== connecting

    /// <summary>Connecting overlay: animated ring, current step, progress, cancel / retry.</summary>
    public sealed class ConnectOverlay : MonoBehaviour
    {
        private LobbyController _lobby;
        private CanvasGroup _group;
        private RectTransform _ring;
        private Text _title;
        private Text _step;
        private Image _bar;
        private AnimatedButton _cancel;
        private AnimatedButton _retry;
        private bool _visible;

        public static ConnectOverlay Create(RectTransform safe, LobbyController lobby)
        {
            Image dim = UIFactory.Image(safe.parent, "Connect", new Color(0, 0, 0, 0.72f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var o = dim.gameObject.AddComponent<ConnectOverlay>();
            o._lobby = lobby;
            o._group = UIAnim.Group(dim);
            Image ring = UIFactory.Image(dim.transform, "Ring", Theme.Accent, UISprites.Ring(128, 0.14f));
            UIFactory.Place(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(150, 150), new Vector2(0, 120));
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillAmount = 0.75f;
            o._ring = ring.rectTransform;
            o._title = UIFactory.Label(dim.transform, "Title", "", 44, Theme.Text, TextAnchor.MiddleCenter, UIFactory.Display);
            UIFactory.Place(o._title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1200, 70), new Vector2(0, -10));
            o._step = UIFactory.Label(dim.transform, "Step", "", 30, Theme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(o._step.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1300, 90), new Vector2(0, -80));
            o._bar = UIFactory.Bar(dim.transform, "Progress", Theme.Accent, new Vector2(600, 12));
            UIFactory.Place((RectTransform)o._bar.transform.parent, new Vector2(0.5f, 0.5f), new Vector2(600, 12), new Vector2(0, -140));
            o._cancel = UIFactory.Button(dim.transform, "Cancel", Loc.T("cancel"), o.OnCancel, 28, false, new Vector2(300, 80));
            UIFactory.Place(o._cancel.Background.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300, 80), new Vector2(0, -240));
            o._retry = UIFactory.Button(dim.transform, "Retry", Loc.T("retry"), o.OnRetry, 28, true, new Vector2(300, 80));
            UIFactory.Place(o._retry.Background.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300, 80), new Vector2(170, -240));
            dim.gameObject.SetActive(false);
            return o;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            _visible = true;
            _group.alpha = 0f;
            StartCoroutine(UIAnim.Fade(_group, 1f, 0.25f));
        }

        private void Hide()
        {
            _visible = false;
            gameObject.SetActive(false);
        }

        private void OnCancel()
        {
            App.Session.Cancel();
            Hide();
        }

        private void OnRetry()
        {
            ServerEntry e = App.Servers.Find(App.Settings.LastServerKey);
            App.Session.Cancel();
            if (e != null) _lobby.Join(e);
            else Hide();
        }

        private void Update()
        {
            if (!_visible) return;
            Session s = App.Session;
            _ring.Rotate(0, 0, -240f * Time.unscaledDeltaTime);
            _title.text = s.ServerName;
            bool failed = s.Phase == SessionPhase.Failed;
            _retry.gameObject.SetActive(failed && s.Mode != SessionMode.OfflineLocal && s.Mode != SessionMode.LanHost);
            _cancel.SetText(failed ? Loc.T("close") : Loc.T("cancel"));
            UIFactory.Place(_cancel.Background.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300, 80), new Vector2(failed && _retry.gameObject.activeSelf ? -170 : 0, -240));
            _ring.gameObject.SetActive(!failed);
            string step;
            float p;
            switch (s.Phase)
            {
                case SessionPhase.Connecting: step = Loc.T("connecting") + "..."; p = 0.15f; break;
                case SessionPhase.Authenticating: step = Loc.T("authenticating") + "..."; p = 0.3f; break;
                case SessionPhase.LoadingWorld: step = Loc.T("loading_island") + $" ({Mathf.RoundToInt(s.Progress * 100)}%)..."; p = 0.3f + s.Progress * 0.6f; break;
                case SessionPhase.LoadingScene:
                case SessionPhase.Spawning: step = Loc.T("spawning") + "..."; p = 0.95f; break;
                case SessionPhase.Failed: step = "<color=#FF6A5E>" + s.Error + "</color>"; p = 0f; break;
                default: step = string.Empty; p = 0f; break;
            }
            if (s.Mode == SessionMode.OfflineLocal) step += "\n<size=22>" + Loc.T("offline_note") + "</size>";
            _step.text = step;
            _bar.fillAmount = Mathf.MoveTowards(_bar.fillAmount, p, Time.unscaledDeltaTime);
            if (s.Phase == SessionPhase.Idle) Hide();
        }
    }
}
