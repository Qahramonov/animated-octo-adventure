using System;
using System.Numerics;
using Survival.Core;
using Survival.World;

namespace Survival.Gameplay
{
    [Flags]
    public enum MoveFlags : byte
    {
        None = 0,
        Grounded = 1 << 0,
        Crouching = 1 << 1,
        Sprinting = 1 << 2,
        Swimming = 1 << 3,
        Moving = 1 << 4,
        Dead = 1 << 5,
    }

    /// <summary>Authoritative kinematic state of a character (player or bot).</summary>
    public struct MoveState
    {
        /// <summary>Feet position.</summary>
        public Vector3 Position;
        public Vector3 Velocity;
        public MoveFlags Flags;
        /// <summary>0..100, drained by sprinting/jumping/swimming. Part of the predicted state.</summary>
        public float Stamina;

        public bool Grounded => (Flags & MoveFlags.Grounded) != 0;
        public bool Crouching => (Flags & MoveFlags.Crouching) != 0;
        public bool Sprinting => (Flags & MoveFlags.Sprinting) != 0;
        public bool Swimming => (Flags & MoveFlags.Swimming) != 0;

        public static MoveState At(Vector3 feet) => new MoveState
        {
            Position = feet,
            Velocity = Vector3.Zero,
            Flags = MoveFlags.Grounded,
            Stamina = 100f,
        };
    }

    /// <summary>Tunable movement constants (identical on client and server).</summary>
    public sealed class MovementSettings
    {
        public static readonly MovementSettings Default = new MovementSettings();

        public float WalkSpeed = 4.2f;
        public float SprintSpeed = 6.8f;
        public float CrouchSpeed = 1.9f;
        public float SwimSpeed = 2.6f;
        public float ShallowWaterFactor = 0.7f;
        public float GroundAcceleration = 40f;
        public float AirAcceleration = 8f;
        public float Gravity = 22f;
        public float JumpVelocity = 6.4f;
        public float MaxClimbTan = 1.15f;     // ~49 degrees: steeper ground blocks walking uphill
        public float StepHeight = 0.45f;
        public float CapsuleRadius = 0.35f;
        public float StandHeight = 1.8f;
        public float CrouchHeight = 1.15f;
        public float EyeStand = 1.62f;
        public float EyeCrouch = 1.0f;
        public float SwimFeetBelowSurface = 1.25f; // head stays above water while swimming
        public float SprintStaminaPerSec = 9f;
        public float SwimStaminaPerSec = 3f;
        public float JumpStaminaCost = 7f;
        public float StaminaRegenPerSec = 12f;
        public float MinStaminaToSprint = 5f;
        public float EdgeMargin = 20f;
    }

    /// <summary>
    /// Deterministic character movement shared by client-side prediction and the server.
    /// Ground is the quantized height field (no physics engine on the server), obstacles are the
    /// trees/rocks of the deterministic node layout.
    /// </summary>
    public static class MovementSim
    {
        public static void Step(ref MoveState s, in PlayerInput input, float dt, IMovementWorld world, MovementSettings m)
        {
            if ((s.Flags & MoveFlags.Dead) != 0)
            {
                s.Velocity = Vector3.Zero;
                return;
            }

            float water = world.WaterLevel;
            float ground = world.GroundHeight(s.Position.X, s.Position.Z);
            float waterDepth = water - ground;
            bool deepWater = waterDepth > m.SwimFeetBelowSurface + 0.15f;
            bool swimming = deepWater && s.Position.Y <= water - m.SwimFeetBelowSurface + 0.3f;

            // --- Desired horizontal velocity from stick + yaw
            float yaw = input.YawDegrees;
            Vector3 fwd = SMath.YawForward(yaw);
            Vector3 right = SMath.YawRight(yaw);
            float ix = input.MoveXf, iy = input.MoveYf;
            float mag = (float)Math.Sqrt(ix * ix + iy * iy);
            if (mag > 1f)
            {
                ix /= mag;
                iy /= mag;
                mag = 1f;
            }
            Vector3 wish = fwd * iy + right * ix;

            bool crouch = input.Has(InputButtons.Crouch) && !swimming;
            bool wantsSprint = input.Has(InputButtons.Sprint) && iy > 0.3f && !crouch && !swimming;
            bool sprint = wantsSprint && s.Stamina > m.MinStaminaToSprint;

            float speed = swimming ? m.SwimSpeed : crouch ? m.CrouchSpeed : sprint ? m.SprintSpeed : m.WalkSpeed;
            if (!swimming && waterDepth > 0.35f) speed *= m.ShallowWaterFactor;

            Vector3 targetVel = wish * speed;
            bool grounded = s.Grounded && !swimming;
            float accel = (grounded || swimming) ? m.GroundAcceleration : m.AirAcceleration;
            float vx = SMath.MoveTowards(s.Velocity.X, targetVel.X, accel * dt);
            float vz = SMath.MoveTowards(s.Velocity.Z, targetVel.Z, accel * dt);
            float vy = s.Velocity.Y;

            // --- Stamina
            float stamina = s.Stamina;
            if (sprint && mag > 0.1f) stamina -= m.SprintStaminaPerSec * dt;
            else if (swimming && mag > 0.1f) stamina -= m.SwimStaminaPerSec * dt;
            else stamina += m.StaminaRegenPerSec * dt;

            // --- Vertical
            if (swimming)
            {
                float surfaceFeet = water - m.SwimFeetBelowSurface;
                vy = (surfaceFeet - s.Position.Y) * 4f; // float to the surface
            }
            else if (grounded)
            {
                vy = 0f;
                if (input.Has(InputButtons.Jump) && !crouch && stamina >= m.JumpStaminaCost * 0.5f)
                {
                    vy = m.JumpVelocity;
                    grounded = false;
                    stamina -= m.JumpStaminaCost;
                }
            }
            else
            {
                vy -= m.Gravity * dt;
            }
            stamina = SMath.Clamp(stamina, 0f, 100f);

            // --- Horizontal move with slope limit and obstacles
            float nx = s.Position.X + vx * dt;
            float nz = s.Position.Z + vz * dt;
            float edgeMin = m.EdgeMargin, edgeMax = world.WorldSize - m.EdgeMargin;
            nx = SMath.Clamp(nx, edgeMin, edgeMax);
            nz = SMath.Clamp(nz, edgeMin, edgeMax);

            if (!swimming)
            {
                float newGround = world.GroundHeight(nx, nz);
                float horiz = (float)Math.Sqrt((nx - s.Position.X) * (nx - s.Position.X) + (nz - s.Position.Z) * (nz - s.Position.Z));
                float rise = newGround - ground;
                if (horiz > 1e-5f && rise > m.StepHeight * 0.25f && rise / horiz > m.MaxClimbTan && newGround > s.Position.Y - 0.05f)
                {
                    // Too steep to climb: stay put horizontally on this axis pair
                    nx = s.Position.X;
                    nz = s.Position.Z;
                    vx = 0f;
                    vz = 0f;
                }
            }

            world.ResolveObstacles(ref nx, ref nz, m.CapsuleRadius);

            float ny = s.Position.Y + vy * dt;
            float g = world.GroundHeight(nx, nz);
            MoveFlags flags = MoveFlags.None;

            if (swimming)
            {
                if (ny < g) ny = g;
                flags |= MoveFlags.Swimming;
            }
            else if (ny <= g + 0.02f || (grounded && vy <= 0f && ny - g < m.StepHeight + 0.3f))
            {
                // Landed or walking along the ground (snap down small drops)
                ny = g;
                vy = 0f;
                flags |= MoveFlags.Grounded;
            }

            if (crouch) flags |= MoveFlags.Crouching;
            if (sprint && mag > 0.1f) flags |= MoveFlags.Sprinting;
            if (mag > 0.1f) flags |= MoveFlags.Moving;

            // Entering deep water while falling/walking switches to swimming next tick
            if (!swimming && water - g > m.SwimFeetBelowSurface + 0.15f && ny <= water - m.SwimFeetBelowSurface + 0.3f)
            {
                flags |= MoveFlags.Swimming;
                flags &= ~MoveFlags.Grounded;
            }

            s.Position = new Vector3(nx, ny, nz);
            s.Velocity = new Vector3(vx, vy, vz);
            s.Flags = flags;
            s.Stamina = stamina;
        }

        public static float EyeHeight(in MoveState s, MovementSettings m) =>
            s.Crouching ? m.EyeCrouch : m.EyeStand;
    }
}
