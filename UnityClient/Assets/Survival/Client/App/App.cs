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
            Application.runInBackground = true;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Settings = GameSettings.Load();
            Profile = PlayerProfile.LoadOrCreate();
            Loc.Language = Settings.Language;
            QualityManager.Apply(Settings);

            Materials = new MaterialLibrary();
            Audio = gameObject.AddComponent<AudioManager>();
            Scenes = gameObject.AddComponent<SceneFlow>();
            Servers = new ServerDirectory();
            Session = new Session();
            UIRoot.EnsureEventSystem(transform);

            SceneManager.sceneLoaded += OnSceneLoaded;
            Log.Info("App", $"{GameInfo.Title} {GameInfo.Version} | {SystemInfo.deviceModel} | {SystemInfo.graphicsDeviceType} | " +
                            $"{SystemInfo.systemMemorySize} MB RAM | quality {Settings.Preset}");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
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
