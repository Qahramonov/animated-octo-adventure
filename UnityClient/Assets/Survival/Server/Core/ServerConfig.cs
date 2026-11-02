using System;
using Survival.World;

namespace Survival.Server
{
    /// <summary>
    /// Dedicated server configuration. Loaded from server.json / environment variables / command line
    /// by the .NET host (see Server/README.md); the Unity offline/LAN host fills it in code.
    /// </summary>
    public sealed class ServerConfig
    {
        // ---- identity / listing ----
        public string ServerName = "DEMO SERVER #1";
        public string MapName = "Demo Island";
        public string Region = "Asia";

        // ---- network ----
        public string BindAddress = "0.0.0.0";
        public int Port = 7777;
        /// <summary>Total active participants (real players + bots) are capped by this value.</summary>
        public int MaxPlayers = 100;
        public int TickRate = 30;
        /// <summary>Send snapshots every N ticks (1 = every tick).</summary>
        public int SnapshotInterval = 1;
        /// <summary>Network relevancy radius in metres (interest management).</summary>
        public float InterestRadius = 320f;
        public int MaxEntitiesPerSnapshot = 80;
        public int DisconnectTimeoutMs = 10000;
        /// <summary>Answer LAN discovery broadcasts.</summary>
        public bool AnswerBroadcast = true;

        // ---- world ----
        public int WorldSeed = 20261102;
        public float WorldSize = 5000f;
        public float SeaLevel = 30f;
        public float ResourceDensity = 1f;
        public float VegetationDensity = 1f;
        public float AnimalDensity = 1f;
        public float EventFrequency = 1f;
        public float DayLengthMinutes = 48f;
        public float StartTimeOfDay01 = 0.32f;

        // ---- bots ----
        /// <summary>Desired number of AI bots while the server is empty (demo: 60-90).</summary>
        public int BotCount = 70;
        /// <summary>Bots further than this from every player run a cheap low-frequency simulation.</summary>
        public float BotFullSimRadius = 400f;
        /// <summary>Tick divider for distant bots (6 = 5 Hz at 30 Hz tick rate).</summary>
        public int DistantBotTickDivider = 6;
        /// <summary>Upper bound of bot brain updates per tick (CPU budget).</summary>
        public int MaxBotThinksPerTick = 12;

        // ---- gameplay ----
        public float RespawnDelaySeconds = 6f;
        public float ReconnectGraceSeconds = 90f;

        /// <summary>Terrain generated at startup around the island centre (0 = lazy only). Keep small on phones.</summary>
        public float PrewarmRadius = 1200f;

        // ---- persistence / admin ----
        public string DataDirectory = "data";
        public float AutosaveSeconds = 60f;
        /// <summary>Token for HTTP admin endpoints. Empty = admin endpoints disabled.</summary>
        public string AdminToken = string.Empty;
        public int StatusHttpPort = 8080;

        public WorldConfig ToWorldConfig() => new WorldConfig
        {
            Seed = WorldSeed,
            Size = WorldSize,
            SeaLevel = SeaLevel,
            ResourceDensity = ResourceDensity,
            VegetationDensity = VegetationDensity,
        };

        /// <summary>Clamps values to safe ranges and returns a list of warnings.</summary>
        public string Validate()
        {
            var warn = new System.Text.StringBuilder();
            if (MaxPlayers < 1 || MaxPlayers > 200) { warn.AppendLine($"maxPlayers {MaxPlayers} clamped to 1..200"); MaxPlayers = Math.Max(1, Math.Min(200, MaxPlayers)); }
            if (BotCount < 0 || BotCount > MaxPlayers) { warn.AppendLine($"botCount {BotCount} clamped to 0..{MaxPlayers}"); BotCount = Math.Max(0, Math.Min(MaxPlayers, BotCount)); }
            if (TickRate < 10 || TickRate > 60) { warn.AppendLine($"tickRate {TickRate} clamped to 10..60"); TickRate = Math.Max(10, Math.Min(60, TickRate)); }
            if (SnapshotInterval < 1 || SnapshotInterval > 6) { SnapshotInterval = Math.Max(1, Math.Min(6, SnapshotInterval)); }
            if (WorldSize < 1000f || WorldSize > 8000f) { warn.AppendLine($"worldSize {WorldSize} clamped to 1000..8000"); WorldSize = Math.Max(1000f, Math.Min(8000f, WorldSize)); }
            if (InterestRadius < 100f) InterestRadius = 100f;
            if (MaxEntitiesPerSnapshot < 16) MaxEntitiesPerSnapshot = 16;
            if (DayLengthMinutes < 2f) DayLengthMinutes = 2f;
            if (DistantBotTickDivider < 1) DistantBotTickDivider = 1;
            if (Port < 0 || Port > 65535) { warn.AppendLine("invalid port, using 7777"); Port = 7777; } // 0 = any free port (tests)
            return warn.ToString();
        }
    }
}
