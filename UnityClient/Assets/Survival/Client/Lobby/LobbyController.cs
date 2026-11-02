using System.Collections;
using Survival.Client.UI;
using Survival.Core;
using Survival.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.Client.Lobby
{
    /// <summary>
    /// Main menu: 3D stage + animated menu (PLAY, SERVERS, INVENTORY, PROFILE, SETTINGS, EXIT),
    /// profile/server chips, side panels and the connect overlay.
    /// </summary>
    public sealed class LobbyController : MonoBehaviour
    {
        private LobbyStage _stage;
        private Canvas _canvas;
        private RectTransform _safe;
        private RectTransform _menu;
        private AnimatedButton _play;
        private Text _playServer;
        private Text _serverChipText;
        private Image _serverChipDot;
        private Text _profileName;
        private Text _profileInitial;
        private LobbyPanel _openPanel;
        private ConnectOverlay _connect;
        private Toaster _toaster;
        private readonly AnimatedButton[] _menuButtons = new AnimatedButton[5];

        public RectTransform Safe => _safe;
        public Toaster Toaster => _toaster;
        public LobbyStage Stage => _stage;

        private void Start()
        {
            _stage = LobbyStage.Create(transform);
            BuildUi();
            StartCoroutine(Intro());
            if (App.Scenes.FadeAlpha > 0f) StartCoroutine(App.Scenes.FadeTo(0f, 0.7f));
            App.Audio.SetAmbience(AmbienceLayer.Ocean, 0.55f);
            App.Audio.SetAmbience(AmbienceLayer.Wind, 0.25f);
            App.Audio.SetAmbience(AmbienceLayer.Night, 0.2f);
            App.Servers.Changed += RefreshServerChip;
            App.Servers.RefreshAll();
            RefreshServerChip();

            // Returning from a failed game session: show why
            if (App.Session.Phase == SessionPhase.Failed && !string.IsNullOrEmpty(App.Session.Error))
            {
                string title = string.IsNullOrEmpty(App.Session.ServerName) ? GameInfo.Title : App.Session.ServerName;
                Modal.Show(_safe, title, App.Session.Error, Loc.T("close"), null);
                App.Session.Cancel();
            }
        }

        private void OnDestroy()
        {
            App.Servers.Changed -= RefreshServerChip;
            App.Audio.SetAmbience(AmbienceLayer.Ocean, 0f);
            App.Audio.SetAmbience(AmbienceLayer.Wind, 0f);
            App.Audio.SetAmbience(AmbienceLayer.Night, 0f);
        }

        private void BuildUi()
        {
            _canvas = UIFactory.Canvas("LobbyUI", 10, transform);
            _safe = UIFactory.SafeRoot(_canvas);

            // Left vignette for menu readability
            Image vignette = UIFactory.Image(_canvas.transform, "Vignette", Color.white, UISprites.GradientH(new Color(0, 0, 0, 0.75f), new Color(0, 0, 0, 0f)));
            vignette.rectTransform.anchorMin = new Vector2(0, 0);
            vignette.rectTransform.anchorMax = new Vector2(0.55f, 1);
            vignette.rectTransform.offsetMin = vignette.rectTransform.offsetMax = Vector2.zero;
            vignette.transform.SetAsFirstSibling();

            // Logo
            Text logo = UIFactory.Label(_safe, "Logo", GameInfo.Title, 110, Theme.Text, TextAnchor.UpperLeft, UIFactory.Display);
            UIFactory.Place(logo.rectTransform, new Vector2(0, 1), new Vector2(600, 130), new Vector2(70, -150), new Vector2(0, 1));
            UIFactory.AddShadow(logo, 0.7f, 4f);
            Text sub = UIFactory.Label(_safe, "Sub", "S U R V I V A L", 30, Theme.Accent, TextAnchor.UpperLeft, UIFactory.Bold);
            UIFactory.Place(sub.rectTransform, new Vector2(0, 1), new Vector2(600, 40), new Vector2(76, -272), new Vector2(0, 1));

            // Menu
            _menu = UIFactory.Rect(_safe, "Menu");
            UIFactory.Place(_menu, new Vector2(0, 0.5f), new Vector2(430, 520), new Vector2(70, -90), new Vector2(0, 0.5f));
            string[] keys = { "servers", "inventory", "profile", "settings", "exit" };
            System.Action[] actions =
            {
                () => Open<ServerBrowserPanel>(),
                () => Open<InventoryPanel>(),
                () => Open<ProfilePanel>(),
                () => Open<SettingsPanel>(),
                ConfirmExit,
            };
            for (int i = 0; i < keys.Length; i++)
            {
                AnimatedButton b = UIFactory.Button(_menu, keys[i], Loc.T(keys[i]), actions[i], 34, false, new Vector2(400, 84));
                UIFactory.Place(b.Background.rectTransform, new Vector2(0, 1), new Vector2(400, 84), new Vector2(0, -i * 100f), new Vector2(0, 1));
                b.Label.alignment = TextAnchor.MiddleLeft;
                b.Label.rectTransform.offsetMin = new Vector2(36, 0);
                Image accent = UIFactory.Image(b.Background.transform, "Accent", Theme.Accent, UISprites.Rounded(4));
                accent.rectTransform.anchorMin = new Vector2(0, 0.2f);
                accent.rectTransform.anchorMax = new Vector2(0, 0.8f);
                accent.rectTransform.sizeDelta = new Vector2(6, 0);
                accent.rectTransform.anchoredPosition = new Vector2(16, 0);
                _menuButtons[i] = b;
            }

            // PLAY
            var playRoot = UIFactory.Rect(_safe, "PlayRoot");
            UIFactory.Place(playRoot, new Vector2(1, 0), new Vector2(520, 200), new Vector2(-60, 50), new Vector2(1, 0));
            Image glow = UIFactory.Image(playRoot, "Glow", Theme.Accent.WithAlpha(0.45f), UISprites.Glow(128));
            UIFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(640, 260));
            glow.gameObject.AddComponent<Pulse>();
            _play = UIFactory.Button(playRoot, "Play", Loc.T("play"), OnPlay, 64, true, new Vector2(500, 130));
            UIFactory.Place(_play.Background.rectTransform, new Vector2(0.5f, 1), new Vector2(500, 130), Vector2.zero, new Vector2(0.5f, 1));
            _play.Label.font = UIFactory.Display;
            _playServer = UIFactory.Label(playRoot, "Server", "", 26, Theme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_playServer.rectTransform, new Vector2(0.5f, 0), new Vector2(500, 50), new Vector2(0, 10), new Vector2(0.5f, 0));

            // Profile chip (top-left of the right side)
            Image prof = UIFactory.Panel(_safe, "ProfileChip", Theme.Panel, 40);
            UIFactory.Place(prof.rectTransform, new Vector2(1, 1), new Vector2(430, 92), new Vector2(-470, -30), new Vector2(1, 1));
            prof.gameObject.AddComponent<Button>().onClick.AddListener(() => Open<ProfilePanel>());
            Image avatar = UIFactory.Image(prof.transform, "Avatar", Theme.Accent, UISprites.Circle(96));
            UIFactory.Place(avatar.rectTransform, new Vector2(0, 0.5f), new Vector2(70, 70), new Vector2(14, 0), new Vector2(0, 0.5f));
            _profileInitial = UIFactory.Label(avatar.transform, "Initial", "", 38, new Color(0.1f, 0.06f, 0.02f), TextAnchor.MiddleCenter, UIFactory.Display);
            UIFactory.Stretch(_profileInitial.rectTransform);
            _profileName = UIFactory.Label(prof.transform, "Name", "", 30, Theme.Text, TextAnchor.MiddleLeft, UIFactory.Bold);
            UIFactory.Place(_profileName.rectTransform, new Vector2(0, 0.5f), new Vector2(320, 40), new Vector2(100, 14), new Vector2(0, 0.5f));
            Text pid = UIFactory.Label(prof.transform, "Id", "ID " + App.Profile.ShortId, 20, Theme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(pid.rectTransform, new Vector2(0, 0.5f), new Vector2(320, 30), new Vector2(100, -18), new Vector2(0, 0.5f));

            // Server chip (top-right)
            Image chip = UIFactory.Panel(_safe, "ServerChip", Theme.Panel, 40);
            UIFactory.Place(chip.rectTransform, new Vector2(1, 1), new Vector2(420, 92), new Vector2(-30, -30), new Vector2(1, 1));
            chip.gameObject.AddComponent<Button>().onClick.AddListener(() => Open<ServerBrowserPanel>());
            _serverChipDot = UIFactory.Image(chip.transform, "Dot", Theme.Neutral, UISprites.Circle(32));
            UIFactory.Place(_serverChipDot.rectTransform, new Vector2(0, 0.5f), new Vector2(22, 22), new Vector2(26, 0), new Vector2(0, 0.5f));
            _serverChipText = UIFactory.Label(chip.transform, "Text", "", 24, Theme.Text, TextAnchor.MiddleLeft);
            UIFactory.Place(_serverChipText.rectTransform, new Vector2(0, 0.5f), new Vector2(360, 80), new Vector2(62, 0), new Vector2(0, 0.5f));

            Text version = UIFactory.Label(_safe, "Version", $"v{GameInfo.Version} · {GameInfo.Phase}", 20, Theme.TextDim.WithAlpha(0.7f), TextAnchor.LowerLeft);
            UIFactory.Place(version.rectTransform, new Vector2(0, 0), new Vector2(700, 30), new Vector2(30, 18), new Vector2(0, 0));

            _toaster = Toaster.Create(_safe);
            RefreshProfile();
        }

        private IEnumerator Intro()
        {
            for (int i = 0; i < _menuButtons.Length; i++)
            {
                CanvasGroup g = UIAnim.Group(_menuButtons[i]);
                g.alpha = 0f;
            }
            yield return new WaitForSecondsRealtime(0.25f);
            for (int i = 0; i < _menuButtons.Length; i++)
            {
                RectTransform rt = _menuButtons[i].Background.rectTransform;
                StartCoroutine(UIAnim.PopIn(rt, UIAnim.Group(_menuButtons[i]), new Vector2(-80, 0), 0.4f));
                yield return new WaitForSecondsRealtime(0.07f);
            }
        }

        public void RefreshProfile()
        {
            _profileName.text = App.Profile.Nickname;
            _profileInitial.text = App.Profile.Nickname.Substring(0, 1).ToUpperInvariant();
        }

        /// <summary>Selected server = last used, else the first configured built-in one.</summary>
        public ServerEntry SelectedServer()
        {
            ServerEntry e = App.Servers.Find(App.Settings.LastServerKey);
            if (e != null) return e;
            foreach (ServerEntry s in App.Servers.Entries) if (s.IsConfigured) return s;
            return App.Servers.Entries.Count > 0 ? App.Servers.Entries[0] : null;
        }

        private void RefreshServerChip()
        {
            ServerEntry e = SelectedServer();
            if (e == null)
            {
                _serverChipText.text = Loc.T("select_server");
                _serverChipDot.color = Theme.Neutral;
                _playServer.text = Loc.T("select_server");
                return;
            }
            string status;
            Color dot;
            Describe(e, out status, out dot);
            _serverChipText.text = $"<b>{e.Name}</b>\n<size=20>{status}</size>";
            _serverChipDot.color = dot;
            _playServer.text = e.Name + " · " + status;
        }

        public static void Describe(ServerEntry e, out string status, out Color dot)
        {
            if (!e.IsConfigured)
            {
                status = Loc.T("not_deployed");
                dot = Theme.Neutral;
            }
            else if (e.Result == null || e.Querying && e.Result == null)
            {
                status = Loc.T("checking");
                dot = Theme.Neutral;
            }
            else if (e.Result.Status != QueryStatus.Online)
            {
                status = Loc.T("offline");
                dot = Theme.Bad;
            }
            else if (e.Result.VersionMismatch)
            {
                status = Loc.T("version_mismatch");
                dot = Theme.Warn;
            }
            else
            {
                ServerInfo i = e.Result.Info;
                string load = e.Result.IsFull ? Loc.T("full") : e.Result.IsEmpty ? Loc.T("empty") : Loc.T("online");
                status = $"{load} · {i.Players}/{i.MaxPlayers} · {e.Result.PingMs} ms";
                dot = e.Result.IsFull ? Theme.Warn : Theme.PingColor(e.Result.PingMs);
            }
        }

        private void OnPlay()
        {
            ServerEntry e = SelectedServer();
            if (e == null || !e.IsConfigured || e.Result == null || e.Result.Status != QueryStatus.Online)
            {
                Open<ServerBrowserPanel>();
                return;
            }
            Join(e);
        }

        public void Join(ServerEntry e)
        {
            if (e.Result != null && e.Result.IsFull)
            {
                Modal.Show(_safe, e.Name, Loc.T("server_full_msg"), Loc.T("close"), null);
                App.Audio.PlayUi(UiSound.Error);
                return;
            }
            CloseOpenPanel();
            ShowConnect();
            App.Session.ConnectOnline(e);
        }

        public void StartLocal(bool lan)
        {
            CloseOpenPanel();
            ShowConnect();
            App.Session.StartLocal(lan);
        }

        private void ShowConnect()
        {
            if (_connect == null) _connect = ConnectOverlay.Create(_safe, this);
            _connect.Show();
        }

        public T Open<T>() where T : LobbyPanel
        {
            if (_openPanel is T same && same.IsOpen) return same;
            CloseOpenPanel();
            var root = UIFactory.Rect(_safe, typeof(T).Name);
            T panel = root.gameObject.AddComponent<T>();
            panel.Init(this);
            _openPanel = panel;
            _stage.SetFocus(1f);
            App.Audio.PlayUi(UiSound.Whoosh);
            return panel;
        }

        public void CloseOpenPanel()
        {
            if (_openPanel != null) _openPanel.Close();
            _openPanel = null;
            _stage.SetFocus(0f);
        }

        public void OnPanelClosed(LobbyPanel p)
        {
            if (_openPanel == p)
            {
                _openPanel = null;
                _stage.SetFocus(0f);
            }
            RefreshServerChip();
            RefreshProfile();
        }

        private void ConfirmExit()
        {
            Modal.Show(_safe, GameInfo.Title, Loc.T("exit_confirm"), Loc.T("yes"), App.Quit, Loc.T("no"), null);
        }

        private void Update()
        {
            // Android back button / Escape closes panels
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (_openPanel != null) CloseOpenPanel();
                else ConfirmExit();
            }
        }
    }

    /// <summary>Gentle scale/alpha pulse for glows.</summary>
    public sealed class Pulse : MonoBehaviour
    {
        private Graphic _g;
        private float _a;

        private void Awake()
        {
            _g = GetComponent<Graphic>();
            _a = _g != null ? _g.color.a : 1f;
        }

        private void Update()
        {
            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f);
            transform.localScale = Vector3.one * (0.95f + 0.08f * k);
            if (_g != null) _g.color = _g.color.WithAlpha(_a * (0.6f + 0.4f * k));
        }
    }
}
