using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    public static class WorldTilePicker
    {
        public static bool TryPick(
            WorldData world,
            Ray ray,
            float maxDistance,
            LayerMask layerMask,
            out TilePickResult result)
        {
            result = default;
            if (world == null
                || !Physics.Raycast(
                    ray,
                    out var hit,
                    maxDistance,
                    layerMask,
                    QueryTriggerInteraction.Ignore)
                || !hit.collider.TryGetComponent<WorldChunkInteractionSurface>(
                    out var surface)
                || !surface.TryResolveMetadata(
                    hit.triangleIndex,
                    out var metadata))
            {
                return false;
            }

            var cell = WorldCellIndex.Decode(
                world,
                metadata.OwnerCellIndex);

            if (!world.Contains(cell.X, cell.Y, cell.Z))
            {
                return false;
            }

            var cellIndex = WorldCellIndex.Encode(
                world,
                cell.X,
                cell.Y,
                cell.Z);
            result = new TilePickResult(
                cell,
                cellIndex,
                metadata.SurfaceType,
                surface,
                hit.point,
                hit.normal);
            return true;
        }
    }
}
