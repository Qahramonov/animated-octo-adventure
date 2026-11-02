using System;
using System.Collections.Generic;
using System.Numerics;
using LiteNetLib.Utils;
using Survival.Core;
using Survival.Gameplay;
using Survival.Networking;
using Survival.World;
using Xunit;

namespace Survival.Tests
{
    /// <summary>Flat test world: ground at 50 m, sea at 30 m, optional deep water band and one tree.</summary>
    internal sealed class FlatWorld : IMovementWorld
    {
        public float WorldSize => 1000f;
        public float WaterLevel => 30f;
        public float WaterStartX = float.MaxValue;
        public Vector2? Tree;

        public float GroundHeight(float x, float z) => x >= WaterStartX ? 10f : 50f;

        public void ResolveObstacles(ref float x, ref float z, float radius)
        {
            if (Tree == null) return;
            Vector2 t = Tree.Value;
            float dx = x - t.X, dz = z - t.Y;
            float min = 0.5f + radius;
            float d = MathF.Sqrt(dx * dx + dz * dz);
            if (d >= min || d < 1e-4f) return;
            x = t.X + dx / d * min;
            z = t.Y + dz / d * min;
        }
    }

    public class MovementTests
    {
        private static PlayerInput Forward(float yaw, InputButtons b = InputButtons.None)
        {
            var i = new PlayerInput { MoveY = 127, Buttons = b };
            i.YawDegrees = yaw;
            return i;
        }

        [Fact]
        public void Walking_MovesAtWalkSpeed()
        {
            var w = new FlatWorld();
            var s = MoveState.At(new Vector3(500, 50, 500));
            for (int i = 0; i < 60; i++) MovementSim.Step(ref s, Forward(0), 1f / 30f, w, MovementSettings.Default);
            float dist = s.Position.Z - 500f;
            Assert.InRange(dist, MovementSettings.Default.WalkSpeed * 2f * 0.9f, MovementSettings.Default.WalkSpeed * 2f * 1.01f);
            Assert.Equal(50f, s.Position.Y, 3);
            Assert.True(s.Grounded);
        }

        [Fact]
        public void Sprinting_IsFaster_AndDrainsStamina()
        {
            var w = new FlatWorld();
            var s = MoveState.At(new Vector3(500, 50, 100));
            for (int i = 0; i < 90; i++) MovementSim.Step(ref s, Forward(0, InputButtons.Sprint), 1f / 30f, w, MovementSettings.Default);
            Assert.True(s.Position.Z - 100f > MovementSettings.Default.WalkSpeed * 3f * 1.3f);
            Assert.True(s.Stamina < 100f);
            Assert.True(s.Sprinting);
        }

        [Fact]
        public void Jump_LeavesGround_AndLandsAgain()
        {
            var w = new FlatWorld();
            var s = MoveState.At(new Vector3(500, 50, 500));
            var jump = new PlayerInput { Buttons = InputButtons.Jump };
            MovementSim.Step(ref s, jump, 1f / 30f, w, MovementSettings.Default);
            Assert.False(s.Grounded);
            Assert.True(s.Position.Y > 50f);
            float peak = 0;
            for (int i = 0; i < 60; i++)
            {
                MovementSim.Step(ref s, new PlayerInput(), 1f / 30f, w, MovementSettings.Default);
                peak = Math.Max(peak, s.Position.Y - 50f);
            }
            Assert.True(s.Grounded);
            Assert.InRange(peak, 0.6f, 1.3f);
        }

        [Fact]
        public void DeepWater_SwitchesToSwimming_AtTheSurface()
        {
            var w = new FlatWorld { WaterStartX = 510f };
            var s = MoveState.At(new Vector3(500, 50, 500));
            for (int i = 0; i < 300; i++) MovementSim.Step(ref s, Forward(90), 1f / 30f, w, MovementSettings.Default);
            Assert.True(s.Swimming);
            Assert.InRange(s.Position.Y, w.WaterLevel - 1.6f, w.WaterLevel - 0.9f);
        }

        [Fact]
        public void Obstacle_BlocksMovement()
        {
            var w = new FlatWorld { Tree = new Vector2(500, 505) };
            var s = MoveState.At(new Vector3(500, 50, 500));
            for (int i = 0; i < 90; i++) MovementSim.Step(ref s, Forward(0), 1f / 30f, w, MovementSettings.Default);
            Assert.True(Vector2.Distance(new Vector2(s.Position.X, s.Position.Z), w.Tree.Value) >= 0.5f + MovementSettings.Default.CapsuleRadius - 0.01f);
        }

        [Fact]
        public void PredictionMatchesServer_ZeroCorrections()
        {
            var world = new WorldRuntime(new WorldConfig());
            var rng = new Rng(5);
            Assert.True(SpawnFinder.TryFind(world, ref rng, null, null, out Vector3 spawn));
            var client = new ClientSimulation(world, MovementSettings.Default, 30);
            client.Initialize(new WelcomeMsg { Spawn = MoveState.At(spawn), ServerTick = 0, Stats = SurvivalStats.Fresh });
            MoveState server = MoveState.At(spawn);
            uint tick = 0;
            for (int frame = 0; frame < 300; frame++)
            {
                var input = new PlayerInput { MoveY = 127, MoveX = (sbyte)(frame % 50 < 25 ? 40 : -40), Buttons = frame % 120 == 0 ? InputButtons.Jump : InputButtons.Sprint };
                input.YawDegrees = frame * 1.5f;
                client.Advance(1f / 30f, input);
                IReadOnlyList<PlayerInput> sent = client.PendingSend;
                if (sent.Count == 0) continue;
                PlayerInput newest = sent[0];
                MovementSim.Step(ref server, newest, 1f / 30f, world, MovementSettings.Default);
                tick++;
                if (frame % 3 == 0)
                {
                    client.ApplyServerState(new SnapshotHeader { Tick = tick, AckInputSequence = newest.Sequence, Own = server, Stats = SurvivalStats.Fresh });
                }
            }
            Assert.Equal(0, client.Corrections);
            Assert.True(Vector3.Distance(client.Predicted.Position, server.Position) < 0.01f);
        }

        [Fact]
        public void Reconciliation_CorrectsServerOverride()
        {
            var world = new WorldRuntime(new WorldConfig());
            var rng = new Rng(8);
            Assert.True(SpawnFinder.TryFind(world, ref rng, null, null, out Vector3 spawn));
            var client = new ClientSimulation(world, MovementSettings.Default, 30);
            client.Initialize(new WelcomeMsg { Spawn = MoveState.At(spawn), Stats = SurvivalStats.Fresh });
            var input = new PlayerInput { MoveY = 127 };
            for (int i = 0; i < 10; i++) client.Advance(1f / 30f, input);
            // Server says we were pushed 2 m sideways at input #5
            MoveState s = MoveState.At(spawn + new Vector3(2f, 0, 0));
            s.Position = new Vector3(s.Position.X, world.GroundHeight(s.Position.X, s.Position.Z), s.Position.Z);
            client.ApplyServerState(new SnapshotHeader { Tick = 5, AckInputSequence = 5, Own = s, Stats = SurvivalStats.Fresh });
            Assert.Equal(1, client.Corrections);
            Assert.True(Math.Abs(client.Predicted.Position.X - (spawn.X + 2f)) < 0.3f);
        }
    }

    public class ProtocolTests
    {
        [Fact]
        public void VarUInt_RoundTrips()
        {
            var w = new NetDataWriter();
            uint[] values = { 0, 1, 127, 128, 300, 16383, 16384, 1u << 21, uint.MaxValue };
            foreach (uint v in values) w.PutVarUInt(v);
            var r = new NetDataReader(w.CopyData());
            foreach (uint v in values) Assert.Equal(v, r.GetVarUInt());
        }

        [Fact]
        public void QuantizedPosition_ErrorIsSmall()
        {
            var w = new NetDataWriter();
            var p = new Vector3(4321.123f, 287.456f, 17.891f);
            w.PutQuantPos(p);
            var q = new NetDataReader(w.CopyData()).GetQuantPos();
            Assert.True(Math.Abs(q.X - p.X) < 0.003f && Math.Abs(q.Z - p.Z) < 0.003f && Math.Abs(q.Y - p.Y) < 0.01f);
        }

        [Fact]
        public void Welcome_RoundTrips()
        {
            var w = new NetDataWriter();
            var msg = new WelcomeMsg
            {
                ProtocolVersion = NetProtocol.Version,
                EntityId = 77,
                SessionToken = "abc",
                ServerName = "DEMO SERVER #1",
                ServerTick = 1234,
                TickRate = 30,
                SnapshotRate = 30,
                MaxPlayers = 100,
                World = new WorldConfig { Seed = 9, Size = 5000, SeaLevel = 30 },
                Spawn = MoveState.At(new Vector3(1, 2, 3)),
                SpawnYaw = 45,
                Stats = SurvivalStats.Fresh,
                TimeOfDay01 = 0.25f,
                DayLengthSeconds = 1800,
            };
            msg.Write(w);
            var r = new NetDataReader(w.CopyData());
            Assert.Equal((byte)MsgId.Welcome, r.GetByte());
            WelcomeMsg back = WelcomeMsg.Read(r);
            Assert.Equal(77u, back.EntityId);
            Assert.Equal(9, back.World.Seed);
            Assert.Equal(new Vector3(1, 2, 3), back.Spawn.Position);
            Assert.Equal("DEMO SERVER #1", back.ServerName);
            Assert.Equal(0, r.AvailableBytes);
        }

        [Fact]
        public void Snapshot_FitsInMtu_With60Entities()
        {
            var w = new NetDataWriter();
            new SnapshotHeader { Tick = 1, Own = MoveState.At(Vector3.One), Stats = SurvivalStats.Fresh }.Write(w);
            w.PutVarUInt(60);
            for (uint i = 0; i < 60; i++) new EntityState { Id = 100000 + i, Position = new Vector3(4000, 300, 4000) }.Write(w);
            Assert.True(w.Length < 1200, "snapshot too large: " + w.Length);
        }

        [Fact]
        public void NameSanitizer_RemovesUnsafeCharacters()
        {
            Assert.Equal("Survivor", NetCodec.SanitizeName(null));
            Assert.Equal("Ali Qahramon", NetCodec.SanitizeName("  Ali <Qahramon>  "));
            Assert.True(NetCodec.SanitizeName(new string('x', 100)).Length <= NetProtocol.MaxNameLength);
        }
    }
}
