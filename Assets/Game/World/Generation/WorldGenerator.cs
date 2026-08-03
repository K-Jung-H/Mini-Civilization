using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Generation
{
    public static class WorldGenerator
    {
        private static readonly (int x, int z)[] CardinalDirections =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        public static WorldData Generate(
            WorldGenerationSettings settings,
            int seed)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (!settings.TryValidate(out var error))
            {
                throw new InvalidOperationException(error);
            }

            var world = new WorldData(
                settings.WorldSize,
                settings.WorldHeight,
                settings.ChunkSizeXZ,
                settings.ChunkHeight,
                settings.ChunkSizeXZ,
                seed);
            world.ConfigureWaterFlow(settings.WaterFlowRules);

            var columnCount = settings.WorldSize * settings.WorldSize;
            var solidHeights = new int[columnCount];
            var waterSurfaces = new int[columnCount];
            var waterRoles = new WaterCellRole[columnCount];
            var waterBedSurfaces = new SurfaceType[columnCount];
            var waterDirections = new WaterFlowDirectionMask[columnCount];

            GenerateBaseTerrain(world, settings, seed, solidHeights);
            InitializeSea(
                world,
                settings,
                solidHeights,
                waterSurfaces,
                waterRoles,
                waterBedSurfaces);
            GenerateLakes(
                world,
                settings,
                seed,
                solidHeights,
                waterSurfaces,
                waterRoles,
                waterBedSurfaces);
            GenerateRivers(
                world,
                settings,
                seed,
                solidHeights,
                waterSurfaces,
                waterRoles,
                waterBedSurfaces,
                waterDirections);
            ApplyColumns(
                world,
                solidHeights,
                waterSurfaces,
                waterRoles,
                waterDirections);
            world.WaterSources.InitializeFromGeneratedWorld(world);
            WaterFlowSolver.PrepareGeneratedWorld(world);
            ApplyBiomes(world, settings, seed, waterBedSurfaces);

            return world;
        }

        public static ulong ComputeStableHash(WorldData world)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            hash = Mix(hash, world.Size, prime);
            hash = Mix(hash, world.Height, prime);
            hash = Mix(hash, world.Seed, prime);
            hash = Mix(hash, world.WaterFlowRules.SpreadAmountLoss, prime);
            hash = Mix(hash, world.WaterFlowRules.MinimumSpreadAmount, prime);
            hash = Mix(hash, world.WaterFlowRules.DissipationAmountLoss, prime);
            foreach (var chunk in world.EnumerateChunks())
            {
                foreach (var cell in chunk.AsSpan())
                {
                    hash = Mix(hash, (ushort)cell.Material, prime);
                    hash = Mix(hash, (ushort)cell.Surface, prime);
                    hash = Mix(hash, cell.Water.Amount, prime);
                    hash = Mix(hash, (byte)cell.Water.Role, prime);
                    hash = Mix(hash, (byte)cell.Water.Direction, prime);
                    hash = Mix(hash, (ushort)cell.Geology, prime);
                    hash = Mix(hash, cell.DepositIndex, prime);
                    hash = Mix(hash, cell.SolidFill, prime);
                    hash = Mix(hash, (ushort)cell.Flags, prime);
                }
            }

            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var column = world.GetSurfaceColumn(x, z);
                hash = Mix(hash, column.SurfaceCellY, prime);
                hash = Mix(hash, column.SurfaceLevel, prime);
                hash = Mix(hash, column.WaterCellY, prime);
                hash = Mix(hash, column.WaterLevel, prime);
                hash = Mix(hash, (ushort)column.Surface, prime);
                var environment = world.GetColumnEnvironment(x, z);
                hash = Mix(hash, (ushort)environment.Biome, prime);
                hash = Mix(hash, environment.Temperature, prime);
                hash = Mix(hash, environment.Moisture, prime);
                hash = Mix(hash, environment.Fertility, prime);
            }

            hash = Mix(
                hash,
                world.WaterFlowSchedule.FrontierCellIndices.Count,
                prime);
            for (var index = 0;
                 index < world.WaterFlowSchedule.FrontierCellIndices.Count;
                 index++)
            {
                hash = Mix(
                    hash,
                    world.WaterFlowSchedule.FrontierCellIndices[index],
                    prime);
            }

            return hash;
        }

        private static ulong Mix(ulong hash, ushort value, ulong prime)
        {
            hash ^= (byte)value;
            hash *= prime;
            hash ^= (byte)(value >> 8);
            return hash * prime;
        }

        private static ulong Mix(ulong hash, byte value, ulong prime)
        {
            hash ^= value;
            return hash * prime;
        }

        private static ulong Mix(ulong hash, int value, ulong prime)
        {
            unchecked
            {
                for (var shift = 0; shift < 32; shift += 8)
                {
                    hash ^= (byte)(value >> shift);
                    hash *= prime;
                }
            }

            return hash;
        }

        private static void GenerateBaseTerrain(
            WorldData world,
            WorldGenerationSettings settings,
            int worldSeed,
            int[] heights)
        {
            var terrainSeed = DeterministicNoise.DeriveSeed(worldSeed, "terrain");
            var mountainSeed = DeterministicNoise.DeriveSeed(worldSeed, "mountains");
            var mountainMaskSeed = DeterministicNoise.DeriveSeed(worldSeed, "mountain-mask");
            var maximumUnits = world.Height * WorldGrid.HeightStepsPerCell - 1;
            var edgeFalloffUnits = world.Height
                * WorldGrid.HeightStepsPerCell
                * 0.35f;

            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var noise = DeterministicNoise.FractalNoise(
                    x * settings.TerrainNoiseScale,
                    z * settings.TerrainNoiseScale,
                    terrainSeed,
                    settings.TerrainOctaves,
                    settings.TerrainLacunarity,
                    settings.TerrainPersistence);
                var mountainRidge = DeterministicNoise.RidgedFractalNoise(
                    x * settings.MountainNoiseScale,
                    z * settings.MountainNoiseScale,
                    mountainSeed,
                    settings.TerrainOctaves,
                    settings.TerrainLacunarity,
                    settings.TerrainPersistence);
                var mountainMaskNoise = DeterministicNoise.FractalNoise(
                    x * settings.MountainNoiseScale * 0.4f,
                    z * settings.MountainNoiseScale * 0.4f,
                    mountainMaskSeed,
                    3,
                    2f,
                    0.5f);

                var normalizedX = world.Size > 1 ? x / (float)(world.Size - 1) * 2f - 1f : 0f;
                var normalizedZ = world.Size > 1 ? z / (float)(world.Size - 1) * 2f - 1f : 0f;
                var edgeDistance = MathF.Max(MathF.Abs(normalizedX), MathF.Abs(normalizedZ));
                var edgePenalty = MathF.Pow(edgeDistance, 3f)
                    * edgeFalloffUnits
                    * settings.IslandFalloff;
                var centeredNoise = noise * 2f - 1f;
                var mountainMask = SmoothStep01(
                    (mountainMaskNoise - settings.MountainThreshold) / 0.2f);
                var inlandMask = 1f - SmoothStep01(
                    (edgeDistance - 0.62f) / 0.38f);
                var mountainHeight = MathF.Pow(
                        mountainRidge,
                        settings.MountainSharpness)
                    * mountainMask
                    * inlandMask
                    * settings.MountainStrengthUnits;
                var height = settings.BaseTerrainHeightUnits
                    + (int)MathF.Round(
                        centeredNoise * settings.TerrainAmplitudeUnits
                        + mountainHeight
                        - edgePenalty);

                height = Math.Clamp(height, 1, maximumUnits);
                heights[ToColumnIndex(world.Size, x, z)] = height;
            }
        }

        private static void InitializeSea(
            WorldData world,
            WorldGenerationSettings settings,
            int[] solidHeights,
            int[] waterSurfaces,
            WaterCellRole[] waterRoles,
            SurfaceType[] waterBedSurfaces)
        {
            var visited = new bool[solidHeights.Length];
            var queue = new Queue<(int x, int z)>();

            for (var x = 0; x < world.Size; x++)
            {
                EnqueueSeaCandidate(x, 0);
                EnqueueSeaCandidate(x, world.Size - 1);
            }

            for (var z = 1; z < world.Size - 1; z++)
            {
                EnqueueSeaCandidate(0, z);
                EnqueueSeaCandidate(world.Size - 1, z);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentIndex = ToColumnIndex(world.Size, current.x, current.z);
                waterSurfaces[currentIndex] = settings.SeaLevelUnits;
                waterRoles[currentIndex] = WaterCellRole.Source;
                waterBedSurfaces[currentIndex] = SurfaceType.Seabed;

                for (var i = 0; i < CardinalDirections.Length; i++)
                {
                    EnqueueSeaCandidate(current.x + CardinalDirections[i].x, current.z + CardinalDirections[i].z);
                }
            }

            void EnqueueSeaCandidate(int x, int z)
            {
                if ((uint)x >= world.Size || (uint)z >= world.Size)
                {
                    return;
                }

                var index = ToColumnIndex(world.Size, x, z);
                if (visited[index] || solidHeights[index] >= settings.SeaLevelUnits)
                {
                    return;
                }

                visited[index] = true;
                queue.Enqueue((x, z));
            }
        }

        private static void GenerateLakes(
            WorldData world,
            WorldGenerationSettings settings,
            int worldSeed,
            int[] solidHeights,
            int[] waterSurfaces,
            WaterCellRole[] waterRoles,
            SurfaceType[] waterBedSurfaces)
        {
            if (settings.LakeCount <= 0)
            {
                return;
            }

            var seed = DeterministicNoise.DeriveSeed(worldSeed, "lakes");
            var chosen = new List<(int x, int z)>();
            var margin = settings.LakeRadius + 2;

            for (var lake = 0; lake < settings.LakeCount; lake++)
            {
                var bestScore = float.MinValue;
                var bestX = -1;
                var bestZ = -1;

                for (var z = margin; z < world.Size - margin; z++)
                for (var x = margin; x < world.Size - margin; x++)
                {
                    var index = ToColumnIndex(world.Size, x, z);
                    if (waterSurfaces[index] > 0 || solidHeights[index] <= settings.SeaLevelUnits + 3)
                    {
                        continue;
                    }

                    var tooClose = false;
                    for (var i = 0; i < chosen.Count; i++)
                    {
                        var dx = chosen[i].x - x;
                        var dz = chosen[i].z - z;
                        if (dx * dx + dz * dz < settings.LakeRadius * settings.LakeRadius * 9)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (tooClose)
                    {
                        continue;
                    }

                    var score = DeterministicNoise.Value01(x, z, seed + lake * 307) - solidHeights[index] * 0.0005f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestX = x;
                        bestZ = z;
                    }
                }

                if (bestX < 0)
                {
                    break;
                }

                chosen.Add((bestX, bestZ));
                var centerIndex = ToColumnIndex(world.Size, bestX, bestZ);
                var waterLevel = Math.Max(settings.SeaLevelUnits + 2, solidHeights[centerIndex] - 1);
                CarveLake(
                    world.Size,
                    bestX,
                    bestZ,
                    settings.LakeRadius,
                    waterLevel,
                    solidHeights,
                    waterSurfaces,
                    waterRoles,
                    waterBedSurfaces);
            }
        }

        private static void CarveLake(
            int size,
            int centerX,
            int centerZ,
            int radius,
            int waterLevel,
            int[] solidHeights,
            int[] waterSurfaces,
            WaterCellRole[] waterRoles,
            SurfaceType[] waterBedSurfaces)
        {
            var outerRadius = radius + 1;
            for (var z = centerZ - outerRadius; z <= centerZ + outerRadius; z++)
            for (var x = centerX - outerRadius; x <= centerX + outerRadius; x++)
            {
                if ((uint)x >= size || (uint)z >= size)
                {
                    continue;
                }

                var dx = x - centerX;
                var dz = z - centerZ;
                var distanceSquared = dx * dx + dz * dz;
                var index = ToColumnIndex(size, x, z);

                if (distanceSquared <= radius * radius)
                {
                    var centerFactor = 1f - MathF.Sqrt(distanceSquared) / Math.Max(1, radius);
                    var depth = 1 + (int)MathF.Round(centerFactor * 2f);
                    solidHeights[index] = Math.Min(solidHeights[index], waterLevel - depth);
                    waterSurfaces[index] = Math.Max(waterSurfaces[index], waterLevel);
                    waterRoles[index] = WaterCellRole.Source;
                    waterBedSurfaces[index] = SurfaceType.Lakebed;
                }
                else if (distanceSquared <= outerRadius * outerRadius && waterSurfaces[index] == 0)
                {
                    solidHeights[index] = Math.Max(solidHeights[index], waterLevel + 1);
                }
            }
        }

        private static void GenerateRivers(
            WorldData world,
            WorldGenerationSettings settings,
            int worldSeed,
            int[] solidHeights,
            int[] waterSurfaces,
            WaterCellRole[] waterRoles,
            SurfaceType[] waterBedSurfaces,
            WaterFlowDirectionMask[] waterDirections)
        {
            if (settings.RiverCount <= 0)
            {
                return;
            }

            var seed = DeterministicNoise.DeriveSeed(worldSeed, "rivers");
            var reservedStarts = new HashSet<int>();

            for (var riverIndex = 0; riverIndex < settings.RiverCount; riverIndex++)
            {
                var start = FindRiverStart(world, settings, solidHeights, waterSurfaces, seed + riverIndex * 997, reservedStarts);
                if (start.x < 0)
                {
                    break;
                }

                reservedStarts.Add(ToColumnIndex(world.Size, start.x, start.z));
                var path = BuildRiverPath(world, start, solidHeights, waterSurfaces, seed + riverIndex * 1877);
                if (path.Count < 3)
                {
                    continue;
                }

                CarveRiver(
                    world,
                    settings,
                    path,
                    solidHeights,
                    waterSurfaces,
                    waterRoles,
                    waterBedSurfaces,
                    waterDirections);
            }
        }

        private static (int x, int z) FindRiverStart(
            WorldData world,
            WorldGenerationSettings settings,
            int[] solidHeights,
            int[] waterSurfaces,
            int seed,
            HashSet<int> reservedStarts)
        {
            var bestScore = float.MinValue;
            var best = (-1, -1);
            var margin = 3;
            for (var z = margin; z < world.Size - margin; z++)
            for (var x = margin; x < world.Size - margin; x++)
            {
                var index = ToColumnIndex(world.Size, x, z);
                if (reservedStarts.Contains(index) || waterSurfaces[index] > 0 || solidHeights[index] < settings.SeaLevelUnits + 8)
                {
                    continue;
                }

                var score = solidHeights[index] + DeterministicNoise.Value01(x, z, seed) * 12f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (x, z);
                }
            }

            return best;
        }

        private static List<(int x, int z)> BuildRiverPath(
            WorldData world,
            (int x, int z) start,
            int[] solidHeights,
            int[] waterSurfaces,
            int seed)
        {
            var path = new List<(int x, int z)> { start };
            var visited = new HashSet<int> { ToColumnIndex(world.Size, start.x, start.z) };
            var current = start;

            for (var step = 0; step < world.Size * 4; step++)
            {
                if (waterSurfaces[ToColumnIndex(world.Size, current.x, current.z)] > 0 && step > 0)
                {
                    break;
                }

                var bestScore = float.MaxValue;
                var best = (-1, -1);
                for (var directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
                {
                    var direction = CardinalDirections[directionIndex];
                    var nextX = current.x + direction.x;
                    var nextZ = current.z + direction.z;
                    if ((uint)nextX >= world.Size || (uint)nextZ >= world.Size)
                    {
                        continue;
                    }

                    var nextIndex = ToColumnIndex(world.Size, nextX, nextZ);
                    if (visited.Contains(nextIndex))
                    {
                        continue;
                    }

                    var edgeDistance = Math.Min(Math.Min(nextX, nextZ), Math.Min(world.Size - 1 - nextX, world.Size - 1 - nextZ));
                    var jitter = DeterministicNoise.Value01(nextX, nextZ, seed) * 0.75f;
                    var score = solidHeights[nextIndex] + edgeDistance * 0.35f + jitter;
                    if (waterSurfaces[nextIndex] > 0)
                    {
                        score -= world.Height * WorldGrid.HeightStepsPerCell;
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = (nextX, nextZ);
                    }
                }

                if (best.Item1 < 0)
                {
                    break;
                }

                current = best;
                path.Add(current);
                visited.Add(ToColumnIndex(world.Size, current.x, current.z));

                if (current.x == 0 || current.z == 0 || current.x == world.Size - 1 || current.z == world.Size - 1)
                {
                    break;
                }
            }

            return path;
        }

        private static void CarveRiver(
            WorldData world,
            WorldGenerationSettings settings,
            List<(int x, int z)> path,
            int[] solidHeights,
            int[] waterSurfaces,
            WaterCellRole[] waterRoles,
            SurfaceType[] waterBedSurfaces,
            WaterFlowDirectionMask[] waterDirections)
        {
            var pathIndices = new HashSet<int>();
            for (var i = 0; i < path.Count; i++)
            {
                pathIndices.Add(ToColumnIndex(world.Size, path[i].x, path[i].z));
            }

            var levels = new int[path.Count];
            var end = path[^1];
            var endIndex = ToColumnIndex(world.Size, end.x, end.z);
            var outletLevel = waterSurfaces[endIndex] > 0
                ? waterSurfaces[endIndex]
                : settings.SeaLevelUnits;
            var start = path[0];
            var startIndex = ToColumnIndex(world.Size, start.x, start.z);
            var sourceLevel = AlignToCellCeiling(Math.Max(
                outletLevel,
                solidHeights[startIndex] - 1));
            sourceLevel = Math.Min(
                sourceLevel,
                world.Height * WorldGrid.HeightStepsPerCell);
            levels[0] = sourceLevel;

            for (var i = 1; i < path.Count - 1; i++)
            {
                var point = path[i];
                var index = ToColumnIndex(world.Size, point.x, point.z);
                var progress = i / (float)(path.Count - 1);
                var profileLevel = (int)MathF.Round(
                    sourceLevel
                    + (outletLevel - sourceLevel) * progress);
                var terrainLevel = Math.Max(
                    outletLevel,
                    solidHeights[index] - 1);
                var desiredLevel = Math.Max(
                    profileLevel,
                    Math.Min(terrainLevel, levels[i - 1]));
                levels[i] = Math.Min(levels[i - 1], desiredLevel);
            }

            levels[^1] = outletLevel;

            for (var i = 0; i < path.Count; i++)
            {
                var point = path[i];
                var index = ToColumnIndex(world.Size, point.x, point.z);
                var waterLevel = levels[i];
                var riverDepth = waterLevel == sourceLevel
                    ? WorldGrid.HeightStepsPerCell
                    : settings.RiverDepthSteps;
                solidHeights[index] = Math.Min(
                    solidHeights[index],
                    waterLevel - riverDepth);
                waterSurfaces[index] = Math.Max(waterSurfaces[index], waterLevel);
                if (waterRoles[index] != WaterCellRole.Source)
                {
                    waterRoles[index] = waterLevel == sourceLevel
                        ? WaterCellRole.Source
                        : WaterCellRole.Dynamic;
                }
                waterBedSurfaces[index] = SurfaceType.Riverbed;

                if (i + 1 < path.Count)
                {
                    var next = path[i + 1];
                    waterDirections[index] |= ToWaterDirection(
                        next.x - point.x,
                        next.z - point.z);
                    if (levels[i] > levels[i + 1])
                    {
                        waterDirections[index] |= WaterFlowDirectionMask.Down;
                    }
                }

                for (var directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
                {
                    var direction = CardinalDirections[directionIndex];
                    var bankX = point.x + direction.x;
                    var bankZ = point.z + direction.z;
                    if ((uint)bankX >= world.Size || (uint)bankZ >= world.Size)
                    {
                        continue;
                    }

                    var bankIndex = ToColumnIndex(world.Size, bankX, bankZ);
                    if (!pathIndices.Contains(bankIndex) && waterSurfaces[bankIndex] == 0)
                    {
                        solidHeights[bankIndex] = Math.Max(solidHeights[bankIndex], waterLevel + 1);
                    }
                }
            }
        }

        private static int AlignToCellCeiling(int heightUnits)
        {
            var steps = WorldGrid.HeightStepsPerCell;
            return ((heightUnits + steps - 1) / steps) * steps;
        }

        private static WaterFlowDirectionMask ToWaterDirection(int dx, int dz)
        {
            if (dx > 0) return WaterFlowDirectionMask.East;
            if (dx < 0) return WaterFlowDirectionMask.West;
            if (dz > 0) return WaterFlowDirectionMask.North;
            if (dz < 0) return WaterFlowDirectionMask.South;
            return WaterFlowDirectionMask.None;
        }

        private static void ApplyColumns(
            WorldData world,
            int[] solidHeights,
            int[] waterSurfaces,
            WaterCellRole[] waterRoles,
            WaterFlowDirectionMask[] waterDirections)
        {
            var writer = new WorldBulkWriter(world);
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var index = ToColumnIndex(world.Size, x, z);
                var waterSurface = waterSurfaces[index] > solidHeights[index]
                    ? waterSurfaces[index]
                    : 0;
                writer.WriteColumn(
                    x,
                    z,
                    solidHeights[index],
                    SurfaceType.Ground,
                    waterSurface,
                    waterRoles[index],
                    waterDirections[index]);
            }

            writer.Complete();
        }

        private static void ApplyBiomes(
            WorldData world,
            WorldGenerationSettings settings,
            int worldSeed,
            SurfaceType[] waterBedSurfaces)
        {
            var climateSeed = DeterministicNoise.DeriveSeed(worldSeed, "climate");
            var waterDistances = BuildWaterDistanceField(
                world,
                settings.WaterMoistureRadius,
                waterBedSurfaces);
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var column = world.GetSurfaceColumn(x, z);
                if (!column.HasSurface)
                {
                    continue;
                }

                var latitude = world.Size > 1 ? MathF.Abs(z / (float)(world.Size - 1) * 2f - 1f) : 0f;
                var altitude = column.SolidTopUnits / (float)(world.Height * WorldGrid.HeightStepsPerCell);
                var temperature = Math.Clamp(1f - latitude * 0.7f - altitude * 0.45f, 0f, 1f);
                var moistureNoise = DeterministicNoise.FractalNoise(x * 0.025f, z * 0.025f, climateSeed, 3, 2f, 0.5f);
                var waterDistance = waterDistances[ToColumnIndex(world.Size, x, z)];
                var waterInfluence = waterDistance > settings.WaterMoistureRadius
                    ? 0f
                    : 1f - waterDistance / (float)(settings.WaterMoistureRadius + 1);
                var moisture = Math.Clamp(moistureNoise * 0.65f + waterInfluence * 0.55f, 0f, 1f);

                BiomeType biome;
                SurfaceType surface;
                if (temperature <= settings.SnowTemperatureThreshold)
                {
                    biome = BiomeType.Snow;
                }
                else if (altitude >= 0.72f)
                {
                    biome = BiomeType.Mountain;
                }
                else if (moisture <= settings.DesertMoistureThreshold)
                {
                    biome = BiomeType.Desert;
                }
                else if (moisture >= settings.WetlandMoistureThreshold && waterInfluence > 0f)
                {
                    biome = BiomeType.Wetland;
                }
                else
                {
                    biome = BiomeType.Grassland;
                }

                var waterBed = waterBedSurfaces[
                    ToColumnIndex(world.Size, x, z)];
                if (waterBed != SurfaceType.None)
                {
                    surface = waterBed;
                }
                else if (IsAdjacentToWater(world, x, z) && Math.Abs(column.SolidTopUnits - settings.SeaLevelUnits) <= 2)
                {
                    surface = SurfaceType.Shore;
                }
                else
                {
                    surface = SurfaceType.Ground;
                }

                column.Surface = surface;
                world.SetSurfaceColumn(x, z, column);
                world.SetColumnEnvironment(x, z, new ColumnEnvironmentData
                {
                    Biome = biome,
                    Temperature = (byte)MathF.Round(temperature * byte.MaxValue),
                    Moisture = (byte)MathF.Round(moisture * byte.MaxValue),
                    Fertility = (byte)MathF.Round(
                        Math.Clamp(
                            moisture * (1f - MathF.Abs(temperature - 0.58f)),
                            0f,
                            1f) * byte.MaxValue)
                });

                var topCell = world.GetCell(x, column.SurfaceCellY, z);
                topCell.Surface = surface;
                world.SetCellBulk(x, column.SurfaceCellY, z, topCell);
            }
        }

        private static int[] BuildWaterDistanceField(
            WorldData world,
            int radius,
            SurfaceType[] plannedWaterBeds)
        {
            var distances = new int[world.Size * world.Size];
            Array.Fill(distances, radius + 1);
            var queue = new Queue<int>();

            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var index = ToColumnIndex(world.Size, x, z);
                if (world.GetSurfaceColumn(x, z).HasWater
                    || plannedWaterBeds[index] != SurfaceType.None)
                {
                    distances[index] = 0;
                    queue.Enqueue(index);
                }
            }

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var distance = distances[index];
                if (distance >= radius)
                {
                    continue;
                }

                var x = index % world.Size;
                var z = index / world.Size;
                for (var directionIndex = 0; directionIndex < CardinalDirections.Length; directionIndex++)
                {
                    var direction = CardinalDirections[directionIndex];
                    var nextX = x + direction.x;
                    var nextZ = z + direction.z;
                    if (!world.ContainsColumn(nextX, nextZ))
                    {
                        continue;
                    }

                    var nextIndex = ToColumnIndex(world.Size, nextX, nextZ);
                    if (distances[nextIndex] <= distance + 1)
                    {
                        continue;
                    }

                    distances[nextIndex] = distance + 1;
                    queue.Enqueue(nextIndex);
                }
            }

            return distances;
        }

        private static bool IsAdjacentToWater(WorldData world, int x, int z)
        {
            for (var i = 0; i < CardinalDirections.Length; i++)
            {
                var nextX = x + CardinalDirections[i].x;
                var nextZ = z + CardinalDirections[i].z;
                if (world.ContainsColumn(nextX, nextZ) && world.GetSurfaceColumn(nextX, nextZ).HasWater)
                {
                    return true;
                }
            }

            return false;
        }

        private static float SmoothStep01(float value)
        {
            value = Math.Clamp(value, 0f, 1f);
            return value * value * (3f - 2f * value);
        }

        private static int ToColumnIndex(int size, int x, int z) => x + size * z;
    }

    internal sealed class WorldBulkWriter
    {
        private readonly WorldData world;
        private bool completed;

        public WorldBulkWriter(WorldData world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public void WriteColumn(
            int x,
            int z,
            int solidHeightUnits,
            SurfaceType surface,
            int waterSurfaceUnits,
            WaterCellRole waterRole,
            WaterFlowDirectionMask waterDirection)
        {
            if (completed)
            {
                throw new InvalidOperationException("Bulk writing has already completed.");
            }

            solidHeightUnits = Math.Clamp(
                solidHeightUnits,
                0,
                world.Height * WorldGrid.HeightStepsPerCell);
            waterSurfaceUnits = Math.Clamp(
                waterSurfaceUnits,
                0,
                world.Height * WorldGrid.HeightStepsPerCell);

            for (var y = 0; y < world.Height; y++)
            {
                var baseUnits = y * WorldGrid.HeightStepsPerCell;
                var solidFill = (byte)Math.Clamp(
                    solidHeightUnits - baseUnits,
                    0,
                    WorldGrid.HeightStepsPerCell);
                var cell = new CellData
                {
                    SolidFill = solidFill
                };

                if (solidFill > 0)
                {
                    cell.Material = y < Math.Max(
                            0,
                            solidHeightUnits / WorldGrid.HeightStepsPerCell - 2)
                        ? CellMaterialType.Rock
                        : CellMaterialType.Soil;
                    cell.Geology = CellMaterialType.Rock;
                    cell.Surface =
                        solidFill < WorldGrid.HeightStepsPerCell
                        || baseUnits + solidFill == solidHeightUnits
                            ? surface
                            : SurfaceType.None;
                    cell.Flags = CellFlags.Generated;
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
                if (waterFill > 0
                    && waterRole == WaterCellRole.Source)
                {
                    cell.Water = new WaterCellData
                    {
                        Amount = WaterAmount.FromRenderFill(
                            waterFill,
                            available),
                        Role = waterRole,
                        Direction = waterDirection
                    };
                    cell.Flags |= CellFlags.Generated;
                }

                world.SetCellBulk(x, y, z, cell);
            }
        }

        public void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            world.RebuildAllSurfaceColumns();
        }
    }
}
