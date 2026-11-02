using System.Runtime.CompilerServices;

namespace Survival.Core
{
    /// <summary>
    /// Integer hashing used for deterministic procedural content (identical on every platform,
    /// independent of floating point behaviour).
    /// </summary>
    public static class Hashing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Mix(uint x)
        {
            // lowbias32 by Chris Wellons
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Combine(uint a, uint b) => Mix(a ^ (b + 0x9e3779b9U + (a << 6) + (a >> 2)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Hash2(int seed, int x, int y) => Mix(Combine(Combine((uint)seed, (uint)x), (uint)y * 0x27d4eb2dU));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Hash3(int seed, int x, int y, int z) => Mix(Combine(Hash2(seed, x, y), (uint)z * 0x165667b1U));

        /// <summary>Maps a hash to [0, 1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float To01(uint h) => (h >> 8) * (1f / 16777216f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double To01d(uint h) => (h >> 5) * (1.0 / 134217728.0);

        /// <summary>FNV-1a 32 bit for strings (stable across runtimes, unlike string.GetHashCode).</summary>
        public static uint Fnv1a(string s)
        {
            uint h = 2166136261U;
            if (s == null) return h;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 16777619U;
            }
            return h;
        }
    }

    /// <summary>
    /// Small, fast, deterministic PRNG (PCG32). Use instead of System.Random for anything that must
    /// reproduce on the server and client.
    /// </summary>
    public struct Rng
    {
        private ulong _state;
        private const ulong Inc = 1442695040888963407UL;

        public Rng(uint seed)
        {
            _state = 0;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = old * 6364136223846793005UL + Inc;
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>[0, 1)</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Integer in [min, max).</summary>
        public int Range(int min, int max) => max <= min ? min : min + (int)(NextUInt() % (uint)(max - min));

        public bool Chance(float p) => NextFloat() < p;
    }
}
