using System.Collections;
using System.Threading.Tasks;
using Survival.Client.Rendering;
using Survival.Client.UI;
using Survival.Client.WorldView;
using Survival.Core;
using Survival.Gameplay;
using Survival.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = Survival.Gameplay.PlayerInput;

namespace Survival.Client.Game
{
    public enum CameraMode
    {
        FirstPerson,
        ThirdPerson,
    }

    /// <summary>
    /// Game scene: builds the island view (terrain streaming, vegetation, ocean, sky), runs local
    /// prediction from touch/keyboard/gamepad input, renders other players/bots and the HUD, and
    /// handles death/respawn, pause, reconnect and leaving.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        private Session _session;
        private ClientSimulation _sim;
        private Camera _cam;
        private SkyController _sky;
        private TerrainStreamer _terrain;
        private VegetationRenderer _vegetation;
        private RemoteViews _remotes;
        private GameHud _hud;
        private HumanoidView _localBody;
        private Task<float[]> _waterTask;
        private bool _ready;
        private float _yaw;
        private float _pitch;
        private float _eye = 1.62f;
        private float _bob;
        private float _landKick;
        private float _stepDistance;
        private bool _wasGrounded = true;
        private bool _wasSwimming;
        private Vector3 _lastFeet;
        private CameraMode _mode = CameraMode.FirstPerson;
        private float _fovKick;
        private bool _cursorLocked;
        private float _readyTimeout = 20f;
        private bool _jumpLatch;

        private void Start()
        {
            _session = App.Session;
            _sim = _session.Sim;
            if (_sim == null)
            {
                Log.Warn("Game", "No active session - back to lobby");
                App.Scenes.Load(App.SceneLobby);
                enabled = false;
                return;
            }

            _yaw = _session.Welcome.SpawnYaw;
            _cam = CameraRig.CreateMain("PlayerCamera", Mathf.Max(QualityManager.ViewDistance * 1.6f, 1800f), 68f);
            _sky = SkyController.Create(_cam, _cam.farClipPlane * 0.9f);
            _sky.FogStart = QualityManager.ViewDistance * 0.25f;
            _sky.FogEnd = QualityManager.ViewDistance * 1.35f;
            if (QualityManager.PostProcessing) PostFx.Create(transform, 0.7f, 0.25f, 6f, 8f);

            _terrain = TerrainStreamer.Create(_session.World, _cam.transform, QualityManager.ViewDistance);
            _vegetation = VegetationRenderer.Create(_session.World, _cam.transform, _cam);
            _remotes = RemoteViews.Create(_sim, _cam.transform);
            WorldRuntimeRef world = new WorldRuntimeRef(_session.World);
            _waterTask = Task.Run(() => WaterSurface.ComputeHeightGrid(world.World, 256));

            _localBody = HumanoidView.Create(transform, "LocalBody", HumanoidView.Palette.Survivor, App.Materials.Lit("character", Color.white, 0.05f, 0.25f), false);
            _localBody.gameObject.SetActive(false);

            _hud = GameHud.Create(transform);
            _hud.LeaveRequested = Leave;
            _hud.RespawnRequested = () =>
            {
                _session.RequestRespawn();
                App.Audio.PlayUi(UiSound.Confirm);
            };
            _hud.ToggleCameraRequested = () =>
            {
                _mode = _mode == CameraMode.FirstPerson ? CameraMode.ThirdPerson : CameraMode.FirstPerson;
                _localBody.gameObject.SetActive(_mode == CameraMode.ThirdPerson);
                _hud.SetPaused(false);
            };

            _session.Notice += OnNotice;
            _session.Died += OnDied;
            _session.Respawned += OnRespawned;
            QualityManager.Changed += OnQualityChanged;

            App.Audio.SetAmbience(AmbienceLayer.Wind, 0.35f);
            _lastFeet = ToUnity(_sim.Predicted.Position);
            PlaceCamera(0f);
            if (App.Scenes.FadeAlpha > 0f) StartCoroutine(App.Scenes.FadeTo(0f, 0.6f));
            StartCoroutine(WaitForWorld());
        }

        private sealed class WorldRuntimeRef
        {
            public readonly Survival.World.WorldRuntime World;
            public WorldRuntimeRef(Survival.World.WorldRuntime w) => World = w;
        }

        private IEnumerator WaitForWorld()
        {
            float t = 0f;
            while (true)
            {
                t += Time.unscaledDeltaTime;
                if (_waterTask != null && _waterTask.IsCompleted)
                {
                    Texture2D heightTex = _waterTask.Status == TaskStatus.RanToCompletion ? WaterSurface.CreateHeightTexture(_waterTask.Result, 256) : null;
                    WaterSurface.Create(_cam.transform, _session.World.Config.SeaLevel, Mathf.Min(_cam.farClipPlane * 1.6f, 7000f), 96, heightTex, _session.World.Config.Size);
                    _waterTask = null;
                }
                float progress = _terrain.NearReadyFraction;
                _hud.SetLoading(0.2f + progress * 0.8f, $"{Loc.T("loading_island")} {Mathf.RoundToInt(progress * 100)}%");
                if ((progress >= 0.999f && _waterTask == null) || t > _readyTimeout) break;
                yield return null;
            }
            _ready = true;
            _session.MarkSceneReady();
            _hud.HideLoading();
            App.Audio.PlayUi(UiSound.Confirm);
            if (_session.WorldMismatch) _hud.Toast(Loc.T("world_mismatch"), Theme.Bad, 8f);
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Notice -= OnNotice;
                _session.Died -= OnDied;
                _session.Respawned -= OnRespawned;
            }
            QualityManager.Changed -= OnQualityChanged;
            App.Audio?.StopAllAmbience();
            Cursor.lockState = CursorLockMode.None;
        }

        private void OnQualityChanged()
        {
            if (_terrain != null) _terrain.ViewDistance = QualityManager.ViewDistance;
            if (_cam != null)
            {
                _cam.farClipPlane = Mathf.Max(QualityManager.ViewDistance * 1.6f, 1800f);
                QualityManager.SetupCamera(_cam);
            }
            if (_sky != null)
            {
                _sky.FogStart = QualityManager.ViewDistance * 0.25f;
                _sky.FogEnd = QualityManager.ViewDistance * 1.35f;
                _sky.Sun.shadows = QualityManager.Shadows ? LightShadows.Soft : LightShadows.None;
            }
        }

        private void OnNotice(NoticeMsg n) => _hud.Notice(n);
        private void OnDied(DiedMsg d) => _hud.ShowDeath(d);

        private void OnRespawned()
        {
            _hud.HideDeath();
            _lastFeet = ToUnity(_sim.Predicted.Position);
        }

        private void Leave()
        {
            _hud.SetPaused(false);
            _session.Leave();
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (_sim == null || _session.Sim != _sim) return;
            float dt = Time.deltaTime;

            HandleMenuKeys();
            PlayerInput input = SampleInput(dt);
            if (_ready && _session.Phase == SessionPhase.Playing)
            {
                // A tap must survive frames in which no 30 Hz simulation tick runs (e.g. at 60 FPS)
                if ((input.Buttons & InputButtons.Jump) != 0) _jumpLatch = true;
                if (_jumpLatch) input.Buttons |= InputButtons.Jump;
                int ticks = _sim.Advance(dt, input);
                if (ticks > 0) _jumpLatch = false;
                _session.SendInputs(_sim.PendingSend);
            }
            _sim.UpdateRemotes();

            _sky.SetTime(_sim.TimeOfDay01);
            PlaceCamera(dt);
            FeedbackAndAudio(dt);
            _hud.UpdateHud(_session, _yaw, _cam.transform.position, _sim);
            _hud.UpdateNameplates(_cam, _sim.Remotes);
        }

        private void HandleMenuKeys()
        {
            var kb = Keyboard.current;
            bool escape = kb != null && kb.escapeKey.wasPressedThisFrame;
            var pad = Gamepad.current;
            if (pad != null && pad.startButton.wasPressedThisFrame) escape = true;
            if (_hud.Controls.Pressed("menu") || escape)
            {
                _hud.SetPaused(!_hud.Paused);
                SetCursorLock(false);
            }
        }

        private void SetCursorLock(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Merges touch, keyboard/mouse and gamepad into one PlayerInput.</summary>
        private PlayerInput SampleInput(float dt)
        {
            GameSettings s = App.Settings;
            MobileControls c = _hud.Controls;
            bool blocked = _hud.Paused || _hud.DeathVisible;

            Vector2 move = c.Move;
            Vector2 look = c.ConsumeLook() * (0.12f * s.LookSensitivity);
            bool jump = c.Pressed("jump");
            bool crouch = c.Toggled("crouch");
            bool sprint = c.Toggled("sprint") || (s.AutoSprintOnStickEdge && c.StickAtEdge);

            var kb = Keyboard.current;
            if (kb != null)
            {
                Vector2 k = Vector2.zero;
                if (kb.wKey.isPressed) k.y += 1;
                if (kb.sKey.isPressed) k.y -= 1;
                if (kb.dKey.isPressed) k.x += 1;
                if (kb.aKey.isPressed) k.x -= 1;
                if (k != Vector2.zero) move = k.normalized;
                jump |= kb.spaceKey.wasPressedThisFrame;
                if (kb.cKey.wasPressedThisFrame) c.SetToggle("crouch", !c.Toggled("crouch"));
                crouch = c.Toggled("crouch") || kb.leftCtrlKey.isPressed;
                sprint |= kb.leftShiftKey.isPressed;
            }
            var mouse = Mouse.current;
            if (mouse != null && !blocked)
            {
                if (mouse.rightButton.wasPressedThisFrame) SetCursorLock(!_cursorLocked);
                if (_cursorLocked) look += mouse.delta.ReadValue() * (0.08f * s.LookSensitivity);
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 ls = pad.leftStick.ReadValue();
                if (ls.sqrMagnitude > 0.02f) move = ls;
                Vector2 rs = pad.rightStick.ReadValue();
                look += rs * (160f * s.LookSensitivity * dt);
                jump |= pad.buttonSouth.wasPressedThisFrame;
                if (pad.buttonEast.wasPressedThisFrame) c.SetToggle("crouch", !c.Toggled("crouch"));
                sprint |= pad.leftStickButton.isPressed;
            }

            if (blocked)
            {
                move = Vector2.zero;
                look = Vector2.zero;
                jump = false;
            }

            _yaw = Mathf.Repeat(_yaw + look.x, 360f);
            _pitch = Mathf.Clamp(_pitch + (s.InvertY ? look.y : -look.y), -85f, 85f);

            var input = new PlayerInput
            {
                MoveX = PlayerInput.EncodeAxis(move.x),
                MoveY = PlayerInput.EncodeAxis(move.y),
            };
            input.YawDegrees = _yaw;
            input.PitchDegrees = _pitch;
            if (jump) input.Buttons |= InputButtons.Jump;
            if (crouch) input.Buttons |= InputButtons.Crouch;
            if (sprint) input.Buttons |= InputButtons.Sprint;
            return input;
        }

        private void PlaceCamera(float dt)
        {
            MoveState st = _sim.Predicted;
            Vector3 feet = ToUnity(_sim.RenderPosition);
            float targetEye = (st.Flags & MoveFlags.Dead) != 0 ? 0.35f : st.Crouching ? 1.0f : st.Swimming ? 1.35f : 1.62f;
            _eye = Mathf.Lerp(_eye, targetEye, 1f - Mathf.Exp(-dt * 10f));
            float speed = new Vector2(st.Velocity.X, st.Velocity.Z).magnitude;
            if (st.Grounded) _bob += dt * speed * 1.9f;
            float bobAmount = st.Grounded ? Mathf.Clamp01(speed / 6f) * 0.045f : 0f;
            _landKick = Mathf.Lerp(_landKick, 0f, 1f - Mathf.Exp(-dt * 8f));
            Vector3 eye = feet + new Vector3(0, _eye + Mathf.Sin(_bob * Mathf.PI) * bobAmount - _landKick, 0);
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, Mathf.Sin(_bob * Mathf.PI * 0.5f) * bobAmount * 8f);

            if (_mode == CameraMode.ThirdPerson)
            {
                Vector3 back = rot * new Vector3(0.6f, 0.35f, -3.4f);
                Vector3 camPos = eye + back;
                float ground = _session.World.GroundHeight(camPos.x, camPos.z) + 0.4f;
                if (camPos.y < ground) camPos.y = ground;
                _cam.transform.SetPositionAndRotation(camPos, rot);
                _localBody.transform.position = feet;
                _localBody.transform.rotation = Quaternion.Euler(0, _yaw, 0);
                _localBody.Speed = speed;
                _localBody.Crouching = st.Crouching;
                _localBody.Swimming = st.Swimming;
                _localBody.Dead = (st.Flags & MoveFlags.Dead) != 0;
            }
            else
            {
                _cam.transform.SetPositionAndRotation(eye, rot);
            }

            float fovTarget = 68f + (st.Sprinting ? 6f : 0f);
            _fovKick = Mathf.Lerp(_fovKick, fovTarget, 1f - Mathf.Exp(-dt * 6f));
            _cam.fieldOfView = _fovKick;
        }

        private void FeedbackAndAudio(float dt)
        {
            MoveState st = _sim.Predicted;
            Vector3 feet = ToUnity(st.Position);
            float sea = _session.World.Config.SeaLevel;

            if (!_wasGrounded && st.Grounded)
            {
                _landKick = 0.12f;
                App.Audio.Play(SfxSound.Land, 0.6f);
            }
            if (!_wasSwimming && st.Swimming) App.Audio.Play(SfxSound.Splash, 0.7f);
            _wasGrounded = st.Grounded;
            _wasSwimming = st.Swimming;

            if (st.Grounded)
            {
                _stepDistance += Vector2.Distance(new Vector2(feet.x, feet.z), new Vector2(_lastFeet.x, _lastFeet.z));
                float stride = st.Sprinting ? 2.1f : st.Crouching ? 1.1f : 1.6f;
                if (_stepDistance > stride)
                {
                    _stepDistance = 0f;
                    bool sand = feet.y < sea + 3f;
                    App.Audio.Play(sand ? SfxSound.FootstepSand : SfxSound.Footstep, st.Crouching ? 0.25f : 0.5f, Random.Range(0.92f, 1.08f));
                }
            }
            _lastFeet = feet;

            // Ambience: ocean near the coast, wind higher up, birds by day, insects at night
            float altitude = feet.y - sea;
            float daylight = _sky.Daylight;
            float coast = Mathf.Clamp01(1f - altitude / 40f);
            App.Audio.SetAmbience(AmbienceLayer.Ocean, 0.15f + coast * 0.6f);
            App.Audio.SetAmbience(AmbienceLayer.Wind, 0.2f + Mathf.Clamp01(altitude / 180f) * 0.6f);
            App.Audio.SetAmbience(AmbienceLayer.Birds, daylight * 0.45f * (1f - coast * 0.5f));
            App.Audio.SetAmbience(AmbienceLayer.Night, (1f - daylight) * 0.4f);
        }
    }
}
