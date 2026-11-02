using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Survival.Core
{
    /// <summary>
    /// Engine-independent math helpers used by both the Unity client and the .NET dedicated server.
    /// Only IEEE-754 basic operations (+ - * / sqrt floor) are used in code that must give identical
    /// results on the server and on the phone (world generation, movement).
    /// </summary>
    public static class SMath
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float InverseLerp(float a, float b, float v) => Math.Abs(b - a) < 1e-9f ? 0f : Clamp01((v - a) / (b - a));

        /// <summary>Hermite smoothstep; edge0 may be greater than edge1 (inverted ramp).</summary>
        public static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3.0 - 2.0 * t);
        }

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }

        /// <summary>Wraps an angle in degrees to [0, 360).</summary>
        public static float WrapDeg(float a)
        {
            a %= 360f;
            return a < 0f ? a + 360f : a;
        }

        /// <summary>Shortest signed difference b - a in degrees, range (-180, 180].</summary>
        public static float DeltaAngleDeg(float a, float b)
        {
            float d = WrapDeg(b - a);
            return d > 180f ? d - 360f : d;
        }

        public static float LerpAngleDeg(float a, float b, float t) => a + DeltaAngleDeg(a, b) * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorToInt(double v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorToInt(float v)
        {
            int i = (int)v;
            return v < i ? i - 1 : i;
        }

        public static float DistanceXZ(in Vector3 a, in Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static float DistanceSqXZ(in Vector3 a, in Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return dx * dx + dz * dz;
        }

        /// <summary>Forward vector on the XZ plane for a yaw in degrees (0 = +Z, 90 = +X) - same convention as Unity.</summary>
        public static Vector3 YawForward(float yawDeg)
        {
            double r = yawDeg * (Math.PI / 180.0);
            return new Vector3((float)Math.Sin(r), 0f, (float)Math.Cos(r));
        }

        public static Vector3 YawRight(float yawDeg)
        {
            double r = yawDeg * (Math.PI / 180.0);
            return new Vector3((float)Math.Cos(r), 0f, (float)-Math.Sin(r));
        }

        /// <summary>Yaw in degrees for a direction on the XZ plane (Unity convention).</summary>
        public static float YawFromDirection(float dx, float dz) => WrapDeg((float)(Math.Atan2(dx, dz) * (180.0 / Math.PI)));
    }
}
