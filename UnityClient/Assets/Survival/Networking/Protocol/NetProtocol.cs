namespace Survival.Networking
{
    /// <summary>
    /// Wire protocol constants shared by the Unity client and the .NET dedicated server.
    /// Bump <see cref="Version"/> on any incompatible change; mismatched clients are rejected with
    /// a clear "update the game" message.
    /// </summary>
    public static class NetProtocol
    {
        public const int Version = 1;
        public const string AppKey = "OROL-SURVIVAL";
        public const int DefaultPort = 7777;
        public const int DefaultStatusPort = 8080;
        public const int QueryMagic = 0x4C4F524F; // "OROL" little endian

        public const byte ChannelReliable = 0;
        public const byte ChannelUnreliable = 1;
        public const byte ChannelCount = 2;

        /// <summary>Simulation tick rate used for client prediction and server movement.</summary>
        public const int DefaultTickRate = 30;
        public const int MaxNameLength = 20;

        /// <summary>Keep unreliable packets below the minimum safe MTU.</summary>
        public const int MaxUnreliablePayload = 1100;

        /// <summary>Number of past inputs repeated in every input packet (loss tolerance).</summary>
        public const int InputRedundancy = 4;
    }

    public enum MsgId : byte
    {
        // client -> server
        ClientInput = 1,
        ClientReady = 2,
        ClientRespawn = 3,
        ClientChat = 4,

        // server -> client
        Welcome = 64,
        Snapshot = 65,
        EntitySpawn = 66,
        EntityDespawn = 67,
        Notice = 68,
        Teleport = 69,
        Chat = 70,
        Died = 71,
    }

    public enum QueryKind : byte
    {
        InfoRequest = 1,
        InfoResponse = 2,
    }

    public enum RejectReason : byte
    {
        Unknown = 0,
        ServerFull = 1,
        VersionMismatch = 2,
        BadRequest = 3,
        AuthFailed = 4,
        Banned = 5,
        ServerStarting = 6,
        AlreadyConnected = 7,
    }

    public enum DespawnReason : byte
    {
        OutOfRange = 0,
        Removed = 1,
        Disconnected = 2,
        Died = 3,
    }

    public enum EntityType : byte
    {
        None = 0,
        Player = 1,
        Bot = 2,
        Animal = 3,
        ItemDrop = 4,
        Container = 5,
        Projectile = 6,
        Airdrop = 7,
    }

    public enum NoticeKind : byte
    {
        Info = 0,
        Warning = 1,
        Event = 2,
        Kill = 3,
        Admin = 4,
    }

    [System.Flags]
    public enum ServerStatusFlags : byte
    {
        None = 0,
        Open = 1,
        Full = 2,
        Whitelist = 4,
        Password = 8,
        Offline = 16,
        LocalHost = 32,
    }
}
