using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class WorldTerrainPatternDebugger : MonoBehaviour
    {
        [SerializeField] private WorldManager worldManager;
        [SerializeField] private PatternMapPalette patternMapPalette;

        public WorldManager WorldManager => worldManager;
        public PatternMapPalette PatternMapPalette => patternMapPalette;
        public long PatternMapRevision => worldManager == null
            ? 0L
            : worldManager.PatternMapRevision;

        public bool TryCreateConfiguration(
            out WorldGenerationConfiguration configuration,
            out string error)
        {
            configuration = null;
            error = string.Empty;
            if (worldManager == null)
            {
                error = "World Manager is not assigned.";
                return false;
            }

            if (worldManager.CurrentWorldRuntime == null
                || worldManager.GenerationConfiguration == null)
            {
                error = "Pattern Map Store가 있는 Runtime World가 필요합니다.";
                return false;
            }

            configuration = worldManager.GenerationConfiguration;
            return true;
        }

        public bool TryRequestMapPreparation(
            PatternTileBounds bounds,
            out string error)
        {
            error = string.Empty;
            if (worldManager == null || worldManager.CurrentWorldRuntime == null)
            {
                error = "Pattern Map Store가 있는 Runtime World가 필요합니다.";
                return false;
            }

            try
            {
                worldManager.SetDebuggerPatternMapDemand(bounds);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryGetPatternTile(
            PatternTileKey key,
            out PatternTilePair tile)
        {
            if (worldManager == null)
            {
                tile = default;
                return false;
            }

            return worldManager.TryGetPatternTile(key, out tile);
        }

        public bool TryGetTerrainPatternTile(
            PatternTileKey key,
            out TerrainPatternTile tile)
        {
            if (worldManager == null)
            {
                tile = null;
                return false;
            }

            return worldManager.TryGetTerrainPatternTile(key, out tile);
        }

        public bool TryGetHydrologyPatternTile(
            PatternTileKey key,
            out HydrologyPatternTile tile)
        {
            if (worldManager == null)
            {
                tile = null;
                return false;
            }

            return worldManager.TryGetHydrologyPatternTile(key, out tile);
        }

        public bool TryGetStreamingTargetCell(
            WorldGenerationConfiguration configuration,
            out Vector2Int cell)
        {
            cell = default;
            var target = worldManager != null
                ? worldManager.StreamingTarget
                : null;
            if (target == null)
            {
                return false;
            }

            var position = target.position;
            if (!float.IsFinite(position.x) || !float.IsFinite(position.z))
            {
                return false;
            }

            cell = new Vector2Int(
                checked((int)MathF.Floor(position.x / configuration.World.CellSize)),
                checked((int)MathF.Floor(position.z / configuration.World.CellSize)));
            return true;
        }

        public bool TryMoveStreamingTargetToChunk(
            WorldGenerationConfiguration configuration,
            Vector2Int chunk)
        {
            var target = worldManager != null
                ? worldManager.StreamingTarget
                : null;
            if (target == null)
            {
                return false;
            }

            var world = configuration.World;
            if (world.WorldType == WorldType.Finite
                && (chunk.x < world.MinimumChunkCoordinate
                    || chunk.x > world.MaximumChunkCoordinate
                    || chunk.y < world.MinimumChunkCoordinate
                    || chunk.y > world.MaximumChunkCoordinate))
            {
                return false;
            }

            var cellX = checked(chunk.x * world.ChunkCellCountXZ
                + world.ChunkCellCountXZ / 2);
            var cellZ = checked(chunk.y * world.ChunkCellCountXZ
                + world.ChunkCellCountXZ / 2);
            worldManager.SetStreamingTarget(new Vector3(
                (cellX + 0.5f) * world.CellSize,
                target.position.y,
                (cellZ + 0.5f) * world.CellSize));
            return true;
        }
    }
}
