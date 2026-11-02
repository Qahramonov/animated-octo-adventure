using System;
using System.Collections.Generic;
using System.Numerics;
using Survival.Core;
using Survival.Gameplay;
using Survival.World;

namespace Survival.Networking
{
    /// <summary>A replicated entity as seen by the client, with an interpolation buffer.</summary>
    public sealed class RemoteEntity
    {
        public struct Sample
        {
            public float Tick;
            public Vector3 Position;
            public float Yaw;
            public MoveFlags Flags;
            public byte Health;
        }

        public uint Id;
        public EntityType Type;
        public byte SubType;
        public string Name;
        public readonly RingBuffer<Sample> Buffer = new RingBuffer<Sample>(32);

        // Interpolated render state (updated by ClientSimulation.UpdateRemotes)
        public Vector3 Position;
        public float Yaw;
        public MoveFlags Flags;
        public float Health01 = 1f;
        public float Speed;
        /// <summary>Free slot for the presentation layer (e.g. the Unity view object).</summary>
        public object View;

        internal void AddSample(Sample s)
        {
            // Drop out-of-order/duplicate samples
            if (Buffer.Count > 0 && s.Tick <= Buffer.Newest.Tick) return;
            Buffer.Add(s);
        }
    }

    /// <summary>
    /// Client-side simulation: fixed-tick local prediction with server reconciliation, a smoothed
    /// estimate of the server clock, and snapshot interpolation for every other entity.
    /// Engine independent; the Unity layer only samples input and renders the results.
    /// </summary>
    public sealed class ClientSimulation
    {
        private struct HistoryEntry
        {
            public PlayerInput Input;
            public MoveState Result;
        }

        private readonly RingBuffer<HistoryEntry> _history = new RingBuffer<HistoryEntry>(256);
        private readonly List<PlayerInput> _sendList = new List<PlayerInput>(NetProtocol.InputRedundancy);
        private readonly Dictionary<uint, RemoteEntity> _remotes = new Dictionary<uint, RemoteEntity>(128);
        private readonly List<uint> _removeScratch = new List<uint>();

        private uint _nextSequence = 1;
        private float _accumulator;
        private MoveState _previous;
        private Vector3 _errorOffset;
        private double _clientTick;
        private uint _latestServerTick;
        private bool _clockInitialized;

        public ClientSimulation(WorldRuntime world, MovementSettings movement, int tickRate)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Movement = movement ?? MovementSettings.Default;
            TickRate = Math.Max(10, tickRate);
            TickDt = 1f / TickRate;
            InterpolationDelayTicks = Math.Max(2f, TickRate * 0.1f);
        }

        public WorldRuntime World { get; }
        public MovementSettings Movement { get; }
        public int TickRate { get; }
        public float TickDt { get; }
        public float InterpolationDelayTicks { get; set; }

        public uint LocalEntityId { get; private set; }
        public MoveState Predicted;
        public SurvivalStats Stats;
        public float TimeOfDay01 { get; private set; }
        public byte Weather { get; private set; }
        public int Corrections { get; private set; }
        public float LastCorrectionDistance { get; private set; }
        /// <summary>Diagnostics: predicted vs authoritative state of the last correction.</summary>
        public MoveState LastCorrectionPredicted { get; private set; }
        public MoveState LastCorrectionServer { get; private set; }
        public uint LastCorrectionSequence { get; private set; }
        public IReadOnlyDictionary<uint, RemoteEntity> Remotes => _remotes;
        public IReadOnlyList<PlayerInput> PendingSend => _sendList;

        /// <summary>Current estimate of the server tick (fractional).</summary>
        public double ServerTickEstimate => _clientTick;

        public event Action<RemoteEntity> RemoteAdded;
        public event Action<RemoteEntity> RemoteRemoved;

        /// <summary>Smoothed local position for rendering (between fixed ticks + correction smoothing).</summary>
        public Vector3 RenderPosition
        {
            get
            {
                float alpha = SMath.Clamp01(_accumulator / TickDt);
                return Vector3.Lerp(_previous.Position, Predicted.Position, alpha) + _errorOffset;
            }
        }

        public void Initialize(in WelcomeMsg welcome)
        {
            LocalEntityId = welcome.EntityId;
            Predicted = welcome.Spawn;
            _previous = Predicted;
            Stats = welcome.Stats;
            TimeOfDay01 = welcome.TimeOfDay01;
            _history.Clear();
            _errorOffset = Vector3.Zero;
            _accumulator = 0f;
            _clientTick = welcome.ServerTick;
            _latestServerTick = welcome.ServerTick;
            _clockInitialized = true;
        }

        /// <summary>
        /// Runs as many fixed prediction ticks as the frame time allows. Each produced input is
        /// queued in <see cref="PendingSend"/> (newest first, with redundancy) for the network layer.
        /// Returns the number of ticks simulated this frame.
        /// </summary>
        public int Advance(float dt, PlayerInput inputTemplate)
        {
            _sendList.Clear();
            if (_clockInitialized) _clientTick += dt * TickRate;

            // Decay visual correction smoothly (~150 ms)
            float decay = (float)Math.Exp(-dt * 12f);
            _errorOffset *= decay;
            if (_errorOffset.LengthSquared() < 1e-6f) _errorOffset = Vector3.Zero;

            _accumulator += Math.Min(dt, 0.25f);
            int ticks = 0;
            while (_accumulator >= TickDt && ticks < 8)
            {
                _accumulator -= TickDt;
                ticks++;

                PlayerInput input = inputTemplate;
                input.Sequence = _nextSequence++;
                _previous = Predicted;
                MovementSim.Step(ref Predicted, input, TickDt, World, Movement);
                _history.Add(new HistoryEntry { Input = input, Result = Predicted });
            }

            // Newest first, include the last few for redundancy (even when no new tick ran)
            for (int i = _history.Count - 1; i >= 0 && _sendList.Count < NetProtocol.InputRedundancy; i--)
            {
                _sendList.Add(_history[i].Input);
            }
            if (ticks == 0) _sendList.Clear();
            return ticks;
        }

        /// <summary>Applies an authoritative snapshot header (own state, vitals, clock).</summary>
        public void ApplyServerState(in SnapshotHeader h)
        {
            Stats = h.Stats;
            TimeOfDay01 = h.TimeOfDay01;
            Weather = h.Weather;
            UpdateClock(h.Tick);

            // Find the predicted state for the acknowledged input
            int ackIndex = -1;
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i].Input.Sequence == h.AckInputSequence)
                {
                    ackIndex = i;
                    break;
                }
                if (_history[i].Input.Sequence < h.AckInputSequence) break;
            }

            if (ackIndex < 0)
            {
                // Server has not processed any of our current inputs yet (or history overflowed):
                // only accept server state when we have nothing newer to predict from.
                if (_history.Count == 0)
                {
                    Vector3 before = RenderPosition;
                    Predicted = h.Own;
                    _previous = h.Own;
                    _errorOffset = before - Predicted.Position;
                    ClampErrorOffset();
                }
                return;
            }

            MoveState predictedAtAck = _history[ackIndex].Result;
            float err = Vector3.Distance(predictedAtAck.Position, h.Own.Position);
            bool flagsDiffer = (predictedAtAck.Flags & (MoveFlags.Swimming | MoveFlags.Dead)) != (h.Own.Flags & (MoveFlags.Swimming | MoveFlags.Dead));

            // Drop acknowledged history
            _history.DropOldest(ackIndex + 1);

            if (err > 0.05f || flagsDiffer || Math.Abs(predictedAtAck.Stamina - h.Own.Stamina) > 8f)
            {
                Vector3 before = RenderPosition;
                // Rewind to the server state and replay the unacknowledged inputs
                MoveState s = h.Own;
                for (int i = 0; i < _history.Count; i++)
                {
                    HistoryEntry e = _history[i];
                    MovementSim.Step(ref s, e.Input, TickDt, World, Movement);
                    e.Result = s;
                    _history[i] = e;
                }
                _previous = s;
                Predicted = s;
                _errorOffset = before - RenderPosition + _errorOffset;
                ClampErrorOffset();
                Corrections++;
                LastCorrectionDistance = err;
                LastCorrectionPredicted = predictedAtAck;
                LastCorrectionServer = h.Own;
                LastCorrectionSequence = h.AckInputSequence;
            }
        }

        private void ClampErrorOffset()
        {
            // Large corrections (teleport-like) snap instead of sliding across the map
            if (_errorOffset.LengthSquared() > 4f * 4f) _errorOffset = Vector3.Zero;
        }

        /// <summary>Hard reset (respawn/teleport issued by the server).</summary>
        public void Teleport(in TeleportMsg t)
        {
            Predicted = t.State;
            _previous = t.State;
            Stats = t.Stats;
            _history.Clear();
            _errorOffset = Vector3.Zero;
        }

        private void UpdateClock(uint serverTick)
        {
            if (serverTick > _latestServerTick) _latestServerTick = serverTick;
            if (!_clockInitialized)
            {
                _clientTick = serverTick;
                _clockInitialized = true;
                return;
            }
            double drift = _latestServerTick - _clientTick;
            if (Math.Abs(drift) > TickRate * 0.5) _clientTick = _latestServerTick;
            else _clientTick += drift * 0.1;
        }

        // ------------------------------------------------------------------ remotes

        public void OnEntitySpawn(in EntitySpawnMsg m)
        {
            if (m.Id == LocalEntityId) return;
            if (!_remotes.TryGetValue(m.Id, out RemoteEntity e))
            {
                e = new RemoteEntity { Id = m.Id };
                _remotes.Add(m.Id, e);
                e.Type = m.Type;
                e.SubType = m.SubType;
                e.Name = m.Name;
                e.Position = m.Position;
                e.Yaw = m.Yaw;
                e.Flags = m.Flags;
                e.Health01 = m.Health / 255f;
                e.Buffer.Clear();
                e.AddSample(new RemoteEntity.Sample { Tick = (float)_clientTick - 1f, Position = m.Position, Yaw = m.Yaw, Flags = m.Flags, Health = m.Health });
                RemoteAdded?.Invoke(e);
            }
            else
            {
                e.Name = m.Name;
                e.SubType = m.SubType;
            }
        }

        public void OnEntityDespawn(in EntityDespawnMsg m)
        {
            if (_remotes.TryGetValue(m.Id, out RemoteEntity e))
            {
                _remotes.Remove(m.Id);
                RemoteRemoved?.Invoke(e);
            }
        }

        public void ApplyEntityStates(uint tick, List<EntityState> states)
        {
            for (int i = 0; i < states.Count; i++)
            {
                EntityState s = states[i];
                if (_remotes.TryGetValue(s.Id, out RemoteEntity e))
                {
                    // Respawn/teleport: do not slide across the map, restart interpolation
                    if (e.Buffer.Count > 0 && SMath.DistanceSqXZ(e.Buffer.Newest.Position, s.Position) > 30f * 30f)
                    {
                        e.Buffer.Clear();
                        e.Position = s.Position;
                    }
                    e.AddSample(new RemoteEntity.Sample { Tick = tick, Position = s.Position, Yaw = s.Yaw, Flags = s.Flags, Health = s.Health });
                }
            }
        }

        /// <summary>Interpolates every remote entity at (server time - interpolation delay).</summary>
        public void UpdateRemotes()
        {
            float renderTick = (float)(_clientTick - InterpolationDelayTicks);
            foreach (RemoteEntity e in _remotes.Values)
            {
                RingBuffer<RemoteEntity.Sample> b = e.Buffer;
                if (b.Count == 0) continue;
                Vector3 oldPos = e.Position;

                if (renderTick <= b.Oldest.Tick || b.Count == 1)
                {
                    Apply(e, b.Oldest);
                }
                else if (renderTick >= b.Newest.Tick)
                {
                    // Short extrapolation (max 200 ms) then hold
                    RemoteEntity.Sample n = b.Newest;
                    if (b.Count >= 2)
                    {
                        RemoteEntity.Sample p = b[b.Count - 2];
                        float span = Math.Max(0.001f, n.Tick - p.Tick);
                        float t = Math.Min(renderTick - n.Tick, TickRate * 0.2f) / span;
                        e.Position = n.Position + (n.Position - p.Position) * t;
                        e.Yaw = n.Yaw;
                        e.Flags = n.Flags;
                        e.Health01 = n.Health / 255f;
                    }
                    else
                    {
                        Apply(e, n);
                    }
                }
                else
                {
                    for (int i = b.Count - 2; i >= 0; i--)
                    {
                        RemoteEntity.Sample a = b[i];
                        if (a.Tick <= renderTick)
                        {
                            RemoteEntity.Sample c = b[i + 1];
                            float t = (renderTick - a.Tick) / Math.Max(0.001f, c.Tick - a.Tick);
                            e.Position = Vector3.Lerp(a.Position, c.Position, t);
                            e.Yaw = SMath.LerpAngleDeg(a.Yaw, c.Yaw, t);
                            e.Flags = t < 0.5f ? a.Flags : c.Flags;
                            e.Health01 = c.Health / 255f;
                            float segSpeed = SMath.DistanceXZ(a.Position, c.Position) / Math.Max(0.001f, (c.Tick - a.Tick) * TickDt);
                            e.Speed = SMath.Lerp(e.Speed, segSpeed, 0.25f);
                            // Old samples are no longer needed
                            if (i > 2) b.DropOldest(i - 2);
                            break;
                        }
                    }
                }

                if (SMath.DistanceSqXZ(oldPos, e.Position) < 1e-8f) e.Speed = SMath.Lerp(e.Speed, 0f, 0.1f);
            }
        }

        private static void Apply(RemoteEntity e, in RemoteEntity.Sample s)
        {
            e.Position = s.Position;
            e.Yaw = s.Yaw;
            e.Flags = s.Flags;
            e.Health01 = s.Health / 255f;
        }

        public void ClearRemotes()
        {
            _removeScratch.Clear();
            _removeScratch.AddRange(_remotes.Keys);
            foreach (uint id in _removeScratch)
            {
                RemoteEntity e = _remotes[id];
                _remotes.Remove(id);
                RemoteRemoved?.Invoke(e);
            }
        }
    }
}
