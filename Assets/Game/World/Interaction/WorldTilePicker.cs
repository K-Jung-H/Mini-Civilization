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
            if (metadata.SurfaceType == SurfaceInteractionType.Terrain
                && metadata.Role == SurfaceTriangleRole.Cliff)
            {
                var localHitPoint = surface.transform.InverseTransformPoint(
                    hit.point);
                var cellY = Mathf.Clamp(
                    Mathf.FloorToInt(localHitPoint.y - 0.0001f),
                    0,
                    world.Height - 1);
                cell = new CellCoordinate(cell.X, cellY, cell.Z);
            }

            if (!world.Contains(cell.X, cell.Y, cell.Z))
            {
                return false;
            }

            var cellIndex = WorldCellIndex.Encode(
                world,
                cell.X,
                cell.Y,
                cell.Z);
            if (metadata.SurfaceType == SurfaceInteractionType.Terrain
                && metadata.Role == SurfaceTriangleRole.Cliff
                && !world.GetCell(cell.X, cell.Y, cell.Z).HasSolid)
            {
                return false;
            }

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
