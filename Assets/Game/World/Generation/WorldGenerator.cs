using System;
using System.Collections.Generic;
using MiniCivilization.World.Authoring;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Hydrology;

namespace MiniCivilization.World.Generation
{
    public sealed class WorldGenerationResult
    {
        public WorldData World { get; }
        public IReadOnlyList<WaterBody> WaterBodies { get; private set; }

        public WorldGenerationResult(WorldData world, IReadOnlyList<WaterBody> waterBodies)
        {
            World = world;
            WaterBodies = waterBodies;
        }

        public void RefreshWaterBodies()
        {
            WaterBodies = WaterBodyResolver.Resolve(World);
        }
    }

    public static class WorldGenerator
    {
        private static readonly (int x, int z)[] CardinalDirections =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        public static WorldGenerationResult Generate(WorldGenerationSettings settings)
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
                settings.Seed);

            var columnCount = settings.WorldSize * settings.WorldSize;
            var solidHeights = new int[columnCount];
            var waterSurfaces = new int[columnCount];
            var waterMaterials = new ushort[columnCount];
            var waterFlags = new CellFlags[columnCount];

            GenerateBaseTerrain(world, settings, solidHeights);
            InitializeSea(world, settings, solidHeights, waterSurfaces, waterMaterials);
            GenerateLakes(world, settings, solidHeights, waterSurfaces, waterMaterials);
            GenerateRivers(world, settings, solidHeights, waterSurfaces, waterMaterials, waterFlags);
            ApplyColumns(world, solidHeights, waterSurfaces, waterMaterials, waterFlags);
            ApplyBiomes(world, settings);

            var waterBodies = WaterBodyResolver.Resolve(world);
            return new WorldGenerationResult(world, waterBodies);
        }

        public static ulong ComputeStableHash(WorldData world)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            hash = Mix(hash, world.Size, prime);
            hash = Mix(hash, world.Height, prime);
            hash = Mix(hash, world.Seed, prime);
            foreach (var chunk in world.EnumerateChunks())
            {
                foreach (var cell in chunk.AsSpan())
                {
                    hash = Mix(hash, cell.MaterialId, prime);
                    hash = Mix(hash, cell.SurfaceMaterialId, prime);
                    hash = Mix(hash, cell.WaterMaterialId, prime);
                    hash = Mix(hash, cell.GeologyId, prime);
                    hash = Mix(hash, cell.DepositIndex, prime);
                    hash = Mix(hash, cell.SolidFill, prime);
                    hash = Mix(hash, cell.WaterFill, prime);
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
                hash = Mix(hash, column.SurfaceMaterialId, prime);
                hash = Mix(hash, column.WaterMaterialId, prime);
                hash = Mix(hash, column.BiomeId, prime);
                hash = Mix(hash, column.Temperature, prime);
                hash = Mix(hash, column.Moisture, prime);
                hash = Mix(hash, column.Fertility, prime);
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

        private static void GenerateBaseTerrain(WorldData world, WorldGenerationSettings settings, int[] heights)
        {
            var terrainSeed = DeterministicNoise.DeriveSeed(settings.Seed, "terrain");
            var maximumUnits = world.Height * WorldGrid.HeightStepsPerCell - 1;

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

                var normalizedX = world.Size > 1 ? x / (float)(world.Size - 1) * 2f - 1f : 0f;
                var normalizedZ = world.Size > 1 ? z / (float)(world.Size - 1) * 2f - 1f : 0f;
                var edgeDistance = MathF.Max(MathF.Abs(normalizedX), MathF.Abs(normalizedZ));
                var edgePenalty = MathF.Pow(edgeDistance, 3f) * settings.TerrainAmplitudeUnits * settings.IslandFalloff * 1.75f;
                var centeredNoise = noise * 2f - 1f;
                var height = settings.BaseTerrainHeightUnits + (int)MathF.Round(centeredNoise * settings.TerrainAmplitudeUnits - edgePenalty);

                if (x == 0 || z == 0 || x == world.Size - 1 || z == world.Size - 1)
                {
                    height = Math.Min(height, settings.SeaLevelUnits - 2);
                }

                height = Math.Clamp(height, 1, maximumUnits);
                heights[ToColumnIndex(world.Size, x, z)] = height;
            }
        }

        private static void InitializeSea(WorldData world, WorldGenerationSettings settings, int[] solidHeights, int[] waterSurfaces, ushort[] waterMaterials)
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
                waterMaterials[currentIndex] = WorldMaterialIds.SeaWater;

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
            int[] solidHeights,
            int[] waterSurfaces,
            ushort[] waterMaterials)
        {
            if (settings.LakeCount <= 0)
            {
                return;
            }

            var seed = DeterministicNoise.DeriveSeed(settings.Seed, "lakes");
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
                CarveLake(world.Size, bestX, bestZ, settings.LakeRadius, waterLevel, solidHeights, waterSurfaces, waterMaterials);
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
            ushort[] waterMaterials)
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
                    waterMaterials[index] = WorldMaterialIds.FreshWater;
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
            int[] solidHeights,
            int[] waterSurfaces,
            ushort[] waterMaterials,
            CellFlags[] waterFlags)
        {
            if (settings.RiverCount <= 0)
            {
                return;
            }

            var seed = DeterministicNoise.DeriveSeed(settings.Seed, "rivers");
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

                CarveRiver(world, settings, path, solidHeights, waterSurfaces, waterMaterials, waterFlags);
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
            ushort[] waterMaterials,
            CellFlags[] waterFlags)
        {
            var pathIndices = new HashSet<int>();
            for (var i = 0; i < path.Count; i++)
            {
                pathIndices.Add(ToColumnIndex(world.Size, path[i].x, path[i].z));
            }

            var levels = new int[path.Count];
            var end = path[^1];
            var endIndex = ToColumnIndex(world.Size, end.x, end.z);
            levels[^1] = waterSurfaces[endIndex] > 0 ? waterSurfaces[endIndex] : settings.SeaLevelUnits;

            for (var i = path.Count - 2; i >= 0; i--)
            {
                var point = path[i];
                var index = ToColumnIndex(world.Size, point.x, point.z);
                var naturalLevel = Math.Max(levels[i + 1], solidHeights[index] - 1);
                var difference = naturalLevel - levels[i + 1];
                var rise = difference >= 4 ? Math.Min(3, difference) : Math.Min(1, Math.Max(0, difference));
                levels[i] = levels[i + 1] + rise;
            }

            for (var i = 0; i < path.Count; i++)
            {
                var point = path[i];
                var index = ToColumnIndex(world.Size, point.x, point.z);
                var waterLevel = levels[i];
                solidHeights[index] = Math.Min(solidHeights[index], waterLevel - settings.RiverDepthSteps);
                waterSurfaces[index] = Math.Max(waterSurfaces[index], waterLevel);
                waterMaterials[index] = WorldMaterialIds.FreshWater;
                waterFlags[index] |= CellFlags.River;

                if (i + 1 < path.Count && levels[i] - levels[i + 1] >= 2)
                {
                    waterFlags[index] |= CellFlags.Waterfall;
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

        private static void ApplyColumns(
            WorldData world,
            int[] solidHeights,
            int[] waterSurfaces,
            ushort[] waterMaterials,
            CellFlags[] waterFlags)
        {
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var index = ToColumnIndex(world.Size, x, z);
                var preliminarySurface = waterMaterials[index] == WorldMaterialIds.SeaWater
                    ? WorldMaterialIds.Sand
                    : WorldMaterialIds.Grass;
                world.SetColumnSolidHeightUnits(x, z, solidHeights[index], preliminarySurface);

                if (waterSurfaces[index] > solidHeights[index])
                {
                    world.SetColumnWaterSurfaceUnits(x, z, waterSurfaces[index], waterMaterials[index], waterFlags[index]);
                }
            }

            world.RebuildAllSurfaceColumns();
        }

        private static void ApplyBiomes(WorldData world, WorldGenerationSettings settings)
        {
            var climateSeed = DeterministicNoise.DeriveSeed(settings.Seed, "climate");
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
                var waterInfluence = FindWaterInfluence(world, x, z, settings.WaterMoistureRadius);
                var moisture = Math.Clamp(moistureNoise * 0.65f + waterInfluence * 0.55f, 0f, 1f);

                ushort biome;
                ushort surfaceMaterial;
                if (column.HasWater)
                {
                    var waterCell = world.GetCell(x, column.WaterCellY, z);
                    var depthUnits = column.WaterTopUnits - column.SolidTopUnits;
                    if (column.WaterMaterialId == WorldMaterialIds.SeaWater)
                    {
                        biome = WorldBiomeIds.Seabed;
                        surfaceMaterial = depthUnits >= WorldGrid.HeightStepsPerCell * 2
                            ? WorldMaterialIds.Rock
                            : WorldMaterialIds.Sand;
                    }
                    else if ((waterCell.Flags & CellFlags.River) != 0)
                    {
                        biome = WorldBiomeIds.Riverbed;
                        surfaceMaterial = WorldMaterialIds.Rock;
                    }
                    else
                    {
                        biome = WorldBiomeIds.Lakebed;
                        surfaceMaterial = WorldMaterialIds.Mud;
                    }
                }
                else if (IsAdjacentToWater(world, x, z) && Math.Abs(column.SolidTopUnits - settings.SeaLevelUnits) <= 2)
                {
                    biome = WorldBiomeIds.Coast;
                    surfaceMaterial = WorldMaterialIds.Sand;
                }
                else if (temperature <= settings.SnowTemperatureThreshold)
                {
                    biome = WorldBiomeIds.Tundra;
                    surfaceMaterial = WorldMaterialIds.Snow;
                }
                else if (altitude >= 0.72f)
                {
                    biome = WorldBiomeIds.Mountain;
                    surfaceMaterial = WorldMaterialIds.Rock;
                }
                else if (moisture <= settings.DesertMoistureThreshold)
                {
                    biome = WorldBiomeIds.Desert;
                    surfaceMaterial = WorldMaterialIds.Sand;
                }
                else if (moisture >= settings.WetlandMoistureThreshold && waterInfluence > 0f)
                {
                    biome = WorldBiomeIds.Wetland;
                    surfaceMaterial = WorldMaterialIds.Mud;
                }
                else
                {
                    biome = WorldBiomeIds.Grassland;
                    surfaceMaterial = WorldMaterialIds.Grass;
                }

                column.BiomeId = biome;
                column.Temperature = (byte)MathF.Round(temperature * byte.MaxValue);
                column.Moisture = (byte)MathF.Round(moisture * byte.MaxValue);
                column.Fertility = (byte)MathF.Round(Math.Clamp(moisture * (1f - MathF.Abs(temperature - 0.58f)), 0f, 1f) * byte.MaxValue);
                column.SurfaceMaterialId = surfaceMaterial;
                world.SetSurfaceColumn(x, z, column);

                var topCell = world.GetCell(x, column.SurfaceCellY, z);
                topCell.SurfaceMaterialId = surfaceMaterial;
                world.SetCell(x, column.SurfaceCellY, z, topCell);
            }
        }

        private static float FindWaterInfluence(WorldData world, int centerX, int centerZ, int radius)
        {
            var bestDistance = radius + 1;
            for (var z = Math.Max(0, centerZ - radius); z <= Math.Min(world.Size - 1, centerZ + radius); z++)
            for (var x = Math.Max(0, centerX - radius); x <= Math.Min(world.Size - 1, centerX + radius); x++)
            {
                if (!world.GetSurfaceColumn(x, z).HasWater)
                {
                    continue;
                }

                var distance = Math.Abs(x - centerX) + Math.Abs(z - centerZ);
                bestDistance = Math.Min(bestDistance, distance);
            }

            return bestDistance > radius ? 0f : 1f - bestDistance / (float)(radius + 1);
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

        private static int ToColumnIndex(int size, int x, int z) => x + size * z;
    }
}
