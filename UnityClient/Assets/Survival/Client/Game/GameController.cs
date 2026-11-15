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

        // ---- gathering ----
        private SurvivalUi _ui;
        private Transform _toolPivot;
        private MeshFilter _toolMesh;
        private ItemId _toolShown = (ItemId)255;
        private float _swing = 1f;          // 0..1 swing animation progress (1 = idle)
        private float _swingCooldown;
        private uint _pendingHit;
        private float _pendingHitDelay;
        private bool _hasTarget;
        private RemoteEntity _entityTarget;
        private uint _pendingAttack;
        private float _lastHealth = 100f;
        private Survival.World.WorldNode _target;
        private readonly System.Collections.Generic.Dictionary<uint, byte> _nodeHealth = new System.Collections.Generic.Dictionary<uint, byte>();
        private readonly System.Collections.Generic.List<Survival.World.WorldNode> _nodeScratch = new System.Collections.Generic.List<Survival.World.WorldNode>(32);

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

            _ui = SurvivalUi.Create(_hud.Safe, _hud.CanvasRoot, _session);
            _ui.DrinkRequested = () =>
            {
                _session.SendDrink();
                App.Audio.Play(SfxSound.Splash, 0.35f, 1.4f);
            };
            BuildToolView();
            _ui.CookRequested = () => _session.SendCook();
            _session.HitResult += OnHitResult;
            _session.AttackResult += OnAttackResult;
            _session.NodesChanged += OnNodesChanged;
            OnNodesChanged();

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
                _session.HitResult -= OnHitResult;
                _session.AttackResult -= OnAttackResult;
                _session.NodesChanged -= OnNodesChanged;
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

        private void OnNotice(NoticeMsg n)
        {
            // Short server codes are localized on the client
            if (n.Text == "craft_failed" || n.Text == "place_failed")
            {
                _ui.ShowMessage(Loc.T(n.Text), Theme.Bad);
                App.Audio.PlayUi(UiSound.Error);
                return;
            }
            _hud.Notice(n);
        }
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
            UpdateGathering(dt);

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
            bool bag = _hud.Controls.Pressed("bag") || (kb != null && kb.tabKey.wasPressedThisFrame);
            if (bag && !_hud.Paused && !_hud.DeathVisible) _ui.SetInventoryOpen(!_ui.InventoryOpen);
            if (_ui.InventoryOpen && escape)
            {
                _ui.SetInventoryOpen(false);
                return;
            }
            if (kb != null && !_ui.InventoryOpen)
            {
                if (kb.digit1Key.wasPressedThisFrame) _ui.SelectHotbar(0);
                if (kb.digit2Key.wasPressedThisFrame) _ui.SelectHotbar(1);
                if (kb.digit3Key.wasPressedThisFrame) _ui.SelectHotbar(2);
                if (kb.digit4Key.wasPressedThisFrame) _ui.SelectHotbar(3);
                if (kb.digit5Key.wasPressedThisFrame) _ui.SelectHotbar(4);
                if (kb.digit6Key.wasPressedThisFrame) _ui.SelectHotbar(5);
                if (kb.eKey.wasPressedThisFrame) _ui.UseHotbarItem();
            }
            if (_hud.Controls.Pressed("menu") || escape)
            {
                _ui.SetInventoryOpen(false);
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
            bool blocked = _hud.Paused || _hud.DeathVisible || _ui.InventoryOpen;

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
            input.HotbarSlot = (byte)_ui.SelectedHotbar;
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
            if (_toolPivot != null) _toolPivot.gameObject.SetActive(_mode == CameraMode.FirstPerson && (st.Flags & MoveFlags.Dead) == 0);

            float fovTarget = 68f + (st.Sprinting ? 6f : 0f);
            _fovKick = Mathf.Lerp(_fovKick, fovTarget, 1f - Mathf.Exp(-dt * 6f));
            _cam.fieldOfView = _fovKick;
        }

        // ------------------------------------------------------------------ gathering

        private void BuildToolView()
        {
            _toolPivot = new GameObject("ToolPivot").transform;
            _toolPivot.SetParent(_cam.transform, false);
            var go = new GameObject("Tool");
            go.transform.SetParent(_toolPivot, false);
            _toolMesh = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = App.Materials.Lit("tool", Color.white, 0.08f, 0.1f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void OnNodesChanged()
        {
            if (_vegetation != null) _vegetation.SetDepleted(_session.DepletedNodes);
            foreach (uint id in _session.DepletedNodes) _nodeHealth.Remove(id);
        }

        private void OnHitResult(HitResultMsg h)
        {
            if (h.Health == 0) _nodeHealth.Remove(h.NodeId);
            else _nodeHealth[h.NodeId] = h.Health;
            _ui.ShowGain(h.Item, h.Count);
            _ui.ShowGain(h.Extra, h.ExtraCount);
            if (h.Full) _ui.ShowMessage(Loc.T("inv_full"), Theme.Warn);
            bool tree = h.Item == ItemId.Wood;
            bool bush = h.Item == ItemId.Fiber;
            App.Audio.Play(bush ? SfxSound.Pickup : tree ? SfxSound.Chop : SfxSound.Mine, 0.75f, Random.Range(0.92f, 1.08f));
            _landKick = Mathf.Max(_landKick, 0.025f);
        }

        private void OnAttackResult(AttackResultMsg a)
        {
            App.Audio.Play(SfxSound.Chop, 0.7f, Random.Range(0.75f, 0.9f));
            _landKick = Mathf.Max(_landKick, 0.03f);
            if (!a.Killed) return;
            _ui.ShowGain(ItemId.RawMeat, a.Meat);
            _ui.ShowGain(ItemId.Hide, a.Hide);
            _ui.ShowGain(ItemId.Bone, a.Bone);
            App.Audio.Play(SfxSound.Pickup, 0.8f);
        }

        /// <summary>Nearest animal / bot in front of the player within melee reach.</summary>
        private RemoteEntity FindEntityTarget()
        {
            MoveState st = _sim.Predicted;
            float yawRad = _yaw * Mathf.Deg2Rad;
            float fx = Mathf.Sin(yawRad), fz = Mathf.Cos(yawRad);
            RemoteEntity best = null;
            float bestDist = CombatRules.MeleeReach;
            foreach (RemoteEntity e in _sim.Remotes.Values)
            {
                if (e.Type != EntityType.Animal && e.Type != EntityType.Bot) continue;
                if ((e.Flags & MoveFlags.Dead) != 0) continue;
                float dx = e.Position.X - st.Position.X, dz = e.Position.Z - st.Position.Z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > bestDist || Mathf.Abs(e.Position.Y - st.Position.Y) > 2.5f) continue;
                if (d > 0.6f && (dx * fx + dz * fz) / d < 0.45f) continue;
                bestDist = d;
                best = e;
            }
            return best;
        }

        /// <summary>True when a placed campfire is close enough to cook on.</summary>
        private bool CampfireNearby()
        {
            MoveState st = _sim.Predicted;
            foreach (RemoteEntity e in _sim.Remotes.Values)
            {
                if (e.Type != EntityType.Structure) continue;
                float dx = e.Position.X - st.Position.X, dz = e.Position.Z - st.Position.Z;
                if (dx * dx + dz * dz <= CombatRules.CookReach * CombatRules.CookReach) return true;
            }
            return false;
        }

        /// <summary>Nearest gatherable node the player is facing and can reach.</summary>
        private bool FindTarget(out Survival.World.WorldNode best)
        {
            best = default;
            MoveState st = _sim.Predicted;
            float px = st.Position.X, pz = st.Position.Z;
            float yawRad = _yaw * Mathf.Deg2Rad;
            float fx = Mathf.Sin(yawRad), fz = Mathf.Cos(yawRad);
            _nodeScratch.Clear();
            _session.World.Nodes.Query(px, pz, GatherRules.Reach + 3f, _nodeScratch);
            float bestScore = float.MaxValue;
            bool found = false;
            for (int i = 0; i < _nodeScratch.Count; i++)
            {
                Survival.World.WorldNode n = _nodeScratch[i];
                if (!GatherRules.CanGather(n.Kind) || _session.DepletedNodes.Contains(n.Id)) continue;
                float dx = n.X - px, dz = n.Z - pz;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float edge = d - n.Radius;
                if (edge > GatherRules.Reach || Mathf.Abs(n.Y - st.Position.Y) > 4f) continue;
                float facing = d < 0.05f ? 1f : (dx * fx + dz * fz) / d;
                // Wide nodes (rocks) are easy to face; thin ones need a tighter aim
                float minFacing = edge < 0.8f ? 0.2f : 0.62f;
                if (facing < minFacing) continue;
                float score = edge + (1f - facing) * 4f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = n;
                    found = true;
                }
            }
            return found;
        }

        private void UpdateGathering(float dt)
        {
            Inventory inv = _session.Inventory;
            MoveState st = _sim.Predicted;
            bool alive = (st.Flags & MoveFlags.Dead) == 0;
            bool playing = _ready && alive && _session.Phase == SessionPhase.Playing;
            bool blocked = _hud.Paused || _hud.DeathVisible || _ui.InventoryOpen;

            // Held item model follows the selected hotbar slot
            ItemStack held = inv != null ? inv.Slots[_ui.SelectedHotbar] : default;
            ItemId show = held.IsEmpty ? ItemId.None : held.Id;
            if (show != _toolShown)
            {
                _toolShown = show;
                _toolMesh.sharedMesh = show == ItemId.None ? null : Models.Tool(show);
            }

            // Animals and bots take priority over trees/rocks behind them
            _entityTarget = playing ? FindEntityTarget() : null;
            _hasTarget = playing && _entityTarget == null && FindTarget(out _target);
            float health01 = 1f;
            if (_hasTarget && _nodeHealth.TryGetValue(_target.Id, out byte hp)) health01 = hp / 255f;
            bool placeable = !held.IsEmpty && ItemDb.Get(held.Id) != null && ItemDb.Get(held.Id).Kind == ItemKind.Placeable;
            if (_entityTarget != null)
            {
                string name = _entityTarget.Type == EntityType.Animal ? Loc.T("animal_" + _entityTarget.SubType) : _entityTarget.Name;
                _ui.SetTarget(!blocked, name, _entityTarget.Health01);
            }
            else if (placeable && playing)
            {
                _ui.SetTarget(!blocked, Loc.T("place") + ": " + ItemVisuals.Name(held.Id), 1f);
            }
            else
            {
                _ui.SetTarget(_hasTarget && !blocked, _hasTarget ? ItemVisuals.NodeName(_target.Kind) : string.Empty, health01);
            }
            _ui.SetCookVisible(playing && !blocked && inv != null && inv.CountOf(ItemId.RawMeat) > 0 && CampfireNearby());

            // Damage feedback
            float healthNow = _sim.Stats.Health;
            if (healthNow < _lastHealth - 1.5f && alive) App.Audio.Play(SfxSound.Hurt, 0.8f);
            _lastHealth = healthNow;
            _ui.SetDrinkVisible(playing && !blocked && st.Position.Y < _session.World.Config.SeaLevel + 0.45f && _sim.Stats.Thirst < 95f);

            // Swing: hold the HIT button (or left mouse button while the cursor is captured)
            _swingCooldown -= dt;
            bool attack = _hud.Controls.Held("attack");
            var mouse = Mouse.current;
            if (mouse != null && _cursorLocked && mouse.leftButton.isPressed) attack = true;
            var pad = Gamepad.current;
            if (pad != null && pad.rightTrigger.isPressed) attack = true;
            if (attack && playing && !blocked && _swingCooldown <= 0f)
            {
                _swingCooldown = GatherRules.SwingSeconds;
                _swing = 0f;
                App.Audio.Play(SfxSound.Swing, 0.5f, Random.Range(0.9f, 1.1f));
                _pendingHitDelay = GatherRules.SwingSeconds * 0.4f; // impact moment of the animation
                if (_entityTarget != null)
                {
                    _pendingAttack = _entityTarget.Id;
                }
                else if (placeable)
                {
                    // Put the held structure on the ground 2 m ahead
                    float yr = _yaw * Mathf.Deg2Rad;
                    _session.SendPlace(_ui.SelectedHotbar, st.Position.X + Mathf.Sin(yr) * 2f, st.Position.Z + Mathf.Cos(yr) * 2f, _yaw);
                    _swingCooldown = 0.8f;
                }
                else if (_hasTarget)
                {
                    _pendingHit = _target.Id;
                }
            }
            if (_pendingHit != 0 || _pendingAttack != 0)
            {
                _pendingHitDelay -= dt;
                if (_pendingHitDelay <= 0f)
                {
                    if (_pendingAttack != 0) _session.SendAttack(_pendingAttack);
                    else _session.SendHit(_pendingHit);
                    _pendingHit = 0;
                    _pendingAttack = 0;
                }
            }

            // First-person tool pose: rest at the lower right, chop forward and down on a swing
            _swing = Mathf.Min(1f, _swing + dt / GatherRules.SwingSeconds);
            float t = _swing;
            float strike = t < 0.4f ? Mathf.SmoothStep(0f, 1f, t / 0.4f) : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.4f) / 0.6f);
            float speed = new Vector2(st.Velocity.X, st.Velocity.Z).magnitude;
            float sway = Mathf.Sin(_bob * Mathf.PI) * Mathf.Clamp01(speed / 6f) * 0.012f;
            _toolPivot.localPosition = new Vector3(0.34f - strike * 0.12f, -0.34f + sway + strike * 0.08f, 0.62f + strike * 0.18f);
            _toolPivot.localRotation = Quaternion.Euler(-38f + strike * 78f, -12f - strike * 14f, 8f);
            // Small items (rock, food, resources) are modelled larger than hand size for the icons
            _toolPivot.localScale = Vector3.one * (ItemDb.ToolOf(held) == ItemDb.Hands || held.Id == ItemId.Rock ? 0.55f : 1f);
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
