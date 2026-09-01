using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Runtime
{
    internal readonly struct StreamingRequest
    {
        public StreamingRequest(
            ChunkCoordinate center,
            int terrainRenderRadius,
            int entityRenderRadius,
            int simulationRadius)
        {
            if (terrainRenderRadius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(terrainRenderRadius));
            }

            if (entityRenderRadius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityRenderRadius));
            }

            if (simulationRadius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(simulationRadius));
            }

            Center = center;
            TerrainRenderRadius = terrainRenderRadius;
            EntityRenderRadius = entityRenderRadius;
            SimulationRadius = simulationRadius;
        }

        public ChunkCoordinate Center { get; }
        public int TerrainRenderRadius { get; }
        public int EntityRenderRadius { get; }
        public int SimulationRadius { get; }
    }

    internal sealed class StreamingChunkDemand
    {
        private readonly HashSet<ChunkCoordinate> preparedChunks;
        private readonly HashSet<ChunkCoordinate> terrainRenderChunks;
        private readonly HashSet<ChunkCoordinate> entityRenderChunks;
        private readonly HashSet<ChunkCoordinate> activeChunks;

        public static StreamingChunkDemand Empty { get; } = new(
            default,
            new HashSet<ChunkCoordinate>(),
            new HashSet<ChunkCoordinate>(),
            new HashSet<ChunkCoordinate>(),
            new HashSet<ChunkCoordinate>());

        public StreamingChunkDemand(
            ChunkCoordinate center,
            HashSet<ChunkCoordinate> preparedChunks,
            HashSet<ChunkCoordinate> terrainRenderChunks,
            HashSet<ChunkCoordinate> entityRenderChunks,
            HashSet<ChunkCoordinate> activeChunks)
        {
            Center = center;
            this.preparedChunks = new HashSet<ChunkCoordinate>(
                preparedChunks ?? throw new ArgumentNullException(nameof(preparedChunks)));
            this.terrainRenderChunks = new HashSet<ChunkCoordinate>(
                terrainRenderChunks ?? throw new ArgumentNullException(nameof(terrainRenderChunks)));
            this.entityRenderChunks = new HashSet<ChunkCoordinate>(
                entityRenderChunks ?? throw new ArgumentNullException(nameof(entityRenderChunks)));
            this.activeChunks = new HashSet<ChunkCoordinate>(
                activeChunks ?? throw new ArgumentNullException(nameof(activeChunks)));
        }

        public ChunkCoordinate Center { get; }
        public IReadOnlyCollection<ChunkCoordinate> PreparedChunks => preparedChunks;
        public IReadOnlyCollection<ChunkCoordinate> TerrainRenderChunks =>
            terrainRenderChunks;
        public bool IsPrepared(ChunkCoordinate coordinate) =>
            preparedChunks.Contains(coordinate);
        public bool IsTerrainRendering(ChunkCoordinate coordinate) =>
            terrainRenderChunks.Contains(coordinate);
        public bool IsEntityRendering(ChunkCoordinate coordinate) =>
            entityRenderChunks.Contains(coordinate);
        public bool IsActive(ChunkCoordinate coordinate) =>
            activeChunks.Contains(coordinate);
    }

    internal static class StreamingChunkDemandBuilder
    {
        public static StreamingChunkDemand Build(
            WorldData world,
            in StreamingRequest request)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var prepared = new HashSet<ChunkCoordinate>();
            var terrainRender = new HashSet<ChunkCoordinate>();
            var entityRender = new HashSet<ChunkCoordinate>();
            var active = new HashSet<ChunkCoordinate>();
            var preparationRadius = Math.Max(
                request.TerrainRenderRadius,
                Math.Max(request.EntityRenderRadius, request.SimulationRadius));
            var chunksPerPatch = world.Settings.RenderChunksPerPatch;
            for (var z = request.Center.Z - preparationRadius;
                 z <= request.Center.Z + preparationRadius;
                 z++)
            for (var x = request.Center.X - preparationRadius;
                 x <= request.Center.X + preparationRadius;
                 x++)
            {
                var coordinate = new ChunkCoordinate(x, z);
                if (!world.IsChunkWithinBounds(coordinate))
                {
                    continue;
                }

                if (Math.Abs(x - request.Center.X)
                    <= request.TerrainRenderRadius
                    && Math.Abs(z - request.Center.Z)
                    <= request.TerrainRenderRadius)
                {
                    AddTerrainPatchDemand(
                        world,
                        coordinate,
                        chunksPerPatch,
                        terrainRender,
                        prepared);
                }

                if (Math.Abs(x - request.Center.X)
                    <= request.EntityRenderRadius
                    && Math.Abs(z - request.Center.Z)
                    <= request.EntityRenderRadius)
                {
                    entityRender.Add(coordinate);
                }

                if (Math.Abs(x - request.Center.X)
                    <= request.SimulationRadius
                    && Math.Abs(z - request.Center.Z)
                    <= request.SimulationRadius)
                {
                    active.Add(coordinate);
                }
            }

            prepared.UnionWith(terrainRender);
            prepared.UnionWith(entityRender);
            prepared.UnionWith(active);
            return new StreamingChunkDemand(
                request.Center,
                prepared,
                terrainRender,
                entityRender,
                active);
        }

        private static void AddTerrainPatchDemand(
            WorldData world,
            ChunkCoordinate coordinate,
            int chunksPerPatch,
            HashSet<ChunkCoordinate> terrainRender,
            HashSet<ChunkCoordinate> prepared)
        {
            var patchX = WorldCoordinateUtility.FloorDivide(
                coordinate.X,
                chunksPerPatch);
            var patchZ = WorldCoordinateUtility.FloorDivide(
                coordinate.Z,
                chunksPerPatch);
            var patchStartX = patchX * chunksPerPatch;
            var patchStartZ = patchZ * chunksPerPatch;
            var patchEndX = patchStartX + chunksPerPatch;
            var patchEndZ = patchStartZ + chunksPerPatch;
            for (var patchChunkZ = patchStartZ;
                 patchChunkZ < patchEndZ;
                 patchChunkZ++)
            for (var patchChunkX = patchStartX;
                 patchChunkX < patchEndX;
                 patchChunkX++)
            {
                var patchChunk = new ChunkCoordinate(
                    patchChunkX,
                    patchChunkZ);
                if (!world.IsChunkWithinBounds(patchChunk))
                {
                    continue;
                }

                terrainRender.Add(patchChunk);
                for (var topologyZ = patchChunkZ - 1;
                     topologyZ <= patchChunkZ + 1;
                     topologyZ++)
                for (var topologyX = patchChunkX - 1;
                     topologyX <= patchChunkX + 1;
                     topologyX++)
                {
                    var topologyChunk = new ChunkCoordinate(
                        topologyX,
                        topologyZ);
                    if (world.IsChunkWithinBounds(topologyChunk))
                    {
                        prepared.Add(topologyChunk);
                    }
                }
            }
        }
    }
}
