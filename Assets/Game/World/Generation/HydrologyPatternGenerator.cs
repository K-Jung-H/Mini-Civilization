using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal enum HydrologyEndpointKind : byte
    {
        Natural,
        Pond,
        Lake,
        Sea
    }

    internal enum HydrologyEndpointRole : byte
    {
        Head,
        End
    }

    internal readonly struct HydrologyEndpoint
    {
        public HydrologyEndpoint(
            int componentId,
            int worldX,
            int worldZ,
            int waterTopUnits,
            HydrologyEndpointKind kind,
            HydrologyEndpointRole role)
        {
            ComponentId = componentId;
            WorldX = worldX;
            WorldZ = worldZ;
            WaterTopUnits = waterTopUnits;
            Kind = kind;
            Role = role;
        }

        public int ComponentId { get; }
        public int WorldX { get; }
        public int WorldZ { get; }
        public int WaterTopUnits { get; }
        public HydrologyEndpointKind Kind { get; }
        public HydrologyEndpointRole Role { get; }
    }

    internal readonly struct HydrologyMapCell
    {
        public HydrologyMapCell(
            int componentId,
            WaterType waterType,
            float membership,
            float interiorProgress,
            float targetBedUnits,
            int waterTopUnits,
            bool hasWater,
            float riverInfluence = 0f,
            float riverDepthUnits = 0f)
        {
            ComponentId = componentId;
            WaterType = waterType;
            Membership = Math.Clamp(membership, 0f, 1f);
            InteriorProgress = Math.Clamp(interiorProgress, 0f, 1f);
            TargetBedUnits = targetBedUnits;
            WaterTopUnits = waterTopUnits;
            HasWater = hasWater;
            RiverInfluence = Math.Clamp(riverInfluence, 0f, 1f);
            RiverDepthUnits = Math.Max(0f, riverDepthUnits);
        }

        public int ComponentId { get; }
        public WaterType WaterType { get; }
        public float Membership { get; }
        public float InteriorProgress { get; }
        public float TargetBedUnits { get; }
        public int WaterTopUnits { get; }
        public float RiverInfluence { get; }
        public float RiverDepthUnits { get; }
        public bool HasTerrainTarget => Membership > 0f;
        public bool HasWater { get; }
    }

    internal static class HydrologyPatternResolver
    {
        public static WorldPatternResult Resolve(
            int worldX,
            int worldZ,
            WorldSettingsData settings,
            HydrologyBatch hydrologyBatch,
            in WorldPatternResult terrain)
        {
            if (hydrologyBatch == null)
            {
                throw new ArgumentNullException(nameof(hydrologyBatch));
            }

            var hydrology = hydrologyBatch.Sample(worldX, worldZ);
            return Compose(settings, terrain, hydrology);
        }

        public static WorldPatternResult Resolve(
            WorldSettingsData settings,
            in HydrologyCellPlan hydrology,
            in WorldPatternResult terrain) => Compose(settings, terrain, hydrology);

        private static WorldPatternResult Compose(
            WorldSettingsData settings,
            in WorldPatternResult terrain,
            in HydrologyCellPlan hydrology)
        {
            if (hydrology.Membership <= 0f)
            {
                return terrain;
            }

            var hydrologyType = hydrology.WaterType != WaterType.None
                ? hydrology.WaterType
                : hydrology.BasinComponent.IsValid
                    ? hydrology.BasinComponent.Type
                    : WaterType.None;
            var riverDepth = hydrology.WaterType == WaterType.River
                ? Math.Max(0f,
                    hydrology.WaterTopUnits
                    - hydrology.TargetTerrainSurfaceUnits)
                : 0f;

            return new WorldPatternResult(
                hydrology.TargetTerrainSurfaceUnits
                    - settings.TerrainBaseHeightUnits,
                terrain.VerticalFactor,
                0f,
                terrain.DominantPattern,
                terrain.RegionKey,
                terrain.InteriorProgress,
                terrain.PatternDepthUnits,
                terrain.PatternDepthProgress,
                terrain.PatternDetailUnits,
                hydrology.HasWater ? hydrology.WaterTopUnits : 0,
                hydrology.HasWater ? hydrology.WaterType : WaterType.None,
                hydrology.WaterType == WaterType.River
                    ? hydrology.Membership
                    : 0f,
                riverDepth,
                HydrologyPlanIdentity.ToDebugComponentId(hydrology),
                hydrology.Membership,
                hydrology.InteriorProgress,
                hydrologyType);
        }
    }

    internal static class LegacyHydrologyMap
    {
        public static LegacyHydrologyBatch CreateBatch(
            HydrologyGenerationContext context,
            int originX,
            int originZ,
            int width,
            int height) => new(
                context,
                originX,
                originZ,
                width,
                height);
    }

    internal sealed class LegacyHydrologyBatch : IDisposable
    {
        private readonly HydrologyGenerationContext context;
        private readonly IDisposable terrainLease;
        private readonly HydrologyRegionPlanner.RegionPlanLease planLease;
        private readonly RiverHydrologyPlanner.RiverEdgeLease riverLease;
        private readonly HydrologyMapCell[] cells;
        private bool disposed;

        public LegacyHydrologyBatch(
            HydrologyGenerationContext context,
            int originX,
            int originZ,
            int width,
            int height)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            this.context = context;
            OriginX = originX;
            OriginZ = originZ;
            Width = width;
            Height = height;
            terrainLease = context.BeginTerrainLease();
            try
            {
                planLease = HydrologyRegionPlanner.AcquireForBatch(
                    context,
                    originX,
                    originZ,
                    width,
                    height);
                riverLease = RiverHydrologyPlanner.AcquireForBatch(
                    context,
                    originX,
                    originZ,
                    width,
                    height);
                cells = RasterizeBaseCells();
                RiverHydrologyPlanner.RasterizeBatch(
                    context,
                    originX,
                    originZ,
                    width,
                    height,
                    cells,
                    riverLease);
            }
            catch
            {
                riverLease?.Dispose();
                planLease?.Dispose();
                terrainLease?.Dispose();
                throw;
            }
        }

        public int OriginX { get; }
        public int OriginZ { get; }
        public int Width { get; }
        public int Height { get; }

        public HydrologyTerrainSample SampleBaseTerrainState(
            int worldX,
            int worldZ)
        {
            EnsureNotDisposed();
            return context.SampleBaseTerrainState(worldX, worldZ);
        }

        public TerrainSurfaceSample SampleBaseTerrain(
            int worldX,
            int worldZ) => SampleBaseTerrainState(worldX, worldZ).Surface;

        public HydrologyMapCell Sample(int worldX, int worldZ)
        {
            EnsureNotDisposed();
            var localX = worldX - OriginX;
            var localZ = worldZ - OriginZ;
            if ((uint)localX >= Width || (uint)localZ >= Height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldX),
                    "Hydrology cell is outside this Batch.");
            }

            return cells[localX + Width * localZ];
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            riverLease.Dispose();
            planLease.Dispose();
            terrainLease.Dispose();
        }

        private HydrologyMapCell[] RasterizeBaseCells()
        {
            var result = new HydrologyMapCell[checked(Width * Height)];
            for (var localZ = 0; localZ < Height; localZ++)
            for (var localX = 0; localX < Width; localX++)
            {
                var worldX = checked(OriginX + localX);
                var worldZ = checked(OriginZ + localZ);
                var terrain = context.SampleBaseTerrainState(worldX, worldZ);
                if (terrain.Terrain.WaterType == WaterType.Sea)
                {
                    result[localX + Width * localZ] = new HydrologyMapCell(
                        terrain.Terrain.RegionKey,
                        WaterType.Sea,
                        1f,
                        terrain.Terrain.PatternDepthProgress,
                        terrain.Surface.SurfaceUnits,
                        terrain.Terrain.WaterTopUnits,
                        true);
                    continue;
                }

                if (HydrologyRegionPlanner.TryGetBasinCell(
                        context,
                        worldX,
                        worldZ,
                        out var basin))
                {
                    result[localX + Width * localZ] = basin;
                }
            }

            return result;
        }

        private void EnsureNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(LegacyHydrologyBatch));
            }
        }
    }

    internal static class HydrologyRegionPlanner
    {
        private static readonly ConditionalWeakTable<
            HydrologyGenerationContext,
            RegionPlanCache> Caches = new();

        public static HydrologyMapCell SampleBase(
            HydrologyGenerationContext context,
            int worldX,
            int worldZ)
        {
            var settings = context.Settings;
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            return GetPlanOrTransient(
                context,
                FloorDivide(worldX, size),
                FloorDivide(worldZ, size)).TryGetBasinCell(
                    worldX,
                    worldZ,
                    out var cell)
                ? cell
                : default;
        }

        public static bool TryGetBasinCell(
            HydrologyGenerationContext context,
            int worldX,
            int worldZ,
            out HydrologyMapCell cell)
        {
            var settings = context.Settings;
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            return GetPlanOrTransient(
                context,
                FloorDivide(worldX, size),
                FloorDivide(worldZ, size)).TryGetBasinCell(
                    worldX,
                    worldZ,
                    out cell);
        }

        public static IReadOnlyList<HydrologyEndpoint> GetEndpoints(
            HydrologyGenerationContext context,
            int regionX,
            int regionZ) => GetPlanOrTransient(
                context,
                regionX,
                regionZ).Endpoints;

        public static bool IsBasinReserved(
            HydrologyGenerationContext context,
            int worldX,
            int worldZ,
            int allowedComponentIdA = 0,
            int allowedComponentIdB = 0)
        {
            var cell = SampleBase(context, worldX, worldZ);
            return cell.WaterType is WaterType.Lake or WaterType.Pond
                && cell.ComponentId != allowedComponentIdA
                && cell.ComponentId != allowedComponentIdB;
        }

        internal static RegionPlanLease AcquireForBatch(
            HydrologyGenerationContext context,
            int originX,
            int originZ,
            int width,
            int height)
        {
            var settings = context.Settings;
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            var halo = GetTopologyDependencyHaloCells(settings);
            var minimumRegionX = FloorDivide(originX - halo, size);
            var maximumRegionX = FloorDivide(
                checked(originX + width - 1 + halo),
                size);
            var minimumRegionZ = FloorDivide(originZ - halo, size);
            var maximumRegionZ = FloorDivide(
                checked(originZ + height - 1 + halo),
                size);
            var cache = Caches.GetValue(context, _ => new RegionPlanCache());
            var entries = new List<RegionPlanEntry>();
            lock (cache.Gate)
            {
                for (var regionZ = minimumRegionZ;
                     regionZ <= maximumRegionZ;
                     regionZ++)
                for (var regionX = minimumRegionX;
                     regionX <= maximumRegionX;
                     regionX++)
                {
                    var targetRegionX = regionX;
                    var targetRegionZ = regionZ;
                    var key = CoordinateKey(targetRegionX, targetRegionZ);
                    var entry = cache.Plans.GetOrAdd(
                        key,
                        _ => new RegionPlanEntry(
                            key,
                            () => BuildPlan(
                                context,
                                targetRegionX,
                                targetRegionZ)));
                    entry.LeaseCount++;
                    entries.Add(entry);
                }
            }

            try
            {
                for (var index = 0; index < entries.Count; index++)
                {
                    _ = entries[index].Plan.Value;
                }
                return new RegionPlanLease(cache, entries);
            }
            catch
            {
                Release(cache, entries);
                throw;
            }
        }

        internal static int GetTopologyDependencyHaloCells(
            WorldSettingsData settings)
        {
            var network = settings.Hydrology.RiverNetwork;
            var corridor = settings.Hydrology.RiverCorridor;
            var routeReach = network.LengthCells.Maximum
                + corridor.WidthCells.Maximum * 0.5f;
            var endpointReach = routeReach * 2f;
            return checked((int)Math.Ceiling(
                routeReach + endpointReach));
        }

        private static HydrologyRegionPlan GetPlanOrTransient(
            HydrologyGenerationContext context,
            int regionX,
            int regionZ)
        {
            var cache = Caches.GetValue(context, _ => new RegionPlanCache());
            var key = CoordinateKey(regionX, regionZ);
            lock (cache.Gate)
            {
                if (cache.Plans.TryGetValue(key, out var cached))
                {
                    return cached.Plan.Value;
                }
            }

            return BuildPlan(context, regionX, regionZ);
        }

        private static HydrologyRegionPlan BuildPlan(
            HydrologyGenerationContext context,
            int regionX,
            int regionZ)
        {
            var plan = new HydrologyRegionPlan(context, regionX, regionZ);
            plan.BuildSeaEndpoints(context);
            BasinRegionBuilder.Rasterize(context, regionX, regionZ, plan);
            return plan;
        }

        private static int FloorDivide(int value, int divisor)
        {
            var quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }

        private static long CoordinateKey(int x, int z) =>
            ((long)x << 32) ^ (uint)z;

        private static void Release(
            RegionPlanCache cache,
            IReadOnlyList<RegionPlanEntry> entries)
        {
            lock (cache.Gate)
            {
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    entry.LeaseCount--;
                    if (entry.LeaseCount == 0)
                    {
                        cache.Plans.TryRemove(entry.Key, out _);
                    }
                }
            }
        }

        internal sealed class RegionPlanLease : IDisposable
        {
            private RegionPlanCache cache;
            private List<RegionPlanEntry> entries;

            public RegionPlanLease(
                RegionPlanCache cache,
                List<RegionPlanEntry> entries)
            {
                this.cache = cache;
                this.entries = entries;
            }

            public void Dispose()
            {
                if (cache == null)
                {
                    return;
                }

                Release(cache, entries);
                cache = null;
                entries = null;
            }
        }

        internal sealed class RegionPlanCache
        {
            public object Gate { get; } = new();
            public ConcurrentDictionary<long, RegionPlanEntry> Plans { get; }
                = new();
        }

        internal sealed class RegionPlanEntry
        {
            public RegionPlanEntry(
                long key,
                Func<HydrologyRegionPlan> create)
            {
                Key = key;
                Plan = new Lazy<HydrologyRegionPlan>(create, true);
            }

            public Lazy<HydrologyRegionPlan> Plan { get; }
            public int LeaseCount;
            public long Key { get; }
        }
    }

    internal sealed class HydrologyRegionPlan
    {
        private const int SeaEndpointChannel = 7180;

        private readonly Dictionary<int, HydrologyMapCell> basinCells = new();
        private readonly List<HydrologyEndpoint> endpoints = new();

        public HydrologyRegionPlan(
            HydrologyGenerationContext context,
            int regionX,
            int regionZ)
        {
            _ = context ?? throw new ArgumentNullException(nameof(context));
            Settings = context.Settings;
            RegionX = regionX;
            RegionZ = regionZ;
            Size = Settings.Hydrology.Map.PlanningRegionSizeCells;
            OriginX = checked(regionX * Size);
            OriginZ = checked(regionZ * Size);
        }

        public WorldSettingsData Settings { get; }
        public int RegionX { get; }
        public int RegionZ { get; }
        public int OriginX { get; }
        public int OriginZ { get; }
        public int Size { get; }
        public IReadOnlyList<HydrologyEndpoint> Endpoints => endpoints;
        public bool TryGetBasinCell(
            int worldX,
            int worldZ,
            out HydrologyMapCell cell) => basinCells.TryGetValue(
                ToIndex(worldX, worldZ),
                out cell);

        public void SetBasinCell(
            int worldX,
            int worldZ,
            in HydrologyMapCell cell)
        {
            basinCells[ToIndex(worldX, worldZ)] = cell;
        }

        public void SetBasinTransition(
            int worldX,
            int worldZ,
            in HydrologyMapCell cell)
        {
            var index = ToIndex(worldX, worldZ);
            if (basinCells.TryGetValue(index, out var current)
                && current.Membership >= cell.Membership)
            {
                return;
            }

            basinCells[index] = cell;
        }

        public void AddEndpoint(in HydrologyEndpoint endpoint) =>
            endpoints.Add(endpoint);

        private int ToIndex(int worldX, int worldZ)
        {
            var localX = worldX - OriginX;
            var localZ = worldZ - OriginZ;
            if ((uint)localX >= Size || (uint)localZ >= Size)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldX),
                    "Hydrology sample is outside the resolved Region.");
            }

            return localX + Size * localZ;
        }

        public void BuildSeaEndpoints(HydrologyGenerationContext context)
        {
            var spacing = Settings.Hydrology.Map.RouteSampleSpacingCells;
            var sampleCount = Size / spacing;
            var seed = unchecked((int)DeterministicNoise.Hash(
                SeaEndpointChannel,
                SeaEndpointChannel * 31L,
                Settings.Seed));
            for (var z = 0; z < sampleCount; z++)
            for (var x = 0; x < sampleCount; x++)
            {
                var worldX = checked(OriginX + x * spacing);
                var worldZ = checked(OriginZ + z * spacing);
                var center = context.SampleBaseTerrain(worldX, worldZ);
                if (!center.HasSeaWater)
                {
                    continue;
                }

                var coastal = !context.SampleBaseTerrain(
                        worldX - spacing,
                        worldZ).HasSeaWater
                    || !context.SampleBaseTerrain(
                        worldX + spacing,
                        worldZ).HasSeaWater
                    || !context.SampleBaseTerrain(
                        worldX,
                        worldZ - spacing).HasSeaWater
                    || !context.SampleBaseTerrain(
                        worldX,
                        worldZ + spacing).HasSeaWater;
                if (!coastal)
                {
                    continue;
                }

                AddEndpoint(new HydrologyEndpoint(
                    unchecked((int)DeterministicNoise.Hash(
                        worldX,
                        worldZ,
                        seed)),
                    worldX,
                    worldZ,
                    center.WaterTopUnits,
                    HydrologyEndpointKind.Sea,
                    HydrologyEndpointRole.End));
            }
        }

    }

    internal static class BasinRegionBuilder
    {
        private const int LakeActivationChannel = 8100;
        private const int PondActivationChannel = 8110;
        private const int SeedPositionChannel = 8120;
        private const int AreaChannel = 8130;
        private const int DepthChannel = 8140;
        private const int ConnectionChannel = 8150;
        private const int RoleChannel = 8160;
        private const int BedChannel = 8170;
        private const int BedAmplitudeChannel = 8180;
        private const int PotentialChannel = 8190;

        public static void Rasterize(
            HydrologyGenerationContext context,
            int regionX,
            int regionZ,
            HydrologyRegionPlan plan)
        {
            var settings = context.Settings;
            var map = settings.Hydrology.Map;
            var basins = settings.Hydrology.Basins;
            var regionSize = map.PlanningRegionSizeCells;
            var originX = checked(regionX * regionSize);
            var originZ = checked(regionZ * regionSize);
            if (plan.OriginX != originX || plan.OriginZ != originZ
                || plan.Size != regionSize)
            {
                throw new InvalidOperationException(
                    "Hydrology Region plan coordinates do not match Basin rasterization.");
            }

            var halo = checked(
                basins.MaximumReachCells + basins.MinimumSeparationCells
                + basins.ShoreTransitionCells);
            var gridSize = checked(regionSize + halo * 2);
            var gridOriginX = checked(originX - halo);
            var gridOriginZ = checked(originZ - halo);
            var count = checked(gridSize * gridSize);
            var surface = new float[count];
            var sea = new bool[count];
            var potential = new float[count];
            var potentialSeed = Seed(settings.Seed, PotentialChannel);

            for (var z = 0; z < gridSize; z++)
            for (var x = 0; x < gridSize; x++)
            {
                var worldX = gridOriginX + x;
                var worldZ = gridOriginZ + z;
                var terrain = context.SampleBaseTerrain(
                    worldX,
                    worldZ);
                var index = x + gridSize * z;
                surface[index] = terrain.SurfaceUnits;
                sea[index] = terrain.HasSeaWater;
                potential[index] = map.BasinPotentialResponse.Evaluate(
                    Sample01(
                        worldX,
                        worldZ,
                        map.BasinPotentialField,
                        potentialSeed));
            }

            var candidates = BuildCandidates(
                settings,
                gridOriginX,
                gridOriginZ,
                gridSize,
                surface,
                sea);
            if (candidates.Count == 0)
            {
                return;
            }

            var owner = new int[count];
            var distance = new float[count];
            Array.Fill(owner, -1);
            Array.Fill(distance, float.PositiveInfinity);
            BuildGeodesicTerritories(
                candidates,
                owner,
                distance,
                surface,
                sea,
                potential,
                gridSize,
                basins.MaximumReachCells,
                map);

            var footprints = BuildFootprints(
                candidates,
                owner,
                distance,
                gridSize);
            ResolveFootprintConflicts(
                candidates,
                footprints,
                gridSize,
                basins.MinimumSeparationCells);

            for (var candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                var candidate = candidates[candidateIndex];
                var footprint = footprints[candidateIndex];
                if (!candidate.Active
                    || footprint.Count < candidate.TargetAreaCells)
                {
                    continue;
                }

                BuildBasinComponent(
                    settings,
                    candidate,
                    footprint,
                    surface,
                    gridOriginX,
                    gridOriginZ,
                    gridSize,
                    originX,
                    originZ,
                    regionSize,
                    plan);
            }
        }

        private static List<BasinCandidate> BuildCandidates(
            WorldSettingsData settings,
            int gridOriginX,
            int gridOriginZ,
            int gridSize,
            float[] surface,
            bool[] sea)
        {
            var result = new List<BasinCandidate>();
            var map = settings.Hydrology.Map;
            var basins = settings.Hydrology.Basins;
            var spacing = map.BasinSeedSpacingCells;
            var minimumSeedX = FloorDivide(gridOriginX, spacing) - 1;
            var maximumSeedX = FloorDivide(gridOriginX + gridSize - 1, spacing) + 1;
            var minimumSeedZ = FloorDivide(gridOriginZ, spacing) - 1;
            var maximumSeedZ = FloorDivide(gridOriginZ + gridSize - 1, spacing) + 1;

            for (var seedZ = minimumSeedZ; seedZ <= maximumSeedZ; seedZ++)
            for (var seedX = minimumSeedX; seedX <= maximumSeedX; seedX++)
            {
                TryAddCandidate(
                    result,
                    settings,
                    basins.Lake,
                    WaterType.Lake,
                    seedX,
                    seedZ,
                    LakeActivationChannel,
                    gridOriginX,
                    gridOriginZ,
                    gridSize,
                    spacing,
                    surface,
                    sea);
                TryAddCandidate(
                    result,
                    settings,
                    basins.Pond,
                    WaterType.Pond,
                    seedX,
                    seedZ,
                    PondActivationChannel,
                    gridOriginX,
                    gridOriginZ,
                    gridSize,
                    spacing,
                    surface,
                    sea);
            }

            return result;
        }

        private static void TryAddCandidate(
            List<BasinCandidate> candidates,
            WorldSettingsData settings,
            in BasinProfileSettingsData profile,
            WaterType type,
            int seedX,
            int seedZ,
            int channel,
            int gridOriginX,
            int gridOriginZ,
            int gridSize,
            int spacing,
            float[] surface,
            bool[] sea)
        {
            if (DeterministicNoise.Value01(
                    seedX,
                    seedZ,
                    Seed(settings.Seed, channel)) >= profile.Occurrence)
            {
                return;
            }

            var positionSeed = Seed(settings.Seed, SeedPositionChannel + channel);
            var worldX = checked(seedX * spacing + Math.Min(
                spacing - 1,
                (int)(DeterministicNoise.Value01(seedX, seedZ, positionSeed)
                    * spacing)));
            var worldZ = checked(seedZ * spacing + Math.Min(
                spacing - 1,
                (int)(DeterministicNoise.Value01(seedZ, seedX, positionSeed)
                    * spacing)));
            var localX = worldX - gridOriginX;
            var localZ = worldZ - gridOriginZ;
            if ((uint)localX >= gridSize || (uint)localZ >= gridSize
                || sea[localX + gridSize * localZ])
            {
                return;
            }

            var componentId = unchecked((int)DeterministicNoise.Hash(
                worldX,
                worldZ,
                Seed(settings.Seed, channel + 1)));
            var area = ResolveRange(
                profile.AreaCells,
                DeterministicNoise.Value01(
                    worldX,
                    worldZ,
                    Seed(settings.Seed, AreaChannel + channel)));
            var depth = ResolveRange(
                profile.MaximumDepthUnits,
                DeterministicNoise.Value01(
                    worldX,
                    worldZ,
                    Seed(settings.Seed, DepthChannel + channel)));
            candidates.Add(new BasinCandidate(
                componentId,
                worldX,
                worldZ,
                localX,
                localZ,
                type,
                Math.Max(1, (int)Math.Round(
                    area,
                    MidpointRounding.AwayFromZero)),
                depth,
                profile,
                surface[localX + gridSize * localZ],
                DeterministicNoise.Value01(
                    worldX,
                    worldZ,
                    Seed(settings.Seed, channel + 2))));
        }

        private static void BuildGeodesicTerritories(
            List<BasinCandidate> candidates,
            int[] owner,
            float[] distance,
            float[] surface,
            bool[] sea,
            float[] potential,
            int gridSize,
            int maximumReachCells,
            in HydrologyMapSettingsData settings)
        {
            var frontier = new TerritoryHeap();
            for (var candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                var candidate = candidates[candidateIndex];
                var index = candidate.LocalX + gridSize * candidate.LocalZ;
                owner[index] = candidateIndex;
                distance[index] = 0f;
                frontier.Push(index, candidateIndex, 0f);
            }

            ReadOnlySpan<int> neighborX = stackalloc int[]
                { -1, 0, 1, -1, 1, -1, 0, 1 };
            ReadOnlySpan<int> neighborZ = stackalloc int[]
                { -1, -1, -1, 0, 0, 1, 1, 1 };
            while (frontier.Count > 0)
            {
                var current = frontier.Pop();
                if (current.Cost > distance[current.Cell]
                    || owner[current.Cell] != current.Owner)
                {
                    continue;
                }

                var x = current.Cell % gridSize;
                var z = current.Cell / gridSize;
                for (var direction = 0;
                     direction < neighborX.Length;
                     direction++)
                {
                    var nextX = x + neighborX[direction];
                    var nextZ = z + neighborZ[direction];
                    if ((uint)nextX >= gridSize || (uint)nextZ >= gridSize)
                    {
                        continue;
                    }

                    var next = nextX + gridSize * nextZ;
                    if (sea[next])
                    {
                        continue;
                    }

                    var candidate = candidates[current.Owner];
                    if (Math.Max(
                        Math.Abs(nextX - candidate.LocalX),
                        Math.Abs(nextZ - candidate.LocalZ))
                        > maximumReachCells)
                    {
                        continue;
                    }

                    var movement = neighborX[direction] != 0
                        && neighborZ[direction] != 0
                        ? 1.41421356f
                        : 1f;
                    var terrainDelta = MathF.Abs(
                            surface[next] - surface[current.Cell])
                        / WorldGrid.HeightStepsPerCell;
                    var slope = terrainDelta / movement;
                    var nextCost = current.Cost + movement * (
                        1f
                        + potential[next] * settings.BasinPotentialCost
                        + terrainDelta * settings.TerrainDeformationCost
                        + slope * settings.SlopeCost);
                    if (nextCost > distance[next]
                        || nextCost == distance[next]
                        && candidates[current.Owner].ComponentId
                            >= candidates[owner[next]].ComponentId)
                    {
                        continue;
                    }

                    owner[next] = current.Owner;
                    distance[next] = nextCost;
                    frontier.Push(next, current.Owner, nextCost);
                }
            }
        }

        private static List<int>[] BuildFootprints(
            List<BasinCandidate> candidates,
            int[] owner,
            float[] distance,
            int gridSize)
        {
            var territories = new List<int>[candidates.Count];
            for (var index = 0; index < territories.Length; index++)
            {
                territories[index] = new List<int>();
            }

            for (var cell = 0; cell < owner.Length; cell++)
            {
                if (owner[cell] >= 0)
                {
                    territories[owner[cell]].Add(cell);
                }
            }

            for (var index = 0; index < territories.Length; index++)
            {
                territories[index].Sort((left, right) =>
                {
                    var order = distance[left].CompareTo(distance[right]);
                    return order != 0 ? order : left.CompareTo(right);
                });
                var area = candidates[index].TargetAreaCells;
                if (territories[index].Count > area)
                {
                    territories[index].RemoveRange(
                        area,
                        territories[index].Count - area);
                }
            }

            return territories;
        }

        private static void ResolveFootprintConflicts(
            List<BasinCandidate> candidates,
            List<int>[] footprints,
            int gridSize,
            int clearanceCells)
        {
            if (clearanceCells <= 0)
            {
                return;
            }

            for (var left = 0; left < footprints.Length; left++)
            for (var right = left + 1; right < footprints.Length; right++)
            {
                if (!candidates[left].Active || !candidates[right].Active
                    || !AreWithinClearance(
                        footprints[left],
                        footprints[right],
                        gridSize,
                        clearanceCells))
                {
                    continue;
                }

                if (candidates[left].Priority > candidates[right].Priority
                    || candidates[left].Priority == candidates[right].Priority
                    && candidates[left].ComponentId
                        > candidates[right].ComponentId)
                {
                    candidates[right].Active = false;
                }
                else
                {
                    candidates[left].Active = false;
                }
            }
        }

        private static bool AreWithinClearance(
            IReadOnlyList<int> left,
            IReadOnlyList<int> right,
            int gridSize,
            int clearanceCells)
        {
            var rightCells = new HashSet<int>(right);
            for (var index = 0; index < left.Count; index++)
            {
                var x = left[index] % gridSize;
                var z = left[index] / gridSize;
                for (var offsetZ = -clearanceCells;
                     offsetZ <= clearanceCells;
                     offsetZ++)
                for (var offsetX = -clearanceCells;
                     offsetX <= clearanceCells;
                     offsetX++)
                {
                    if (Math.Max(Math.Abs(offsetX), Math.Abs(offsetZ))
                        > clearanceCells)
                    {
                        continue;
                    }

                    var testX = x + offsetX;
                    var testZ = z + offsetZ;
                    if ((uint)testX < gridSize && (uint)testZ < gridSize
                        && rightCells.Contains(testX + gridSize * testZ))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void BuildBasinComponent(
            WorldSettingsData settings,
            BasinCandidate candidate,
            IReadOnlyList<int> footprint,
            float[] surface,
            int gridOriginX,
            int gridOriginZ,
            int gridSize,
            int regionOriginX,
            int regionOriginZ,
            int regionSize,
            HydrologyRegionPlan plan)
        {
            var footprintSet = new HashSet<int>(footprint);
            var boundary = new List<int>();
            for (var index = 0; index < footprint.Count; index++)
            {
                var cell = footprint[index];
                var x = cell % gridSize;
                var z = cell / gridSize;
                if (x == 0 || z == 0 || x == gridSize - 1 || z == gridSize - 1
                    || !footprintSet.Contains(cell - 1)
                    || !footprintSet.Contains(cell + 1)
                    || !footprintSet.Contains(cell - gridSize)
                    || !footprintSet.Contains(cell + gridSize))
                {
                    boundary.Add(cell);
                }
            }

            var waterTop = SelectWaterTop(
                footprint,
                boundary,
                surface,
                settings.Hydrology.Basins);
            var interiorDistance = BuildInteriorDistance(
                footprint,
                boundary,
                footprintSet,
                gridSize,
                out var maximumDistance);
            var basinSettings = settings.Hydrology.Basins;
            var bedSeed = Seed(settings.Seed, BedChannel);
            var bedAmplitude = ResolveRange(
                basinSettings.BedAmplitudeUnits,
                DeterministicNoise.Value01(
                    candidate.WorldX,
                    candidate.WorldZ,
                    Seed(settings.Seed, BedAmplitudeChannel)));
            var connectionPort = -1;
            var connectionPortDistance = int.MaxValue;
            var connectionPortCost = float.PositiveInfinity;

            for (var index = 0; index < footprint.Count; index++)
            {
                var cell = footprint[index];
                var localX = cell % gridSize;
                var localZ = cell / gridSize;
                var worldX = gridOriginX + localX;
                var worldZ = gridOriginZ + localZ;
                var progress = maximumDistance > 0
                    ? interiorDistance[cell] / (float)maximumDistance
                    : 1f;
                var depthProgress = basinSettings.DepthByInterior.Evaluate(progress);
                var bedDetail = SampleSigned(
                        worldX,
                        worldZ,
                        basinSettings.BedField,
                        bedSeed)
                    * bedAmplitude * depthProgress;
                var targetBed = waterTop
                    - candidate.MaximumDepthUnits * depthProgress
                    + bedDetail;
                var portDistance = interiorDistance[cell];
                var portCost = MathF.Abs(surface[cell] - waterTop);
                if (targetBed < waterTop - 0.5f
                    && (portDistance < connectionPortDistance
                        || portDistance == connectionPortDistance
                        && (portCost < connectionPortCost
                            || portCost == connectionPortCost
                            && cell < connectionPort)))
                {
                    connectionPort = cell;
                    connectionPortDistance = portDistance;
                    connectionPortCost = portCost;
                }

                if (!InsideRegion(
                        worldX,
                        worldZ,
                        regionOriginX,
                        regionOriginZ,
                        regionSize))
                {
                    continue;
                }

                plan.SetBasinCell(worldX, worldZ, new HydrologyMapCell(
                    candidate.ComponentId,
                    candidate.Type,
                    1f,
                    progress,
                    targetBed,
                    waterTop,
                    true));
            }

            BuildShoreTransition(
                candidate,
                footprintSet,
                boundary,
                surface,
                waterTop,
                gridOriginX,
                gridOriginZ,
                gridSize,
                regionOriginX,
                regionOriginZ,
                regionSize,
                basinSettings,
                plan);

            if (DeterministicNoise.Value01(
                    candidate.WorldX,
                    candidate.WorldZ,
                    Seed(settings.Seed, ConnectionChannel))
                >= candidate.Profile.RiverConnectionChance)
            {
                return;
            }

            if (connectionPort < 0)
            {
                return;
            }

            var port = connectionPort;
            var portX = gridOriginX + port % gridSize;
            var portZ = gridOriginZ + port / gridSize;
            if (!InsideRegion(
                    portX,
                    portZ,
                    regionOriginX,
                    regionOriginZ,
                    regionSize))
            {
                return;
            }

            var role = DeterministicNoise.Value01(
                    candidate.WorldX,
                    candidate.WorldZ,
                    Seed(settings.Seed, RoleChannel))
                < candidate.Profile.HeadRoleChance
                    ? HydrologyEndpointRole.Head
                    : HydrologyEndpointRole.End;
            plan.AddEndpoint(new HydrologyEndpoint(
                candidate.ComponentId,
                portX,
                portZ,
                waterTop,
                candidate.Type == WaterType.Lake
                    ? HydrologyEndpointKind.Lake
                    : HydrologyEndpointKind.Pond,
                role));
        }

        private static int SelectWaterTop(
            IReadOnlyList<int> footprint,
            IReadOnlyList<int> boundary,
            float[] surface,
            in BasinPatternSettingsData settings)
        {
            var minimum = int.MaxValue;
            var maximum = int.MinValue;
            for (var index = 0; index < footprint.Count; index++)
            {
                var units = (int)Math.Round(
                    surface[footprint[index]],
                    MidpointRounding.AwayFromZero);
                minimum = Math.Min(minimum, units);
                maximum = Math.Max(maximum, units);
            }

            var boundarySet = new HashSet<int>(boundary);
            var bestUnits = minimum;
            var bestCost = float.PositiveInfinity;
            for (var candidate = minimum; candidate <= maximum; candidate++)
            {
                var cost = 0f;
                for (var index = 0; index < footprint.Count; index++)
                {
                    var cell = footprint[index];
                    var delta = surface[cell] - candidate;
                    cost += delta >= 0f
                        ? delta * settings.CutCost
                        : -delta * settings.FillCost;
                    if (boundarySet.Contains(cell))
                    {
                        cost += MathF.Abs(delta) * settings.RimCost;
                    }
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestUnits = candidate;
                }
            }

            return bestUnits;
        }

        private static Dictionary<int, int> BuildInteriorDistance(
            IReadOnlyList<int> footprint,
            IReadOnlyList<int> boundary,
            HashSet<int> footprintSet,
            int gridSize,
            out int maximumDistance)
        {
            var result = new Dictionary<int, int>(footprint.Count);
            var queue = new Queue<int>();
            for (var index = 0; index < boundary.Count; index++)
            {
                result[boundary[index]] = 0;
                queue.Enqueue(boundary[index]);
            }

            maximumDistance = 0;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var distance = result[current];
                ReadOnlySpan<int> neighbors = stackalloc int[]
                    { current - 1, current + 1,
                      current - gridSize, current + gridSize };
                for (var index = 0; index < neighbors.Length; index++)
                {
                    var next = neighbors[index];
                    if (!footprintSet.Contains(next) || result.ContainsKey(next))
                    {
                        continue;
                    }

                    result[next] = distance + 1;
                    maximumDistance = Math.Max(maximumDistance, distance + 1);
                    queue.Enqueue(next);
                }
            }

            return result;
        }

        private static void BuildShoreTransition(
            BasinCandidate candidate,
            HashSet<int> footprint,
            IReadOnlyList<int> boundary,
            float[] surface,
            int waterTop,
            int gridOriginX,
            int gridOriginZ,
            int gridSize,
            int regionOriginX,
            int regionOriginZ,
            int regionSize,
            in BasinPatternSettingsData settings,
            HydrologyRegionPlan plan)
        {
            var distance = new Dictionary<int, int>();
            var queue = new Queue<int>();
            for (var index = 0; index < boundary.Count; index++)
            {
                distance[boundary[index]] = 0;
                queue.Enqueue(boundary[index]);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentDistance = distance[current];
                if (currentDistance >= settings.ShoreTransitionCells)
                {
                    continue;
                }

                var x = current % gridSize;
                var z = current / gridSize;
                ReadOnlySpan<int> offsetX = stackalloc int[] { -1, 1, 0, 0 };
                ReadOnlySpan<int> offsetZ = stackalloc int[] { 0, 0, -1, 1 };
                for (var direction = 0; direction < offsetX.Length; direction++)
                {
                    var nextX = x + offsetX[direction];
                    var nextZ = z + offsetZ[direction];
                    if ((uint)nextX >= gridSize || (uint)nextZ >= gridSize)
                    {
                        continue;
                    }

                    var next = nextX + gridSize * nextZ;
                    if (footprint.Contains(next) || distance.ContainsKey(next))
                    {
                        continue;
                    }

                    var nextDistance = currentDistance + 1;
                    distance[next] = nextDistance;
                    queue.Enqueue(next);
                    var worldX = gridOriginX + nextX;
                    var worldZ = gridOriginZ + nextZ;
                    if (!InsideRegion(
                            worldX,
                            worldZ,
                            regionOriginX,
                            regionOriginZ,
                            regionSize))
                    {
                        continue;
                    }

                    var progress = 1f - nextDistance
                        / (float)settings.ShoreTransitionCells;
                    var membership = settings.ShoreTransition.Evaluate(progress);
                    var target = surface[next]
                        + (waterTop - surface[next]) * membership;
                    plan.SetBasinTransition(
                        worldX,
                        worldZ,
                        new HydrologyMapCell(
                            candidate.ComponentId,
                            candidate.Type,
                            membership,
                            0f,
                            target,
                            0,
                            false));
                }
            }
        }

        private static bool InsideRegion(
            int worldX,
            int worldZ,
            int originX,
            int originZ,
            int size) => worldX >= originX && worldX < originX + size
            && worldZ >= originZ && worldZ < originZ + size;

        private static float Sample01(
            int worldX,
            int worldZ,
            in WorldNoiseFieldSettingsData field,
            int seed)
        {
            var value = WorldNoiseFieldSampler.Sample2D(
                worldX,
                worldZ,
                field,
                seed);
            return field.Mode is WorldNoiseMode.Signed
                or WorldNoiseMode.SignedRidge
                    ? Math.Clamp((value + 1f) * 0.5f, 0f, 1f)
                    : Math.Clamp(value, 0f, 1f);
        }

        private static float SampleSigned(
            int worldX,
            int worldZ,
            in WorldNoiseFieldSettingsData field,
            int seed) => Sample01(worldX, worldZ, field, seed) * 2f - 1f;

        private static float ResolveRange(
            in WorldSeededRangeSettingsData range,
            float amount) => range.Minimum
                + (range.Maximum - range.Minimum) * amount;

        private static int Seed(int worldSeed, int channel) => unchecked(
            (int)DeterministicNoise.Hash(channel, channel * 31L, worldSeed));

        private static int FloorDivide(int value, int divisor)
        {
            var quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }

        private sealed class BasinCandidate
        {
            public BasinCandidate(
                int componentId,
                int worldX,
                int worldZ,
                int localX,
                int localZ,
                WaterType type,
                int targetAreaCells,
                float maximumDepthUnits,
                in BasinProfileSettingsData profile,
                float seedSurfaceUnits,
                float priority)
            {
                ComponentId = componentId;
                WorldX = worldX;
                WorldZ = worldZ;
                LocalX = localX;
                LocalZ = localZ;
                Type = type;
                TargetAreaCells = targetAreaCells;
                MaximumDepthUnits = maximumDepthUnits;
                Profile = profile;
                SeedSurfaceUnits = seedSurfaceUnits;
                Priority = priority;
                Active = true;
            }

            public int ComponentId { get; }
            public int WorldX { get; }
            public int WorldZ { get; }
            public int LocalX { get; }
            public int LocalZ { get; }
            public WaterType Type { get; }
            public int TargetAreaCells { get; }
            public float MaximumDepthUnits { get; }
            public BasinProfileSettingsData Profile { get; }
            public float SeedSurfaceUnits { get; }
            public float Priority { get; }
            public bool Active { get; set; }
        }

        private readonly struct TerritoryEntry
        {
            public TerritoryEntry(int cell, int owner, float cost)
            {
                Cell = cell;
                Owner = owner;
                Cost = cost;
            }

            public int Cell { get; }
            public int Owner { get; }
            public float Cost { get; }
        }

        private sealed class TerritoryHeap
        {
            private readonly List<TerritoryEntry> entries = new();
            public int Count => entries.Count;

            public void Push(int cell, int owner, float cost)
            {
                var entry = new TerritoryEntry(cell, owner, cost);
                entries.Add(entry);
                var index = entries.Count - 1;
                while (index > 0)
                {
                    var parent = (index - 1) / 2;
                    if (entries[parent].Cost <= cost) break;
                    entries[index] = entries[parent];
                    index = parent;
                }
                entries[index] = entry;
            }

            public TerritoryEntry Pop()
            {
                var root = entries[0];
                var last = entries[^1];
                entries.RemoveAt(entries.Count - 1);
                if (entries.Count == 0) return root;
                var index = 0;
                while (true)
                {
                    var left = index * 2 + 1;
                    if (left >= entries.Count) break;
                    var right = left + 1;
                    var child = right < entries.Count
                        && entries[right].Cost < entries[left].Cost
                            ? right : left;
                    if (entries[child].Cost >= last.Cost) break;
                    entries[index] = entries[child];
                    index = child;
                }
                entries[index] = last;
                return root;
            }
        }
    }
}
