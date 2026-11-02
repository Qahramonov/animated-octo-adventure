using System;
using System.Collections.Generic;
using Survival.Client.Lobby;
using Survival.Client.UI;
using Survival.Gameplay;
using Survival.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.Client.Game
{
    /// <summary>
    /// In-game HUD: vitals, compass, crosshair, notices, network/FPS info, nameplates, pause menu,
    /// death screen and reconnect overlay.
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        private Canvas _canvas;
        private RectTransform _safe;
        private Image _health, _hunger, _thirst, _stamina;
        private Text _healthV, _hungerV, _thirstV;
        private RectTransform _compassStrip;
        private Text _info;
        private Text _modeBanner;
        private Toaster _toaster;
        private GameObject _death;
        private Text _deathCause;
        private AnimatedButton _respawn;
        private float _deathTimer;
        private GameObject _pause;
        private RectTransform _pauseBody;
        private GameObject _reconnect;
        private Text _reconnectText;
        private GameObject _loading;
        private Image _loadingBar;
        private Text _loadingText;
        private RectTransform _nameplates;
        private readonly List<Text> _plates = new List<Text>();
        private float _fps;
        private float _fpsTimer;
        private int _fpsFrames;

        public MobileControls Controls { get; private set; }
        public RectTransform Safe => _safe;
        public bool Paused => _pause != null && _pause.activeSelf;
        public bool DeathVisible => _death != null && _death.activeSelf;
        public Action LeaveRequested;
        public Action RespawnRequested;
        public Action ToggleCameraRequested;

        public static GameHud Create(Transform parent)
        {
            var go = new GameObject("[HUD]");
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<GameHud>();
            h.Build();
            return h;
        }

        private void Build()
        {
            _canvas = UIFactory.Canvas("HUD", 20, transform);
            _safe = UIFactory.SafeRoot(_canvas);
            _nameplates = UIFactory.Rect(_canvas.transform, "Nameplates");
            UIFactory.Stretch(_nameplates);
            _nameplates.SetAsFirstSibling();

            Controls = MobileControls.Create(_safe);

            // Vitals (top-left)
            var vit = UIFactory.Rect(_safe, "Vitals");
            UIFactory.Place(vit, new Vector2(0, 1), new Vector2(460, 200), new Vector2(30, -26), new Vector2(0, 1));
            Image panel = UIFactory.Panel(vit, "Bg", new Color(0.04f, 0.05f, 0.07f, 0.55f), 18);
            UIFactory.Stretch(panel.rectTransform);
            panel.raycastTarget = false;
            _health = Vital(vit, "H", Theme.Health, 0, out _healthV);
            _hunger = Vital(vit, "F", Theme.Hunger, 1, out _hungerV);
            _thirst = Vital(vit, "W", Theme.Thirst, 2, out _thirstV);
            _stamina = Vital(vit, "S", Theme.Stamina, 3, out _);

            // Compass (top-centre)
            var compass = UIFactory.Rect(_safe, "Compass");
            UIFactory.Place(compass, new Vector2(0.5f, 1), new Vector2(620, 54), new Vector2(0, -24), new Vector2(0.5f, 1));
            compass.gameObject.AddComponent<RectMask2D>();
            Image cbg = UIFactory.Image(compass, "Bg", new Color(0, 0, 0, 0.35f), UISprites.Rounded(14));
            UIFactory.Stretch(cbg.rectTransform);
            _compassStrip = UIFactory.Rect(compass, "Strip");
            _compassStrip.anchorMin = _compassStrip.anchorMax = new Vector2(0.5f, 0.5f);
            _compassStrip.sizeDelta = new Vector2(3600, 54);
            string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int rep = -1; rep <= 1; rep++)
            {
                for (int i = 0; i < 8; i++)
                {
                    Text t = UIFactory.Label(_compassStrip, dirs[i], dirs[i], i % 2 == 0 ? 30 : 22, i == 0 ? Theme.Accent : Theme.Text, TextAnchor.MiddleCenter, UIFactory.Bold);
                    t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    t.rectTransform.sizeDelta = new Vector2(80, 50);
                    t.rectTransform.anchoredPosition = new Vector2((rep * 360 + i * 45) * 4f, 0);
                }
            }
            Image tick = UIFactory.Image(compass, "Tick", Theme.Accent, UISprites.Rounded(2));
            UIFactory.Place(tick.rectTransform, new Vector2(0.5f, 0), new Vector2(4, 14), Vector2.zero, new Vector2(0.5f, 0));

            // Crosshair
            Image cross = UIFactory.Image(_canvas.transform, "Crosshair", new Color(1, 1, 1, 0.8f), UISprites.Circle(32));
            UIFactory.Place(cross.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(8, 8));

            // Info (top-right under the menu button)
            _info = UIFactory.Label(_safe, "Info", "", 22, Theme.TextDim, TextAnchor.UpperRight);
            UIFactory.Place(_info.rectTransform, new Vector2(1, 1), new Vector2(560, 120), new Vector2(-30, -150), new Vector2(1, 1));
            _modeBanner = UIFactory.Label(_safe, "Mode", "", 24, Theme.Warn, TextAnchor.UpperCenter, UIFactory.Bold);
            UIFactory.Place(_modeBanner.rectTransform, new Vector2(0.5f, 1), new Vector2(1100, 40), new Vector2(0, -88), new Vector2(0.5f, 1));

            _toaster = Toaster.Create(_safe);
            ((RectTransform)_toaster.transform).anchoredPosition = new Vector2(0, -140);

            BuildDeath();
            BuildPause();
            BuildReconnect();
            BuildLoading();
        }

        private Image Vital(RectTransform parent, string icon, Color c, int row, out Text value)
        {
            float y = -24 - row * 42;
            // Icons are procedural (coloured disc + letter) so no symbol glyphs are needed from the font
            Image dot = UIFactory.Image(parent, "Icon" + row, c, UISprites.Circle(64));
            UIFactory.Place(dot.rectTransform, new Vector2(0, 1), new Vector2(30, 30), new Vector2(22, y - 3), new Vector2(0, 1));
            Text i = UIFactory.Label(dot.transform, "L", icon, 18, new Color(0.05f, 0.05f, 0.07f), TextAnchor.MiddleCenter, UIFactory.Bold);
            UIFactory.Stretch(i.rectTransform);
            Image fill = UIFactory.Bar(parent, "Bar" + row, c, new Vector2(330, 20));
            UIFactory.Place((RectTransform)fill.transform.parent, new Vector2(0, 1), new Vector2(330, 20), new Vector2(62, y - 8), new Vector2(0, 1));
            value = UIFactory.Label(parent, "V" + row, "", 22, Theme.Text, TextAnchor.MiddleLeft, UIFactory.Bold);
            UIFactory.Place(value.rectTransform, new Vector2(0, 1), new Vector2(60, 36), new Vector2(400, y), new Vector2(0, 1));
            return fill;
        }

        private void BuildDeath()
        {
            Image dim = UIFactory.Image(_canvas.transform, "Death", new Color(0.25f, 0.02f, 0.02f, 0.55f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            Text title = UIFactory.Label(dim.transform, "Title", Loc.T("you_died"), 96, Theme.Text, TextAnchor.MiddleCenter, UIFactory.Display);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1400, 140), new Vector2(0, 120));
            UIFactory.AddShadow(title, 0.8f, 5f);
            _deathCause = UIFactory.Label(dim.transform, "Cause", "", 32, Theme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_deathCause.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1200, 60), new Vector2(0, 30));
            _respawn = UIFactory.Button(dim.transform, "Respawn", Loc.T("respawn"), () => RespawnRequested?.Invoke(), 40, true, new Vector2(420, 110));
            UIFactory.Place(_respawn.Background.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(420, 110), new Vector2(0, -90));
            _death = dim.gameObject;
            _death.SetActive(false);
        }

        private void BuildPause()
        {
            Image dim = UIFactory.Image(_canvas.transform, "Pause", new Color(0, 0, 0, 0.6f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var safe = UIFactory.Rect(dim.transform, "Safe");
            UIFactory.Stretch(safe);
            safe.gameObject.AddComponent<SafeArea>();
            var col = UIFactory.Rect(safe, "Menu");
            UIFactory.Place(col, new Vector2(0, 0.5f), new Vector2(440, 560), new Vector2(60, 0), new Vector2(0, 0.5f));
            string[] labels = { Loc.T("resume"), Loc.T("settings"), Loc.T("edit_layout"), "1ST / 3RD PERSON", Loc.T("leave") };
            Action[] actions =
            {
                () => SetPaused(false),
                ShowPauseSettings,
                () => LayoutEditor.Open(_safe),
                () => ToggleCameraRequested?.Invoke(),
                () => LeaveRequested?.Invoke(),
            };
            for (int i = 0; i < labels.Length; i++)
            {
                AnimatedButton b = UIFactory.Button(col, "B" + i, labels[i], actions[i], 30, i == 0, new Vector2(440, 86));
                UIFactory.Place(b.Background.rectTransform, new Vector2(0, 1), new Vector2(440, 86), new Vector2(0, -i * 102), new Vector2(0, 1));
            }
            Image panel = UIFactory.Panel(safe, "SettingsPanel");
            panel.rectTransform.anchorMin = new Vector2(0.38f, 0.06f);
            panel.rectTransform.anchorMax = new Vector2(0.97f, 0.94f);
            panel.rectTransform.offsetMin = panel.rectTransform.offsetMax = Vector2.zero;
            var listRoot = UIFactory.Rect(panel.transform, "ListRoot");
            UIFactory.Stretch(listRoot, 30);
            _pauseBody = UIFactory.ScrollList(listRoot, "List", out _);
            UIFactory.Stretch((RectTransform)_pauseBody.parent);
            panel.gameObject.SetActive(false);
            _pause = dim.gameObject;
            _pause.SetActive(false);
        }

        private int _pauseTab;

        private void ShowPauseSettings()
        {
            Transform panel = _pauseBody.parent.parent.parent;
            panel.gameObject.SetActive(true);
            RefreshPauseSettings();
        }

        private void RefreshPauseSettings()
        {
            SettingsPanel.BuildInto(_pauseBody, _pauseTab, RefreshPauseSettings, null, _pauseBody);
            var tabs = UIFactory.Rect(_pauseBody, "Tabs");
            UIFactory.Layout(tabs, 76);
            tabs.SetAsFirstSibling();
            string[] names = { Loc.T("graphics"), Loc.T("controls"), Loc.T("audio"), Loc.T("general") };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                AnimatedButton b = UIFactory.Button(tabs, "T" + i, names[i], () =>
                {
                    _pauseTab = idx;
                    RefreshPauseSettings();
                }, 22, i == _pauseTab, new Vector2(200, 64));
                UIFactory.Place(b.Background.rectTransform, new Vector2(0, 0.5f), new Vector2(200, 64), new Vector2(i * 210, 0), new Vector2(0, 0.5f));
            }
        }

        private void BuildReconnect()
        {
            Image dim = UIFactory.Image(_canvas.transform, "Reconnect", new Color(0, 0, 0, 0.55f));
            UIFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            _reconnectText = UIFactory.Label(dim.transform, "Text", "", 40, Theme.Warn, TextAnchor.MiddleCenter, UIFactory.Bold);
            UIFactory.Place(_reconnectText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1400, 120));
            AnimatedButton leave = UIFactory.Button(dim.transform, "Leave", Loc.T("leave"), () => LeaveRequested?.Invoke(), 28, false, new Vector2(380, 84));
            UIFactory.Place(leave.Background.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(380, 84), new Vector2(0, -120));
            _reconnect = dim.gameObject;
            _reconnect.SetActive(false);
        }

        private void BuildLoading()
        {
            Image bg = UIFactory.Image(_canvas.transform, "Loading", new Color(0.02f, 0.025f, 0.035f, 1f));
            UIFactory.Stretch(bg.rectTransform);
            bg.raycastTarget = true;
            Text t = UIFactory.Label(bg.transform, "Title", Survival.Core.GameInfo.Title, 120, Theme.Text, TextAnchor.MiddleCenter, UIFactory.Display);
            UIFactory.Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900, 160), new Vector2(0, 90));
            _loadingText = UIFactory.Label(bg.transform, "Text", "", 30, Theme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_loadingText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1200, 60), new Vector2(0, -30));
            _loadingBar = UIFactory.Bar(bg.transform, "Bar", Theme.Accent, new Vector2(700, 12));
            UIFactory.Place((RectTransform)_loadingBar.transform.parent, new Vector2(0.5f, 0.5f), new Vector2(700, 12), new Vector2(0, -90));
            _loading = bg.gameObject;
        }

        public void SetLoading(float progress, string text)
        {
            if (_loading == null) return;
            _loadingBar.fillAmount = Mathf.MoveTowards(_loadingBar.fillAmount, progress, Time.unscaledDeltaTime * 1.5f);
            _loadingText.text = text;
        }

        public void HideLoading()
        {
            if (_loading != null) Destroy(_loading);
            _loading = null;
        }

        public void SetPaused(bool paused)
        {
            _pause.SetActive(paused);
            if (paused) App.Audio.PlayUi(UiSound.Whoosh);
            else _pauseBody.parent.parent.parent.gameObject.SetActive(false);
        }

        public void ShowDeath(DiedMsg d)
        {
            _death.SetActive(true);
            _deathTimer = d.RespawnDelay;
            string cause = string.IsNullOrEmpty(d.KillerName) ? d.Cause.ToString() : d.KillerName;
            _deathCause.text = cause;
            App.Audio.Play(SfxSound.Death, 0.8f);
        }

        public void HideDeath() => _death.SetActive(false);

        public void Notice(NoticeMsg n)
        {
            Color c = n.Kind == NoticeKind.Kill ? Theme.Bad : n.Kind == NoticeKind.Event ? Theme.Accent : n.Kind == NoticeKind.Admin ? Theme.Warn : Theme.Secondary;
            _toaster.Show(n.Text, c, Mathf.Clamp(n.Duration, 2f, 8f));
            App.Audio.PlayUi(UiSound.Notify);
        }

        public void Toast(string text, Color c, float seconds = 3f) => _toaster.Show(text, c, seconds);

        public void UpdateHud(Session s, float yaw, Vector3 pos, ClientSimulation sim)
        {
            SurvivalStats st = sim.Stats;
            _health.fillAmount = st.Health / 100f;
            _hunger.fillAmount = st.Hunger / 100f;
            _thirst.fillAmount = st.Thirst / 100f;
            _stamina.fillAmount = sim.Predicted.Stamina / 100f;
            _healthV.text = Mathf.CeilToInt(st.Health).ToString();
            _hungerV.text = Mathf.CeilToInt(st.Hunger).ToString();
            _thirstV.text = Mathf.CeilToInt(st.Thirst).ToString();
            _health.color = st.Health < 25f ? Color.Lerp(Theme.Health, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 8f)) : Theme.Health;

            _compassStrip.anchoredPosition = new Vector2(-yaw * 4f, 0);

            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                _fps = _fpsFrames / _fpsTimer;
                _fpsFrames = 0;
                _fpsTimer = 0f;
                if (App.Settings.ShowFps)
                {
                    int remotes = sim.Remotes.Count;
                    string net = s.IsLocalServer ? "local" : $"{s.RttMs} ms";
                    _info.text = $"{s.ServerName}\n{Mathf.RoundToInt(_fps)} FPS · {net} · {remotes} nearby\n{pos.x:0} , {pos.z:0}";
                }
                else
                {
                    _info.text = string.Empty;
                }
            }

            _modeBanner.text = s.Mode == SessionMode.OfflineLocal ? Loc.T("offline_practice") + " — " + Loc.T("offline_note")
                : s.Mode == SessionMode.LanHost ? Loc.T("host_lan") + " · IP " + Session.LocalIPv4()
                : s.WorldMismatch ? Loc.T("world_mismatch") : string.Empty;

            bool reconnecting = s.Phase == SessionPhase.Reconnecting;
            if (_reconnect.activeSelf != reconnecting) _reconnect.SetActive(reconnecting);
            if (reconnecting)
            {
                int attempt = s.Client != null ? s.Client.ReconnectAttempt : 0;
                _reconnectText.text = $"{Loc.T("conn_lost")} ({attempt}/{GameClient.MaxReconnectAttempts})" + new string('.', 1 + (int)(Time.unscaledTime * 2) % 3);
            }

            if (_death.activeSelf)
            {
                _deathTimer -= Time.unscaledDeltaTime;
                _respawn.Interactable = _deathTimer <= 0f;
                _respawn.SetText(_deathTimer > 0f ? $"{Loc.T("respawn")} ({Mathf.CeilToInt(_deathTimer)})" : Loc.T("respawn"));
            }
        }

        /// <summary>Screen-space nameplates for nearby players/bots.</summary>
        public void UpdateNameplates(Camera cam, IReadOnlyDictionary<uint, RemoteEntity> remotes)
        {
            int used = 0;
            Vector3 camPos = cam.transform.position;
            float scaleX = ((RectTransform)_canvas.transform).rect.width / Mathf.Max(1, Screen.width);
            float scaleY = ((RectTransform)_canvas.transform).rect.height / Mathf.Max(1, Screen.height);
            foreach (RemoteEntity e in remotes.Values)
            {
                if (e.Type != EntityType.Player && e.Type != EntityType.Bot) continue;
                Vector3 head = new Vector3(e.Position.X, e.Position.Y + 2.15f, e.Position.Z);
                float d = Vector3.Distance(head, camPos);
                if (d > 45f) continue;
                Vector3 sp = cam.WorldToScreenPoint(head);
                if (sp.z <= 0.1f) continue;
                Text t;
                if (used < _plates.Count)
                {
                    t = _plates[used];
                }
                else
                {
                    t = UIFactory.Label(_nameplates, "Plate", "", 24, Theme.Text, TextAnchor.MiddleCenter, UIFactory.Bold);
                    t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
                    t.rectTransform.sizeDelta = new Vector2(360, 40);
                    UIFactory.AddShadow(t, 0.8f, 2f);
                    _plates.Add(t);
                }
                used++;
                t.gameObject.SetActive(true);
                bool bot = e.Type == EntityType.Bot;
                t.text = bot ? "<color=#9AA3AD>[BOT]</color> " + e.Name : e.Name;
                t.color = (bot ? Theme.TextDim : Theme.Text).WithAlpha(Mathf.Clamp01((45f - d) / 15f));
                t.rectTransform.anchoredPosition = new Vector2(sp.x * scaleX, sp.y * scaleY);
            }
            for (int i = used; i < _plates.Count; i++) _plates[i].gameObject.SetActive(false);
        }
    }
}
