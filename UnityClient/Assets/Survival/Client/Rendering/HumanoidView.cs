using UnityEngine;

namespace Survival.Client.Rendering
{
    /// <summary>
    /// Procedural low-poly survivor (hooded jacket, scarf, backpack) built from parts, animated in
    /// code: idle breathing/weight shift/look-around, walk/run cycle from speed, crouch, swim and death.
    /// Used for the lobby hero, other players and bots (with different clothing colours).
    /// </summary>
    public sealed class HumanoidView : MonoBehaviour
    {
        public struct Palette
        {
            public Color32 Jacket, Pants, Boots, Skin, Hood, Scarf, Pack;

            public static Palette Survivor => new Palette
            {
                Jacket = MeshBuilder.Rgb(0.28f, 0.32f, 0.24f),
                Pants = MeshBuilder.Rgb(0.20f, 0.20f, 0.23f),
                Boots = MeshBuilder.Rgb(0.22f, 0.15f, 0.10f),
                Skin = MeshBuilder.Rgb(0.78f, 0.6f, 0.48f),
                Hood = MeshBuilder.Rgb(0.24f, 0.27f, 0.21f),
                Scarf = MeshBuilder.Rgb(0.75f, 0.36f, 0.14f),
                Pack = MeshBuilder.Rgb(0.36f, 0.27f, 0.18f),
            };

            public static Palette FromSeed(uint seed, bool bot)
            {
                var rng = new System.Random((int)seed);
                float h = (float)rng.NextDouble();
                Color jacket = Color.HSVToRGB(h, bot ? 0.25f : 0.45f, bot ? 0.32f : 0.42f);
                Color scarf = bot ? new Color(0.55f, 0.55f, 0.58f) : Color.HSVToRGB((h + 0.45f) % 1f, 0.7f, 0.75f);
                Palette p = Survivor;
                p.Jacket = jacket;
                p.Hood = (Color)jacket * 0.85f;
                p.Scarf = scarf;
                p.Pants = Color.HSVToRGB((h + 0.6f) % 1f, 0.15f, 0.2f + (float)rng.NextDouble() * 0.1f);
                return p;
            }
        }

        private Transform _hips, _spine, _chest, _head, _armL, _armR, _foreL, _foreR, _thighL, _thighR, _shinL, _shinR;
        private float _phase;
        private float _speed;
        private float _blendMove;
        private float _crouch;
        private float _swim;
        private float _dead;
        private float _lookTimer;
        private float _lookYaw;
        private float _lookYawTarget;
        private float _seed;

        public bool Crouching;
        public bool Swimming;
        public bool Dead;
        /// <summary>Horizontal speed in m/s (drives the gait).</summary>
        public float Speed;
        /// <summary>Extra idle liveliness (lobby hero).</summary>
        public bool Expressive;

        public static HumanoidView Create(Transform parent, string name, Palette pal, Material mat, bool detailed = true)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<HumanoidView>();
            view.Build(pal, mat, detailed);
            view._seed = Random.value * 100f;
            return view;
        }

        private readonly System.Collections.Generic.List<Mesh> _ownedMeshes = new System.Collections.Generic.List<Mesh>(12);

        private void OnDestroy()
        {
            // Meshes are generated per character (palette colours): release them with the view
            foreach (Mesh m in _ownedMeshes) if (m != null) Destroy(m);
            _ownedMeshes.Clear();
        }

        private Transform Part(string name, Transform parent, Vector3 localPos, Mesh mesh, Material mat)
        {
            if (mesh != null && !_ownedMeshes.Contains(mesh)) _ownedMeshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            if (mesh != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return go.transform;
        }

        private static readonly MeshBuilder MB = new MeshBuilder();

        private static Mesh BoxMesh(string name, Vector3 center, Vector3 size, Color32 c, float taper = 1f)
        {
            MB.Clear();
            MB.Box(center, size, c, taper);
            return MB.ToMesh(name);
        }

        private void Build(Palette p, Material mat, bool detailed)
        {
            _hips = Part("Hips", transform, new Vector3(0, 0.95f, 0), BoxMesh("hips", Vector3.zero, new Vector3(0.36f, 0.2f, 0.22f), p.Pants), mat);
            _spine = Part("Spine", _hips, new Vector3(0, 0.1f, 0), null, mat);

            MB.Clear();
            MB.Box(new Vector3(0, 0.26f, 0), new Vector3(0.44f, 0.52f, 0.26f), p.Jacket, 0.9f);
            if (detailed)
            {
                MB.Box(new Vector3(0, 0.5f, 0.0f), new Vector3(0.3f, 0.1f, 0.24f), p.Scarf);                 // scarf
                MB.Box(new Vector3(0, 0.3f, -0.2f), new Vector3(0.34f, 0.42f, 0.16f), p.Pack, 0.92f);       // backpack
                MB.Box(new Vector3(0, 0.56f, -0.2f), new Vector3(0.3f, 0.08f, 0.14f), MeshBuilder.Shade(p.Pack, 0.8f)); // bedroll
                MB.Box(new Vector3(0.12f, 0.2f, 0.135f), new Vector3(0.1f, 0.12f, 0.02f), MeshBuilder.Shade(p.Jacket, 0.8f)); // pocket
            }
            _chest = Part("Chest", _spine, Vector3.zero, MB.ToMesh("chest"), mat);

            MB.Clear();
            MB.Blob(new Vector3(0, 0.13f, 0.01f), new Vector3(0.12f, 0.14f, 0.13f), p.Skin, detailed ? 1 : 0);
            MB.Blob(new Vector3(0, 0.17f, -0.02f), new Vector3(0.155f, 0.165f, 0.165f), p.Hood, 0, 0.05f, 3);   // hood
            if (detailed)
            {
                MB.Box(new Vector3(0.045f, 0.15f, 0.12f), new Vector3(0.03f, 0.02f, 0.01f), MeshBuilder.Rgb(0.1f, 0.1f, 0.1f));
                MB.Box(new Vector3(-0.045f, 0.15f, 0.12f), new Vector3(0.03f, 0.02f, 0.01f), MeshBuilder.Rgb(0.1f, 0.1f, 0.1f));
            }
            _head = Part("Head", _chest, new Vector3(0, 0.56f, 0), MB.ToMesh("head"), mat);

            Mesh upperArm = BoxMesh("uarm", new Vector3(0, -0.15f, 0), new Vector3(0.12f, 0.32f, 0.12f), p.Jacket, 0.9f);
            Mesh foreArm = BoxMesh("farm", new Vector3(0, -0.14f, 0), new Vector3(0.1f, 0.3f, 0.1f), p.Jacket, 0.85f);
            Mesh hand = BoxMesh("hand", new Vector3(0, -0.05f, 0), new Vector3(0.08f, 0.1f, 0.06f), p.Skin);
            _armL = Part("ArmL", _chest, new Vector3(-0.28f, 0.46f, 0), upperArm, mat);
            _armR = Part("ArmR", _chest, new Vector3(0.28f, 0.46f, 0), upperArm, mat);
            _foreL = Part("ForeL", _armL, new Vector3(0, -0.31f, 0), foreArm, mat);
            _foreR = Part("ForeR", _armR, new Vector3(0, -0.31f, 0), foreArm, mat);
            Part("HandL", _foreL, new Vector3(0, -0.29f, 0), hand, mat);
            Part("HandR", _foreR, new Vector3(0, -0.29f, 0), hand, mat);

            Mesh thigh = BoxMesh("thigh", new Vector3(0, -0.2f, 0), new Vector3(0.16f, 0.42f, 0.17f), p.Pants, 0.9f);
            MB.Clear();
            MB.Box(new Vector3(0, -0.2f, 0), new Vector3(0.13f, 0.4f, 0.14f), p.Pants, 0.9f);
            MB.Box(new Vector3(0, -0.4f, 0.04f), new Vector3(0.14f, 0.12f, 0.26f), p.Boots);
            Mesh shin = MB.ToMesh("shin");
            _thighL = Part("ThighL", _hips, new Vector3(-0.1f, -0.06f, 0), thigh, mat);
            _thighR = Part("ThighR", _hips, new Vector3(0.1f, -0.06f, 0), thigh, mat);
            _shinL = Part("ShinL", _thighL, new Vector3(0, -0.42f, 0), shin, mat);
            _shinR = Part("ShinR", _thighR, new Vector3(0, -0.42f, 0), shin, mat);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            float t = Time.time + _seed;
            _speed = Mathf.Lerp(_speed, Speed, 1f - Mathf.Exp(-dt * 8f));
            _blendMove = Mathf.Clamp01(_speed / 1.2f);
            _crouch = Mathf.MoveTowards(_crouch, Crouching ? 1f : 0f, dt * 5f);
            _swim = Mathf.MoveTowards(_swim, Swimming ? 1f : 0f, dt * 3f);
            _dead = Mathf.MoveTowards(_dead, Dead ? 1f : 0f, dt * 2.5f);

            float run = Mathf.Clamp01((_speed - 4.5f) / 2.5f);
            float cadence = Mathf.Lerp(1.6f, 2.6f, run);
            _phase += dt * Mathf.Max(0.0f, _speed) * cadence * 0.9f;
            float s = Mathf.Sin(_phase * Mathf.PI);
            float c = Mathf.Cos(_phase * Mathf.PI);

            // Idle
            float breathe = Mathf.Sin(t * 1.8f) * 0.012f;
            float sway = Mathf.Sin(t * 0.6f) * (Expressive ? 2.2f : 1f);
            _lookTimer -= dt;
            if (_lookTimer <= 0f)
            {
                _lookTimer = Random.Range(2.5f, 6f);
                _lookYawTarget = Expressive ? Random.Range(-35f, 35f) : Random.Range(-15f, 15f);
            }
            _lookYaw = Mathf.Lerp(_lookYaw, _lookYawTarget * (1f - _blendMove), 1f - Mathf.Exp(-dt * 2f));

            float legSwing = Mathf.Lerp(28f, 45f, run) * _blendMove;
            float armSwing = Mathf.Lerp(22f, 50f, run) * _blendMove;
            float bob = Mathf.Abs(s) * 0.05f * _blendMove;
            float crouchDrop = _crouch * 0.32f;

            _hips.localPosition = new Vector3(0, 0.95f + bob - crouchDrop + breathe * 0.3f, 0);
            _hips.localRotation = Quaternion.Euler(0, s * 6f * _blendMove, sway * (1f - _blendMove));
            _spine.localRotation = Quaternion.Euler(8f * run + 25f * _crouch, 0, -sway * 0.6f * (1f - _blendMove));
            _chest.localScale = new Vector3(1f, 1f + breathe, 1f + breathe * 1.5f);
            _head.localRotation = Quaternion.Euler(-6f * run - 10f * _crouch, _lookYaw, 0);

            _thighL.localRotation = Quaternion.Euler(-s * legSwing - 70f * _crouch, 0, 0);
            _thighR.localRotation = Quaternion.Euler(s * legSwing - 70f * _crouch, 0, 0);
            _shinL.localRotation = Quaternion.Euler(Mathf.Max(0, c) * legSwing * 1.2f + 95f * _crouch, 0, 0);
            _shinR.localRotation = Quaternion.Euler(Mathf.Max(0, -c) * legSwing * 1.2f + 95f * _crouch, 0, 0);

            float idleArm = Expressive ? Mathf.Sin(t * 0.9f) * 3f : 0f;
            _armL.localRotation = Quaternion.Euler(s * armSwing + idleArm, 0, -6f - 4f * (1f - _blendMove));
            _armR.localRotation = Quaternion.Euler(-s * armSwing - idleArm, 0, 6f + 4f * (1f - _blendMove));
            _foreL.localRotation = Quaternion.Euler(-15f - 25f * run - Mathf.Max(0, s) * 15f * _blendMove, 0, 0);
            _foreR.localRotation = Quaternion.Euler(-15f - 25f * run - Mathf.Max(0, -s) * 15f * _blendMove, 0, 0);

            // Swimming: body horizontal, breast-stroke style arms
            if (_swim > 0.01f)
            {
                float stroke = Mathf.Sin(t * 3f);
                _hips.localRotation = Quaternion.Slerp(_hips.localRotation, Quaternion.Euler(70f, 0, 0), _swim);
                _hips.localPosition = Vector3.Lerp(_hips.localPosition, new Vector3(0, 0.75f, 0), _swim);
                _armL.localRotation = Quaternion.Slerp(_armL.localRotation, Quaternion.Euler(-150f + stroke * 30f, 0, -30f + stroke * 25f), _swim);
                _armR.localRotation = Quaternion.Slerp(_armR.localRotation, Quaternion.Euler(-150f + stroke * 30f, 0, 30f - stroke * 25f), _swim);
                _thighL.localRotation = Quaternion.Slerp(_thighL.localRotation, Quaternion.Euler(stroke * 20f, 0, 0), _swim);
                _thighR.localRotation = Quaternion.Slerp(_thighR.localRotation, Quaternion.Euler(-stroke * 20f, 0, 0), _swim);
            }

            // Death: collapse to the ground
            if (_dead > 0.01f)
            {
                float k = _dead * _dead;
                _hips.localRotation = Quaternion.Slerp(_hips.localRotation, Quaternion.Euler(-85f, 0, 10f), k);
                _hips.localPosition = Vector3.Lerp(_hips.localPosition, new Vector3(0, 0.18f, 0), k);
                _armL.localRotation = Quaternion.Slerp(_armL.localRotation, Quaternion.Euler(-20f, 0, -70f), k);
                _armR.localRotation = Quaternion.Slerp(_armR.localRotation, Quaternion.Euler(10f, 0, 80f), k);
            }
        }
    }
}
