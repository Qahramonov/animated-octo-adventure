using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Survival.Core;
using Survival.Gameplay;

namespace Survival.Server.Persistence
{
    /// <summary>Everything the server keeps about a player between sessions.</summary>
    public sealed class PlayerRecord
    {
        public const int FormatVersion = 1;

        public string PlayerId;
        public string SecretHash;
        public string Nickname;
        public bool HasPosition;
        public Vector3 Position;
        public float Yaw;
        public SurvivalStats Stats = SurvivalStats.Fresh;
        public bool Alive = true;
        public long CreatedUnix;
        public long LastSeenUnix;
        public double PlayTimeSeconds;
        public int Deaths;
        public int Kills;

        // Phase 2: inventory slots are appended to this record (format version 2).

        public void Write(BinaryWriter w)
        {
            w.Write(FormatVersion);
            w.Write(PlayerId ?? string.Empty);
            w.Write(SecretHash ?? string.Empty);
            w.Write(Nickname ?? string.Empty);
            w.Write(HasPosition);
            w.Write(Position.X);
            w.Write(Position.Y);
            w.Write(Position.Z);
            w.Write(Yaw);
            w.Write(Stats.Health);
            w.Write(Stats.Hunger);
            w.Write(Stats.Thirst);
            w.Write(Stats.Bleeding);
            w.Write(Alive);
            w.Write(CreatedUnix);
            w.Write(LastSeenUnix);
            w.Write(PlayTimeSeconds);
            w.Write(Deaths);
            w.Write(Kills);
        }

        public static PlayerRecord Read(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version < 1 || version > FormatVersion) throw new InvalidDataException("Unsupported player record version " + version);
            var p = new PlayerRecord
            {
                PlayerId = r.ReadString(),
                SecretHash = r.ReadString(),
                Nickname = r.ReadString(),
                HasPosition = r.ReadBoolean(),
            };
            p.Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            p.Yaw = r.ReadSingle();
            p.Stats = new SurvivalStats
            {
                Health = r.ReadSingle(),
                Hunger = r.ReadSingle(),
                Thirst = r.ReadSingle(),
                Bleeding = r.ReadSingle(),
            };
            p.Alive = r.ReadBoolean();
            p.CreatedUnix = r.ReadInt64();
            p.LastSeenUnix = r.ReadInt64();
            p.PlayTimeSeconds = r.ReadDouble();
            p.Deaths = r.ReadInt32();
            p.Kills = r.ReadInt32();
            return p;
        }
    }

    public interface IPlayerStore
    {
        PlayerRecord Load(string playerId);
        void Save(PlayerRecord record);
        int Count { get; }
    }

    /// <summary>In-memory store (tests, temporary servers).</summary>
    public sealed class MemoryPlayerStore : IPlayerStore
    {
        private readonly Dictionary<string, byte[]> _data = new Dictionary<string, byte[]>();

        public int Count => _data.Count;

        public PlayerRecord Load(string playerId)
        {
            if (!_data.TryGetValue(playerId, out byte[] bytes)) return null;
            using (var ms = new MemoryStream(bytes))
            using (var r = new BinaryReader(ms))
            {
                return PlayerRecord.Read(r);
            }
        }

        public void Save(PlayerRecord record)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms, Encoding.UTF8, true)) record.Write(w);
                _data[record.PlayerId] = ms.ToArray();
            }
        }
    }

    /// <summary>
    /// One binary file per player, written atomically (temp file + rename) so a crash during save
    /// never corrupts a profile. Works on Linux servers and on Android (offline host).
    /// </summary>
    public sealed class FilePlayerStore : IPlayerStore
    {
        private readonly string _dir;

        public FilePlayerStore(string dataDirectory)
        {
            _dir = Path.Combine(dataDirectory, "players");
            Directory.CreateDirectory(_dir);
        }

        public int Count => Directory.GetFiles(_dir, "*.bin").Length;

        private string PathFor(string playerId) => Path.Combine(_dir, SafeFileName(playerId) + ".bin");

        public PlayerRecord Load(string playerId)
        {
            string path = PathFor(playerId);
            if (!File.Exists(path)) return null;
            try
            {
                using (var fs = File.OpenRead(path))
                using (var r = new BinaryReader(fs))
                {
                    return PlayerRecord.Read(r);
                }
            }
            catch (Exception e)
            {
                Log.Error("Store", $"Corrupt player file {path}: {e.Message}");
                return null;
            }
        }

        public void Save(PlayerRecord record)
        {
            string path = PathFor(record.PlayerId);
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var w = new BinaryWriter(fs))
            {
                record.Write(w);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static string SafeFileName(string id)
        {
            var sb = new StringBuilder(id.Length);
            foreach (char c in id)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_') sb.Append(c);
            }
            return sb.ToString();
        }
    }

    public static class Secrets
    {
        public static string Hash(string secret)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("orol:" + (secret ?? string.Empty)));
                var sb = new StringBuilder(64);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static bool IsValidPlayerId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 8 || id.Length > 64) return false;
            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        public static string NewToken()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder(32);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
