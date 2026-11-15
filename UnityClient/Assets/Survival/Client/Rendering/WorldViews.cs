using System.Collections.Generic;
using Survival.Gameplay;
using UnityEngine;

namespace Survival.Client.Rendering
{
    /// <summary>Procedural low-poly animal (deer, boar, wolf) with a simple walk/run leg cycle.</summary>
    public sealed class AnimalView : MonoBehaviour
    {
        private static readonly MeshBuilder MB = new MeshBuilder();
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

        private readonly Transform[] _legs = new Transform[4];
        private Transform _body, _head, _tail;
        private float _phase;
        private float _legLength;
        private float _stride;

        public float Speed;
        public AnimalKind Kind { get; private set; }

        public static AnimalView Create(Transform parent, AnimalKind kind, Material mat)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<AnimalView>();
            v.Kind = kind;
            v.Build(kind, mat);
            v._phase = Random.value * 6f;
            return v;
        }

        private static Mesh Box(string key, Vector3 center, Vector3 size, Color32 c, float taper = 1f)
        {
            if (Meshes.TryGetValue(key, out Mesh m) && m != null) return m;
            MB.Clear();
            MB.Box(center, size, c, taper);
            m = MB.ToMesh(key);
            Meshes[key] = m;
            return m;
        }

        private static Transform Part(string name, Transform parent, Vector3 pos, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            if (mesh != null)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return go.transform;
        }

        private void Build(AnimalKind kind, Material mat)
        {
            // Dimensions: body length/height/width, leg length, head size, colours
            float len, height, width, leg;
            Color32 coat, dark, belly;
            switch (kind)
            {
                case AnimalKind.Deer:
                    len = 1.25f; height = 0.5f; width = 0.38f; leg = 0.78f;
                    coat = MeshBuilder.Rgb(0.60f, 0.42f, 0.26f); dark = MeshBuilder.Rgb(0.36f, 0.25f, 0.16f); belly = MeshBuilder.Rgb(0.86f, 0.78f, 0.66f);
                    break;
                case AnimalKind.Boar:
                    len = 1.15f; height = 0.6f; width = 0.5f; leg = 0.42f;
                    coat = MeshBuilder.Rgb(0.30f, 0.24f, 0.20f); dark = MeshBuilder.Rgb(0.18f, 0.15f, 0.13f); belly = MeshBuilder.Rgb(0.42f, 0.34f, 0.28f);
                    break;
                default:
                    len = 1.2f; height = 0.42f; width = 0.34f; leg = 0.55f;
                    coat = MeshBuilder.Rgb(0.47f, 0.48f, 0.50f); dark = MeshBuilder.Rgb(0.26f, 0.27f, 0.29f); belly = MeshBuilder.Rgb(0.78f, 0.78f, 0.76f);
                    break;
            }
            string k = kind.ToString();
            _legLength = leg;
            _stride = kind == AnimalKind.Boar ? 26f : 34f;
            float bodyY = leg + height * 0.5f;
            _body = Part("Body", transform, new Vector3(0, bodyY, 0), Box(k + "_body", Vector3.zero, new Vector3(width, height, len), coat, 0.86f), mat);
            Part("Belly", _body, new Vector3(0, -height * 0.32f, 0), Box(k + "_belly", Vector3.zero, new Vector3(width * 0.8f, height * 0.4f, len * 0.8f), belly), mat);

            // Neck + head (deer hold the head high, boars low)
            float neckUp = kind == AnimalKind.Deer ? 0.42f : kind == AnimalKind.Wolf ? 0.16f : 0.02f;
            _head = Part("Head", _body, new Vector3(0, height * 0.25f + neckUp, len * 0.5f + 0.1f), null, mat);
            if (kind == AnimalKind.Deer) Part("Neck", _head, new Vector3(0, -0.22f, -0.12f), Box(k + "_neck", Vector3.zero, new Vector3(0.17f, 0.5f, 0.2f), coat), mat);
            Vector3 headSize = kind == AnimalKind.Boar ? new Vector3(0.36f, 0.36f, 0.46f) : new Vector3(0.22f, 0.22f, 0.36f);
            Part("Skull", _head, new Vector3(0, 0, 0.1f), Box(k + "_skull", Vector3.zero, headSize, coat, 0.7f), mat);
            Part("Snout", _head, new Vector3(0, -0.05f, 0.1f + headSize.z * 0.6f), Box(k + "_snout", Vector3.zero, new Vector3(headSize.x * 0.55f, headSize.y * 0.5f, 0.2f), dark), mat);
            float earY = headSize.y * 0.6f;
            Part("EarL", _head, new Vector3(-headSize.x * 0.4f, earY, 0.02f), Box(k + "_ear", Vector3.zero, new Vector3(0.06f, 0.14f, 0.05f), dark, 0.4f), mat);
            Part("EarR", _head, new Vector3(headSize.x * 0.4f, earY, 0.02f), Box(k + "_ear", Vector3.zero, new Vector3(0.06f, 0.14f, 0.05f), dark, 0.4f), mat);
            if (kind == AnimalKind.Deer)
            {
                Color32 antler = MeshBuilder.Rgb(0.82f, 0.74f, 0.58f);
                for (int s = -1; s <= 1; s += 2)
                {
                    Transform a = Part("Antler", _head, new Vector3(s * 0.08f, earY + 0.12f, -0.02f), Box("deer_antler", new Vector3(0, 0.16f, 0), new Vector3(0.035f, 0.34f, 0.035f), antler), mat);
                    a.localRotation = Quaternion.Euler(-12f, 0, -s * 22f);
                    Transform t = Part("Tine", a, new Vector3(0, 0.2f, 0.05f), Box("deer_tine", new Vector3(0, 0.07f, 0), new Vector3(0.03f, 0.16f, 0.03f), antler), mat);
                    t.localRotation = Quaternion.Euler(50f, 0, 0);
                }
            }
            if (kind == AnimalKind.Boar)
            {
                Color32 tusk = MeshBuilder.Rgb(0.92f, 0.9f, 0.82f);
                Part("TuskL", _head, new Vector3(-0.1f, -0.1f, 0.42f), Box("boar_tusk", Vector3.zero, new Vector3(0.03f, 0.1f, 0.03f), tusk, 0.3f), mat);
                Part("TuskR", _head, new Vector3(0.1f, -0.1f, 0.42f), Box("boar_tusk", Vector3.zero, new Vector3(0.03f, 0.1f, 0.03f), tusk, 0.3f), mat);
                Part("Mane", _body, new Vector3(0, height * 0.5f, 0.1f), Box("boar_mane", Vector3.zero, new Vector3(0.1f, 0.1f, len * 0.7f), dark), mat);
            }

            // Tail
            _tail = Part("Tail", _body, new Vector3(0, height * 0.2f, -len * 0.5f), null, mat);
            Vector3 tailSize = kind == AnimalKind.Wolf ? new Vector3(0.1f, 0.1f, 0.42f) : new Vector3(0.07f, 0.07f, 0.14f);
            Part("TailMesh", _tail, new Vector3(0, 0, -tailSize.z * 0.5f), Box(k + "_tail", Vector3.zero, tailSize, kind == AnimalKind.Deer ? belly : dark), mat);
            if (kind == AnimalKind.Wolf) _tail.localRotation = Quaternion.Euler(28f, 0, 0);

            // Legs pivot at the hip so they can swing
            float lx = width * 0.5f - 0.06f, lz = len * 0.5f - 0.12f;
            float thick = kind == AnimalKind.Boar ? 0.13f : 0.09f;
            Mesh legMesh = Box(k + "_leg", new Vector3(0, -leg * 0.5f, 0), new Vector3(thick, leg, thick), dark, 1f);
            _legs[0] = Part("LegFL", transform, new Vector3(-lx, leg, lz), legMesh, mat);
            _legs[1] = Part("LegFR", transform, new Vector3(lx, leg, lz), legMesh, mat);
            _legs[2] = Part("LegBL", transform, new Vector3(-lx, leg, -lz), legMesh, mat);
            _legs[3] = Part("LegBR", transform, new Vector3(lx, leg, -lz), legMesh, mat);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float move = Mathf.Clamp01(Speed / 2.5f);
            _phase += dt * (3f + Speed * 2.2f) * (move > 0.05f ? 1f : 0.15f);
            float swing = Mathf.Sin(_phase) * _stride * move;
            _legs[0].localRotation = Quaternion.Euler(swing, 0, 0);
            _legs[3].localRotation = Quaternion.Euler(swing, 0, 0);
            _legs[1].localRotation = Quaternion.Euler(-swing, 0, 0);
            _legs[2].localRotation = Quaternion.Euler(-swing, 0, 0);
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.035f * move;
            _body.localPosition = new Vector3(0, _legLength + _body.localScale.y * 0f + BodyHalf() + bob, 0);
            // Idle: slow breathing and head movement
            float idle = 1f - move;
            _head.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.6f + _phase) * 8f * idle, Mathf.Sin(Time.time * 0.37f + _phase * 2f) * 14f * idle, 0);
            if (Kind != AnimalKind.Wolf) _tail.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 5f + _phase) * 20f, 0);
        }

        private float BodyHalf() => Kind == AnimalKind.Deer ? 0.25f : Kind == AnimalKind.Boar ? 0.3f : 0.21f;
    }

    /// <summary>Placed campfire: stone ring, logs, flickering flame and light.</summary>
    public sealed class CampfireView : MonoBehaviour
    {
        private static Mesh _base, _flame;
        private Transform _flameT;
        private Light _light;
        private float _seed;

        public static CampfireView Create(Transform parent, Material mat)
        {
            var go = new GameObject("Campfire");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<CampfireView>();
            v.Build(mat);
            return v;
        }

        public static Mesh BaseMesh()
        {
            if (_base != null) return _base;
            var b = new MeshBuilder();
            Color32 stone = MeshBuilder.Rgb(0.48f, 0.47f, 0.46f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                b.Blob(new Vector3(Mathf.Cos(a) * 0.55f, 0.08f, Mathf.Sin(a) * 0.55f), new Vector3(0.17f, 0.12f, 0.15f), MeshBuilder.Shade(stone, 0.85f + (i % 3) * 0.1f), 0, 0.25f, 60 + i, 0.1f);
            }
            Color32 wood = MeshBuilder.Rgb(0.34f, 0.23f, 0.14f);
            Color32 cut = MeshBuilder.Rgb(0.74f, 0.6f, 0.4f);
            for (int i = 0; i < 5; i++)
            {
                b.Transform = Matrix4x4.TRS(new Vector3(0, 0.06f, 0), Quaternion.Euler(0, i * 72f, 0) * Quaternion.Euler(0, 0, 62f), Vector3.one);
                b.Cylinder(new Vector3(0, -0.1f, 0), 0.7f, 0.065f, 0.055f, 5, wood, true, true, cut);
            }
            b.Transform = Matrix4x4.identity;
            b.Blob(new Vector3(0, 0.03f, 0), new Vector3(0.36f, 0.04f, 0.36f), MeshBuilder.Rgb(0.1f, 0.09f, 0.09f)); // ash
            _base = b.ToMesh("campfire_base");
            return _base;
        }

        public static Mesh FlameMesh()
        {
            if (_flame != null) return _flame;
            var b = new MeshBuilder();
            b.Cylinder(new Vector3(0, 0.1f, 0), 0.62f, 0.2f, 0f, 6, MeshBuilder.Rgb(1f, 0.42f, 0.08f), false, false, MeshBuilder.Rgb(1f, 0.86f, 0.3f));
            b.Cylinder(new Vector3(0.09f, 0.1f, 0.05f), 0.4f, 0.12f, 0f, 5, MeshBuilder.Rgb(1f, 0.55f, 0.1f), false, false, MeshBuilder.Rgb(1f, 0.9f, 0.45f));
            b.Cylinder(new Vector3(-0.1f, 0.1f, -0.04f), 0.34f, 0.1f, 0f, 5, MeshBuilder.Rgb(1f, 0.36f, 0.06f), false, false, MeshBuilder.Rgb(1f, 0.8f, 0.3f));
            _flame = b.ToMesh("campfire_flame");
            return _flame;
        }

        private void Build(Material mat)
        {
            gameObject.AddComponent<MeshFilter>().sharedMesh = BaseMesh();
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var flame = new GameObject("Flame");
            flame.transform.SetParent(transform, false);
            flame.AddComponent<MeshFilter>().sharedMesh = FlameMesh();
            var mr = flame.AddComponent<MeshRenderer>();
            mr.sharedMaterial = App.Materials.Lit("flame", Color.white, 0f, 0f, 1.6f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _flameT = flame.transform;
            var lg = new GameObject("Light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(0, 0.9f, 0);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.58f, 0.24f);
            _light.range = 9f;
            _light.shadows = LightShadows.None;
            _seed = Random.value * 10f;
        }

        private void Update()
        {
            float t = Time.time * 9f + _seed;
            float f = 0.5f + 0.5f * Mathf.PerlinNoise(t, _seed);
            _flameT.localScale = new Vector3(0.9f + f * 0.25f, 0.8f + f * 0.55f, 0.9f + f * 0.25f);
            _flameT.localRotation = Quaternion.Euler(0, Time.time * 40f, 0);
            _light.intensity = 1.6f + f * 1.2f;
        }
    }

    /// <summary>
    /// Renders item models into small sprites once (off-screen camera) so inventory slots show
    /// real pictures. Falls back to null (letter icons) if rendering to a texture is not possible.
    /// </summary>
    public static class IconBaker
    {
        private const int Size = 128;
        private const int Layer = 30;
        private static readonly Dictionary<ItemId, Sprite> Cache = new Dictionary<ItemId, Sprite>();
        private static bool _failed;

        public static Sprite Get(ItemId id)
        {
            if (id == ItemId.None || _failed) return null;
            if (Cache.TryGetValue(id, out Sprite s)) return s;
            try
            {
                s = Bake(id);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Icons] Baking failed, using letter icons: " + e.Message);
                _failed = true;
                s = null;
            }
            Cache[id] = s;
            return s;
        }

        private static Sprite Bake(ItemId id)
        {
            Mesh mesh = Models.Tool(id);
            Vector3 origin = new Vector3(0, 3000f, 0);
            var obj = new GameObject("IconModel") { layer = Layer };
            obj.transform.position = origin;
            obj.transform.rotation = Quaternion.Euler(-24f, 128f, 18f);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = obj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = App.Materials.Lit("icon", Color.white, 0f, 0f, 0.85f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            Bounds b = mr.bounds;
            var camGo = new GameObject("IconCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.y, b.extents.z * 0.8f) * 1.12f;
            cam.transform.position = b.center - Vector3.forward * 5f;
            cam.transform.rotation = Quaternion.identity;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 12f;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.allowHDR = false;
            cam.allowMSAA = false;
            var rt = RenderTexture.GetTemporary(Size, Size, 16, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "icon_" + id, wrapMode = TextureWrapMode.Clamp };
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            RenderTexture.active = prev;

            // The background is cleared to black: build alpha from "is there any colour here", and reject empty bakes
            Color32[] px = tex.GetPixels32();
            int filled = 0;
            for (int i = 0; i < px.Length; i++)
            {
                bool solid = px[i].r > 6 || px[i].g > 6 || px[i].b > 6;
                px[i].a = solid ? (byte)255 : (byte)0;
                if (solid) filled++;
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);

            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.Destroy(camGo);
            Object.Destroy(obj);
            if (filled < 40)
            {
                Object.Destroy(tex);
                throw new System.InvalidOperationException("empty render for " + id);
            }
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
