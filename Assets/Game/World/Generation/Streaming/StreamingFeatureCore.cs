using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Streaming
{
    internal readonly struct WorldCellRectangle
    {
        public WorldCellRectangle(
            int minimumX,
            int minimumZ,
            int maximumXExclusive,
            int maximumZExclusive)
        {
            if (maximumXExclusive <= minimumX
                || maximumZExclusive <= minimumZ)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumXExclusive));
            }

            MinimumX = minimumX;
            MinimumZ = minimumZ;
            MaximumXExclusive = maximumXExclusive;
            MaximumZExclusive = maximumZExclusive;
        }

        public int MinimumX { get; }
        public int MinimumZ { get; }
        public int MaximumXExclusive { get; }
        public int MaximumZExclusive { get; }
        public int MaximumX => MaximumXExclusive - 1;
        public int MaximumZ => MaximumZExclusive - 1;

        public bool Contains(int worldX, int worldZ) => worldX >= MinimumX
            && worldX < MaximumXExclusive
            && worldZ >= MinimumZ
            && worldZ < MaximumZExclusive;

        public bool Intersects(in WorldCellRectangle other) => MinimumX
            < other.MaximumXExclusive
            && MaximumXExclusive > other.MinimumX
            && MinimumZ < other.MaximumZExclusive
            && MaximumZExclusive > other.MinimumZ;

        public WorldCellRectangle Expand(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            return new WorldCellRectangle(
                checked(MinimumX - amount),
                checked(MinimumZ - amount),
                checked(MaximumXExclusive + amount),
                checked(MaximumZExclusive + amount));
        }

        public static WorldCellRectangle FromChunk(
            in ChunkCoordinate chunk,
            int chunkCellCountXZ)
        {
            if (chunkCellCountXZ <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCellCountXZ));
            }

            var minimumX = checked(chunk.X * chunkCellCountXZ);
            var minimumZ = checked(chunk.Z * chunkCellCountXZ);
            return new WorldCellRectangle(
                minimumX,
                minimumZ,
                checked(minimumX + chunkCellCountXZ),
                checked(minimumZ + chunkCellCountXZ));
        }

        public static WorldCellRectangle Union(
            in WorldCellRectangle first,
            in WorldCellRectangle second) => new(
            Math.Min(first.MinimumX, second.MinimumX),
            Math.Min(first.MinimumZ, second.MinimumZ),
            Math.Max(first.MaximumXExclusive, second.MaximumXExclusive),
            Math.Max(first.MaximumZExclusive, second.MaximumZExclusive));
    }

    internal readonly struct PlanningTileKey :
        IEquatable<PlanningTileKey>, IComparable<PlanningTileKey>
    {
        public PlanningTileKey(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }
        public int Z { get; }

        public int CompareTo(PlanningTileKey other)
        {
            var z = Z.CompareTo(other.Z);
            return z != 0 ? z : X.CompareTo(other.X);
        }

        public bool Equals(PlanningTileKey other) => X == other.X
            && Z == other.Z;

        public override bool Equals(object obj) => obj is PlanningTileKey other
            && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Z);

        public WorldCellRectangle ToCore(int tileSizeCells)
        {
            if (tileSizeCells <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileSizeCells));
            }

            var minimumX = checked(X * tileSizeCells);
            var minimumZ = checked(Z * tileSizeCells);
            return new WorldCellRectangle(
                minimumX,
                minimumZ,
                checked(minimumX + tileSizeCells),
                checked(minimumZ + tileSizeCells));
        }

        public static PlanningTileKey FromCell(
            int worldX,
            int worldZ,
            int tileSizeCells)
        {
            if (tileSizeCells <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileSizeCells));
            }

            return new PlanningTileKey(
                WorldCoordinateUtility.FloorDivide(worldX, tileSizeCells),
                WorldCoordinateUtility.FloorDivide(worldZ, tileSizeCells));
        }
    }

    internal readonly struct StreamingCellKey :
        IEquatable<StreamingCellKey>, IComparable<StreamingCellKey>
    {
        public StreamingCellKey(int worldX, int worldZ)
        {
            WorldX = worldX;
            WorldZ = worldZ;
        }

        public int WorldX { get; }
        public int WorldZ { get; }

        public int CompareTo(StreamingCellKey other)
        {
            var z = WorldZ.CompareTo(other.WorldZ);
            return z != 0 ? z : WorldX.CompareTo(other.WorldX);
        }

        public bool Equals(StreamingCellKey other) => WorldX == other.WorldX
            && WorldZ == other.WorldZ;

        public override bool Equals(object obj) => obj is StreamingCellKey other
            && Equals(other);

        public override int GetHashCode() => HashCode.Combine(WorldX, WorldZ);
    }

    internal readonly struct StreamingBaseTerrainFact
    {
        public StreamingBaseTerrainFact(
            in WorldFieldSample field,
            in WorldPatternResult terrain,
            in TerrainSurfaceSample surface)
        {
            Field = field;
            Terrain = terrain;
            Surface = surface;
        }

        public WorldFieldSample Field { get; }
        public WorldPatternResult Terrain { get; }
        public TerrainSurfaceSample Surface { get; }
        public bool HasSeaWater => Surface.HasSeaWater;
        public int SeaWaterTopUnits => Surface.WaterTopUnits;
    }

    internal sealed class StreamingBaseTerrainEvaluator
    {
        private readonly WorldSettingsData settings;
        private readonly WorldNoiseRouter router;
        private readonly WorldDensityField density;

        public StreamingBaseTerrainEvaluator(WorldSettingsData settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(
                nameof(settings));
            router = new WorldNoiseRouter(settings);
            density = new WorldDensityField(settings);
        }

        public StreamingBaseTerrainFact Sample(int worldX, int worldZ)
        {
            var field = router.Sample(worldX, worldZ);
            var terrain = WorldPatternResolver.Resolve(
                router,
                worldX,
                worldZ,
                field,
                settings,
                out _);
            var surface = TerrainSurfaceSampler.SampleResolved(
                density,
                settings,
                worldX,
                worldZ,
                field,
                terrain);
            return new StreamingBaseTerrainFact(field, terrain, surface);
        }
    }
}
