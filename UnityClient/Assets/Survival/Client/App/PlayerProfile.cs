using System;
using System.Security.Cryptography;
using System.Text;
using Survival.Networking;
using UnityEngine;

namespace Survival.Client
{
    /// <summary>
    /// Device-bound player identity: a random player id plus a secret the server pins on first
    /// join (trust on first use). Replace with platform sign-in (Google Play Games / Game Center)
    /// before a public release - see docs/ARCHITECTURE.md "Authentication".
    /// </summary>
    public sealed class PlayerProfile
    {
        private const string KeyId = "orol.player.id";
        private const string KeySecret = "orol.player.secret";
        private const string KeyName = "orol.player.name";
        private const string KeyStats = "orol.player.stats";

        public string PlayerId { get; private set; }
        public string Secret { get; private set; }
        public string Nickname { get; private set; }
        public LocalStats Stats { get; private set; }

        [Serializable]
        public sealed class LocalStats
        {
            public int Sessions;
            public double PlaySeconds;
            public int Deaths;
            public string LastServer = string.Empty;
            public float LastX;
            public float LastZ;
            public float LastHealth = 100f;
            public float LastHunger = 80f;
            public float LastThirst = 80f;
            public long LastPlayedUnix;
        }

        public static PlayerProfile LoadOrCreate()
        {
            var p = new PlayerProfile
            {
                PlayerId = PlayerPrefs.GetString(KeyId, string.Empty),
                Secret = PlayerPrefs.GetString(KeySecret, string.Empty),
                Nickname = PlayerPrefs.GetString(KeyName, string.Empty),
            };
            if (string.IsNullOrEmpty(p.PlayerId) || p.PlayerId.Length < 8)
            {
                p.PlayerId = Guid.NewGuid().ToString("N");
                p.Secret = RandomHex(32);
                PlayerPrefs.SetString(KeyId, p.PlayerId);
                PlayerPrefs.SetString(KeySecret, p.Secret);
            }
            if (string.IsNullOrEmpty(p.Nickname))
            {
                p.Nickname = "Survivor" + UnityEngine.Random.Range(1000, 9999);
                PlayerPrefs.SetString(KeyName, p.Nickname);
            }
            try
            {
                p.Stats = JsonUtility.FromJson<LocalStats>(PlayerPrefs.GetString(KeyStats, "{}")) ?? new LocalStats();
            }
            catch
            {
                p.Stats = new LocalStats();
            }
            PlayerPrefs.Save();
            return p;
        }

        public ClientIdentity ToIdentity() => new ClientIdentity { PlayerId = PlayerId, Secret = Secret, Nickname = Nickname };

        public void SetNickname(string name)
        {
            Nickname = NetCodec.SanitizeName(name);
            PlayerPrefs.SetString(KeyName, Nickname);
            PlayerPrefs.Save();
        }

        public void SaveStats()
        {
            PlayerPrefs.SetString(KeyStats, JsonUtility.ToJson(Stats));
            PlayerPrefs.Save();
        }

        public string ShortId => PlayerId.Length > 8 ? PlayerId.Substring(0, 8).ToUpperInvariant() : PlayerId;

        private static string RandomHex(int bytes)
        {
            var data = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(data);
            var sb = new StringBuilder(bytes * 2);
            foreach (byte b in data) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
