using System;

namespace Survival.Gameplay
{
    /// <summary>Server-authoritative vital stats of a player or bot.</summary>
    public struct SurvivalStats
    {
        public const float Max = 100f;

        public float Health;
        public float Hunger;   // 100 = full, 0 = starving
        public float Thirst;   // 100 = hydrated, 0 = dehydrated
        /// <summary>Bleeding intensity, health lost per second (0 = not bleeding). Extended in Phase 3.</summary>
        public float Bleeding;

        public bool IsDead => Health <= 0f;

        public static SurvivalStats Fresh => new SurvivalStats { Health = 100f, Hunger = 80f, Thirst = 80f, Bleeding = 0f };
    }

    public enum DamageType : byte
    {
        Generic,
        Melee,
        Arrow,
        Bite,
        Fall,
        Drowning,
        Starvation,
        Dehydration,
        Bleeding,
    }

    /// <summary>Tunable survival rates (server config can override).</summary>
    public sealed class SurvivalSettings
    {
        public static readonly SurvivalSettings Default = new SurvivalSettings();

        public float HungerPerSecond = 100f / (60f * 55f);  // ~55 min from full to empty
        public float ThirstPerSecond = 100f / (60f * 38f);  // ~38 min
        public float SprintDrainMultiplier = 1.8f;
        public float StarvationDamagePerSecond = 0.6f;
        public float DehydrationDamagePerSecond = 0.9f;
        public float RegenPerSecond = 0.35f;     // when well fed and hydrated
        public float RegenThreshold = 60f;
        public float BleedDecayPerSecond = 0.08f;
        public float SeaWaterThirstPerSecond = 0f; // reserved: drinking sea water (Phase 3)
        public float FallDamageMinSpeed = 11f;
        public float FallDamagePerMs = 9f;
    }

    /// <summary>Pure rules for vitals; called by the server simulation each tick.</summary>
    public static class SurvivalRules
    {
        /// <summary>Advances vitals. Returns damage taken this step (already applied) and its main cause.</summary>
        public static float Tick(ref SurvivalStats s, float dt, bool sprinting, SurvivalSettings cfg, out DamageType cause)
        {
            cause = DamageType.Generic;
            if (s.IsDead) return 0f;

            float drain = sprinting ? cfg.SprintDrainMultiplier : 1f;
            s.Hunger = Math.Max(0f, s.Hunger - cfg.HungerPerSecond * drain * dt);
            s.Thirst = Math.Max(0f, s.Thirst - cfg.ThirstPerSecond * drain * dt);

            float damage = 0f;
            if (s.Hunger <= 0f)
            {
                damage += cfg.StarvationDamagePerSecond * dt;
                cause = DamageType.Starvation;
            }
            if (s.Thirst <= 0f)
            {
                damage += cfg.DehydrationDamagePerSecond * dt;
                cause = DamageType.Dehydration;
            }
            if (s.Bleeding > 0f)
            {
                damage += s.Bleeding * dt;
                s.Bleeding = Math.Max(0f, s.Bleeding - cfg.BleedDecayPerSecond * dt);
                cause = DamageType.Bleeding;
            }

            if (damage > 0f)
            {
                s.Health = Math.Max(0f, s.Health - damage);
            }
            else if (s.Hunger > cfg.RegenThreshold && s.Thirst > cfg.RegenThreshold && s.Health < SurvivalStats.Max)
            {
                s.Health = Math.Min(SurvivalStats.Max, s.Health + cfg.RegenPerSecond * dt);
            }
            return damage;
        }

        public static float FallDamage(float impactSpeed, SurvivalSettings cfg) =>
            impactSpeed <= cfg.FallDamageMinSpeed ? 0f : (impactSpeed - cfg.FallDamageMinSpeed) * cfg.FallDamagePerMs;

        public static void Eat(ref SurvivalStats s, float food, float water, float heal)
        {
            s.Hunger = Math.Min(SurvivalStats.Max, s.Hunger + food);
            s.Thirst = Math.Min(SurvivalStats.Max, s.Thirst + water);
            s.Health = Math.Min(SurvivalStats.Max, s.Health + heal);
        }
    }
}
