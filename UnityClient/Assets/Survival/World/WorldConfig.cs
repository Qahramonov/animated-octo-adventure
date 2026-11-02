namespace Survival.World
{
    /// <summary>
    /// Parameters that fully define the island. The server sends these to clients in the Welcome
    /// message; every client regenerates the identical terrain and vegetation locally, so terrain is
    /// never streamed over the network (only changes, e.g. a felled tree, are replicated).
    /// </summary>
    public sealed class WorldConfig
    {
        /// <summary>Bump when the generator changes in a way that alters terrain/node layout.</summary>
        public const int CurrentGeneratorVersion = 1;

        /// <summary>Distance between height samples, metres. Fixed: movement code relies on it.</summary>
        public const float CellSize = 2f;

        public int Seed = 20261102;

        /// <summary>World edge length in metres (5000 = 5 x 5 km = 25 km2).</summary>
        public float Size = 5000f;

        /// <summary>Height of the ocean surface, metres.</summary>
        public float SeaLevel = 30f;

        public int GeneratorVersion = CurrentGeneratorVersion;

        /// <summary>Multiplier for tree/bush/rock density (server config "resourceDensity").</summary>
        public float VegetationDensity = 1f;

        /// <summary>Multiplier for stone/metal/sulfur node density.</summary>
        public float ResourceDensity = 1f;

        public WorldConfig Clone() => (WorldConfig)MemberwiseClone();

        public override string ToString() => $"seed={Seed} size={Size}m sea={SeaLevel}m gen=v{GeneratorVersion}";
    }
}
