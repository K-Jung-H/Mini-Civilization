using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WorldChunkStreamingController : MonoBehaviour
    {
        [SerializeField] private Transform streamingTarget;
        [SerializeField] private Transform worldOrigin;
        [SerializeField, Min(0)] private int renderRadius = 1;
        [SerializeField, Min(0)] private int simulationRadius;

        private WorldRuntime runtime;
        private Transform resolvedTarget;
        private ChunkColumnCoordinate currentCenter;
        private int appliedRenderRadius = -1;
        private int appliedSimulationRadius = -1;
        private bool hasCenter;

        public Transform StreamingTarget => streamingTarget;
        public Transform ResolvedTarget => resolvedTarget;
        public Transform WorldOrigin => worldOrigin;
        public int RenderRadius => renderRadius;
        public int SimulationRadius => simulationRadius;
        public bool HasCenter => hasCenter;
        public ChunkColumnCoordinate CurrentCenter => currentCenter;

        private void Update()
        {
            RefreshStreaming(force: false);
        }

        private void OnValidate()
        {
            renderRadius = Math.Max(0, renderRadius);
            simulationRadius = Math.Clamp(
                simulationRadius,
                0,
                renderRadius);
        }

        public void Configure(
            Transform target,
            Transform origin,
            int columnRenderRadius,
            int columnSimulationRadius)
        {
            if (columnRenderRadius < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columnRenderRadius));
            }

            if (columnSimulationRadius < 0
                || columnSimulationRadius > columnRenderRadius)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columnSimulationRadius));
            }

            streamingTarget = target;
            worldOrigin = origin;
            renderRadius = columnRenderRadius;
            simulationRadius = columnSimulationRadius;
            resolvedTarget = null;
            RefreshStreaming(force: true);
        }

        public void SetStreamingTarget(Transform target)
        {
            streamingTarget = target;
            resolvedTarget = null;
            RefreshStreaming(force: true);
        }

        public void SetRadii(
            int columnRenderRadius,
            int columnSimulationRadius)
        {
            if (columnRenderRadius < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columnRenderRadius));
            }

            if (columnSimulationRadius < 0
                || columnSimulationRadius > columnRenderRadius)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columnSimulationRadius));
            }

            renderRadius = columnRenderRadius;
            simulationRadius = columnSimulationRadius;
            RefreshStreaming(force: true);
        }

        public void Bind(WorldRuntime worldRuntime, Transform origin)
        {
            if (worldRuntime == null)
            {
                throw new ArgumentNullException(nameof(worldRuntime));
            }

            Unbind();
            runtime = worldRuntime;
            if (worldOrigin == null)
            {
                worldOrigin = origin;
            }

            resolvedTarget = null;
            hasCenter = false;
            appliedRenderRadius = -1;
            appliedSimulationRadius = -1;
            RefreshStreaming(force: true);
        }

        public void Unbind()
        {
            runtime?.ClearStreamingColumns();
            runtime = null;
            resolvedTarget = null;
            hasCenter = false;
            appliedRenderRadius = -1;
            appliedSimulationRadius = -1;
        }

        private void RefreshStreaming(bool force)
        {
            if (runtime == null)
            {
                return;
            }

            var target = ResolveTarget();
            if (target == null)
            {
                return;
            }

            var center = ResolveCenterColumn(target.position, runtime.Data);
            if (!force
                && hasCenter
                && center.Equals(currentCenter)
                && appliedRenderRadius == renderRadius
                && appliedSimulationRadius == simulationRadius)
            {
                return;
            }

            currentCenter = center;
            hasCenter = true;
            appliedRenderRadius = renderRadius;
            appliedSimulationRadius = simulationRadius;
            runtime.UpdateStreamingColumns(
                center,
                renderRadius,
                simulationRadius);
        }

        private Transform ResolveTarget()
        {
            if (streamingTarget != null)
            {
                resolvedTarget = streamingTarget;
                return resolvedTarget;
            }

            if (resolvedTarget != null)
            {
                return resolvedTarget;
            }

            var mainCamera = Camera.main;
            resolvedTarget = mainCamera != null
                ? mainCamera.transform
                : null;
            return resolvedTarget;
        }

        private ChunkColumnCoordinate ResolveCenterColumn(
            Vector3 targetPosition,
            WorldData world)
        {
            var localPosition = worldOrigin != null
                ? worldOrigin.InverseTransformPoint(targetPosition)
                : targetPosition;
            var chunkWorldSize = world.ChunkSizeX * world.CellSize;
            var chunkX = Mathf.FloorToInt(localPosition.x / chunkWorldSize);
            var chunkZ = Mathf.FloorToInt(localPosition.z / chunkWorldSize);
            return new ChunkColumnCoordinate(
                Math.Clamp(chunkX, 0, world.ChunkCountX - 1),
                Math.Clamp(chunkZ, 0, world.ChunkCountZ - 1));
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
