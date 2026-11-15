using System;
using System.Collections.Generic;
using System.Numerics;
using LiteNetLib;
using Survival.Core;
using Survival.Gameplay;
using Survival.Networking;
using Survival.Server.AI;
using Survival.World;

namespace Survival.Server.AI
{
    public enum AnimalState : byte
    {
        Rest,
        Wander,
        Flee,
        Chase,
    }

    /// <summary>
    /// Simple wildlife brain. Deer flee from players, boars flee but fight back when hit, wolves
    /// hunt players. Like bots, animals produce a <see cref="PlayerInput"/> and move with the shared
    /// movement simulation.
    /// </summary>
    public sealed class AnimalBrain
    {
        public AnimalKind Kind;
        public AnimalState State = AnimalState.Rest;
        public Vector3 Target;
        public uint TargetEntity;
        public float BiteCooldown;
        public bool Active;

        private Rng _rng;
        private float _timer;
        private float _yaw;
        private float _stuck;
        private Vector3 _lastPos;

        public AnimalBrain(AnimalKind kind, uint seed, float yaw)
        {
            Kind = kind;
            _rng = new Rng(seed);
            _yaw = yaw;
            _timer = _rng.Range(1f, 5f);
        }

        public void Provoke(ServerEntity attacker)
        {
            if (Kind == AnimalKind.Deer)
            {
                FleeFrom(attacker.Move.Position, 9f);
            }
            else
            {
                State = AnimalState.Chase;
                TargetEntity = attacker.Id;
                _timer = 14f;
            }
        }

        private void FleeFrom(Vector3 threat, float seconds)
        {
            State = AnimalState.Flee;
            Target = threat;
            _timer = seconds;
        }

        public PlayerInput Think(ServerEntity self, GameServer server, ServerEntity nearestPlayer, float nearestDist, float dt)
        {
            WorldRuntime world = server.World;
            Vector3 pos = self.Move.Position;
            _timer -= dt;
            BiteCooldown -= dt;

            // Reactions to the nearest player
            if (nearestPlayer != null && State != AnimalState.Chase)
            {
                if (Kind == AnimalKind.Deer && nearestDist < 15f) FleeFrom(nearestPlayer.Move.Position, 6f);
                else if (Kind == AnimalKind.Boar && nearestDist < 6f && State != AnimalState.Flee) FleeFrom(nearestPlayer.Move.Position, 4f);
                else if (Kind == AnimalKind.Wolf && nearestDist < 20f)
                {
                    State = AnimalState.Chase;
                    TargetEntity = nearestPlayer.Id;
                    _timer = 18f;
                }
            }

            var input = new PlayerInput();
            float desiredYaw = _yaw;
            bool move = false, sprint = false;

            switch (State)
            {
                case AnimalState.Chase:
                    if (_timer <= 0f || !server.TryGetEntity(TargetEntity, out ServerEntity prey) || prey.IsDead || prey.Removed)
                    {
                        Enter(AnimalState.Rest, 2f);
                        break;
                    }
                    float dx = prey.Move.Position.X - pos.X, dz = prey.Move.Position.Z - pos.Z;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist > 48f)
                    {
                        Enter(AnimalState.Rest, 3f);
                        break;
                    }
                    desiredYaw = SMath.YawFromDirection(dx, dz);
                    if (dist > 1.5f)
                    {
                        move = true;
                        sprint = dist > 4f;
                    }
                    if (dist < 2.1f && BiteCooldown <= 0f && Math.Abs(prey.Move.Position.Y - pos.Y) < 2.5f)
                    {
                        BiteCooldown = 1.3f;
                        server.ApplyDamage(prey, CombatRules.BiteDamage(Kind), DamageType.Bite, self);
                        if (Kind == AnimalKind.Wolf) prey.Stats.Bleeding = Math.Min(1.2f, prey.Stats.Bleeding + 0.25f);
                    }
                    break;

                case AnimalState.Flee:
                    if (_timer <= 0f)
                    {
                        Enter(AnimalState.Rest, _rng.Range(2f, 5f));
                        break;
                    }
                    desiredYaw = SMath.YawFromDirection(pos.X - Target.X, pos.Z - Target.Z);
                    move = true;
                    sprint = true;
                    break;

                case AnimalState.Wander:
                    float wx = Target.X - pos.X, wz = Target.Z - pos.Z;
                    if (_timer <= 0f || wx * wx + wz * wz < 4f)
                    {
                        Enter(AnimalState.Rest, _rng.Range(3f, 9f));
                        break;
                    }
                    desiredYaw = SMath.YawFromDirection(wx, wz);
                    move = true;
                    break;

                default:
                    if (_timer <= 0f) PickWanderTarget(pos, world);
                    break;
            }

            if (move)
            {
                // Never run into the sea: turn along the shore instead
                Vector3 ahead = pos + SMath.YawForward(desiredYaw) * 3.5f;
                if (world.GroundHeight(ahead.X, ahead.Z) < world.WaterLevel - 0.3f)
                {
                    desiredYaw = SMath.WrapDeg(desiredYaw + 100f);
                    if (State == AnimalState.Wander) Enter(AnimalState.Rest, 1f);
                }
                _stuck += dt;
                if (_stuck > 2f)
                {
                    if (SMath.DistanceXZ(pos, _lastPos) < 0.6f) desiredYaw = SMath.WrapDeg(desiredYaw + _rng.Range(60f, 140f));
                    _lastPos = pos;
                    _stuck = 0f;
                }
                input.MoveY = 127;
                if (sprint) input.Buttons |= InputButtons.Sprint;
            }

            float turn = 300f * dt;
            _yaw = SMath.WrapDeg(_yaw + SMath.Clamp(SMath.DeltaAngleDeg(_yaw, desiredYaw), -turn, turn));
            input.YawDegrees = _yaw;
            return input;
        }

        private void Enter(AnimalState s, float seconds)
        {
            State = s;
            _timer = seconds;
        }

        private void PickWanderTarget(Vector3 pos, WorldRuntime world)
        {
            for (int i = 0; i < 6; i++)
            {
                Vector3 t = pos + SMath.YawForward(_rng.Range(0f, 360f)) * _rng.Range(8f, 30f);
                if (!world.Heights.IsInsideWorld(t.X, t.Z)) continue;
                if (world.GroundHeight(t.X, t.Z) < world.WaterLevel + 0.8f) continue;
                if (world.Heights.GetSlopeTanGrid(t.X, t.Z) > 0.7f) continue;
                Target = t;
                Enter(AnimalState.Wander, 14f);
                return;
            }
            Enter(AnimalState.Rest, 3f);
        }
    }
}

namespace Survival.Server
{
    /// <summary>Wildlife population, melee combat, placeable structures and cooking.</summary>
    public sealed partial class GameServer
    {
        private const float AnimalRange = 210f;
        private const float AnimalDespawnRange = 290f;
        private const int MaxAnimals = 160;
        private const int MaxStructures = 400;

        private readonly List<ServerEntity> _animals = new List<ServerEntity>(64);
        private readonly List<ServerEntity> _structures = new List<ServerEntity>(32);
        private Rng _animalRng;
        private double _lastAnimalBalance;

        public IReadOnlyList<ServerEntity> Animals => _animals;
        public IReadOnlyList<ServerEntity> Structures => _structures;

        private static string AnimalName(AnimalKind k) => k == AnimalKind.Deer ? "Deer" : k == AnimalKind.Boar ? "Boar" : "Wolf";

        public ServerEntity SpawnAnimal(AnimalKind kind, Vector3 pos)
        {
            ServerEntity e = CreateEntity(EntityType.Animal, AnimalName(kind), pos, _animalRng.Range(0f, 360f));
            e.SubType = (byte)kind;
            e.Animal = new AnimalBrain(kind, _animalRng.NextUInt(), e.Yaw);
            _animals.Add(e);
            return e;
        }

        private void RemoveAnimal(ServerEntity e, DespawnReason reason)
        {
            _animals.Remove(e);
            RemoveEntity(e, reason);
        }

        private ServerEntity NearestPlayer(Vector3 pos, out float dist)
        {
            ServerEntity best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < _sessionList.Count; i++)
            {
                PlayerSession s = _sessionList[i];
                ServerEntity p = s.Entity;
                if (p == null || p.Removed || p.IsDead || !s.IsConnected || !s.Ready) continue;
                float d = SMath.DistanceSqXZ(p.Move.Position, pos);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = p;
                }
            }
            dist = best != null ? (float)Math.Sqrt(bestSq) : float.MaxValue;
            return best;
        }

        /// <summary>Keeps a few animals around every player and removes the ones nobody is near.</summary>
        private void BalanceAnimals()
        {
            int perPlayer = (int)Math.Round(Config.AnimalsPerPlayer * Math.Max(0f, Config.AnimalDensity));
            for (int i = _animals.Count - 1; i >= 0; i--)
            {
                NearestPlayer(_animals[i].Move.Position, out float d);
                if (d > AnimalDespawnRange) RemoveAnimal(_animals[i], DespawnReason.OutOfRange);
            }
            if (perPlayer == 0) return;
            for (int i = 0; i < _sessionList.Count && _animals.Count < MaxAnimals; i++)
            {
                PlayerSession s = _sessionList[i];
                ServerEntity p = s.Entity;
                if (p == null || p.Removed || !s.IsConnected || !s.Ready) continue;
                int near = 0;
                for (int k = 0; k < _animals.Count; k++)
                {
                    if (SMath.DistanceSqXZ(_animals[k].Move.Position, p.Move.Position) < AnimalRange * AnimalRange) near++;
                }
                if (near >= perPlayer) continue;
                // One new animal per balance pass, out of sight range of a close look
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    Vector3 pos = p.Move.Position + SMath.YawForward(_animalRng.Range(0f, 360f)) * _animalRng.Range(55f, 150f);
                    if (!SpawnFinder.IsSafe(World, pos.X, pos.Z, out float y)) continue;
                    float roll = _animalRng.NextFloat();
                    AnimalKind kind = roll < 0.5f ? AnimalKind.Deer : roll < 0.8f ? AnimalKind.Boar : AnimalKind.Wolf;
                    SpawnAnimal(kind, new Vector3(pos.X, y, pos.Z));
                    break;
                }
            }
        }

        private void TickWildlife(double now)
        {
            if (now - _lastAnimalBalance >= 1.5)
            {
                _lastAnimalBalance = now;
                BalanceAnimals();
            }
            for (int i = 0; i < _animals.Count; i++)
            {
                ServerEntity a = _animals[i];
                ServerEntity player = NearestPlayer(a.Move.Position, out float dist);
                // Animals far from everyone only think a few times per second
                bool near = dist < 90f;
                if (!near && (CurrentTick + a.Id) % 6 != 0) continue;
                float dt = near ? TickDt : TickDt * 6f;
                PlayerInput input = a.Animal.Think(a, this, player, dist, dt);
                a.Yaw = input.YawDegrees;
                MovementSim.Step(ref a.Move, input, dt, World, Movement);
                OnEntityMoved(a);
            }
        }

        private void HandleAttack(PlayerSession s, uint entityId)
        {
            if (!CanAct(s)) return;
            double now = Time;
            if (now - s.LastSwingAt < GatherRules.SwingSeconds * 0.8) return;
            s.LastSwingAt = now;
            ServerEntity e = s.Entity;
            if (!TryGetEntity(entityId, out ServerEntity target) || target == e || target.Removed || target.IsDead) return;
            if (target.Type != EntityType.Animal && target.Type != EntityType.Bot) return;
            if (SMath.DistanceSqXZ(target.Move.Position, e.Move.Position) > (CombatRules.MeleeReach + 0.8f) * (CombatRules.MeleeReach + 0.8f)) return;
            if (Math.Abs(target.Move.Position.Y - e.Move.Position.Y) > 3f) return;

            int slot = Math.Min((int)s.LastInput.HotbarSlot, Inventory.HotbarSlots - 1);
            float damage = ItemDb.ToolOf(e.Items.Slots[slot]).Damage;
            var result = new AttackResultMsg { EntityId = entityId };
            if (target.Type == EntityType.Animal)
            {
                var kind = (AnimalKind)target.SubType;
                target.Stats.Health = Math.Max(0f, target.Stats.Health - damage * CombatRules.DamageScale(kind));
                if (target.Stats.Health <= 0f)
                {
                    CombatRules.Loot(kind, out int meat, out int hide, out int bone);
                    e.Items.Add(ItemId.RawMeat, meat);
                    e.Items.Add(ItemId.Hide, hide);
                    e.Items.Add(ItemId.Bone, bone);
                    result.Killed = true;
                    result.Meat = (byte)meat;
                    result.Hide = (byte)hide;
                    result.Bone = (byte)bone;
                    RemoveAnimal(target, DespawnReason.Died);
                    SendInventory(s);
                }
                else
                {
                    target.Animal.Provoke(e);
                }
            }
            else
            {
                ApplyDamage(target, damage, DamageType.Melee, e);
                result.Killed = target.IsDead;
            }
            result.Health = NetCodec.EncodePercent(target.Stats.Health);
            _writer.Reset();
            result.Write(_writer);
            s.Peer.Send(_writer, NetProtocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
        }

        private void HandlePlace(PlayerSession s, byte slot, float x, float z, float yaw)
        {
            if (!CanAct(s) || slot >= Inventory.Size) return;
            ServerEntity e = s.Entity;
            ItemDef def = ItemDb.Get(e.Items.Slots[slot].Id);
            if (def == null || def.Kind != ItemKind.Placeable || float.IsNaN(x) || float.IsNaN(z)) return;
            float dx = x - e.Move.Position.X, dz = z - e.Move.Position.Z;
            bool ok = dx * dx + dz * dz <= CombatRules.PlaceReach * CombatRules.PlaceReach
                      && World.Heights.IsInsideWorld(x, z)
                      && World.GroundHeight(x, z) > World.WaterLevel + 0.3f
                      && World.Heights.GetSlopeDeg(x, z) < 28f
                      && !World.OverlapsObstacle(x, z, 0.7f)
                      && _structures.Count < MaxStructures;
            for (int i = 0; ok && i < _structures.Count; i++)
            {
                if (SMath.DistanceSqXZ(_structures[i].Move.Position, new Vector3(x, 0, z)) < 1.6f * 1.6f) ok = false;
            }
            if (!ok || !e.Items.RemoveAt(slot, 1))
            {
                SendNotice(s, NoticeKind.Warning, "place_failed", 2f);
                return;
            }
            ServerEntity st = CreateEntity(EntityType.Structure, string.Empty, new Vector3(x, 0, z), SMath.WrapDeg(yaw));
            st.SubType = (byte)StructureKind.Campfire;
            _structures.Add(st);
            SendInventory(s);
        }

        private bool NearCampfire(Vector3 pos)
        {
            for (int i = 0; i < _structures.Count; i++)
            {
                ServerEntity st = _structures[i];
                if (st.SubType == (byte)StructureKind.Campfire && SMath.DistanceSqXZ(st.Move.Position, pos) <= CombatRules.CookReach * CombatRules.CookReach) return true;
            }
            return false;
        }

        private void HandleCook(PlayerSession s)
        {
            if (!CanAct(s)) return;
            double now = Time;
            if (now - s.LastUseAt < 0.35) return;
            Inventory inv = s.Entity.Items;
            if (inv.CountOf(ItemId.RawMeat) <= 0 || !NearCampfire(s.Entity.Move.Position)) return;
            s.LastUseAt = now;
            inv.Remove(ItemId.RawMeat, 1);
            if (inv.Add(ItemId.CookedMeat, 1) > 0) inv.Add(ItemId.RawMeat, 1); // no room: give it back
            SendInventory(s);
        }
    }
}
