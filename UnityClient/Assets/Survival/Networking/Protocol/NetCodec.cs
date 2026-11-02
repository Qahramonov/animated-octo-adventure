using System;
using System.Numerics;
using LiteNetLib.Utils;
using Survival.Core;

namespace Survival.Networking
{
    /// <summary>
    /// Compact encodings for the hot paths (snapshots, inputs). Positions of remote entities are
    /// quantized to ~4 mm on X/Z and ~1.6 cm on Y which is invisible after interpolation.
    /// </summary>
    public static class NetCodec
    {
        public const float PosXZScale = 256f;   // 1/256 m, 24 bit -> up to 65 km
        public const float PosYScale = 64f;     // 1/64 m, 16 bit -> 0..1024 m

        public static void PutVarUInt(this NetDataWriter w, uint value)
        {
            while (value >= 0x80)
            {
                w.Put((byte)(value | 0x80));
                value >>= 7;
            }
            w.Put((byte)value);
        }

        public static uint GetVarUInt(this NetDataReader r)
        {
            uint result = 0;
            int shift = 0;
            while (shift < 35)
            {
                byte b = r.GetByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
            throw new FormatException("VarUInt too long");
        }

        public static void PutUInt24(this NetDataWriter w, uint v)
        {
            w.Put((byte)v);
            w.Put((byte)(v >> 8));
            w.Put((byte)(v >> 16));
        }

        public static uint GetUInt24(this NetDataReader r)
        {
            uint a = r.GetByte();
            uint b = r.GetByte();
            uint c = r.GetByte();
            return a | (b << 8) | (c << 16);
        }

        public static void PutQuantPos(this NetDataWriter w, Vector3 p)
        {
            w.PutUInt24((uint)SMath.Clamp((int)(p.X * PosXZScale + 0.5f), 0, 0xFFFFFF));
            w.PutUInt24((uint)SMath.Clamp((int)(p.Z * PosXZScale + 0.5f), 0, 0xFFFFFF));
            w.Put((ushort)SMath.Clamp((int)(p.Y * PosYScale + 0.5f), 0, 0xFFFF));
        }

        public static Vector3 GetQuantPos(this NetDataReader r)
        {
            float x = r.GetUInt24() / PosXZScale;
            float z = r.GetUInt24() / PosXZScale;
            float y = r.GetUShort() / PosYScale;
            return new Vector3(x, y, z);
        }

        public static void PutVector3(this NetDataWriter w, Vector3 v)
        {
            w.Put(v.X);
            w.Put(v.Y);
            w.Put(v.Z);
        }

        public static Vector3 GetVector3(this NetDataReader r) => new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());

        public static ushort EncodeYaw(float deg) => (ushort)((int)(SMath.WrapDeg(deg) * (65536f / 360f) + 0.5f) & 0xFFFF);
        public static float DecodeYaw(ushort v) => v * (360f / 65536f);

        public static byte EncodePercent(float v) => (byte)SMath.Clamp((int)(v * 2.55f + 0.5f), 0, 255);
        public static float DecodePercent(byte b) => b / 2.55f;

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Survivor";
            var chars = new System.Text.StringBuilder(NetProtocol.MaxNameLength);
            foreach (char c in name.Trim())
            {
                if (chars.Length >= NetProtocol.MaxNameLength) break;
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ' ') chars.Append(c);
            }
            string s = chars.ToString().Trim();
            return s.Length < 2 ? "Survivor" : s;
        }
    }
}
