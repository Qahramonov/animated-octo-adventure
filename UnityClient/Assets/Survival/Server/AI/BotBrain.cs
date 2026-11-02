using System;
using System.Numerics;
using Survival.Core;
using Survival.Gameplay;
using Survival.World;

namespace Survival.Server.AI
{
    public enum BotSkill : byte
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }

    public enum BotState : byte
    {
        Idle,
        Wander,
        Rest,
    }

    /// <summary>
    /// Phase 1 bot brain: explores the island (wander to random reachable land targets, rest,
    /// look around), avoids deep water and gets unstuck. It produces the same <see cref="PlayerInput"/>
    /// a human would, so bots obey exactly the same movement rules as players.
    /// Phase 5 extends this with gathering, hunting, combat, retreat/heal and looting.
    /// </summary>
    public sealed class BotBrain
    {
        public BotSkill Skill;
        public BotState State = BotState.Idle;
        public Vector3 Target;
        public bool NearPlayer;
        public int TickPhase;

        private Rng _rng;
        private float _stateTimer;
        private float _thinkTimer;
        private float _stuckTimer;
        private Vector3 _lastProgressPos;
        private float _yaw;
        private bool _sprint;

        public BotBrain(uint seed, BotSkill skill)
        {
            _rng = new Rng(seed);
            Skill = skill;
            TickPhase = (int)(seed % 6);
        }

        public PlayerInput Think(ServerEntity self, WorldRuntime world, float dt)
        {
            _stateTimer -= dt;
            _thinkTimer -= dt;
            Vector3 pos = self.Move.Position;

            if (_thinkTimer <= 0f)
            {
                _thinkTimer = 0.5f + _rng.NextFloat() * 0.5f;
                Decide(self, world);
            }

            var input = new PlayerInput { MoveX = 0, MoveY = 0 };
            if (State == BotState.Wander)
            {
                float dx = Target.X - pos.X, dz = Target.Z - pos.Z;
                float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dist < 3f)
                {
                    EnterState(BotState.Rest, 3f + _rng.NextFloat() * 6f);
                }
                else
                {
                    float desiredYaw = SMath.YawFromDirection(dx, dz);
                    float turnRate = Skill == BotSkill.Hard ? 360f : Skill == BotSkill.Normal ? 240f : 160f;
                    _yaw = SMath.WrapDeg(_yaw + SMath.Clamp(SMath.DeltaAngleDeg(_yaw, desiredYaw), -turnRate * dt, turnRate * dt));
                    input.MoveY = 127;

                    // Look ahead: never walk into deep water
                    Vector3 ahead = pos + SMath.YawForward(_yaw) * 4f;
                    if (world.GroundHeight(ahead.X, ahead.Z) < world.WaterLevel - 0.6f && !self.Move.Swimming)
                    {
                        PickTarget(self, world, 120f);
                    }

                    // Stuck detection (obstacles, steep slopes)
                    _stuckTimer += dt;
                    if (_stuckTimer > 2.5f)
                    {
                        if (SMath.DistanceXZ(pos, _lastProgressPos) < 1.0f) PickTarget(self, world, 80f);
                        _lastProgressPos = pos;
                        _stuckTimer = 0f;
                    }
                }
            }
            else if (State == BotState.Rest || State == BotState.Idle)
            {
                // Look around while resting
                _yaw = SMath.WrapDeg(_yaw + (float)Math.Sin(_stateTimer * 0.7f) * 25f * dt);
            }

            if (_sprint && self.Move.Stamina > 30f) input.Buttons |= InputButtons.Sprint;
            input.YawDegrees = _yaw;
            return input;
        }

        private void Decide(ServerEntity self, WorldRuntime world)
        {
            if (_stateTimer > 0f) return;
            switch (State)
            {
                case BotState.Idle:
                case BotState.Rest:
                    PickTarget(self, world, 60f + _rng.NextFloat() * 220f);
                    break;
                case BotState.Wander:
                    // Wandering timed out (target unreachable): rest briefly, then pick again
                    EnterState(BotState.Rest, 1f + _rng.NextFloat() * 2f);
                    break;
            }
        }

        private void PickTarget(ServerEntity self, WorldRuntime world, float range)
        {
            Vector3 pos = self.Move.Position;
            for (int i = 0; i < 12; i++)
            {
                float angle = _rng.Range(0f, 360f);
                float dist = _rng.Range(range * 0.4f, range);
                Vector3 t = pos + SMath.YawForward(angle) * dist;
                if (!world.Heights.IsInsideWorld(t.X, t.Z)) continue;
                float h = world.GroundHeight(t.X, t.Z);
                if (h < world.WaterLevel + 1.0f) continue;
                if (world.Heights.GetSlopeTanGrid(t.X, t.Z) > 0.8f) continue;
                Target = new Vector3(t.X, h, t.Z);
                _sprint = _rng.Chance(Skill == BotSkill.Hard ? 0.35f : 0.15f);
                _lastProgressPos = pos;
                _stuckTimer = 0f;
                EnterState(BotState.Wander, dist / 3.5f + 10f);
                return;
            }
            // Could not find land nearby (e.g. swimming): head towards the island centre
            float c = (float)world.Generator.Center;
            Target = new Vector3(c, pos.Y, c);
            EnterState(BotState.Wander, 30f);
        }

        private void EnterState(BotState s, float duration)
        {
            State = s;
            _stateTimer = duration;
        }
    }
}
