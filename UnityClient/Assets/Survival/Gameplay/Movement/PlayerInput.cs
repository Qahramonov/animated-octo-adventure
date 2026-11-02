using System;

namespace Survival.Gameplay
{
    [Flags]
    public enum InputButtons : ushort
    {
        None = 0,
        Jump = 1 << 0,
        Crouch = 1 << 1,
        Sprint = 1 << 2,
        Attack = 1 << 3,
        Interact = 1 << 4,
        Reload = 1 << 5,
        Aim = 1 << 6,
    }

    /// <summary>
    /// One fixed-tick worth of player intent. Sent by the client every simulation tick (with
    /// redundancy), executed by the server with a fixed delta time so speed cannot be cheated.
    /// </summary>
    public struct PlayerInput
    {
        /// <summary>Client-side monotonically increasing input sequence number.</summary>
        public uint Sequence;
        /// <summary>Movement stick, -127..127 each axis (x = strafe, y = forward).</summary>
        public sbyte MoveX;
        public sbyte MoveY;
        /// <summary>View yaw, 0..65535 maps to 0..360 degrees.</summary>
        public ushort Yaw;
        /// <summary>View pitch in 1/100 degree, -9000..9000.</summary>
        public short Pitch;
        public InputButtons Buttons;
        /// <summary>Selected hotbar slot (Phase 2 items).</summary>
        public byte HotbarSlot;

        public float YawDegrees
        {
            get => Yaw * (360f / 65536f);
            set
            {
                float w = value % 360f;
                if (w < 0f) w += 360f;
                Yaw = (ushort)((int)(w * (65536f / 360f) + 0.5f) & 0xFFFF);
            }
        }

        public float PitchDegrees
        {
            get => Pitch * 0.01f;
            set => Pitch = (short)Math.Max(-9000, Math.Min(9000, (int)Math.Round(value * 100f)));
        }

        public float MoveXf => MoveX / 127f;
        public float MoveYf => MoveY / 127f;

        public bool Has(InputButtons b) => (Buttons & b) != 0;

        public static sbyte EncodeAxis(float v)
        {
            int i = (int)Math.Round(Math.Max(-1f, Math.Min(1f, v)) * 127f);
            return (sbyte)i;
        }
    }
}
