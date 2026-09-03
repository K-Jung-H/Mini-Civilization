using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    public enum TerrainPatternType : byte
    {
        Smooth,
        Rugged,
        Mountain,
        Canyon
    }

    public enum HydrologyFeatureKind : byte
    {
        Sea,
        Lake,
        Pond,
        River
    }

    public readonly struct PatternTileKey :
        IEquatable<PatternTileKey>,
        IComparable<PatternTileKey>
    {
        public PatternTileKey(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }
        public int Z { get; }

        public int CompareTo(PatternTileKey other)
        {
            var z = Z.CompareTo(other.Z);
            return z != 0 ? z : X.CompareTo(other.X);
        }

        public bool Equals(PatternTileKey other) =>
            X == other.X && Z == other.Z;

        public override bool Equals(object obj) =>
            obj is PatternTileKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Z);
        public override string ToString() => $"({X}, {Z})";
    }

    public readonly struct PatternTileBounds : IEquatable<PatternTileBounds>
    {
        public PatternTileBounds(
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
        public int Width => checked(MaximumXExclusive - MinimumX);
        public int Height => checked(MaximumZExclusive - MinimumZ);

        public bool Contains(int x, int z) =>
            x >= MinimumX && x < MaximumXExclusive
            && z >= MinimumZ && z < MaximumZExclusive;

        public bool Intersects(PatternTileBounds other) =>
            MinimumX < other.MaximumXExclusive
            && MaximumXExclusive > other.MinimumX
            && MinimumZ < other.MaximumZExclusive
            && MaximumZExclusive > other.MinimumZ;

        public PatternTileBounds Expand(int cells)
        {
            if (cells < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cells));
            }

            return new PatternTileBounds(
                checked(MinimumX - cells),
                checked(MinimumZ - cells),
                checked(MaximumXExclusive + cells),
                checked(MaximumZExclusive + cells));
        }

        public bool Equals(PatternTileBounds other) =>
            MinimumX == other.MinimumX
            && MinimumZ == other.MinimumZ
            && MaximumXExclusive == other.MaximumXExclusive
            && MaximumZExclusive == other.MaximumZExclusive;

        public override bool Equals(object obj) =>
            obj is PatternTileBounds other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            MinimumX,
            MinimumZ,
            MaximumXExclusive,
            MaximumZExclusive);
    }

    public readonly struct WaterFeatureIdentity :
        IEquatable<WaterFeatureIdentity>,
        IComparable<WaterFeatureIdentity>
    {
        public WaterFeatureIdentity(
            HydrologyFeatureKind kind,
            int ownerX,
            int ownerZ,
            int worldSeed,
            uint seedSalt)
        {
            if (!Enum.IsDefined(typeof(HydrologyFeatureKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
            OwnerX = ownerX;
            OwnerZ = ownerZ;
            WorldSeed = worldSeed;
            SeedSalt = seedSalt;
        }

        public HydrologyFeatureKind Kind { get; }
        public int OwnerX { get; }
        public int OwnerZ { get; }
        public int WorldSeed { get; }
        public uint SeedSalt { get; }

        public int CompareTo(WaterFeatureIdentity other)
        {
            var kind = Kind.CompareTo(other.Kind);
            if (kind != 0)
            {
                return kind;
            }

            var z = OwnerZ.CompareTo(other.OwnerZ);
            if (z != 0)
            {
                return z;
            }

            var x = OwnerX.CompareTo(other.OwnerX);
            if (x != 0)
            {
                return x;
            }

            var seed = WorldSeed.CompareTo(other.WorldSeed);
            if (seed != 0)
            {
                return seed;
            }

            var salt = SeedSalt.CompareTo(other.SeedSalt);
            return salt;
        }

        public bool Equals(WaterFeatureIdentity other) =>
            Kind == other.Kind
            && OwnerX == other.OwnerX
            && OwnerZ == other.OwnerZ
            && WorldSeed == other.WorldSeed
            && SeedSalt == other.SeedSalt;

        public override bool Equals(object obj) =>
            obj is WaterFeatureIdentity other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            Kind,
            OwnerX,
            OwnerZ,
            WorldSeed,
            SeedSalt);
    }

    public readonly struct HydrologyFeatureKey :
        IEquatable<HydrologyFeatureKey>,
        IComparable<HydrologyFeatureKey>
    {
        private readonly WaterFeatureIdentity identity;

        private HydrologyFeatureKey(WaterFeatureIdentity identity)
        {
            this.identity = identity;
            Kind = identity.Kind;
        }

        public HydrologyFeatureKind Kind { get; }
        public WaterFeatureIdentity Identity => identity;

        public static HydrologyFeatureKey FromIdentity(
            WaterFeatureIdentity identity) => new(identity);

        public int CompareTo(HydrologyFeatureKey other)
        {
            var kind = Kind.CompareTo(other.Kind);
            return kind != 0 ? kind : identity.CompareTo(other.identity);
        }

        public bool Equals(HydrologyFeatureKey other) =>
            Kind == other.Kind
            && identity.Equals(other.identity);

        public override bool Equals(object obj) =>
            obj is HydrologyFeatureKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            Kind,
            identity);
    }

    public readonly struct TerrainPatternCell
    {
        public TerrainPatternCell(
            TerrainPatternType type,
            float baseSurfaceHeight,
            float detailSurfaceHeight,
            float slope,
            bool hasSeaPattern = false,
            int seaRegionKey = 0,
            float seaInteriorProgress = 0f,
            bool hasSecondarySeaPattern = false,
            int secondarySeaRegionKey = 0,
            float secondarySeaInteriorProgress = 0f,
            float primaryInfluence = 1f,
            float primaryTerrainSurfaceHeight = 0f,
            float secondaryTerrainSurfaceHeight = 0f)
        {
            if (!Enum.IsDefined(typeof(TerrainPatternType), type)
                || !float.IsFinite(baseSurfaceHeight)
                || !float.IsFinite(detailSurfaceHeight)
                || !float.IsFinite(slope)
                || !float.IsFinite(seaInteriorProgress)
                || !float.IsFinite(secondarySeaInteriorProgress)
                || !float.IsFinite(primaryInfluence)
                || !float.IsFinite(primaryTerrainSurfaceHeight)
                || !float.IsFinite(secondaryTerrainSurfaceHeight))
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            Type = type;
            BaseSurfaceHeight = baseSurfaceHeight;
            DetailSurfaceHeight = detailSurfaceHeight;
            Slope = slope;
            HasSeaPattern = hasSeaPattern;
            SeaRegionKey = seaRegionKey;
            SeaInteriorProgress = seaInteriorProgress;
            HasSecondarySeaPattern = hasSecondarySeaPattern;
            SecondarySeaRegionKey = secondarySeaRegionKey;
            SecondarySeaInteriorProgress = secondarySeaInteriorProgress;
            PrimaryInfluence = primaryInfluence;
            PrimaryTerrainSurfaceHeight = primaryTerrainSurfaceHeight;
            SecondaryTerrainSurfaceHeight = secondaryTerrainSurfaceHeight;
        }

        public TerrainPatternType Type { get; }
        public float BaseSurfaceHeight { get; }
        public float DetailSurfaceHeight { get; }
        public float Slope { get; }
        public bool HasSeaPattern { get; }
        public int SeaRegionKey { get; }
        public float SeaInteriorProgress { get; }
        public bool HasSecondarySeaPattern { get; }
        public int SecondarySeaRegionKey { get; }
        public float SecondarySeaInteriorProgress { get; }
        public float PrimaryInfluence { get; }
        public float PrimaryTerrainSurfaceHeight { get; }
        public float SecondaryTerrainSurfaceHeight { get; }
        public float SurfaceHeight => BaseSurfaceHeight + DetailSurfaceHeight;
    }

    public readonly struct HydrologyPatternCell
    {
        private HydrologyPatternCell(
            bool hasGroundOverride,
            WaterType waterType,
            int featureIndex,
            float groundHeight,
            float waterSurfaceHeight,
            float interiorInfluence,
            float boundaryInfluence)
        {
            HasGroundOverride = hasGroundOverride;
            WaterType = waterType;
            FeatureIndex = featureIndex;
            GroundHeight = groundHeight;
            WaterSurfaceHeight = waterSurfaceHeight;
            InteriorInfluence = interiorInfluence;
            BoundaryInfluence = boundaryInfluence;
        }

        public static HydrologyPatternCell None => new(
            false,
            WaterType.None,
            -1,
            0f,
            0f,
            0f,
            0f);

        public static HydrologyPatternCell CreateWater(
            WaterType waterType,
            int featureIndex,
            float groundHeight,
            float waterSurfaceHeight,
            float interiorInfluence,
            float boundaryInfluence)
        {
            if (!Enum.IsDefined(typeof(WaterType), waterType)
                || waterType == WaterType.None
                || featureIndex < 0
                || !float.IsFinite(groundHeight)
                || !float.IsFinite(waterSurfaceHeight)
                || !float.IsFinite(interiorInfluence)
                || !float.IsFinite(boundaryInfluence))
            {
                throw new ArgumentOutOfRangeException(nameof(waterType));
            }

            return new HydrologyPatternCell(
                true,
                waterType,
                featureIndex,
                groundHeight,
                waterSurfaceHeight,
                interiorInfluence,
                boundaryInfluence);
        }

        public static HydrologyPatternCell CreateGroundOverride(
            int featureIndex,
            float groundHeight,
            float boundaryInfluence)
        {
            if (featureIndex < 0
                || !float.IsFinite(groundHeight)
                || !float.IsFinite(boundaryInfluence))
            {
                throw new ArgumentOutOfRangeException(nameof(featureIndex));
            }

            return new HydrologyPatternCell(
                true,
                WaterType.None,
                featureIndex,
                groundHeight,
                0f,
                0f,
                boundaryInfluence);
        }

        public bool HasGroundOverride { get; }
        public WaterType WaterType { get; }
        public int FeatureIndex { get; }
        public float GroundHeight { get; }
        public float WaterSurfaceHeight { get; }
        public float InteriorInfluence { get; }
        public float BoundaryInfluence { get; }
        public bool HasWater => WaterType != WaterType.None;
    }

    public readonly struct CombinedPatternCell
    {
        public CombinedPatternCell(
            TerrainPatternCell terrain,
            HydrologyPatternCell hydrology)
        {
            Terrain = terrain;
            Hydrology = hydrology;
        }

        public TerrainPatternCell Terrain { get; }
        public HydrologyPatternCell Hydrology { get; }
        public float GroundHeight => Hydrology.HasGroundOverride
            ? Hydrology.GroundHeight
            : Terrain.SurfaceHeight;
    }

    public static class PatternCellComposition
    {
        public static CombinedPatternCell Combine(
            TerrainPatternCell terrain,
            HydrologyPatternCell hydrology) => new(terrain, hydrology);
    }

    public static class PatternTileComposition
    {
        public static CombinedPatternCell GetCell(
            TerrainPatternTile terrain,
            HydrologyPatternTile hydrology,
            int absoluteX,
            int absoluteZ)
        {
            ValidatePair(terrain, hydrology);
            return PatternCellComposition.Combine(
                terrain.GetCell(absoluteX, absoluteZ),
                hydrology.GetCell(absoluteX, absoluteZ));
        }

        public static void ValidatePair(
            TerrainPatternTile terrain,
            HydrologyPatternTile hydrology)
        {
            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            if (hydrology == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            if (!terrain.Key.Equals(hydrology.Key)
                || !terrain.Bounds.Equals(hydrology.Bounds))
            {
                throw new ArgumentException(
                    "Terrain and Hydrology Pattern Tiles must share a Core.",
                    nameof(hydrology));
            }
        }
    }

    public sealed class TerrainPatternTile
    {
        private readonly TerrainPatternCell[] cells;

        public TerrainPatternTile(
            PatternTileKey key,
            PatternTileBounds bounds,
            TerrainPatternCell[] cells)
        {
            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            if (cells.Length != checked(bounds.Width * bounds.Height))
            {
                throw new ArgumentException(
                    "Terrain Pattern Tile cell count does not match its bounds.",
                    nameof(cells));
            }

            Key = key;
            Bounds = bounds;
            this.cells = (TerrainPatternCell[])cells.Clone();
        }

        public PatternTileKey Key { get; }
        public PatternTileBounds Bounds { get; }
        public int CellCount => cells.Length;

        public TerrainPatternCell GetCell(int absoluteX, int absoluteZ) =>
            cells[GetIndex(absoluteX, absoluteZ)];

        private int GetIndex(int absoluteX, int absoluteZ)
        {
            if (!Bounds.Contains(absoluteX, absoluteZ))
            {
                throw new ArgumentOutOfRangeException(nameof(absoluteX));
            }

            return checked(
                absoluteX - Bounds.MinimumX
                + Bounds.Width * (absoluteZ - Bounds.MinimumZ));
        }
    }

    public sealed class HydrologyPatternTile
    {
        private readonly HydrologyFeatureKey[] features;
        private readonly HydrologyPatternCell[] cells;

        public HydrologyPatternTile(
            PatternTileKey key,
            PatternTileBounds bounds,
            HydrologyFeatureKey[] features,
            HydrologyPatternCell[] cells)
        {
            if (features == null)
            {
                throw new ArgumentNullException(nameof(features));
            }

            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            if (cells.Length != checked(bounds.Width * bounds.Height))
            {
                throw new ArgumentException(
                    "Hydrology Pattern Tile cell count does not match its bounds.",
                    nameof(cells));
            }

            for (var index = 0; index < cells.Length; index++)
            {
                var cell = cells[index];
                if (cell.HasGroundOverride)
                {
                    if (cell.FeatureIndex < 0
                        || cell.FeatureIndex >= features.Length)
                    {
                        throw new ArgumentException(
                            "Hydrology Pattern Tile references a missing Feature Key.",
                        nameof(cells));
                    }
                }
                else if (cell.FeatureIndex != -1)
                {
                    throw new ArgumentException(
                        "Hydrology cells without a ground override cannot reference a Feature Key.",
                        nameof(cells));
                }
            }

            Key = key;
            Bounds = bounds;
            this.features = (HydrologyFeatureKey[])features.Clone();
            this.cells = (HydrologyPatternCell[])cells.Clone();
        }

        public PatternTileKey Key { get; }
        public PatternTileBounds Bounds { get; }
        public int CellCount => cells.Length;
        public int FeatureCount => features.Length;

        public HydrologyPatternCell GetCell(int absoluteX, int absoluteZ) =>
            cells[GetIndex(absoluteX, absoluteZ)];

        public HydrologyFeatureKey GetFeature(int featureIndex)
        {
            if ((uint)featureIndex >= features.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(featureIndex));
            }

            return features[featureIndex];
        }

        private int GetIndex(int absoluteX, int absoluteZ)
        {
            if (!Bounds.Contains(absoluteX, absoluteZ))
            {
                throw new ArgumentOutOfRangeException(nameof(absoluteX));
            }

            return checked(
                absoluteX - Bounds.MinimumX
                + Bounds.Width * (absoluteZ - Bounds.MinimumZ));
        }
    }

}
