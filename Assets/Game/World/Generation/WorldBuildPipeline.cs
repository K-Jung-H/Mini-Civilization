using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Generation
{
    public sealed class WorldBuildInput
    {
        private WorldBuildInput(WorldGenerationSettings settings, int seed)
        {
            Settings = settings.CreateData(seed);
        }

        internal WorldBuildInput(WorldSettingsData settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public WorldSettingsData Settings { get; }
        public int Seed => Settings.Seed;
        public int WorldSize => Settings.InitialWorldSize;
        public int WorldHeight => Settings.WorldHeight;
        public int ChunkSizeXZ => Settings.ChunkCellCountXZ;
        public int ChunkHeight => Settings.ChunkSectionCellCountY;
        public int MinimumCellCoordinate => Settings.MinimumCellCoordinate;
        public int SeaLevelUnits => Settings.SeaLevelUnits;
        public float ColdClimateThreshold => Settings.ColdClimateThreshold;
        public WaterFlowRules WaterFlowRules => Settings.WaterFlowRules;

        public static WorldBuildInput Create(WorldGenerationSettings settings, int seed)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!settings.TryValidate(out var error)) throw new InvalidOperationException(error);
            return new WorldBuildInput(settings, seed);
        }
    }

    public sealed class WorldBuildData
    {
        public WorldBuildData(WorldBuildInput input)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            Size = input.WorldSize;
            Height = input.WorldHeight;
            Seed = input.Seed;
            OriginX = input.MinimumCellCoordinate;
            OriginZ = input.MinimumCellCoordinate;
            var columns = checked(Size * Size);
            SolidHeights = new int[columns];
            WaterSurfaces = new int[columns];
            WaterRoles = new WaterRole[columns];
            WaterTypes = new WaterType[columns];
            WaterBedSurfaces = new SurfaceType[columns];
            TopSurfaces = new SurfaceType[columns];
            Biomes = new CellBiome[columns];
        }

        public WorldBuildInput Input { get; }
        public int Size { get; }
        public int Height { get; }
        public int Seed { get; }
        public int OriginX { get; }
        public int OriginZ { get; }
        public int[] SolidHeights { get; }
        public int[] WaterSurfaces { get; }
        public WaterRole[] WaterRoles { get; }
        public WaterType[] WaterTypes { get; }
        public SurfaceType[] WaterBedSurfaces { get; }
        public SurfaceType[] TopSurfaces { get; }
        public CellBiome[] Biomes { get; }
        public WaterFlowRules WaterFlowRules => Input.WaterFlowRules;
        public bool ContainsColumn(int x, int z) =>
            (uint)(x - OriginX) < Size && (uint)(z - OriginZ) < Size;
        public int ToColumnIndex(int x, int z) =>
            checked(x - OriginX + Size * (z - OriginZ));
        public int ToWorldX(int localX) => checked(OriginX + localX);
        public int ToWorldZ(int localZ) => checked(OriginZ + localZ);
    }

    internal readonly struct TerrainFieldParameters
    {
        public readonly int TerrainSeed;
        public readonly int MountainSeed;
        public readonly int MountainMaskSeed;
        public readonly int ContinentalSeed;
        public readonly int MaximumHeightUnits;
        public readonly float TerrainScale;
        public readonly int TerrainLayers;
        public readonly float TerrainSpacing;
        public readonly float TerrainDetail;
        public readonly int BaseHeightUnits;
        public readonly int HeightVariationUnits;
        public readonly int SeaLevelUnits;
        public readonly int MaximumSeaDepthUnits;
        public readonly float MountainScale;
        public readonly int MountainHeightUnits;
        public readonly float MountainCoverage;
        public readonly float MountainSteepness;
        public readonly float ContinentalScale;
        public readonly float LandThreshold;
        public readonly float CoastTransitionWidth;

        public TerrainFieldParameters(WorldSettingsData settings)
        {
            TerrainSeed = DeterministicNoise.DeriveSeed(settings.Seed, "terrain");
            MountainSeed = DeterministicNoise.DeriveSeed(settings.Seed, "mountains");
            MountainMaskSeed = DeterministicNoise.DeriveSeed(settings.Seed, "mountain-mask");
            ContinentalSeed = DeterministicNoise.DeriveSeed(settings.Seed, "continental");
            MaximumHeightUnits = settings.WorldHeight * WorldGrid.HeightStepsPerCell - 1;
            TerrainScale = settings.TerrainScale;
            TerrainLayers = settings.TerrainLayers;
            TerrainSpacing = settings.TerrainSpacing;
            TerrainDetail = settings.TerrainDetail;
            BaseHeightUnits = settings.BaseHeightUnits;
            HeightVariationUnits = settings.HeightVariationUnits;
            SeaLevelUnits = settings.SeaLevelUnits;
            MaximumSeaDepthUnits = settings.MaximumSeaDepthUnits;
            MountainScale = settings.MountainScale;
            MountainHeightUnits = settings.MountainHeightUnits;
            MountainCoverage = settings.MountainCoverage;
            MountainSteepness = settings.MountainSteepness;
            ContinentalScale = settings.ContinentalScale;
            LandThreshold = settings.LandThreshold;
            CoastTransitionWidth = settings.CoastTransitionWidth;
        }

        public float SampleContinental(int x, int z) =>
            DeterministicNoise.FractalNoise(
                x * ContinentalScale,
                z * ContinentalScale,
                ContinentalSeed,
                4,
                2f,
                0.5f);

        public int SampleHeight(int x, int z)
        {
            var noise = DeterministicNoise.FractalNoise(
                x * TerrainScale,
                z * TerrainScale,
                TerrainSeed,
                TerrainLayers,
                TerrainSpacing,
                TerrainDetail);
            var ridge = DeterministicNoise.RidgedFractalNoise(
                x * MountainScale,
                z * MountainScale,
                MountainSeed,
                TerrainLayers,
                TerrainSpacing,
                TerrainDetail);
            var maskNoise = DeterministicNoise.FractalNoise(
                x * MountainScale * 0.4f,
                z * MountainScale * 0.4f,
                MountainMaskSeed,
                3,
                2f,
                0.5f);
            var continental = SampleContinental(x, z);
            var coastAmount = SmoothStep01(
                (continental - LandThreshold)
                / CoastTransitionWidth);
            var mountainMask = SmoothStep01(
                (maskNoise - MountainCoverage) / 0.2f);
            var mountainHeight = MathF.Pow(ridge, MountainSteepness)
                * mountainMask * MountainHeightUnits;
            if (continental < LandThreshold)
            {
                var oceanAmount = SmoothStep01(
                    (LandThreshold - continental)
                    / Math.Max(0.0001f, LandThreshold));
                var depthAmount = MathF.Sqrt(oceanAmount);
                var oceanDepth = 1 + (int)MathF.Round(
                    depthAmount * Math.Max(0, MaximumSeaDepthUnits - 1));
                var seabedDetail = (noise * 2f - 1f)
                    * Math.Min(HeightVariationUnits, MaximumSeaDepthUnits)
                    * oceanAmount * 0.2f;
                return Math.Clamp(
                    SeaLevelUnits - oceanDepth
                        + (int)MathF.Round(seabedDetail),
                    1,
                    SeaLevelUnits - 1);
            }

            var inlandHeight = BaseHeightUnits
                + (noise * 2f - 1f) * HeightVariationUnits
                + mountainHeight;
            var height = (int)MathF.Round(
                SeaLevelUnits
                + (inlandHeight - SeaLevelUnits) * coastAmount);
            return Math.Clamp(height, 1, MaximumHeightUnits);
        }

        private static float SmoothStep01(float value)
        {
            value = Math.Clamp(value, 0f, 1f);
            return value * value * (3f - 2f * value);
        }
    }

    internal readonly struct WorldFieldColumn
    {
        public readonly int SolidHeight;
        public readonly int WaterSurface;
        public readonly WaterRole WaterRole;
        public readonly WaterType WaterType;
        public readonly SurfaceType WaterBedSurface;
        public readonly SurfaceType TopSurface;
        public readonly CellBiome Biome;

        public WorldFieldColumn(
            int solidHeight,
            int waterSurface,
            WaterRole waterRole,
            WaterType waterType,
            SurfaceType waterBedSurface,
            SurfaceType topSurface,
            CellBiome biome)
        {
            SolidHeight = solidHeight;
            WaterSurface = waterSurface;
            WaterRole = waterRole;
            WaterType = waterType;
            WaterBedSurface = waterBedSurface;
            TopSurface = topSurface;
            Biome = biome;
        }
    }

    internal sealed class WorldFieldSampler
    {
        private enum RiverExecutionMode : byte
        {
            Dynamic,
            Source
        }

        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        private readonly WorldSettingsData settings;
        private readonly TerrainFieldParameters terrain;
        private readonly int riverSeed;
        private readonly int lakeSeed;
        private readonly int climateSeed;
        private readonly Dictionary<long, LakeBasin> lakeBasins = new();
        private readonly Dictionary<long, RiverFeature> riverFeatures = new();
        private readonly Dictionary<long, RiverRegionField> riverRegionFields = new();
        private readonly Queue<long> lakeBasinOrder = new();
        private readonly Queue<long> riverFeatureOrder = new();
        private readonly Queue<long> riverRegionFieldOrder = new();

        public WorldFieldSampler(WorldSettingsData settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            terrain = new TerrainFieldParameters(settings);
            riverSeed = DeterministicNoise.DeriveSeed(settings.Seed, "river-channel");
            lakeSeed = DeterministicNoise.DeriveSeed(settings.Seed, "lake-basin");
            climateSeed = DeterministicNoise.DeriveSeed(settings.Seed, "climate");
        }

        public WorldFieldColumn Sample(int x, int z) =>
            Sample(x, z, terrain.SampleHeight(x, z));

        public WorldFieldColumn Sample(int x, int z, int baseHeight)
        {
            var core = SampleCore(x, z, baseHeight);
            var topSurface = core.WaterType != WaterType.None
                ? core.WaterBedSurface
                : IsShore(x, z, core.SolidHeight)
                    ? SurfaceType.Shore
                    : SurfaceType.Ground;
            var biome = SampleBiome(
                x,
                z,
                core.SolidHeight,
                core.WaterType);
            return new WorldFieldColumn(
                core.SolidHeight,
                core.WaterSurface,
                core.WaterRole,
                core.WaterType,
                core.WaterBedSurface,
                topSurface,
                biome);
        }

        public CellBiome SampleBiome(
            int x,
            int z,
            int solidHeight,
            WaterType waterType)
        {
            var climate = ResolveClimate(x, z, solidHeight);
            var altitude = solidHeight /
                (float)(settings.WorldHeight * WorldGrid.HeightStepsPerCell);
            return new CellBiome(
                climate,
                BiomeStage.ResolveTerrain(climate, altitude),
                BiomeStage.ResolveWater(waterType));
        }

        private CoreColumn SampleCore(int x, int z) =>
            SampleCore(x, z, terrain.SampleHeight(x, z));

        private CoreColumn SampleCore(int x, int z, int baseHeight)
        {
            if (terrain.SampleContinental(x, z) < settings.LandThreshold
                && baseHeight < settings.SeaLevelUnits)
            {
                return new CoreColumn(
                    baseHeight,
                    settings.SeaLevelUnits,
                    WaterRole.Source,
                    WaterType.Sea,
                    SurfaceType.Seabed);
            }

            if (TrySampleLake(x, z, baseHeight, out var lake))
            {
                return lake;
            }

            if (TrySampleRiver(x, z, baseHeight, out var river))
            {
                return river;
            }

            return new CoreColumn(
                baseHeight,
                0,
                WaterRole.None,
                WaterType.None,
                SurfaceType.None);
        }

        private bool TrySampleLake(
            int x,
            int z,
            int terrainHeight,
            out CoreColumn column)
        {
            column = default;
            if (settings.LakeDensity <= 0f)
            {
                return false;
            }

            var regionSize = settings.LakeRegionSizeCells;
            var regionX = WorldCoordinateUtility.FloorDivide(x, regionSize);
            var regionZ = WorldCoordinateUtility.FloorDivide(z, regionSize);
            var found = false;
            var bestDistance = float.MaxValue;
            var bestBasin = default(LakeBasin);
            for (var offsetZ = -1; offsetZ <= 1; offsetZ++)
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                var basin = GetLakeBasin(
                    regionX + offsetX,
                    regionZ + offsetZ);
                if (!basin.Exists)
                {
                    continue;
                }

                var deltaX = x - basin.CenterX;
                var deltaZ = z - basin.CenterZ;
                var distance = MathF.Sqrt(
                    deltaX * deltaX + deltaZ * deltaZ) / basin.Radius;
                if (distance > 1f || distance >= bestDistance)
                {
                    continue;
                }

                found = true;
                bestDistance = distance;
                bestBasin = basin;
            }

            if (!found)
            {
                return false;
            }

            var centerFactor = 1f - bestDistance;
            var depth = Math.Max(
                settings.MinimumInlandLakeDepthSteps,
                (int)MathF.Round(
                    bestBasin.MaximumDepth * centerFactor));
            var solidHeight = Math.Max(
                1,
                Math.Min(terrainHeight, bestBasin.Surface - depth));
            var type = bestBasin.Area <= settings.PondMaximumArea
                ? WaterType.Pond
                : WaterType.Lake;
            column = new CoreColumn(
                solidHeight,
                bestBasin.Surface,
                WaterRole.Source,
                type,
                SurfaceType.Lakebed);
            return true;
        }

        private LakeBasin GetLakeBasin(int regionX, int regionZ)
        {
            var key = CoordinateKey(regionX, regionZ);
            if (lakeBasins.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var basin = BuildLakeBasin(regionX, regionZ);
            AddBounded(
                lakeBasins,
                lakeBasinOrder,
                key,
                basin,
                2048);
            return basin;
        }

        private LakeBasin BuildLakeBasin(int regionX, int regionZ)
        {
            if (DeterministicNoise.Value01(regionX, regionZ, lakeSeed)
                > settings.LakeDensity)
            {
                return default;
            }

            var regionSize = settings.LakeRegionSizeCells;
            var centerX = regionX * regionSize
                + (int)(DeterministicNoise.Hash(regionX, regionZ, lakeSeed + 101)
                    % (uint)regionSize);
            var centerZ = regionZ * regionSize
                + (int)(DeterministicNoise.Hash(regionX, regionZ, lakeSeed + 211)
                    % (uint)regionSize);
            if (terrain.SampleContinental(centerX, centerZ) < settings.LandThreshold)
            {
                return default;
            }

            var radius = Math.Max(
                1,
                1 + (int)MathF.Round(
                    DeterministicNoise.Value01(regionX, regionZ, lakeSeed + 307)
                    * Math.Max(0, settings.MaximumLakeRadiusCells - 1)));
            var area = 0;
            for (var offsetZ = -radius; offsetZ <= radius; offsetZ++)
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                if (offsetX * offsetX + offsetZ * offsetZ <= radius * radius)
                {
                    area++;
                }
            }

            if (area < settings.MinimumInlandLakeArea)
            {
                return default;
            }

            var rimRadius = radius + 1;
            var surface = int.MaxValue;
            for (var offsetZ = -rimRadius; offsetZ <= rimRadius; offsetZ++)
            for (var offsetX = -rimRadius; offsetX <= rimRadius; offsetX++)
            {
                var distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
                if (distanceSquared <= radius * radius
                    || distanceSquared > rimRadius * rimRadius)
                {
                    continue;
                }

                var sampleX = centerX + offsetX;
                var sampleZ = centerZ + offsetZ;
                if (terrain.SampleContinental(sampleX, sampleZ)
                    < settings.LandThreshold)
                {
                    return default;
                }

                surface = Math.Min(surface, terrain.SampleHeight(sampleX, sampleZ));
            }

            if (surface <= settings.SeaLevelUnits)
            {
                return default;
            }

            var sizeFactor = radius /
                (float)Math.Max(1, settings.MaximumLakeRadiusCells);
            var maximumDepth = Math.Max(
                settings.MinimumInlandLakeDepthSteps,
                (int)MathF.Round(settings.MaximumLakeDepthSteps * sizeFactor));
            return new LakeBasin(
                centerX,
                centerZ,
                radius,
                surface,
                maximumDepth,
                area);
        }

        private bool TrySampleRiver(
            int x,
            int z,
            int terrainHeight,
            out CoreColumn column)
        {
            column = default;
            if (settings.RiverDensity <= 0f
                || terrain.SampleContinental(x, z) < settings.LandThreshold)
            {
                return false;
            }

            var regionSize = ResolveRiverRegionSize();
            var regionX = WorldCoordinateUtility.FloorDivide(x, regionSize);
            var regionZ = WorldCoordinateUtility.FloorDivide(z, regionSize);
            var field = GetRiverRegionField(regionX, regionZ);
            if (field.TryGetChannel(x, z, out var river))
            {
                column = new CoreColumn(
                    Math.Max(1, Math.Min(terrainHeight, river.BedHeight)),
                    river.SurfaceHeight,
                    river.Mode == RiverExecutionMode.Source
                        ? WaterRole.Source
                        : WaterRole.Dynamic,
                    WaterType.River,
                    SurfaceType.Riverbed);
                return true;
            }

            if (!field.TryGetTerrainHeight(x, z, out var riverTerrainHeight))
            {
                return false;
            }

            column = new CoreColumn(
                riverTerrainHeight,
                0,
                WaterRole.None,
                WaterType.None,
                SurfaceType.None);
            return true;
        }

        private RiverRegionField GetRiverRegionField(int regionX, int regionZ)
        {
            var key = CoordinateKey(regionX, regionZ);
            if (riverRegionFields.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var regionSize = ResolveRiverRegionSize();
            var minimumX = checked(regionX * regionSize);
            var minimumZ = checked(regionZ * regionSize);
            var maximumX = checked(minimumX + regionSize - 1);
            var maximumZ = checked(minimumZ + regionSize - 1);
            var blendRadius = Math.Max(3, ResolveMaximumRiverWidth() + 2);
            var field = new RiverRegionField();
            var searchRadius = ResolveMaximumRiverCourseRegions() + 1;
            for (var offsetZ = -searchRadius; offsetZ <= searchRadius; offsetZ++)
            for (var offsetX = -searchRadius; offsetX <= searchRadius; offsetX++)
            {
                var feature = GetRiverFeature(
                    regionX + offsetX,
                    regionZ + offsetZ);
                feature.CopyChannelsTo(
                    field,
                    minimumX - blendRadius,
                    maximumX + blendRadius,
                    minimumZ - blendRadius,
                    maximumZ + blendRadius);
            }

            field.BuildTerrainHeights(
                terrain,
                minimumX,
                maximumX,
                minimumZ,
                maximumZ,
                blendRadius);
            AddBounded(
                riverRegionFields,
                riverRegionFieldOrder,
                key,
                field,
                256);
            return field;
        }

        private RiverFeature GetRiverFeature(int sourceRegionX, int sourceRegionZ)
        {
            var key = CoordinateKey(sourceRegionX, sourceRegionZ);
            if (riverFeatures.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var feature = BuildRiverFeature(sourceRegionX, sourceRegionZ);
            AddBounded(
                riverFeatures,
                riverFeatureOrder,
                key,
                feature,
                2048);
            return feature;
        }

        private static void AddBounded<T>(
            IDictionary<long, T> cache,
            Queue<long> order,
            long key,
            T value,
            int capacity)
        {
            while (cache.Count >= capacity && order.Count > 0)
            {
                cache.Remove(order.Dequeue());
            }

            cache.Add(key, value);
            order.Enqueue(key);
        }

        private RiverFeature BuildRiverFeature(int sourceRegionX, int sourceRegionZ)
        {
            if (!IsRiverSourceRegion(sourceRegionX, sourceRegionZ))
            {
                return RiverFeature.Empty;
            }

            var nodes = new List<RiverNode>();
            var visitedRegions = new HashSet<long>();
            var current = GetRiverNode(sourceRegionX, sourceRegionZ);
            if (current.Height <= settings.SeaLevelUnits
                || terrain.SampleContinental(current.X, current.Z)
                    < settings.LandThreshold)
            {
                return RiverFeature.Empty;
            }

            nodes.Add(current);
            visitedRegions.Add(CoordinateKey(current.RegionX, current.RegionZ));
            var maximumRegions = ResolveMaximumRiverCourseRegions();
            for (var index = 1; index <= maximumRegions; index++)
            {
                if (!TryGetNextRiverNode(current, visitedRegions, out var next))
                {
                    break;
                }

                nodes.Add(next);
                visitedRegions.Add(CoordinateKey(next.RegionX, next.RegionZ));
                current = next;
                if (terrain.SampleContinental(current.X, current.Z)
                        < settings.LandThreshold
                    || current.Height <= settings.SeaLevelUnits)
                {
                    break;
                }

                if (index >= 4
                    && DeterministicNoise.Value01(
                        current.RegionX,
                        current.RegionZ,
                        riverSeed + 907) < 0.08f)
                {
                    break;
                }
            }

            if (nodes.Count < 2)
            {
                return RiverFeature.Empty;
            }

            var path = new List<RiverPoint>();
            for (var index = 1; index < nodes.Count; index++)
            {
                AppendGridPath(path, nodes[index - 1], nodes[index]);
            }

            if (path.Count < 4)
            {
                return RiverFeature.Empty;
            }

            return BuildRiverFeature(path);
        }

        private RiverFeature BuildRiverFeature(IReadOnlyList<RiverPoint> path)
        {
            var surfaces = BuildRiverSurfaceProfile(path);
            var widths = BuildRiverWidthProfile(path.Count);
            var feature = new RiverFeature();
            var maximumDepth = Math.Max(
                settings.RiverDepthCells,
                settings.MaximumRiverDepthCells)
                * WorldGrid.HeightStepsPerCell;
            var minimumDepth = Math.Max(
                1,
                settings.RiverDepthCells * WorldGrid.HeightStepsPerCell);
            var maximumDynamicRun = WaterFlowReachability
                .GetSafeHorizontalSpreadCount(settings.WaterFlowRules);
            var dynamicRun = 0;
            for (var index = 0; index < path.Count; index++)
            {
                var progress = index / (float)Math.Max(1, path.Count - 1);
                var centerDepth = Math.Max(
                    minimumDepth,
                    (int)MathF.Round(
                        maximumDepth
                        + (minimumDepth - maximumDepth) * progress));
                var width = widths[index];
                var radius = (width - 1) / 2;
                ResolveRiverPerpendicular(path, index, out var perpendicularX, out var perpendicularZ);
                var descends = index > 0
                    && surfaces[index] < surfaces[index - 1];
                var mode = descends && dynamicRun < maximumDynamicRun
                    ? RiverExecutionMode.Dynamic
                    : RiverExecutionMode.Source;
                dynamicRun = mode == RiverExecutionMode.Dynamic
                    ? dynamicRun + 1
                    : 0;
                for (var offset = -radius; offset <= radius; offset++)
                {
                    var lateralAmount = radius == 0
                        ? 0f
                        : Math.Abs(offset) / (float)radius;
                    var depth = Math.Max(
                        1,
                        (int)MathF.Round(
                            centerDepth
                            + (minimumDepth - centerDepth) * lateralAmount));
                    feature.AddChannel(
                        path[index].X + perpendicularX * offset,
                        path[index].Z + perpendicularZ * offset,
                        new RiverChannel(
                            Math.Max(1, surfaces[index] - depth),
                            surfaces[index],
                            mode));
                }
            }

            return feature;
        }

        private int[] BuildRiverSurfaceProfile(IReadOnlyList<RiverPoint> path)
        {
            var surfaces = new int[path.Count];
            var previous = int.MaxValue;
            var minimumSurface =
                (settings.SeaLevelUnits + WorldGrid.HeightStepsPerCell - 1)
                / WorldGrid.HeightStepsPerCell
                * WorldGrid.HeightStepsPerCell;
            for (var index = 0; index < path.Count; index++)
            {
                var terrainHeight = terrain.SampleHeight(path[index].X, path[index].Z);
                var aligned = terrainHeight
                    - terrainHeight % WorldGrid.HeightStepsPerCell;
                aligned = Math.Max(minimumSurface, aligned);
                previous = Math.Min(previous, aligned);
                surfaces[index] = previous;
            }

            return surfaces;
        }

        private int[] BuildRiverWidthProfile(int pathLength)
        {
            var widths = new int[pathLength];
            var maximumWidth = ResolveMaximumRiverWidth();
            var regionSize = ResolveRiverRegionSize();
            var courseWidth = pathLength >= regionSize * 4
                ? maximumWidth
                : pathLength >= regionSize * 2
                    ? Math.Min(3, maximumWidth)
                    : 1;
            for (var index = 0; index < pathLength; index++)
            {
                var progress = index / (float)Math.Max(1, pathLength - 1);
                var width = progress < 0.2f
                    ? 1
                    : progress < 0.6f
                        ? Math.Min(3, courseWidth)
                        : courseWidth;
                var remaining = pathLength - 1 - index;
                if (remaining <= 1)
                {
                    width = 1;
                }
                else if (remaining <= 3)
                {
                    width = Math.Min(width, 3);
                }

                widths[index] = width;
            }

            return widths;
        }

        private static void ResolveRiverPerpendicular(
            IReadOnlyList<RiverPoint> path,
            int index,
            out int perpendicularX,
            out int perpendicularZ)
        {
            var previous = path[Math.Max(0, index - 1)];
            var next = path[Math.Min(path.Count - 1, index + 1)];
            var deltaX = next.X - previous.X;
            var deltaZ = next.Z - previous.Z;
            if (Math.Abs(deltaX) >= Math.Abs(deltaZ))
            {
                perpendicularX = 0;
                perpendicularZ = 1;
                return;
            }

            perpendicularX = 1;
            perpendicularZ = 0;
        }

        private static void AppendGridPath(
            ICollection<RiverPoint> path,
            RiverNode start,
            RiverNode end)
        {
            var x = start.X;
            var z = start.Z;
            AppendRiverPoint(path, x, z);
            while (x != end.X || z != end.Z)
            {
                var remainingX = end.X - x;
                var remainingZ = end.Z - z;
                if (Math.Abs(remainingX) >= Math.Abs(remainingZ)
                    && remainingX != 0)
                {
                    x += Math.Sign(remainingX);
                }
                else
                {
                    z += Math.Sign(remainingZ);
                }

                AppendRiverPoint(path, x, z);
            }
        }

        private static void AppendRiverPoint(
            ICollection<RiverPoint> path,
            int x,
            int z)
        {
            if (path is List<RiverPoint> list
                && list.Count > 0
                && list[list.Count - 1].X == x
                && list[list.Count - 1].Z == z)
            {
                return;
            }

            path.Add(new RiverPoint(x, z));
        }

        private RiverNode GetRiverNode(int regionX, int regionZ)
        {
            var regionSize = ResolveRiverRegionSize();
            var margin = Math.Max(1, regionSize / 5);
            var span = Math.Max(1, regionSize - margin * 2);
            var x = regionX * regionSize + margin
                + (int)(DeterministicNoise.Hash(regionX, regionZ, riverSeed + 101)
                    % (uint)span);
            var z = regionZ * regionSize + margin
                + (int)(DeterministicNoise.Hash(regionX, regionZ, riverSeed + 211)
                    % (uint)span);
            return new RiverNode(
                regionX,
                regionZ,
                x,
                z,
                terrain.SampleHeight(x, z));
        }

        private int ResolveRiverRegionSize() => Math.Max(
            settings.ChunkCellCountXZ,
            (int)MathF.Round(0.25f / Math.Max(0.001f, settings.RiverScale)));

        private int ResolveMaximumRiverCourseRegions() => 8;

        private int ResolveMaximumRiverWidth()
        {
            var width = Math.Clamp(settings.MaximumRiverWidthCells, 1, 5);
            return (width & 1) == 0 ? width - 1 : width;
        }

        private bool IsRiverSourceRegion(int regionX, int regionZ) =>
            DeterministicNoise.Value01(regionX, regionZ, riverSeed)
            <= settings.RiverDensity * 0.35f;

        private bool TryGetNextRiverNode(
            RiverNode start,
            ISet<long> visitedRegions,
            out RiverNode downstream)
        {
            var found = false;
            downstream = default;
            var bestScore = float.MaxValue;
            for (var offsetZ = -1; offsetZ <= 1; offsetZ++)
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetZ == 0)
                {
                    continue;
                }

                var candidateRegionX = start.RegionX + offsetX;
                var candidateRegionZ = start.RegionZ + offsetZ;
                if (visitedRegions.Contains(CoordinateKey(
                        candidateRegionX,
                        candidateRegionZ)))
                {
                    continue;
                }

                var candidate = GetRiverNode(candidateRegionX, candidateRegionZ);
                var isSea = terrain.SampleContinental(candidate.X, candidate.Z)
                    < settings.LandThreshold;
                if (!isSea
                    && candidate.Height > start.Height
                        + WorldGrid.HeightStepsPerCell * 2)
                {
                    continue;
                }

                var meander = DeterministicNoise.Value01(
                    start.RegionX + candidateRegionX,
                    start.RegionZ + candidateRegionZ,
                    riverSeed + 613) * WorldGrid.HeightStepsPerCell;
                var score = isSea
                    ? settings.SeaLevelUnits - WorldGrid.HeightStepsPerCell * 4
                    : candidate.Height + meander;
                if (found && score >= bestScore)
                {
                    continue;
                }

                found = true;
                downstream = candidate;
                bestScore = score;
            }

            return found;
        }

        private static long CoordinateKey(int x, int z) =>
            ((long)x << 32) ^ (uint)z;

        private static void DecodeCoordinateKey(
            long key,
            out int x,
            out int z)
        {
            x = (int)(key >> 32);
            z = (int)key;
        }

        private bool IsShore(int x, int z, int height)
        {
            for (var index = 0; index < Directions.Length; index++)
            {
                var next = SampleCore(
                    x + Directions[index].x,
                    z + Directions[index].z);
                if (next.WaterType != WaterType.None
                    && Math.Abs(height - next.WaterSurface) <= 2)
                {
                    return true;
                }
            }

            return false;
        }

        private ClimateBiome ResolveClimate(int x, int z, int height)
        {
            var altitude = height /
                (float)(settings.WorldHeight * WorldGrid.HeightStepsPerCell);
            var temperature = DeterministicNoise.FractalNoise(
                x * 0.00625f,
                z * 0.00625f,
                climateSeed,
                4,
                2f,
                0.5f);
            temperature = Math.Clamp(
                temperature * 0.85f + 0.15f - altitude * 0.35f,
                0f,
                1f);
            return BiomeStage.ResolveClimate(
                temperature,
                settings.ColdClimateThreshold);
        }

        private static float SmoothStep01(float value)
        {
            value = Math.Clamp(value, 0f, 1f);
            return value * value * (3f - 2f * value);
        }

        private readonly struct CoreColumn
        {
            public readonly int SolidHeight;
            public readonly int WaterSurface;
            public readonly WaterRole WaterRole;
            public readonly WaterType WaterType;
            public readonly SurfaceType WaterBedSurface;

            public CoreColumn(
                int solidHeight,
                int waterSurface,
                WaterRole waterRole,
                WaterType waterType,
                SurfaceType waterBedSurface)
            {
                SolidHeight = solidHeight;
                WaterSurface = waterSurface;
                WaterRole = waterRole;
                WaterType = waterType;
                WaterBedSurface = waterBedSurface;
            }
        }

        private readonly struct LakeBasin
        {
            public readonly bool Exists;
            public readonly int CenterX;
            public readonly int CenterZ;
            public readonly int Radius;
            public readonly int Surface;
            public readonly int MaximumDepth;
            public readonly int Area;

            public LakeBasin(
                int centerX,
                int centerZ,
                int radius,
                int surface,
                int maximumDepth,
                int area)
            {
                Exists = true;
                CenterX = centerX;
                CenterZ = centerZ;
                Radius = radius;
                Surface = surface;
                MaximumDepth = maximumDepth;
                Area = area;
            }
        }

        private readonly struct RiverNode
        {
            public readonly int RegionX;
            public readonly int RegionZ;
            public readonly int X;
            public readonly int Z;
            public readonly int Height;

            public RiverNode(
                int regionX,
                int regionZ,
                int x,
                int z,
                int height)
            {
                RegionX = regionX;
                RegionZ = regionZ;
                X = x;
                Z = z;
                Height = height;
            }
        }

        private readonly struct RiverPoint
        {
            public readonly int X;
            public readonly int Z;

            public RiverPoint(int x, int z)
            {
                X = x;
                Z = z;
            }
        }

        private readonly struct RiverChannel
        {
            public readonly int BedHeight;
            public readonly int SurfaceHeight;
            public readonly RiverExecutionMode Mode;

            public RiverChannel(
                int bedHeight,
                int surfaceHeight,
                RiverExecutionMode mode)
            {
                BedHeight = bedHeight;
                SurfaceHeight = surfaceHeight;
                Mode = mode;
            }

            public static RiverChannel Merge(
                RiverChannel current,
                RiverChannel candidate)
            {
                if (candidate.SurfaceHeight < current.SurfaceHeight)
                {
                    return new RiverChannel(
                        Math.Min(current.BedHeight, candidate.BedHeight),
                        candidate.SurfaceHeight,
                        candidate.Mode);
                }

                return new RiverChannel(
                    Math.Min(current.BedHeight, candidate.BedHeight),
                    current.SurfaceHeight,
                    current.Mode);
            }
        }

        private sealed class RiverFeature
        {
            public static readonly RiverFeature Empty = new();

            private readonly Dictionary<long, RiverChannel> channels = new();

            public void AddChannel(int x, int z, RiverChannel channel)
            {
                var key = CoordinateKey(x, z);
                if (channels.TryGetValue(key, out var current))
                {
                    channels[key] = RiverChannel.Merge(current, channel);
                    return;
                }

                channels.Add(key, channel);
            }

            public void CopyChannelsTo(
                RiverRegionField target,
                int minimumX,
                int maximumX,
                int minimumZ,
                int maximumZ)
            {
                foreach (var pair in channels)
                {
                    DecodeCoordinateKey(pair.Key, out var x, out var z);
                    if (x < minimumX || x > maximumX
                        || z < minimumZ || z > maximumZ)
                    {
                        continue;
                    }

                    target.AddChannel(x, z, pair.Value);
                }
            }
        }

        private sealed class RiverRegionField
        {
            private readonly Dictionary<long, RiverChannel> channels = new();
            private readonly Dictionary<long, RiverTerrainHeight> terrainHeights = new();

            public void AddChannel(int x, int z, RiverChannel channel)
            {
                var key = CoordinateKey(x, z);
                if (channels.TryGetValue(key, out var current))
                {
                    channels[key] = RiverChannel.Merge(current, channel);
                    return;
                }

                channels.Add(key, channel);
            }

            public bool TryGetChannel(int x, int z, out RiverChannel channel) =>
                channels.TryGetValue(CoordinateKey(x, z), out channel);

            public bool TryGetTerrainHeight(int x, int z, out int height)
            {
                if (terrainHeights.TryGetValue(
                        CoordinateKey(x, z),
                        out var target))
                {
                    height = target.Height;
                    return true;
                }

                height = 0;
                return false;
            }

            public void BuildTerrainHeights(
                TerrainFieldParameters terrain,
                int minimumX,
                int maximumX,
                int minimumZ,
                int maximumZ,
                int blendRadius)
            {
                foreach (var pair in channels)
                {
                    DecodeCoordinateKey(pair.Key, out var channelX, out var channelZ);
                    var channel = pair.Value;
                    for (var offsetZ = -blendRadius; offsetZ <= blendRadius; offsetZ++)
                    for (var offsetX = -blendRadius; offsetX <= blendRadius; offsetX++)
                    {
                        var x = channelX + offsetX;
                        var z = channelZ + offsetZ;
                        if (x < minimumX || x > maximumX
                            || z < minimumZ || z > maximumZ
                            || channels.ContainsKey(CoordinateKey(x, z)))
                        {
                            continue;
                        }

                        var distance = MathF.Sqrt(
                            offsetX * offsetX + offsetZ * offsetZ);
                        if (distance > blendRadius)
                        {
                            continue;
                        }

                        var influence = 1f - distance / (blendRadius + 1f);
                        var terrainHeight = terrain.SampleHeight(x, z);
                        var height = (int)MathF.Round(
                            terrainHeight
                            + (channel.SurfaceHeight - terrainHeight) * influence);
                        if (Math.Abs(offsetX) + Math.Abs(offsetZ) == 1)
                        {
                            height = Math.Max(height, channel.SurfaceHeight);
                        }

                        var key = CoordinateKey(x, z);
                        if (terrainHeights.TryGetValue(key, out var current)
                            && current.Influence >= influence)
                        {
                            if (Math.Abs(offsetX) + Math.Abs(offsetZ) == 1
                                && height > current.Height)
                            {
                                terrainHeights[key] = new RiverTerrainHeight(
                                    height,
                                    current.Influence);
                            }

                            continue;
                        }

                        terrainHeights[key] = new RiverTerrainHeight(
                            height,
                            influence);
                    }
                }
            }
        }

        private readonly struct RiverTerrainHeight
        {
            public readonly int Height;
            public readonly float Influence;

            public RiverTerrainHeight(int height, float influence)
            {
                Height = height;
                Influence = influence;
            }
        }
    }

    public static class WorldDataBuilder
    {
        public static WorldData Build(WorldBuildData build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            var world = new WorldData(build.Input.Settings);
            for (var localZ = 0; localZ < build.Size; localZ++)
            for (var localX = 0; localX < build.Size; localX++)
            {
                var index = localX + build.Size * localZ;
                WriteColumn(
                    world,
                    build.ToWorldX(localX),
                    build.ToWorldZ(localZ),
                    build.SolidHeights[index],
                    build.TopSurfaces[index] == SurfaceType.None
                        ? SurfaceType.Ground
                        : build.TopSurfaces[index],
                    build.WaterSurfaces[index],
                    build.WaterRoles[index],
                    build.WaterTypes[index],
                    build.Biomes[index]);
            }

            return world;
        }

        internal static void WriteColumn(
            WorldData world,
            int x,
            int z,
            int solidHeightUnits,
            SurfaceType surface,
            int waterSurfaceUnits,
            WaterRole waterRole,
            WaterType waterType,
            CellBiome biome)
        {
            var maximumUnits = world.Height * WorldGrid.HeightStepsPerCell;
            solidHeightUnits = Math.Clamp(solidHeightUnits, 0, maximumUnits);
            waterSurfaceUnits = Math.Clamp(waterSurfaceUnits, 0, maximumUnits);
            var usedHeightUnits = Math.Max(solidHeightUnits, waterSurfaceUnits);
            var usedCellCount = Math.Min(
                world.Height,
                Math.Max(0, (usedHeightUnits + WorldGrid.HeightStepsPerCell - 1)
                    / WorldGrid.HeightStepsPerCell));
            for (var y = 0; y < usedCellCount; y++)
            {
                var baseUnits = y * WorldGrid.HeightStepsPerCell;
                var solidFill = (byte)Math.Clamp(
                    solidHeightUnits - baseUnits,
                    0,
                    WorldGrid.HeightStepsPerCell);
                var cell = new CellData
                {
                    Terrain = new TerrainData { SolidHeight = solidFill }
                };
                if (solidFill > 0)
                {
                    cell.Terrain.Material = y < Math.Max(
                        0,
                        solidHeightUnits / WorldGrid.HeightStepsPerCell - 2)
                            ? MaterialType.Rock
                            : MaterialType.Soil;
                    cell.Terrain.Geology = MaterialType.Rock;
                    cell.Terrain.Surface = solidFill < WorldGrid.HeightStepsPerCell
                        || baseUnits + solidFill == solidHeightUnits
                            ? surface
                            : SurfaceType.None;
                }

                var available = WorldGrid.HeightStepsPerCell - solidFill;
                var desiredTop = Math.Clamp(
                    waterSurfaceUnits - baseUnits,
                    0,
                    WorldGrid.HeightStepsPerCell);
                var waterFill = (byte)Math.Clamp(
                    desiredTop - solidFill,
                    0,
                    available);
                if (waterFill > 0 && waterRole == WaterRole.Source)
                {
                    cell.Water = new WaterData
                    {
                        Amount = WaterAmount.FromRenderFill(waterFill, available),
                        Role = WaterRole.Source,
                        Type = waterType,
                        Flow = FlowDirection.None
                    };
                }

                if (cell.HasTerrain || cell.HasWater)
                {
                    cell.Biome = biome;
                    world.SetCellBulk(x, y, z, cell);
                }
            }
        }
    }

    internal static class TerrainStage
    {
        public static void Build(WorldBuildData build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            var field = new TerrainFieldParameters(build.Input.Settings);
            for (var localZ = 0; localZ < build.Size; localZ++)
            for (var localX = 0; localX < build.Size; localX++)
            {
                var index = localX + build.Size * localZ;
                build.SolidHeights[index] = field.SampleHeight(
                    build.ToWorldX(localX),
                    build.ToWorldZ(localZ));
            }
        }
    }

    internal static class WaterFeatureStage
    {
        public static void Build(WorldBuildData build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            var sampler = new WorldFieldSampler(build.Input.Settings);
            for (var localZ = 0; localZ < build.Size; localZ++)
            for (var localX = 0; localX < build.Size; localX++)
            {
                var index = localX + build.Size * localZ;
                var column = sampler.Sample(
                    build.ToWorldX(localX),
                    build.ToWorldZ(localZ),
                    build.SolidHeights[index]);
                build.SolidHeights[index] = column.SolidHeight;
                build.WaterSurfaces[index] = column.WaterSurface;
                build.WaterRoles[index] = column.WaterRole;
                build.WaterTypes[index] = column.WaterType;
                build.WaterBedSurfaces[index] = column.WaterBedSurface;
                build.TopSurfaces[index] = column.TopSurface;
                build.Biomes[index] = column.Biome;
            }
        }
    }

    internal static class BiomeStage
    {
        public static void Build(WorldBuildData build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            var sampler = new WorldFieldSampler(build.Input.Settings);
            for (var localZ = 0; localZ < build.Size; localZ++)
            for (var localX = 0; localX < build.Size; localX++)
            {
                var index = localX + build.Size * localZ;
                build.Biomes[index] = sampler.SampleBiome(
                    build.ToWorldX(localX),
                    build.ToWorldZ(localZ),
                    build.SolidHeights[index],
                    build.WaterTypes[index]);
            }
        }

        internal static ClimateBiome ResolveClimate(
            float temperature,
            float coldThreshold)
        {
            if (temperature <= coldThreshold)
            {
                return ClimateBiome.Cold;
            }

            return temperature >= 1f - coldThreshold
                ? ClimateBiome.Warm
                : ClimateBiome.Temperate;
        }

        internal static TerrainBiome ResolveTerrain(
            ClimateBiome climate,
            float altitude)
        {
            if (altitude >= 0.72f)
            {
                return TerrainBiome.Mountain;
            }

            return climate switch
            {
                ClimateBiome.Cold => TerrainBiome.Snow,
                ClimateBiome.Warm => TerrainBiome.Desert,
                _ => TerrainBiome.Field
            };
        }

        internal static WaterBiome ResolveWater(WaterType waterType) =>
            waterType switch
            {
                WaterType.Pond => WaterBiome.Pond,
                WaterType.Lake => WaterBiome.Lake,
                WaterType.Sea => WaterBiome.Sea,
                WaterType.River => WaterBiome.River,
                _ => WaterBiome.None
            };
    }

    internal static class WorldChunkGenerator
    {
        public static void Generate(WorldData world, ChunkCoordinate coordinate)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsChunkWithinBounds(coordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            }

            if (world.IsChunkLoaded(coordinate))
            {
                return;
            }

            Apply(world, Build(world.Settings, coordinate));
        }

        public static WorldChunkBuildData Build(
            WorldSettingsData settings,
            ChunkCoordinate coordinate)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return Build(settings, coordinate, new WorldFieldSampler(settings));
        }

        internal static WorldChunkBuildData Build(
            WorldSettingsData settings,
            ChunkCoordinate coordinate,
            WorldFieldSampler sampler)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            lock (sampler)
            {
                return BuildLocked(settings, coordinate, sampler);
            }
        }

        private static WorldChunkBuildData BuildLocked(
            WorldSettingsData settings,
            ChunkCoordinate coordinate,
            WorldFieldSampler sampler)
        {
            var columns = new WorldFieldColumn[
                settings.ChunkCellCountXZ * settings.ChunkCellCountXZ];
            var startX = checked(coordinate.X * settings.ChunkCellCountXZ);
            var startZ = checked(coordinate.Z * settings.ChunkCellCountXZ);
            for (var localZ = 0; localZ < settings.ChunkCellCountXZ; localZ++)
            for (var localX = 0; localX < settings.ChunkCellCountXZ; localX++)
            {
                var x = startX + localX;
                var z = startZ + localZ;
                columns[localX + settings.ChunkCellCountXZ * localZ] =
                    sampler.Sample(x, z);
            }

            return new WorldChunkBuildData(coordinate, columns);
        }

        public static void Apply(WorldData world, WorldChunkBuildData build)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (!world.IsChunkWithinBounds(build.Coordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(build));
            }

            if (world.IsChunkLoaded(build.Coordinate))
            {
                return;
            }

            var expectedColumnCount = world.ChunkSizeX * world.ChunkSizeZ;
            if (build.Columns.Length != expectedColumnCount)
            {
                throw new InvalidOperationException(
                    "Chunk build data does not match the world chunk size.");
            }

            world.EnsureChunkLoaded(build.Coordinate);
            var startX = checked(build.Coordinate.X * world.ChunkSizeX);
            var startZ = checked(build.Coordinate.Z * world.ChunkSizeZ);
            for (var localZ = 0; localZ < world.ChunkSizeZ; localZ++)
            for (var localX = 0; localX < world.ChunkSizeX; localX++)
            {
                var x = startX + localX;
                var z = startZ + localZ;
                var column = build.Columns[localX + world.ChunkSizeX * localZ];
                WorldDataBuilder.WriteColumn(
                    world,
                    x,
                    z,
                    column.SolidHeight,
                    column.TopSurface,
                    column.WaterSurface,
                    column.WaterRole,
                    column.WaterType,
                    column.Biome);
            }
        }
    }

    internal sealed class WorldChunkBuildData
    {
        public WorldChunkBuildData(
            ChunkCoordinate coordinate,
            WorldFieldColumn[] columns)
        {
            Coordinate = coordinate;
            Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        }

        public ChunkCoordinate Coordinate { get; }
        public WorldFieldColumn[] Columns { get; }
    }
}
