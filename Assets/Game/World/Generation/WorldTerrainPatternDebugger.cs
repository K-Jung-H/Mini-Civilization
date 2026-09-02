using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Generation
{
    [DisallowMultipleComponent]
    public sealed class WorldTerrainPatternDebugger : MonoBehaviour
    {
        [SerializeField] private WorldGenerationController generationController;
        [SerializeField] private WorldManager worldManager;
        [SerializeField] private WorldPatternMapPalette patternMapPalette;

        public WorldGenerationController GenerationController =>
            generationController;
        public WorldManager WorldManager => worldManager;
        public WorldPatternMapPalette PatternMapPalette => patternMapPalette;
        public WorldChunkStreamingController StreamingController =>
            worldManager != null ? worldManager.StreamingController : null;

        public bool TryGetStreamingTargetCell(out Vector2Int cell)
        {
            cell = default;
            var runtime = worldManager != null
                ? worldManager.CurrentWorldRuntime
                : null;
            var streaming = StreamingController;
            var target = ResolveStreamingTarget(streaming);
            if (runtime == null || target == null)
            {
                return false;
            }

            var localPosition = streaming.WorldOrigin != null
                ? streaming.WorldOrigin.InverseTransformPoint(target.position)
                : target.position;
            var cellSize = runtime.Data.CellSize;
            cell = new Vector2Int(
                Mathf.FloorToInt(localPosition.x / cellSize),
                Mathf.FloorToInt(localPosition.z / cellSize));
            return true;
        }

        public bool TryMoveStreamingTargetToCell(int cellX, int cellZ)
        {
            var runtime = worldManager != null
                ? worldManager.CurrentWorldRuntime
                : null;
            var streaming = StreamingController;
            var target = ResolveStreamingTarget(streaming);
            if (runtime == null || target == null)
            {
                return false;
            }

            var world = runtime.Data;
            if (!world.IsInfinite
                && (cellX < world.MinimumCellX
                    || cellX >= world.MaximumCellXExclusive
                    || cellZ < world.MinimumCellZ
                    || cellZ >= world.MaximumCellZExclusive))
            {
                return false;
            }

            var origin = streaming.WorldOrigin;
            var currentLocalPosition = origin != null
                ? origin.InverseTransformPoint(target.position)
                : target.position;
            var targetLocalPosition = new Vector3(
                (cellX + 0.5f) * world.CellSize,
                currentLocalPosition.y,
                (cellZ + 0.5f) * world.CellSize);
            target.position = origin != null
                ? origin.TransformPoint(targetLocalPosition)
                : targetLocalPosition;
            return true;
        }

        public Transform ResolveStreamingTarget() =>
            ResolveStreamingTarget(StreamingController);

        public void Configure(
            WorldGenerationController controller,
            WorldManager manager)
        {
            generationController = controller;
            worldManager = manager;
        }

        private static Transform ResolveStreamingTarget(
            WorldChunkStreamingController streaming)
        {
            if (streaming == null)
            {
                return null;
            }

            if (streaming.ResolvedTarget != null)
            {
                return streaming.ResolvedTarget;
            }

            if (streaming.StreamingTarget != null)
            {
                return streaming.StreamingTarget;
            }

            return Camera.main != null ? Camera.main.transform : null;
        }
    }
}
