using System;
using System.Collections.Generic;
using System.Numerics;
using LiteNetLib.Utils;
using Survival.Gameplay;
using Survival.World;

namespace Survival.Networking
{
    /// <summary>Payload of LiteNetLib's connection request (sent before a connection exists).</summary>
    public struct ConnectRequestData
    {
        public int ProtocolVersion;
        public string PlayerId;
        public string PlayerSecret;
        public string Nickname;
        /// <summary>Token from a previous Welcome; lets the server re-attach a disconnected character.</summary>
        public string SessionToken;
        public string ClientVersion;
        public byte Platform;

        public void Write(NetDataWriter w)
        {
            w.Put(NetProtocol.QueryMagic);
            w.Put(NetProtocol.AppKey);
            w.Put(ProtocolVersion);
            w.Put(PlayerId ?? string.Empty, 64);
            w.Put(PlayerSecret ?? string.Empty, 128);
            w.Put(Nickname ?? string.Empty, NetProtocol.MaxNameLength * 2);
            w.Put(SessionToken ?? string.Empty, 64);
            w.Put(ClientVersion ?? string.Empty, 32);
            w.Put(Platform);
        }

        public static bool TryRead(NetDataReader r, out ConnectRequestData d, out string error)
        {
            d = default;
            error = null;
            try
            {
                if (r.GetInt() != NetProtocol.QueryMagic) { error = "bad magic"; return false; }
                if (r.GetString(64) != NetProtocol.AppKey) { error = "bad app key"; return false; }
                d.ProtocolVersion = r.GetInt();
                d.PlayerId = r.GetString(64);
                d.PlayerSecret = r.GetString(128);
                d.Nickname = r.GetString(NetProtocol.MaxNameLength * 2);
                d.SessionToken = r.GetString(64);
                d.ClientVersion = r.GetString(32);
                d.Platform = r.GetByte();
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }

    public struct RejectData
    {
        public RejectReason Reason;
        public string Message;

        public byte[] ToBytes()
        {
            var w = new NetDataWriter();
            w.Put((byte)Reason);
            w.Put(Message ?? string.Empty, 200);
            return w.CopyData();
        }

        public static RejectData Read(NetDataReader r)
        {
            var d = new RejectData { Reason = RejectReason.Unknown, Message = string.Empty };
            try
            {
                if (r != null && r.AvailableBytes > 0)
                {
                    d.Reason = (RejectReason)r.GetByte();
                    d.Message = r.GetString(200);
                }
            }
            catch
            {
                // malformed reject payload: keep Unknown
            }
            return d;
        }
    }

    /// <summary>Public server info returned to the server browser over an unconnected UDP query.</summary>
    public struct ServerInfo
    {
        public int ProtocolVersion;
        public string Name;
        public string Region;
        public string MapName;
        public string GameVersion;
        public ushort Players;
        public ushort Bots;
        public ushort MaxPlayers;
        public ServerStatusFlags Flags;
        public int UptimeSeconds;
        public float WorldSize;

        public static void WriteRequest(NetDataWriter w, long timestamp)
        {
            w.Put(NetProtocol.QueryMagic);
            w.Put((byte)QueryKind.InfoRequest);
            w.Put(NetProtocol.Version);
            w.Put(timestamp);
        }

        public static bool TryReadRequest(NetDataReader r, out long timestamp)
        {
            timestamp = 0;
            if (r.AvailableBytes < 17) return false;
            if (r.GetInt() != NetProtocol.QueryMagic) return false;
            if (r.GetByte() != (byte)QueryKind.InfoRequest) return false;
            r.GetInt(); // client protocol version (informational)
            timestamp = r.GetLong();
            return true;
        }

        public void WriteResponse(NetDataWriter w, long echoTimestamp)
        {
            w.Put(NetProtocol.QueryMagic);
            w.Put((byte)QueryKind.InfoResponse);
            w.Put(echoTimestamp);
            w.Put(ProtocolVersion);
            w.Put(Name ?? string.Empty, 48);
            w.Put(Region ?? string.Empty, 24);
            w.Put(MapName ?? string.Empty, 32);
            w.Put(GameVersion ?? string.Empty, 24);
            w.Put(Players);
            w.Put(Bots);
            w.Put(MaxPlayers);
            w.Put((byte)Flags);
            w.Put(UptimeSeconds);
            w.Put(WorldSize);
        }

        public static bool TryReadResponse(NetDataReader r, out ServerInfo info, out long echoTimestamp)
        {
            info = default;
            echoTimestamp = 0;
            try
            {
                if (r.GetInt() != NetProtocol.QueryMagic) return false;
                if (r.GetByte() != (byte)QueryKind.InfoResponse) return false;
                echoTimestamp = r.GetLong();
                info.ProtocolVersion = r.GetInt();
                info.Name = r.GetString(48);
                info.Region = r.GetString(24);
                info.MapName = r.GetString(32);
                info.GameVersion = r.GetString(24);
                info.Players = r.GetUShort();
                info.Bots = r.GetUShort();
                info.MaxPlayers = r.GetUShort();
                info.Flags = (ServerStatusFlags)r.GetByte();
                info.UptimeSeconds = r.GetInt();
                info.WorldSize = r.GetFloat();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public struct WelcomeMsg
    {
        public int ProtocolVersion;
        public uint EntityId;
        public string SessionToken;
        public string ServerName;
        public uint ServerTick;
        public byte TickRate;
        public byte SnapshotRate;
        public ushort MaxPlayers;
        public bool Resumed;
        public WorldConfig World;
        public MoveState Spawn;
        public float SpawnYaw;
        public SurvivalStats Stats;
        public float TimeOfDay01;
        public float DayLengthSeconds;
        /// <summary><see cref="Survival.World.WorldFingerprint"/> of the server's island.</summary>
        public uint WorldFingerprint;

        public void Write(NetDataWriter w)
        {
            w.Put((byte)MsgId.Welcome);
            w.Put(ProtocolVersion);
            w.Put(EntityId);
            w.Put(SessionToken ?? string.Empty, 64);
            w.Put(ServerName ?? string.Empty, 48);
            w.Put(ServerTick);
            w.Put(TickRate);
            w.Put(SnapshotRate);
            w.Put(MaxPlayers);
            w.Put(Resumed);
            w.Put(World.Seed);
            w.Put(World.Size);
            w.Put(World.SeaLevel);
            w.Put(World.GeneratorVersion);
            w.Put(World.VegetationDensity);
            w.Put(World.ResourceDensity);
            w.PutVector3(Spawn.Position);
            w.PutVector3(Spawn.Velocity);
            w.Put((byte)Spawn.Flags);
            w.Put(Spawn.Stamina);
            w.Put(SpawnYaw);
            w.Put(Stats.Health);
            w.Put(Stats.Hunger);
            w.Put(Stats.Thirst);
            w.Put(Stats.Bleeding);
            w.Put(TimeOfDay01);
            w.Put(DayLengthSeconds);
            w.Put(WorldFingerprint);
        }

        public static WelcomeMsg Read(NetDataReader r)
        {
            var m = new WelcomeMsg
            {
                ProtocolVersion = r.GetInt(),
                EntityId = r.GetUInt(),
                SessionToken = r.GetString(64),
                ServerName = r.GetString(48),
                ServerTick = r.GetUInt(),
                TickRate = r.GetByte(),
                SnapshotRate = r.GetByte(),
                MaxPlayers = r.GetUShort(),
                Resumed = r.GetBool(),
                World = new WorldConfig
                {
                    Seed = r.GetInt(),
                    Size = r.GetFloat(),
                    SeaLevel = r.GetFloat(),
                    GeneratorVersion = r.GetInt(),
                    VegetationDensity = r.GetFloat(),
                    ResourceDensity = r.GetFloat(),
                },
            };
            m.Spawn.Position = r.GetVector3();
            m.Spawn.Velocity = r.GetVector3();
            m.Spawn.Flags = (MoveFlags)r.GetByte();
            m.Spawn.Stamina = r.GetFloat();
            m.SpawnYaw = r.GetFloat();
            m.Stats.Health = r.GetFloat();
            m.Stats.Hunger = r.GetFloat();
            m.Stats.Thirst = r.GetFloat();
            m.Stats.Bleeding = r.GetFloat();
            m.TimeOfDay01 = r.GetFloat();
            m.DayLengthSeconds = r.GetFloat();
            m.WorldFingerprint = r.GetUInt();
            return m;
        }
    }

    public struct EntitySpawnMsg
    {
        public uint Id;
        public EntityType Type;
        public byte SubType;
        public string Name;
        public Vector3 Position;
        public float Yaw;
        public MoveFlags Flags;
        public byte Health;

        public void Write(NetDataWriter w)
        {
            w.Put((byte)MsgId.EntitySpawn);
            w.PutVarUInt(Id);
            w.Put((byte)Type);
            w.Put(SubType);
            w.Put(Name ?? string.Empty, NetProtocol.MaxNameLength * 2);
            w.PutQuantPos(Position);
            w.Put(NetCodec.EncodeYaw(Yaw));
            w.Put((byte)Flags);
            w.Put(Health);
        }

        public static EntitySpawnMsg Read(NetDataReader r) => new EntitySpawnMsg
        {
            Id = r.GetVarUInt(),
            Type = (EntityType)r.GetByte(),
            SubType = r.GetByte(),
            Name = r.GetString(NetProtocol.MaxNameLength * 2),
            Position = r.GetQuantPos(),
            Yaw = NetCodec.DecodeYaw(r.GetUShort()),
            Flags = (MoveFlags)r.GetByte(),
            Health = r.GetByte(),
        };
    }

    public struct EntityDespawnMsg
    {
        public uint Id;
        public DespawnReason Reason;

        public void Write(NetDataWriter w)
        {
            w.Put((byte)MsgId.EntityDespawn);
            w.PutVarUInt(Id);
            w.Put((byte)Reason);
        }

        public static EntityDespawnMsg Read(NetDataReader r) => new EntityDespawnMsg
        {
            Id = r.GetVarUInt(),
            Reason = (DespawnReason)r.GetByte(),
        };
    }

    /// <summary>State of one replicated entity inside a snapshot.</summary>
    public struct EntityState
    {
        public uint Id;
        public Vector3 Position;
        public float Yaw;
        public MoveFlags Flags;
        public byte Health;

        public const int MaxEncodedSize = 5 + 8 + 2 + 1 + 1;

        public void Write(NetDataWriter w)
        {
            w.PutVarUInt(Id);
            w.PutQuantPos(Position);
            w.Put(NetCodec.EncodeYaw(Yaw));
            w.Put((byte)Flags);
            w.Put(Health);
        }

        public static EntityState Read(NetDataReader r) => new EntityState
        {
            Id = r.GetVarUInt(),
            Position = r.GetQuantPos(),
            Yaw = NetCodec.DecodeYaw(r.GetUShort()),
            Flags = (MoveFlags)r.GetByte(),
            Health = r.GetByte(),
        };
    }

    /// <summary>
    /// Per-client snapshot header: authoritative own state (for reconciliation) + vitals + world
    /// clock. Followed by a count and that many <see cref="EntityState"/> entries. Large snapshots
    /// are split into several packets, each with this header.
    /// </summary>
    public struct SnapshotHeader
    {
        public uint Tick;
        public uint AckInputSequence;
        public float TimeOfDay01;
        public byte Weather;
        public MoveState Own;
        public SurvivalStats Stats;

        public void Write(NetDataWriter w)
        {
            w.Put((byte)MsgId.Snapshot);
            w.Put(Tick);
            w.Put(AckInputSequence);
            w.Put((ushort)(Math.Max(0f, Math.Min(0.99999f, TimeOfDay01)) * 65536f));
            w.Put(Weather);
            w.PutVector3(Own.Position);
            w.PutVector3(Own.Velocity);
            w.Put((byte)Own.Flags);
            w.Put(Own.Stamina);
            w.Put(NetCodec.EncodePercent(Stats.Health));
            w.Put(NetCodec.EncodePercent(Stats.Hunger));
            w.Put(NetCodec.EncodePercent(Stats.Thirst));
            w.Put((byte)Math.Min(255, (int)(Stats.Bleeding * 20f)));
        }

        public static SnapshotHeader Read(NetDataReader r)
        {
            var h = new SnapshotHeader
            {
                Tick = r.GetUInt(),
                AckInputSequence = r.GetUInt(),
                TimeOfDay01 = r.GetUShort() / 65536f,
                Weather = r.GetByte(),
            };
            h.Own.Position = r.GetVector3();
            h.Own.Velocity = r.GetVector3();
            h.Own.Flags = (MoveFlags)r.GetByte();
            h.Own.Stamina = r.GetFloat();
            h.Stats.Health = NetCodec.DecodePercent(r.GetByte());
            h.Stats.Hunger = NetCodec.DecodePercent(r.GetByte());
            h.Stats.Thirst = NetCodec.DecodePercent(r.GetByte());
            h.Stats.Bleeding = r.GetByte() / 20f;
            return h;
        }
    }

    public static class InputPacket
    {
        /// <summary>Writes the newest inputs (newest first) with redundancy for loss tolerance.</summary>
        public static void Write(NetDataWriter w, IReadOnlyList<PlayerInput> newestFirst, int count)
        {
            count = Math.Min(count, Math.Min(newestFirst.Count, 16));
            w.Put((byte)MsgId.ClientInput);
            w.Put((byte)count);
            for (int i = 0; i < count; i++)
            {
                PlayerInput p = newestFirst[i];
                w.Put(p.Sequence);
                w.Put(p.MoveX);
                w.Put(p.MoveY);
                w.Put(p.Yaw);
                w.Put(p.Pitch);
                w.Put((ushort)p.Buttons);
                w.Put(p.HotbarSlot);
            }
        }

        /// <summary>Reads inputs; returns them newest first.</summary>
        public static int Read(NetDataReader r, PlayerInput[] buffer)
        {
            int count = Math.Min(r.GetByte(), buffer.Length);
            for (int i = 0; i < count; i++)
            {
                buffer[i] = new PlayerInput
                {
                    Sequence = r.GetUInt(),
                    MoveX = r.GetSByte(),
                    MoveY = r.GetSByte(),
                    Yaw = r.GetUShort(),
                    Pitch = r.GetShort(),
                    Buttons = (InputButtons)r.GetUShort(),
                    HotbarSlot = r.GetByte(),
                };
            }
            return count;
        }
    }

    public struct NoticeMsg
    {
        public NoticeKind Kind;
        public string Text;
        public float Duration;

        public void Write(NetDataWriter w)
        {
            w.Put((byte)MsgId.Notice);
            w.Put((byte)Kind);
            w.Put(Text ?? string.Empty, 200);
            w.Put(Duration);
        }

        public static NoticeMsg Read(NetDataReader r) => new NoticeMsg
        {
            Kind = (NoticeKind)r.GetByte(),
            Text = r.GetString(200),
            Duration = r.GetFloat(),
        };
    }

    public struct TeleportMsg
    {
        public MoveState State;
        public float Yaw;
        public SurvivalStats Stats;

        public void Write(NetDataWriter w)
        {
            w.Put((byte)MsgId.Teleport);
            w.PutVector3(State.Position);
            w.PutVector3(State.Velocity);
            w.Put((byte)State.Flags);
            w.Put(State.Stamina);
            w.Put(Yaw);
            w.Put(Stats.Health);
            w.Put(Stats.Hunger);
            w.Put(Stats.Thirst);
            w.Put(Stats.Bleeding);
        }

        public static TeleportMsg Read(NetDataReader r)
        {
            var m = new TeleportMsg();
            m.State.Position = r.GetVector3();
            m.State.Velocity = r.GetVector3();
            m.State.Flags = (MoveFlags)r.GetByte();
            m.State.Stamina = r.GetFloat();
            m.Yaw = r.GetFloat();
            m.Stats.Health = r.GetFloat();
            m.Stats.Hunger = r.GetFloat();
            m.Stats.Thirst = r.GetFloat();
            m.Stats.Bleeding = r.GetFloat();
            return m;
        }
    }

    public struct DiedMsg
    {
        public DamageType Cause;
        public string KillerName;
        public float RespawnDelay;

        public void Write(NetDataWriter w)
        {
            w.Put((byte)MsgId.Died);
            w.Put((byte)Cause);
            w.Put(KillerName ?? string.Empty, 48);
            w.Put(RespawnDelay);
        }

        public static DiedMsg Read(NetDataReader r) => new DiedMsg
        {
            Cause = (DamageType)r.GetByte(),
            KillerName = r.GetString(48),
            RespawnDelay = r.GetFloat(),
        };
    }

    public struct ChatMsg
    {
        public string From;
        public string Text;

        public void Write(NetDataWriter w, MsgId id)
        {
            w.Put((byte)id);
            w.Put(From ?? string.Empty, 48);
            w.Put(Text ?? string.Empty, 160);
        }

        public static ChatMsg Read(NetDataReader r) => new ChatMsg
        {
            From = r.GetString(48),
            Text = r.GetString(160),
        };
    }
}
