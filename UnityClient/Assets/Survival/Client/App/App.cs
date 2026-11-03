using Survival.Client.Rendering;
using Survival.Client.UI;
using Survival.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Survival.Client
{
    /// <summary>
    /// Application entry point and service hub. Created automatically before the first scene loads
    /// (no scene objects or prefabs required), survives scene changes and wires each scene to its
    /// controller: Boot -> Intro, Lobby -> Lobby, Game -> Game.
    /// </summary>
    public sealed class App : MonoBehaviour
    {
        public const string SceneBoot = "Boot";
        public const string SceneLobby = "Lobby";
        public const string SceneGame = "Game";

        public static App Instance { get; private set; }
        public static GameSettings Settings { get; private set; }
        public static PlayerProfile Profile { get; private set; }
        public static AudioManager Audio { get; private set; }
        public static SceneFlow Scenes { get; private set; }
        public static Session Session { get; private set; }
        public static ServerDirectory Servers { get; private set; }
        public static MaterialLibrary Materials { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            Log.Sink = new UnityLogSink();
            Log.MinLevel = Debug.isDebugBuild ? LogLevel.Debug : LogLevel.Info;

            var go = new GameObject("[App]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<App>();
            Instance.Initialize();
        }

        private void Initialize()
        {
            // Scene wiring and error reporting first: if any service below fails, the game must still
            // start and show the error on screen instead of freezing after the Unity splash.
            SceneManager.sceneLoaded += OnSceneLoaded;
            gameObject.AddComponent<ErrorOverlay>();
            Application.runInBackground = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.LandscapeLeft;

            Step("settings", () => Settings = GameSettings.Load());
            if (Settings == null) Settings = new GameSettings();
            Step("profile", () => Profile = PlayerProfile.LoadOrCreate());
            Loc.Language = Settings.Language;
            Step("quality", () => QualityManager.Apply(Settings));
            Step("materials", () => Materials = new MaterialLibrary());
            Step("audio", () => Audio = gameObject.AddComponent<AudioManager>());
            Step("scenes", () => Scenes = gameObject.AddComponent<SceneFlow>());
            Step("servers", () => Servers = new ServerDirectory());
            Step("session", () => Session = new Session());
            Step("eventsystem", () => UIRoot.EnsureEventSystem(transform));

            Log.Info("App", $"{GameInfo.Title} {GameInfo.Version} | {SystemInfo.deviceModel} | {SystemInfo.graphicsDeviceType} | " +
                            $"{SystemInfo.systemMemorySize} MB RAM | quality {Settings.Preset}");
        }

        private static void Step(string name, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[App] Startup step '{name}' failed: {e}");
            }
        }

        private static int _wiredSceneHandle;

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            WireScene(scene, "sceneLoaded");
        }

        /// <summary>
        /// Creates the controller for a scene exactly once. Called from sceneLoaded and, as a fallback, from
        /// Update: on device the first scene's sceneLoaded event was observed not to reach a handler
        /// registered in BeforeSceneLoad, which left the game on a black screen after the Unity splash.
        /// </summary>
        private static void WireScene(Scene scene, string source)
        {
            if (!scene.IsValid() || !scene.isLoaded || scene.handle == _wiredSceneHandle) return;
            _wiredSceneHandle = scene.handle;
            Debug.Log($"[App] Scene loaded: {scene.name} (via {source})");
            var root = new GameObject("[" + scene.name + "Controller]");
            switch (scene.name)
            {
                case SceneBoot:
                    root.AddComponent<Intro.IntroController>();
                    break;
                case SceneLobby:
                    root.AddComponent<Lobby.LobbyController>();
                    break;
                case SceneGame:
                    root.AddComponent<Game.GameController>();
                    break;
                default:
                    Log.Warn("App", "No controller for scene " + scene.name);
                    Destroy(root);
                    break;
            }
        }

        private void Update()
        {
            WireScene(SceneManager.GetActiveScene(), "update");
            Session?.Update(Time.unscaledDeltaTime);
            Servers?.Update();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Settings?.Save();
                Session?.OnAppPaused();
            }
        }

        private void OnApplicationQuit()
        {
            Settings?.Save();
            Session?.Shutdown();
            Servers?.Dispose();
        }

        public static void Quit()
        {
            Settings?.Save();
            Session?.Shutdown();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary>Routes engine-independent logging to the Unity console / logcat.</summary>
    internal sealed class UnityLogSink : ILogSink
    {
        public void Write(LogLevel level, string category, string message)
        {
            string line = "[" + category + "] " + message;
            switch (level)
            {
                case LogLevel.Error: Debug.LogError(line); break;
                case LogLevel.Warning: Debug.LogWarning(line); break;
                default: Debug.Log(line); break;
            }
        }
    }
}
