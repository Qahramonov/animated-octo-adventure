using System.Collections.Generic;
using Survival.Client.Rendering;
using Survival.Gameplay;
using Survival.Networking;
using UnityEngine;

namespace Survival.Client.Game
{
    /// <summary>Visual objects for replicated entities (other players, bots; animals/items in later phases).</summary>
    public sealed class RemoteViews : MonoBehaviour
    {
        private ClientSimulation _sim;
        private Material _material;
        private Transform _camera;
        private readonly List<RemoteEntity> _scratch = new List<RemoteEntity>(128);

        public static RemoteViews Create(ClientSimulation sim, Transform camera)
        {
            var go = new GameObject("[Remotes]");
            var v = go.AddComponent<RemoteViews>();
            v._sim = sim;
            v._camera = camera;
            v._material = App.Materials.Lit("character", Color.white, 0.05f, 0.25f);
            sim.RemoteAdded += v.OnAdded;
            sim.RemoteRemoved += v.OnRemoved;
            foreach (RemoteEntity e in sim.Remotes.Values) v.OnAdded(e);
            return v;
        }

        private void OnDestroy()
        {
            if (_sim == null) return;
            _sim.RemoteAdded -= OnAdded;
            _sim.RemoteRemoved -= OnRemoved;
        }

        private void OnAdded(RemoteEntity e)
        {
            if (e.Type != EntityType.Player && e.Type != EntityType.Bot) return;
            bool bot = e.Type == EntityType.Bot;
            HumanoidView view = HumanoidView.Create(transform, (bot ? "Bot " : "Player ") + e.Name,
                HumanoidView.Palette.FromSeed(e.Id * 2654435761u, bot), _material, false);
            view.transform.position = new Vector3(e.Position.X, e.Position.Y, e.Position.Z);
            e.View = view;
        }

        private void OnRemoved(RemoteEntity e)
        {
            if (e.View is HumanoidView v && v != null) Destroy(v.gameObject);
            e.View = null;
        }

        private void LateUpdate()
        {
            Vector3 cam = _camera != null ? _camera.position : Vector3.zero;
            foreach (RemoteEntity e in _sim.Remotes.Values)
            {
                if (!(e.View is HumanoidView v) || v == null) continue;
                var pos = new Vector3(e.Position.X, e.Position.Y, e.Position.Z);
                Transform t = v.transform;
                t.position = pos;
                t.rotation = Quaternion.Euler(0, e.Yaw, 0);
                v.Speed = e.Speed;
                v.Crouching = (e.Flags & MoveFlags.Crouching) != 0;
                v.Swimming = (e.Flags & MoveFlags.Swimming) != 0;
                v.Dead = (e.Flags & MoveFlags.Dead) != 0;
                // Far characters: skip the animation work (they are a few pixels tall)
                bool near = (pos - cam).sqrMagnitude < 90f * 90f;
                if (v.enabled != near) v.enabled = near;
            }
        }
    }
}
