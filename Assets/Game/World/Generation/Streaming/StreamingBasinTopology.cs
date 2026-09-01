using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Streaming
{
    internal readonly struct StreamingBasinComponentId :
        IEquatable<StreamingBasinComponentId>,
        IComparable<StreamingBasinComponentId>
    {
        public StreamingBasinComponentId(WaterType type, int seedGridX, int seedGridZ)
        {
            if (type is not (WaterType.Lake or WaterType.Pond))
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            Type = type;
            SeedGridX = seedGridX;
            SeedGridZ = seedGridZ;
        }

        public WaterType Type { get; }
        public int SeedGridX { get; }
        public int SeedGridZ { get; }
        public bool IsValid => Type is WaterType.Lake or WaterType.Pond;

        public int CompareTo(StreamingBasinComponentId other)
        {
            var type = Type.CompareTo(other.Type);
            if (type != 0)
            {
                return type;
            }

            var x = SeedGridX.CompareTo(other.SeedGridX);
            return x != 0 ? x : SeedGridZ.CompareTo(other.SeedGridZ);
        }

        public bool Equals(StreamingBasinComponentId other) => Type == other.Type
            && SeedGridX == other.SeedGridX
            && SeedGridZ == other.SeedGridZ;

        public override bool Equals(object obj) =>
            obj is StreamingBasinComponentId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            (byte)Type,
            SeedGridX,
            SeedGridZ);
    }

    internal readonly struct StreamingBasinCell
    {
        public StreamingBasinCell(int worldX, int worldZ, float interiorProgress)
        {
            WorldX = worldX;
            WorldZ = worldZ;
            InteriorProgress = Math.Clamp(interiorProgress, 0f, 1f);
        }

        public int WorldX { get; }
        public int WorldZ { get; }
        public float InteriorProgress { get; }
    }

    /// <summary>
    /// Coordinate-owned raw Basin geometry.  It contains no request Tile,
    /// cache lifetime, or consumer dependency.
    /// </summary>
    internal sealed class StreamingBasinComponent
    {
        private readonly ReadOnlyCollection<StreamingBasinCell> footprint;
        private readonly ReadOnlyCollection<StreamingBasinCell> boundary;
        private readonly Dictionary<StreamingCellKey, StreamingBasinCell>
            footprintByCell;

        public StreamingBasinComponent(
            StreamingBasinComponentId id,
            bool isCandidate,
            float priority,
            int seedWorldX,
            int seedWorldZ,
            float maximumDepthUnits,
            int waterTopUnits,
            float bedAmplitudeUnits,
            IList<StreamingBasinCell> footprint,
            IList<StreamingBasinCell> boundary)
        {
            Id = id;
            IsCandidate = isCandidate;
            Priority = priority;
            SeedWorldX = seedWorldX;
            SeedWorldZ = seedWorldZ;
            MaximumDepthUnits = maximumDepthUnits;
            WaterTopUnits = waterTopUnits;
            BedAmplitudeUnits = bedAmplitudeUnits;
            this.footprint = new ReadOnlyCollection<StreamingBasinCell>(
                footprint ?? throw new ArgumentNullException(nameof(footprint)));
            this.boundary = new ReadOnlyCollection<StreamingBasinCell>(
                boundary ?? throw new ArgumentNullException(nameof(boundary)));
            footprintByCell = new Dictionary<StreamingCellKey,
                StreamingBasinCell>(this.footprint.Count);
            MinimumX = int.MaxValue;
            MinimumZ = int.MaxValue;
            MaximumX = int.MinValue;
            MaximumZ = int.MinValue;
            for (var index = 0; index < this.footprint.Count; index++)
            {
                var cell = this.footprint[index];
                footprintByCell.Add(new StreamingCellKey(cell.WorldX, cell.WorldZ),
                    cell);
                MinimumX = Math.Min(MinimumX, cell.WorldX);
                MinimumZ = Math.Min(MinimumZ, cell.WorldZ);
                MaximumX = Math.Max(MaximumX, cell.WorldX);
                MaximumZ = Math.Max(MaximumZ, cell.WorldZ);
            }

            if (this.footprint.Count == 0)
            {
                MinimumX = MaximumX = seedWorldX;
                MinimumZ = MaximumZ = seedWorldZ;
            }
        }

        public StreamingBasinComponentId Id { get; }
        public bool IsCandidate { get; }
        public float Priority { get; }
        public int SeedWorldX { get; }
        public int SeedWorldZ { get; }
        public float MaximumDepthUnits { get; }
        public int WaterTopUnits { get; }
        public float BedAmplitudeUnits { get; }
        public int MinimumX { get; }
        public int MinimumZ { get; }
        public int MaximumX { get; }
        public int MaximumZ { get; }
        public IReadOnlyList<StreamingBasinCell> Footprint => footprint;
        public IReadOnlyList<StreamingBasinCell> Boundary => boundary;

        public bool Contains(int worldX, int worldZ) => footprintByCell.ContainsKey(
            new StreamingCellKey(worldX, worldZ));

        public bool TryGetCell(
            int worldX,
            int worldZ,
            out StreamingBasinCell cell) => footprintByCell.TryGetValue(
            new StreamingCellKey(worldX, worldZ), out cell);
    }

    internal readonly struct StreamingBasinCandidateSeed
    {
        public StreamingBasinCandidateSeed(
            StreamingBasinComponentId id,
            int seedWorldX,
            int seedWorldZ,
            float priority,
            bool passesOccurrence)
        {
            Id = id;
            SeedWorldX = seedWorldX;
            SeedWorldZ = seedWorldZ;
            Priority = priority;
            PassesOccurrence = passesOccurrence;
        }

        public StreamingBasinComponentId Id { get; }
        public int SeedWorldX { get; }
        public int SeedWorldZ { get; }
        public float Priority { get; }
        public bool PassesOccurrence { get; }
    }

    /// <summary>
    /// Explicit base-terrain facts needed by one candidate.  The facts are
    /// supplied by the planning request; the candidate evaluator never opens
    /// regions or starts another planning operation.
    /// </summary>
    internal sealed class StreamingBasinTerrainInput
    {
        private readonly StreamingBaseTerrainFact[] samples;

        public StreamingBasinTerrainInput(
            int originX,
            int originZ,
            int size,
            StreamingBaseTerrainFact[] samples)
        {
            if (size <= 0 || samples == null
                || samples.Length != checked(size * size))
            {
                throw new ArgumentOutOfRangeException(nameof(samples));
            }

            OriginX = originX;
            OriginZ = originZ;
            Size = size;
            this.samples = samples;
        }

        public int OriginX { get; }
        public int OriginZ { get; }
        public int Size { get; }
        public StreamingBaseTerrainFact this[int localX, int localZ] => samples[
            localX + Size * localZ];
        public StreamingBaseTerrainFact this[int index] => samples[index];
    }

    internal sealed class StreamingBasinFieldInput
    {
        private readonly Func<int, int, StreamingBaseTerrainFact> sampleBaseTerrain;
        private readonly Func<int, int, float> samplePotential;

        public StreamingBasinFieldInput(
            int originX,
            int originZ,
            int size,
            Func<int, int, StreamingBaseTerrainFact> sampleBaseTerrain,
            Func<int, int, float> samplePotential)
        {
            if (size <= 0 || sampleBaseTerrain == null || samplePotential == null)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            OriginX = originX;
            OriginZ = originZ;
            Size = size;
            this.sampleBaseTerrain = sampleBaseTerrain;
            this.samplePotential = samplePotential;
        }

        public int OriginX { get; }
        public int OriginZ { get; }
        public int Size { get; }

        public StreamingBaseTerrainFact GetBaseTerrain(int localX, int localZ) =>
            sampleBaseTerrain(checked(OriginX + localX), checked(OriginZ + localZ));

        public StreamingBaseTerrainFact GetBaseTerrain(int index) => GetBaseTerrain(
            index % Size,
            index / Size);

        public float GetPotential(int index) => samplePotential(
            checked(OriginX + index % Size),
            checked(OriginZ + index / Size));
    }

    /// <summary>
    /// Pure Basin candidate evaluator.  The input rectangle is an explicit
    /// fact prepared by the request planner, rather than a cached region.
    /// </summary>
    internal sealed class StreamingBasinCandidateEvaluator
    {
        private static readonly (int x, int z, float cost)[] growthNeighbors =
        {
            (-1, -1, 1.41421356f), (0, -1, 1f), (1, -1, 1.41421356f),
            (-1, 0, 1f),                       (1, 0, 1f),
            (-1, 1, 1.41421356f),  (0, 1, 1f),  (1, 1, 1.41421356f)
        };

        private static readonly (int x, int z)[] cardinalNeighbors =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1)
        };

        private readonly WorldSettingsData settings;
        public StreamingBasinCandidateEvaluator(
            WorldSettingsData settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(
                nameof(settings));
        }

        public StreamingBasinCandidateSeed Describe(
            StreamingBasinComponentId id)
        {
            var profile = GetProfile(id.Type);
            var typeName = GetTypeName(id.Type);
            var spacing = settings.Hydrology.Map.BasinSeedSpacingCells;
            var positionSeed = Seed($"Hydrology.Topology.Basin.{typeName}.Position");
            var seedWorldX = checked(id.SeedGridX * spacing + Math.Min(
                spacing - 1,
                (int)(DeterministicNoise.Value01(
                    id.SeedGridX,
                    id.SeedGridZ,
                    positionSeed) * spacing)));
            var seedWorldZ = checked(id.SeedGridZ * spacing + Math.Min(
                spacing - 1,
                (int)(DeterministicNoise.Value01(
                    id.SeedGridZ,
                    id.SeedGridX,
                    positionSeed) * spacing)));
            var priority = DeterministicNoise.Value01(
                id.SeedGridX,
                id.SeedGridZ,
                Seed($"Hydrology.Topology.Basin.{typeName}.Priority"));
            var passesOccurrence = DeterministicNoise.Value01(
                id.SeedGridX,
                id.SeedGridZ,
                Seed($"Hydrology.Topology.Basin.{typeName}.Activation"))
                < profile.Occurrence;
            return new StreamingBasinCandidateSeed(
                id,
                seedWorldX,
                seedWorldZ,
                priority,
                passesOccurrence);
        }

        public StreamingBasinComponent Build(
            in StreamingBasinCandidateSeed seed,
            StreamingBasinTerrainInput terrain)
        {
            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            var reach = settings.Hydrology.Basins.MaximumReachCells;
            var expectedSize = checked(reach * 2 + 1);
            if (terrain.Size != expectedSize
                || terrain.OriginX != checked(seed.SeedWorldX - reach)
                || terrain.OriginZ != checked(seed.SeedWorldZ - reach))
            {
                throw new ArgumentException(
                    "Candidate terrain input does not match the Basin coordinate contract.",
                    nameof(terrain));
            }

            if (!seed.PassesOccurrence || terrain[reach, reach].HasSeaWater)
            {
                return CreateInactive(seed);
            }

            var profile = GetProfile(seed.Id.Type);
            var potential = new float[checked(terrain.Size * terrain.Size)];
            var potentialSeed = Seed("Hydrology.Topology.Basin.Potential");
            for (var localZ = 0; localZ < terrain.Size; localZ++)
            for (var localX = 0; localX < terrain.Size; localX++)
            {
                var worldX = checked(terrain.OriginX + localX);
                var worldZ = checked(terrain.OriginZ + localZ);
                var value = WorldNoiseFieldSampler.Sample2D(
                    worldX,
                    worldZ,
                    settings.Hydrology.Map.BasinPotentialField,
                    potentialSeed);
                potential[localX + terrain.Size * localZ] = settings
                    .Hydrology.Map.BasinPotentialResponse.Evaluate(ToUnit(
                        value,
                        settings.Hydrology.Map.BasinPotentialField.Mode));
            }
            var typeName = GetTypeName(seed.Id.Type);
            var targetArea = (int)Math.Round(ResolveRange(
                profile.AreaCells,
                DeterministicNoise.Value01(
                    seed.SeedWorldX,
                    seed.SeedWorldZ,
                    Seed($"Hydrology.Topology.Basin.{typeName}.Area"))),
                MidpointRounding.AwayFromZero);
            var maximumDepth = ResolveRange(
                profile.MaximumDepthUnits,
                DeterministicNoise.Value01(
                    seed.SeedWorldX,
                    seed.SeedWorldZ,
                    Seed($"Hydrology.Topology.Basin.{typeName}.Depth")));
            var footprint = BuildFootprint(terrain, potential, targetArea, reach);
            if (footprint == null)
            {
                return CreateInactive(seed);
            }

            var boundary = FindBoundary(footprint, terrain.Size);
            var waterTop = SelectWaterTop(footprint, boundary, terrain);
            var interiorDistance = BuildInteriorDistance(
                footprint,
                boundary,
                terrain.Size,
                out var maximumInteriorDistance);
            var cells = new List<StreamingBasinCell>(footprint.Count);
            var boundaryCells = new List<StreamingBasinCell>(boundary.Count);
            var boundarySet = new HashSet<int>(boundary);
            for (var index = 0; index < footprint.Count; index++)
            {
                var localCell = footprint[index];
                var localX = localCell % terrain.Size;
                var localZ = localCell / terrain.Size;
                var progress = maximumInteriorDistance > 0
                    ? interiorDistance[localCell] / (float)maximumInteriorDistance
                    : 1f;
                var cell = new StreamingBasinCell(
                    checked(terrain.OriginX + localX),
                    checked(terrain.OriginZ + localZ),
                    progress);
                cells.Add(cell);
                if (boundarySet.Contains(localCell))
                {
                    boundaryCells.Add(cell);
                }
            }

            var amplitude = ResolveRange(
                settings.Hydrology.Basins.BedAmplitudeUnits,
                DeterministicNoise.Value01(
                    seed.SeedWorldX,
                    seed.SeedWorldZ,
                    Seed("Hydrology.Topology.Basin.BedAmplitude")));
            return new StreamingBasinComponent(
                seed.Id,
                true,
                seed.Priority,
                seed.SeedWorldX,
                seed.SeedWorldZ,
                maximumDepth,
                waterTop,
                amplitude,
                cells,
                boundaryCells);
        }

        public StreamingBasinComponent Build(
            in StreamingBasinCandidateSeed seed,
            StreamingBasinFieldInput field)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            var reach = settings.Hydrology.Basins.MaximumReachCells;
            var expectedSize = checked(reach * 2 + 1);
            if (field.Size != expectedSize
                || field.OriginX != checked(seed.SeedWorldX - reach)
                || field.OriginZ != checked(seed.SeedWorldZ - reach))
            {
                throw new ArgumentException(
                    "Candidate field input does not match the Basin coordinate contract.",
                    nameof(field));
            }

            if (!seed.PassesOccurrence
                || field.GetBaseTerrain(reach, reach).HasSeaWater)
            {
                return CreateInactive(seed);
            }

            var profile = GetProfile(seed.Id.Type);
            var typeName = GetTypeName(seed.Id.Type);
            var targetArea = (int)Math.Round(ResolveRange(
                profile.AreaCells,
                DeterministicNoise.Value01(
                    seed.SeedWorldX,
                    seed.SeedWorldZ,
                    Seed($"Hydrology.Topology.Basin.{typeName}.Area"))),
                MidpointRounding.AwayFromZero);
            var maximumDepth = ResolveRange(
                profile.MaximumDepthUnits,
                DeterministicNoise.Value01(
                    seed.SeedWorldX,
                    seed.SeedWorldZ,
                    Seed($"Hydrology.Topology.Basin.{typeName}.Depth")));
            var footprint = BuildFootprint(field, targetArea, reach);
            if (footprint == null)
            {
                return CreateInactive(seed);
            }

            var boundary = FindBoundary(footprint, field.Size);
            var waterTop = SelectWaterTop(footprint, boundary, field);
            var interiorDistance = BuildInteriorDistance(
                footprint,
                boundary,
                field.Size,
                out var maximumInteriorDistance);
            var cells = new List<StreamingBasinCell>(footprint.Count);
            var boundaryCells = new List<StreamingBasinCell>(boundary.Count);
            var boundarySet = new HashSet<int>(boundary);
            for (var index = 0; index < footprint.Count; index++)
            {
                var localCell = footprint[index];
                var localX = localCell % field.Size;
                var localZ = localCell / field.Size;
                var progress = maximumInteriorDistance > 0
                    ? interiorDistance[localCell] / (float)maximumInteriorDistance
                    : 1f;
                var cell = new StreamingBasinCell(
                    checked(field.OriginX + localX),
                    checked(field.OriginZ + localZ),
                    progress);
                cells.Add(cell);
                if (boundarySet.Contains(localCell))
                {
                    boundaryCells.Add(cell);
                }
            }

            var amplitude = ResolveRange(
                settings.Hydrology.Basins.BedAmplitudeUnits,
                DeterministicNoise.Value01(
                    seed.SeedWorldX,
                    seed.SeedWorldZ,
                    Seed("Hydrology.Topology.Basin.BedAmplitude")));
            return new StreamingBasinComponent(
                seed.Id,
                true,
                seed.Priority,
                seed.SeedWorldX,
                seed.SeedWorldZ,
                maximumDepth,
                waterTop,
                amplitude,
                cells,
                boundaryCells);
        }

        public StreamingBasinComponent CreateInactive(
            in StreamingBasinCandidateSeed seed) => new(
            seed.Id,
            false,
            seed.Priority,
            seed.SeedWorldX,
            seed.SeedWorldZ,
            0f,
            0,
            0f,
            Array.Empty<StreamingBasinCell>(),
            Array.Empty<StreamingBasinCell>());

        private List<int> BuildFootprint(
            StreamingBasinTerrainInput terrain,
            IReadOnlyList<float> potential,
            int targetArea,
            int reach)
        {
            var seedCell = reach + terrain.Size * reach;
            var distances = new Dictionary<int, float>();
            var footprint = new List<int>(targetArea);
            var frontier = new BasinCellCostHeap();
            distances.Add(seedCell, 0f);
            frontier.Push(seedCell, 0f);
            while (frontier.Count > 0 && footprint.Count < targetArea)
            {
                var current = frontier.Pop();
                if (!distances.TryGetValue(current.Cell, out var known)
                    || current.Cost != known)
                {
                    continue;
                }

                footprint.Add(current.Cell);
                var currentX = current.Cell % terrain.Size;
                var currentZ = current.Cell / terrain.Size;
                for (var direction = 0; direction < growthNeighbors.Length;
                     direction++)
                {
                    var neighbor = growthNeighbors[direction];
                    var nextX = currentX + neighbor.x;
                    var nextZ = currentZ + neighbor.z;
                    if ((uint)nextX >= terrain.Size || (uint)nextZ >= terrain.Size)
                    {
                        continue;
                    }

                    var next = nextX + terrain.Size * nextZ;
                    if (terrain[next].HasSeaWater)
                    {
                        continue;
                    }

                    var terrainDelta = MathF.Abs(
                        terrain[next].Surface.SurfaceUnits
                        - terrain[current.Cell].Surface.SurfaceUnits)
                        / WorldGrid.HeightStepsPerCell;
                    var slope = terrainDelta / neighbor.cost;
                    var cost = current.Cost + neighbor.cost + neighbor.cost * (
                        potential[next] * settings.Hydrology.Map.BasinPotentialCost
                        + terrainDelta * settings.Hydrology.Map.TerrainDeformationCost
                        + slope * settings.Hydrology.Map.SlopeCost);
                    if (distances.TryGetValue(next, out var previous)
                        && previous <= cost)
                    {
                        continue;
                    }

                    distances[next] = cost;
                    frontier.Push(next, cost);
                }
            }

            return footprint.Count == targetArea ? footprint : null;
        }

        private List<int> BuildFootprint(
            StreamingBasinFieldInput field,
            int targetArea,
            int reach)
        {
            var seedCell = reach + field.Size * reach;
            var distances = new Dictionary<int, float>();
            var footprint = new List<int>(targetArea);
            var frontier = new BasinCellCostHeap();
            distances.Add(seedCell, 0f);
            frontier.Push(seedCell, 0f);
            while (frontier.Count > 0 && footprint.Count < targetArea)
            {
                var current = frontier.Pop();
                if (!distances.TryGetValue(current.Cell, out var known)
                    || current.Cost != known)
                {
                    continue;
                }

                footprint.Add(current.Cell);
                var currentX = current.Cell % field.Size;
                var currentZ = current.Cell / field.Size;
                var currentTerrain = field.GetBaseTerrain(current.Cell);
                for (var direction = 0; direction < growthNeighbors.Length;
                     direction++)
                {
                    var neighbor = growthNeighbors[direction];
                    var nextX = currentX + neighbor.x;
                    var nextZ = currentZ + neighbor.z;
                    if ((uint)nextX >= field.Size || (uint)nextZ >= field.Size)
                    {
                        continue;
                    }

                    var next = nextX + field.Size * nextZ;
                    var nextTerrain = field.GetBaseTerrain(next);
                    if (nextTerrain.HasSeaWater)
                    {
                        continue;
                    }

                    var terrainDelta = MathF.Abs(
                        nextTerrain.Surface.SurfaceUnits
                        - currentTerrain.Surface.SurfaceUnits)
                        / WorldGrid.HeightStepsPerCell;
                    var slope = terrainDelta / neighbor.cost;
                    var cost = current.Cost + neighbor.cost + neighbor.cost * (
                        field.GetPotential(next)
                        * settings.Hydrology.Map.BasinPotentialCost
                        + terrainDelta * settings.Hydrology.Map.TerrainDeformationCost
                        + slope * settings.Hydrology.Map.SlopeCost);
                    if (distances.TryGetValue(next, out var previous)
                        && previous <= cost)
                    {
                        continue;
                    }

                    distances[next] = cost;
                    frontier.Push(next, cost);
                }
            }

            return footprint.Count == targetArea ? footprint : null;
        }

        private static List<int> FindBoundary(
            IReadOnlyList<int> footprint,
            int size)
        {
            var membership = new HashSet<int>(footprint);
            var result = new List<int>();
            for (var index = 0; index < footprint.Count; index++)
            {
                var cell = footprint[index];
                var x = cell % size;
                var z = cell / size;
                for (var direction = 0; direction < cardinalNeighbors.Length;
                     direction++)
                {
                    var neighbor = cardinalNeighbors[direction];
                    var nextX = x + neighbor.x;
                    var nextZ = z + neighbor.z;
                    if ((uint)nextX >= size || (uint)nextZ >= size
                        || !membership.Contains(nextX + size * nextZ))
                    {
                        result.Add(cell);
                        break;
                    }
                }
            }

            return result;
        }

        private int SelectWaterTop(
            IReadOnlyList<int> footprint,
            IReadOnlyList<int> boundary,
            StreamingBasinTerrainInput terrain)
        {
            var minimum = int.MaxValue;
            var maximum = int.MinValue;
            for (var index = 0; index < footprint.Count; index++)
            {
                var surface = (int)MathF.Round(
                    terrain[footprint[index]].Surface.SurfaceUnits,
                    MidpointRounding.AwayFromZero);
                minimum = Math.Min(minimum, surface);
                maximum = Math.Max(maximum, surface);
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
                    var delta = terrain[cell].Surface.SurfaceUnits - candidate;
                    cost += delta >= 0f
                        ? delta * settings.Hydrology.Basins.CutCost
                        : -delta * settings.Hydrology.Basins.FillCost;
                    if (boundarySet.Contains(cell))
                    {
                        cost += MathF.Abs(delta)
                            * settings.Hydrology.Basins.RimCost;
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

        private int SelectWaterTop(
            IReadOnlyList<int> footprint,
            IReadOnlyList<int> boundary,
            StreamingBasinFieldInput field)
        {
            var minimum = int.MaxValue;
            var maximum = int.MinValue;
            for (var index = 0; index < footprint.Count; index++)
            {
                var surface = (int)MathF.Round(
                    field.GetBaseTerrain(footprint[index]).Surface.SurfaceUnits,
                    MidpointRounding.AwayFromZero);
                minimum = Math.Min(minimum, surface);
                maximum = Math.Max(maximum, surface);
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
                    var delta = field.GetBaseTerrain(cell).Surface.SurfaceUnits
                        - candidate;
                    cost += delta >= 0f
                        ? delta * settings.Hydrology.Basins.CutCost
                        : -delta * settings.Hydrology.Basins.FillCost;
                    if (boundarySet.Contains(cell))
                    {
                        cost += MathF.Abs(delta)
                            * settings.Hydrology.Basins.RimCost;
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
            int size,
            out int maximumDistance)
        {
            var membership = new HashSet<int>(footprint);
            var distance = new Dictionary<int, int>(footprint.Count);
            var queue = new Queue<int>();
            for (var index = 0; index < boundary.Count; index++)
            {
                distance.Add(boundary[index], 0);
                queue.Enqueue(boundary[index]);
            }

            maximumDistance = 0;
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                var current = distance[cell];
                var x = cell % size;
                var z = cell / size;
                for (var direction = 0; direction < cardinalNeighbors.Length;
                     direction++)
                {
                    var neighbor = cardinalNeighbors[direction];
                    var nextX = x + neighbor.x;
                    var nextZ = z + neighbor.z;
                    if ((uint)nextX >= size || (uint)nextZ >= size)
                    {
                        continue;
                    }

                    var next = nextX + size * nextZ;
                    if (!membership.Contains(next) || distance.ContainsKey(next))
                    {
                        continue;
                    }

                    var nextDistance = current + 1;
                    distance.Add(next, nextDistance);
                    maximumDistance = Math.Max(maximumDistance, nextDistance);
                    queue.Enqueue(next);
                }
            }

            return distance;
        }

        private BasinProfileSettingsData GetProfile(WaterType type) => type switch
        {
            WaterType.Lake => settings.Hydrology.Basins.Lake,
            WaterType.Pond => settings.Hydrology.Basins.Pond,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        private static string GetTypeName(WaterType type) => type switch
        {
            WaterType.Lake => "Lake",
            WaterType.Pond => "Pond",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        private static float ResolveRange(
            in WorldSeededRangeSettingsData range,
            float amount) => range.Minimum
                + (range.Maximum - range.Minimum) * amount;

        private int Seed(string channel) => DeterministicNoise.DeriveSeed(
            settings.Seed,
            channel);

        private static float ToUnit(float value, WorldNoiseMode mode)
        {
            var unit = mode is WorldNoiseMode.Signed or WorldNoiseMode.SignedRidge
                ? (value + 1f) * 0.5f
                : value;
            return Math.Clamp(unit, 0f, 1f);
        }

        private readonly struct BasinCellCost
        {
            public BasinCellCost(int cell, float cost)
            {
                Cell = cell;
                Cost = cost;
            }

            public int Cell { get; }
            public float Cost { get; }
        }

        private sealed class BasinCellCostHeap
        {
            private readonly List<BasinCellCost> entries = new();
            public int Count => entries.Count;

            public void Push(int cell, float cost)
            {
                var entry = new BasinCellCost(cell, cost);
                entries.Add(entry);
                var index = entries.Count - 1;
                while (index > 0)
                {
                    var parent = (index - 1) / 2;
                    if (Compare(entries[parent], entry) <= 0)
                    {
                        break;
                    }

                    entries[index] = entries[parent];
                    index = parent;
                }

                entries[index] = entry;
            }

            public BasinCellCost Pop()
            {
                var root = entries[0];
                var last = entries[^1];
                entries.RemoveAt(entries.Count - 1);
                if (entries.Count == 0)
                {
                    return root;
                }

                var index = 0;
                while (true)
                {
                    var left = index * 2 + 1;
                    if (left >= entries.Count)
                    {
                        break;
                    }

                    var right = left + 1;
                    var child = right < entries.Count
                        && Compare(entries[right], entries[left]) < 0
                            ? right
                            : left;
                    if (Compare(entries[child], last) >= 0)
                    {
                        break;
                    }

                    entries[index] = entries[child];
                    index = child;
                }

                entries[index] = last;
                return root;
            }

            private static int Compare(in BasinCellCost left, in BasinCellCost right)
            {
                var cost = left.Cost.CompareTo(right.Cost);
                return cost != 0 ? cost : left.Cell.CompareTo(right.Cell);
            }
        }
    }

    /// <summary>
    /// Immutable owner output.  Only candidates whose deterministic seed is in
    /// Key's core belong to this Tile.
    /// </summary>
    internal sealed class StreamingBasinAllocationTile
    {
        private readonly ReadOnlyCollection<StreamingBasinComponentId>
            candidateIds;
        private readonly ReadOnlyCollection<StreamingBasinComponent>
            activeComponents;

        public StreamingBasinAllocationTile(
            PlanningTileKey key,
            IList<StreamingBasinComponentId> candidateIds,
            IList<StreamingBasinComponent> activeComponents)
        {
            Key = key;
            this.candidateIds = new ReadOnlyCollection<StreamingBasinComponentId>(
                candidateIds ?? throw new ArgumentNullException(nameof(candidateIds)));
            this.activeComponents = new ReadOnlyCollection<StreamingBasinComponent>(
                activeComponents ?? throw new ArgumentNullException(
                    nameof(activeComponents)));
        }

        public PlanningTileKey Key { get; }
        public IReadOnlyList<StreamingBasinComponentId> CandidateIds => candidateIds;
        public IReadOnlyList<StreamingBasinComponent> ActiveComponents =>
            activeComponents;
    }

    /// <summary>
    /// Request-local planner for Basin facts.  Its dictionaries are planning
    /// inputs only; it has no eviction, lazy construction, or consumer scope.
    /// </summary>
    internal readonly struct StreamingTopologyCell
    {
        public StreamingTopologyCell(
            int targetTerrainSurfaceUnits,
            int waterTopUnits,
            WaterType waterType,
            StreamingBasinComponentId basinComponent,
            float membership,
            float interiorProgress)
        {
            if (targetTerrainSurfaceUnits < 0 || waterTopUnits < 0
                || !float.IsFinite(membership)
                || !float.IsFinite(interiorProgress))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetTerrainSurfaceUnits));
            }

            var hasWater = waterTopUnits > targetTerrainSurfaceUnits;
            if (hasWater != (waterType != WaterType.None))
            {
                throw new ArgumentException(
                    "Water height and type must describe the same cell fact.");
            }

            if (waterType is WaterType.Lake or WaterType.Pond)
            {
                if (!basinComponent.IsValid || basinComponent.Type != waterType)
                {
                    throw new ArgumentException(
                        "Basin water must identify its Basin component.",
                        nameof(basinComponent));
                }
            }
            else if (waterType == WaterType.Sea && basinComponent.IsValid)
            {
                throw new ArgumentException(
                    "Sea cells cannot identify a Basin component.",
                    nameof(basinComponent));
            }

            TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
            WaterTopUnits = waterTopUnits;
            WaterType = waterType;
            BasinComponent = basinComponent;
            Membership = Math.Clamp(membership, 0f, 1f);
            InteriorProgress = Math.Clamp(interiorProgress, 0f, 1f);
        }

        public int TargetTerrainSurfaceUnits { get; }
        public int WaterTopUnits { get; }
        public WaterType WaterType { get; }
        public StreamingBasinComponentId BasinComponent { get; }
        public float Membership { get; }
        public float InteriorProgress { get; }
        public bool HasWater => WaterTopUnits > TargetTerrainSurfaceUnits;
        public bool IsBasinProtected => BasinComponent.IsValid;
    }

    internal sealed class StreamingTopologyInput
    {
        private readonly ReadOnlyCollection<StreamingBasinComponent> components;
        private readonly ReadOnlyDictionary<StreamingCellKey,
            StreamingBaseTerrainFact> baseTerrainFacts;

        public StreamingTopologyInput(
            WorldCellRectangle core,
            IList<StreamingBasinComponent> components,
            IDictionary<StreamingCellKey, StreamingBaseTerrainFact> baseTerrainFacts)
        {
            if (components == null || baseTerrainFacts == null)
            {
                throw new ArgumentNullException(
                    components == null ? nameof(components) : nameof(baseTerrainFacts));
            }

            Core = core;
            var ordered = new List<StreamingBasinComponent>(components);
            ordered.Sort((left, right) => left.Id.CompareTo(right.Id));
            this.components = new ReadOnlyCollection<StreamingBasinComponent>(ordered);
            this.baseTerrainFacts = new ReadOnlyDictionary<StreamingCellKey,
                StreamingBaseTerrainFact>(
                new Dictionary<StreamingCellKey, StreamingBaseTerrainFact>(
                    baseTerrainFacts));
        }

        public WorldCellRectangle Core { get; }
        public IReadOnlyList<StreamingBasinComponent> Components => components;

        public StreamingBaseTerrainFact GetBaseTerrain(int worldX, int worldZ)
        {
            if (!Core.Contains(worldX, worldZ)
                || !baseTerrainFacts.TryGetValue(
                    new StreamingCellKey(worldX, worldZ), out var fact))
            {
                throw new KeyNotFoundException(
                    "Topology input does not own this base-terrain fact.");
            }

            return fact;
        }
    }

    /// <summary>
    /// Pure Sea/Basin topology evaluator.  It receives exact component and
    /// terrain facts, and it does not own a dense Tile raster.
    /// </summary>
    internal sealed class StreamingTopologyEvaluator
    {
        private readonly WorldSettingsData settings;

        public StreamingTopologyEvaluator(WorldSettingsData settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(
                nameof(settings));
        }

        public StreamingTopologyEvaluation CreateEvaluation(
            StreamingTopologyInput input) => new(settings,
            input ?? throw new ArgumentNullException(nameof(input)));

        public StreamingTopologyEvaluation CreateEvaluation(
            WorldCellRectangle core,
            IList<StreamingBasinComponent> components) => new(
            settings,
            core,
            components ?? throw new ArgumentNullException(nameof(components)));
    }

    internal sealed class StreamingTopologyEvaluation
    {
        private static readonly (int x, int z)[] cardinalNeighbors =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1)
        };

        private readonly WorldSettingsData settings;
        private readonly StreamingTopologyInput input;
        private readonly WorldCellRectangle core;
        private readonly ReadOnlyCollection<StreamingBasinComponent> components;
        private readonly Dictionary<StreamingCellKey, StreamingShoreFact>
            shoreFacts = new();

        internal StreamingTopologyEvaluation(
            WorldSettingsData settings,
            StreamingTopologyInput input)
        {
            this.settings = settings;
            this.input = input;
            core = input.Core;
            components = new ReadOnlyCollection<StreamingBasinComponent>(
                new List<StreamingBasinComponent>(input.Components));
            for (var index = 0; index < components.Count; index++)
            {
                AddShoreFacts(components[index]);
            }
        }

        internal StreamingTopologyEvaluation(
            WorldSettingsData settings,
            WorldCellRectangle core,
            IList<StreamingBasinComponent> components)
        {
            this.settings = settings;
            input = null;
            this.core = core;
            var ordered = new List<StreamingBasinComponent>(components);
            ordered.Sort((left, right) => left.Id.CompareTo(right.Id));
            this.components = new ReadOnlyCollection<StreamingBasinComponent>(
                ordered);
            for (var index = 0; index < this.components.Count; index++)
            {
                AddShoreFacts(this.components[index]);
            }
        }

        public StreamingTopologyCell Sample(int worldX, int worldZ)
        {
            if (input == null)
            {
                throw new InvalidOperationException(
                    "This topology evaluation requires an explicit base-terrain fact.");
            }

            return Sample(input.GetBaseTerrain(worldX, worldZ), worldX, worldZ);
        }

        public StreamingTopologyCell Sample(
            in StreamingBaseTerrainFact baseTerrain,
            int worldX,
            int worldZ)
        {
            if (!core.Contains(worldX, worldZ))
            {
                throw new ArgumentOutOfRangeException(nameof(worldX));
            }

            var result = BuildBaseCell(baseTerrain);
            for (var index = 0; index < components.Count; index++)
            {
                var component = components[index];
                if (result.HasWater || !component.TryGetCell(
                        worldX,
                        worldZ,
                        out var basinCell))
                {
                    continue;
                }

                var target = ResolveBasinFloor(component, basinCell);
                result = new StreamingTopologyCell(
                    target,
                    component.WaterTopUnits,
                    target < component.WaterTopUnits ? component.Id.Type : WaterType.None,
                    component.Id,
                    1f,
                    basinCell.InteriorProgress);
            }

            if (!result.HasWater && shoreFacts.TryGetValue(
                    new StreamingCellKey(worldX, worldZ), out var shore)
                && (!result.BasinComponent.IsValid
                    || result.Membership < shore.Membership
                    || result.Membership == shore.Membership
                    && result.BasinComponent.CompareTo(shore.Component.Id) > 0))
            {
                var target = ToHeightUnits(baseTerrain.Surface.SurfaceUnits
                    + (shore.Component.WaterTopUnits
                        - baseTerrain.Surface.SurfaceUnits) * shore.Membership);
                result = new StreamingTopologyCell(
                    target,
                    0,
                    WaterType.None,
                    shore.Component.Id,
                    shore.Membership,
                    0f);
            }

            return result;
        }

        private StreamingTopologyCell BuildBaseCell(
            in StreamingBaseTerrainFact sample)
        {
            var terrain = ToHeightUnits(sample.Surface.SurfaceUnits);
            if (!sample.HasSeaWater)
            {
                return new StreamingTopologyCell(
                    terrain,
                    0,
                    WaterType.None,
                    default,
                    0f,
                    0f);
            }

            var waterTop = Math.Clamp(
                sample.SeaWaterTopUnits,
                0,
                MaximumHeightUnits());
            return new StreamingTopologyCell(
                terrain,
                waterTop,
                waterTop > terrain ? WaterType.Sea : WaterType.None,
                default,
                1f,
                sample.Terrain.PatternDepthProgress);
        }

        private int ResolveBasinFloor(
            StreamingBasinComponent component,
            in StreamingBasinCell cell)
        {
            var depthProgress = settings.Hydrology.Basins.DepthByInterior.Evaluate(
                cell.InteriorProgress);
            var bed = ToSigned(WorldNoiseFieldSampler.Sample2D(
                    cell.WorldX,
                    cell.WorldZ,
                    settings.Hydrology.Basins.BedField,
                    DeterministicNoise.DeriveSeed(
                        settings.Seed,
                        "Hydrology.Topology.Basin.Bed")),
                settings.Hydrology.Basins.BedField.Mode)
                * component.BedAmplitudeUnits * depthProgress;
            return ToHeightUnits(component.WaterTopUnits
                - component.MaximumDepthUnits * depthProgress + bed);
        }

        private void AddShoreFacts(StreamingBasinComponent component)
        {
            var maximumDistance = settings.Hydrology.Basins.ShoreTransitionCells;
            var distance = new Dictionary<StreamingCellKey, int>();
            var queue = new Queue<StreamingCellKey>();
            for (var index = 0; index < component.Boundary.Count; index++)
            {
                var boundary = component.Boundary[index];
                var key = new StreamingCellKey(boundary.WorldX, boundary.WorldZ);
                distance.Add(key, 0);
                queue.Enqueue(key);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentDistance = distance[current];
                if (currentDistance >= maximumDistance)
                {
                    continue;
                }

                for (var direction = 0; direction < cardinalNeighbors.Length;
                     direction++)
                {
                    var offset = cardinalNeighbors[direction];
                    var next = new StreamingCellKey(
                        checked(current.WorldX + offset.x),
                        checked(current.WorldZ + offset.z));
                    if (component.Contains(next.WorldX, next.WorldZ)
                        || distance.ContainsKey(next))
                    {
                        continue;
                    }

                    var nextDistance = currentDistance + 1;
                    distance.Add(next, nextDistance);
                    queue.Enqueue(next);
                    if (!core.Contains(next.WorldX, next.WorldZ))
                    {
                        continue;
                    }

                    var membership = settings.Hydrology.Basins.ShoreTransition
                        .Evaluate(1f - nextDistance / (float)maximumDistance);
                    if (shoreFacts.TryGetValue(next, out var currentShore)
                        && (currentShore.Membership > membership
                            || currentShore.Membership == membership
                            && currentShore.Component.Id.CompareTo(component.Id) <= 0))
                    {
                        continue;
                    }

                    shoreFacts[next] = new StreamingShoreFact(component, membership);
                }
            }
        }

        private int ToHeightUnits(float value) => Math.Clamp(
            (int)MathF.Round(value, MidpointRounding.AwayFromZero),
            0,
            MaximumHeightUnits());

        private int MaximumHeightUnits() => checked(
            settings.WorldHeight * WorldGrid.HeightStepsPerCell);

        private static float ToSigned(float value, WorldNoiseMode mode)
        {
            var unit = mode is WorldNoiseMode.Signed or WorldNoiseMode.SignedRidge
                ? (value + 1f) * 0.5f
                : value;
            return Math.Clamp(unit, 0f, 1f) * 2f - 1f;
        }

        private readonly struct StreamingShoreFact
        {
            public StreamingShoreFact(
                StreamingBasinComponent component,
                float membership)
            {
                Component = component;
                Membership = membership;
            }

            public StreamingBasinComponent Component { get; }
            public float Membership { get; }
        }
    }

    internal enum StreamingEndpointKind : byte
    {
        Natural,
        Pond,
        Lake,
        Sea
    }

    internal readonly struct StreamingEndpointId :
        IEquatable<StreamingEndpointId>, IComparable<StreamingEndpointId>
    {
        public StreamingEndpointId(
            StreamingEndpointKind kind,
            int worldX,
            int worldZ,
            StreamingBasinComponentId basinComponent)
        {
            if (kind is StreamingEndpointKind.Lake or StreamingEndpointKind.Pond)
            {
                if (!basinComponent.IsValid
                    || basinComponent.Type != ToWaterType(kind))
                {
                    throw new ArgumentException(
                        "A Basin Endpoint must identify its matching component.",
                        nameof(basinComponent));
                }
            }
            else if (basinComponent.IsValid)
            {
                throw new ArgumentException(
                    "Only Basin Endpoints identify a Basin component.",
                    nameof(basinComponent));
            }

            Kind = kind;
            WorldX = worldX;
            WorldZ = worldZ;
            BasinComponent = basinComponent;
        }

        public StreamingEndpointKind Kind { get; }
        public int WorldX { get; }
        public int WorldZ { get; }
        public StreamingBasinComponentId BasinComponent { get; }

        public int CompareTo(StreamingEndpointId other)
        {
            var kind = Kind.CompareTo(other.Kind);
            if (kind != 0)
            {
                return kind;
            }

            var component = BasinComponent.CompareTo(other.BasinComponent);
            if (component != 0)
            {
                return component;
            }

            var x = WorldX.CompareTo(other.WorldX);
            return x != 0 ? x : WorldZ.CompareTo(other.WorldZ);
        }

        public bool Equals(StreamingEndpointId other) => Kind == other.Kind
            && WorldX == other.WorldX
            && WorldZ == other.WorldZ
            && BasinComponent.Equals(other.BasinComponent);

        public override bool Equals(object obj) =>
            obj is StreamingEndpointId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            (byte)Kind,
            WorldX,
            WorldZ,
            BasinComponent);

        private static WaterType ToWaterType(StreamingEndpointKind kind) => kind
            switch
            {
                StreamingEndpointKind.Lake => WaterType.Lake,
                StreamingEndpointKind.Pond => WaterType.Pond,
                _ => WaterType.None
            };
    }

    internal readonly struct StreamingEndpoint
    {
        public StreamingEndpoint(in StreamingEndpointId id, int waterTopUnits)
        {
            if (waterTopUnits < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(waterTopUnits));
            }

            Id = id;
            WaterTopUnits = waterTopUnits;
        }

        public StreamingEndpointId Id { get; }
        public StreamingEndpointKind Kind => Id.Kind;
        public int WorldX => Id.WorldX;
        public int WorldZ => Id.WorldZ;
        public int WaterTopUnits { get; }
    }

    /// <summary>
    /// Tile-owned endpoint output.  All endpoints in this collection have a
    /// coordinate inside Key's core.
    /// </summary>
    internal sealed class StreamingEndpointTile
    {
        private readonly ReadOnlyCollection<StreamingEndpoint> endpoints;

        public StreamingEndpointTile(
            PlanningTileKey key,
            IList<StreamingEndpoint> endpoints)
        {
            Key = key;
            this.endpoints = new ReadOnlyCollection<StreamingEndpoint>(
                endpoints ?? throw new ArgumentNullException(nameof(endpoints)));
        }

        public PlanningTileKey Key { get; }
        public IReadOnlyList<StreamingEndpoint> Endpoints => endpoints;
    }

}
