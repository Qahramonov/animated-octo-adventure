using System.Threading.Tasks;
using Survival.Client.Rendering;
using Survival.Client.UI;
using Survival.Client.WorldView;
using Survival.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Survival.Client.Lobby
{
    /// <summary>
    /// The 3D lobby backdrop: the survivor on a rocky outcrop by a campfire at sunset, the real
    /// island's silhouette across the sea, fireflies and embers, and a slowly drifting camera.
    /// Kept cheap: ~40 draw calls, one directional light, no realtime point lights.
    /// </summary>
    public sealed class LobbyStage : MonoBehaviour
    {
        private Camera _cam;
        private HumanoidView _hero;
        private Transform _fireGlow;
        private Material _fireGlowMat;
        private SkyController _sky;
        private Vector3 _camBase;
        private Vector3 _lookTarget;
        private float _focusX;
        private float _focusTarget;
        private Task<TerrainMeshes.FarIslandData> _islandTask;
        private Transform _islandRoot;

        public Camera Camera => _cam;
        public HumanoidView Hero => _hero;

        /// <summary>0 = default framing, 1 = hero shifted left (a panel is open on the right).</summary>
        public void SetFocus(float f) => _focusTarget = f;

        public static LobbyStage Create(Transform parent)
        {
            var go = new GameObject("LobbyStage");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<LobbyStage>();
            s.Build();
            return s;
        }

        private void Build()
        {
            _cam = CameraRig.CreateMain("LobbyCamera", 6000f, 50f);
            _cam.transform.SetParent(transform, false);
            _camBase = new Vector3(1.55f, 1.55f, -4.6f);
            _lookTarget = new Vector3(0.55f, 1.15f, 0f);
            if (QualityManager.PostProcessing) PostFx.Create(transform, 0.9f, 0.32f, 10f, 12f);

            _sky = SkyController.Create(_cam, 5000f);
            _sky.transform.SetParent(transform, false);
            _sky.FogStart = 300f;
            _sky.FogEnd = 4200f;
            _sky.SetTime(0.712f);
            _sky.Sun.shadows = QualityManager.Shadows ? LightShadows.Soft : LightShadows.None;

            var ocean = WaterSurface.Create(_cam.transform, -1.6f, 6000f, 96, null, 0f);
            ocean.transform.SetParent(transform, true);

            Material rockMat = App.Materials.Lit("lobby_rock", Color.white, 0.05f, 0.15f);
            Material foliage = App.Materials.Lit("foliage", Color.white, 0f, 0f, 0f, 0.6f);
            Material plain = App.Materials.Lit("plain", Color.white);

            // Outcrop under the hero and scattered rocks
            var mb = new MeshBuilder();
            mb.Blob(new Vector3(0, -1.2f, 0.6f), new Vector3(4.2f, 1.25f, 3.6f), MeshBuilder.Rgb(0.42f, 0.40f, 0.37f), 1, 0.12f, 3, 0.1f);
            mb.Blob(new Vector3(-3.6f, -1.4f, 2.5f), new Vector3(2.6f, 1.6f, 2.4f), MeshBuilder.Rgb(0.38f, 0.37f, 0.35f), 1, 0.2f, 4, 0.1f);
            mb.Blob(new Vector3(3.8f, -1.6f, 3.0f), new Vector3(2.2f, 1.3f, 2.0f), MeshBuilder.Rgb(0.40f, 0.38f, 0.36f), 0, 0.2f, 5, 0.1f);
            mb.Blob(new Vector3(1.5f, -1.5f, -2.2f), new Vector3(1.8f, 0.9f, 1.3f), MeshBuilder.Rgb(0.36f, 0.35f, 0.33f), 0, 0.2f, 6, 0.1f);
            // Moss/grass cap on the outcrop
            mb.Blob(new Vector3(0.2f, -0.17f, 0.8f), new Vector3(3.2f, 0.22f, 2.6f), MeshBuilder.Rgb(0.30f, 0.42f, 0.2f), 1, 0.08f, 9, 0.12f);
            Spawn("Outcrop", mb.ToMesh("outcrop"), rockMat, Vector3.zero);

            // Trees behind the hero
            SpawnNode(NodeKind.TreePine, new Vector3(-2.6f, -0.3f, 3.4f), 1.1f, 20f, foliage);
            SpawnNode(NodeKind.TreePine, new Vector3(3.4f, -0.6f, 4.6f), 1.3f, 75f, foliage);
            SpawnNode(NodeKind.TreeBroadleaf, new Vector3(-5.5f, -0.8f, 6f), 1.2f, 140f, foliage);
            SpawnNode(NodeKind.Bush, new Vector3(2.4f, -0.25f, 1.6f), 1f, 10f, foliage);
            SpawnNode(NodeKind.Bush, new Vector3(-2.0f, -0.25f, 0.4f), 0.8f, 200f, foliage);
            SpawnNode(NodeKind.Boulder, new Vector3(2.9f, -0.35f, -0.2f), 0.7f, 30f, rockMat);
            var grass = Models.GrassTuft();
            for (int i = 0; i < 26; i++)
            {
                float a = i * 2.4f;
                Vector3 p = new Vector3(Mathf.Cos(a) * (1.2f + (i % 5) * 0.45f), -0.12f, 0.8f + Mathf.Sin(a) * (1f + (i % 4) * 0.4f));
                var g = Spawn("Grass", grass, foliage, p);
                g.localScale = Vector3.one * (0.8f + (i % 3) * 0.25f);
                g.rotation = Quaternion.Euler(0, i * 37f, 0);
            }

            // Hero
            _hero = HumanoidView.Create(transform, "Hero", HumanoidView.Palette.Survivor, plain, true);
            _hero.transform.position = new Vector3(0.55f, -0.08f, 0.2f);
            _hero.transform.rotation = Quaternion.Euler(0, 200f, 0);
            _hero.Expressive = true;

            BuildCampfire(new Vector3(-0.9f, -0.12f, -0.6f), plain);
            BuildFireflies();

            // Real island silhouette across the water (generated off-thread)
            var cfg = new WorldConfig();
            var world = new WorldRuntime(cfg);
            _islandTask = Task.Run(() => TerrainMeshes.ComputeFarIsland(world, 50f, 0f));
        }

        private Transform Spawn(string name, Mesh mesh, Material mat, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        private void SpawnNode(NodeKind kind, Vector3 pos, float scale, float rot, Material mat)
        {
            Transform t = Spawn(kind.ToString(), Models.Get(kind, 0), mat, pos);
            t.localScale = Vector3.one * scale;
            t.localRotation = Quaternion.Euler(0, rot, 0);
        }

        private void BuildCampfire(Vector3 pos, Material mat)
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                mb.Blob(new Vector3(Mathf.Cos(a) * 0.42f, 0.06f, Mathf.Sin(a) * 0.42f), new Vector3(0.14f, 0.1f, 0.12f), MeshBuilder.Rgb(0.35f, 0.34f, 0.32f), 0, 0.2f, 60 + i);
            }
            Color32 wood = MeshBuilder.Rgb(0.30f, 0.20f, 0.12f);
            for (int i = 0; i < 4; i++)
            {
                mb.Transform = Matrix4x4.TRS(new Vector3(0, 0.05f, 0), Quaternion.Euler(70f, i * 90f + 20f, 0), Vector3.one);
                mb.Cylinder(new Vector3(0, -0.3f, 0), 0.6f, 0.05f, 0.04f, 5, wood, true);
            }
            mb.Transform = Matrix4x4.identity;
            mb.Blob(new Vector3(0, 0.04f, 0), new Vector3(0.22f, 0.05f, 0.22f), MeshBuilder.Rgb(1f, 0.45f, 0.12f), 0, 0.2f, 70);
            Transform fire = Spawn("Campfire", mb.ToMesh("campfire"), App.Materials.Lit("campfire", Color.white, 0f, 0f, 0.35f), pos);

            Texture2D glow = UISprites.GlowTexture(64);
            _fireGlowMat = new Material(App.Materials.Additive("fire_glow", glow, new Color(1f, 0.5f, 0.15f), 1f));
            var g = new GameObject("FireGlow");
            g.transform.SetParent(fire, false);
            g.transform.localPosition = new Vector3(0, 0.35f, 0);
            g.AddComponent<MeshFilter>().sharedMesh = Models.Quad();
            g.AddComponent<MeshRenderer>().sharedMaterial = _fireGlowMat;
            g.transform.localScale = Vector3.one * 2.2f;
            _fireGlow = g.transform;

            // Flames and embers
            var flames = Fx(fire, "Flames", glow, new Color(1f, 0.62f, 0.2f, 0.9f), new Color(1f, 0.3f, 0.08f, 0.8f), 40, 0.5f, 0.9f, 0.18f, 0.42f, 0.6f);
            var shape = flames.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.16f;
            shape.rotation = new Vector3(-90f, 0, 0);
            var sizeOver = flames.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.1f));
            var embers = Fx(fire, "Embers", glow, new Color(1f, 0.6f, 0.2f, 1f), new Color(1f, 0.4f, 0.1f, 1f), 8, 1.4f, 2.6f, 0.02f, 0.05f, 1.1f);
            var es = embers.shape;
            es.shapeType = ParticleSystemShapeType.Circle;
            es.radius = 0.2f;
            es.rotation = new Vector3(-90f, 0, 0);
            var noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            App.Audio.SetAmbience(AmbienceLayer.Fire, 0.45f);
        }

        private void BuildFireflies()
        {
            Texture2D glow = UISprites.GlowTexture(64);
            var ps = Fx(transform, "Fireflies", glow, new Color(0.9f, 1f, 0.55f, 0.9f), new Color(1f, 0.85f, 0.45f, 0.7f), 14, 3f, 6f, 0.03f, 0.06f, 0.15f);
            ps.transform.localPosition = new Vector3(0, 1f, 2f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(9f, 2.5f, 6f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.5f;
        }

        private static ParticleSystem Fx(Transform parent, string name, Texture2D tex, Color a, Color b, float rate, float minLife, float maxLife,
            float minSize, float maxSize, float speed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.prewarm = true;
            var em = ps.emission;
            em.rateOverTime = rate;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = App.Materials.Additive("particles_glow", tex, Color.white, 1.4f);
            ps.Play();
            return ps;
        }

        private void Update()
        {
            float t = Time.time;
            if (_islandTask != null && _islandTask.IsCompleted)
            {
                if (_islandTask.Status == TaskStatus.RanToCompletion)
                {
                    Mesh island = TerrainMeshes.ToMesh(_islandTask.Result);
                    _islandRoot = Spawn("IslandSilhouette", island, App.Materials.Lit("far_terrain", Color.white, 0f, 0f, 0f, 0f, 0.2f), Vector3.zero);
                    // Place the island's west coast ~1.1 km behind the hero, sea level at our ocean height
                    _islandRoot.localPosition = new Vector3(-2500f, -1.6f - 30f, 1400f);
                    _islandRoot.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
                _islandTask = null;
            }

            _focusX = Mathf.Lerp(_focusX, _focusTarget, 1f - Mathf.Exp(-Time.deltaTime * 4f));
            float orbit = Mathf.Sin(t * 0.12f) * 0.35f;
            Vector3 camPos = _camBase + new Vector3(orbit + _focusX * 1.1f, Mathf.Sin(t * 0.21f) * 0.06f, Mathf.Cos(t * 0.12f) * 0.15f);
            _cam.transform.localPosition = camPos;
            Vector3 look = _lookTarget + new Vector3(_focusX * 1.25f, 0, 0);
            _cam.transform.rotation = Quaternion.LookRotation(look - camPos) * Quaternion.Euler(0, 0, Mathf.Sin(t * 0.17f) * 0.6f);

            if (_fireGlowMat != null)
            {
                float flicker = 0.75f + 0.25f * Mathf.PerlinNoise(t * 6f, 0.3f);
                _fireGlowMat.SetFloat("_Intensity", flicker);
                _fireGlow.rotation = Quaternion.LookRotation(_fireGlow.position - _cam.transform.position);
            }
        }

        private void OnDestroy()
        {
            App.Audio?.SetAmbience(AmbienceLayer.Fire, 0f);
        }
    }
}
