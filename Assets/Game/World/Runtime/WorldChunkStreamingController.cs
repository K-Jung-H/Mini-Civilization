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
        [SerializeField, Min(0)] private int entityRenderRadius = 1;
        [SerializeField, Min(0)] private int simulationRadius;
        [SerializeField, Min(1)] private int chunkApplicationsPerFrame = 1;

        private WorldRuntime runtime;
        private Transform resolvedTarget;
        private ChunkCoordinate currentCenter;
        private int appliedRenderRadius = -1;
        private int appliedEntityRenderRadius = -1;
        private int appliedSimulationRadius = -1;
        private bool hasCenter;

        public Transform StreamingTarget => streamingTarget;
        public Transform ResolvedTarget => resolvedTarget;
        public Transform WorldOrigin => worldOrigin;
        public int RenderRadius => renderRadius;
        public int EntityRenderRadius => entityRenderRadius;
        public int SimulationRadius => simulationRadius;
        public bool HasCenter => hasCenter;
        public ChunkCoordinate CurrentCenter => currentCenter;

        private void Update()
        {
            RefreshStreaming(force: false);
            runtime?.ProcessStreamingWork(chunkApplicationsPerFrame);
        }

        private void OnValidate()
        {
            renderRadius = Math.Max(0, renderRadius);
            entityRenderRadius = Math.Max(0, entityRenderRadius);
            simulationRadius = Math.Max(0, simulationRadius);
            chunkApplicationsPerFrame = Math.Max(1, chunkApplicationsPerFrame);
        }

        public void Configure(
            Transform target,
            Transform origin,
            int chunkRenderRadius,
            int entityRadius,
            int chunkSimulationRadius)
        {
            ValidateRadii(
                chunkRenderRadius,
                entityRadius,
                chunkSimulationRadius);

            streamingTarget = target;
            worldOrigin = origin;
            renderRadius = chunkRenderRadius;
            entityRenderRadius = entityRadius;
            simulationRadius = chunkSimulationRadius;
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
            int chunkRenderRadius,
            int entityRadius,
            int chunkSimulationRadius)
        {
            ValidateRadii(
                chunkRenderRadius,
                entityRadius,
                chunkSimulationRadius);

            renderRadius = chunkRenderRadius;
            entityRenderRadius = entityRadius;
            simulationRadius = chunkSimulationRadius;
            RefreshStreaming(force: true);
        }

        private static void ValidateRadii(
            int chunkRenderRadius,
            int entityRadius,
            int chunkSimulationRadius)
        {
            if (chunkRenderRadius < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkRenderRadius));
            }

            if (entityRadius < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entityRadius));
            }

            if (chunkSimulationRadius < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkSimulationRadius));
            }
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
            appliedEntityRenderRadius = -1;
            appliedSimulationRadius = -1;
            RefreshStreaming(force: true);
        }

        public void Unbind()
        {
            runtime?.ClearStreamingChunks();
            runtime = null;
            resolvedTarget = null;
            hasCenter = false;
            appliedRenderRadius = -1;
            appliedEntityRenderRadius = -1;
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

            var center = ResolveCenterChunk(target.position, runtime.Data);
            if (!force
                && hasCenter
                && center.Equals(currentCenter)
                && appliedRenderRadius == renderRadius
                && appliedEntityRenderRadius == entityRenderRadius
                && appliedSimulationRadius == simulationRadius)
            {
                return;
            }

            currentCenter = center;
            hasCenter = true;
            appliedRenderRadius = renderRadius;
            appliedEntityRenderRadius = entityRenderRadius;
            appliedSimulationRadius = simulationRadius;
            runtime.UpdateStreamingChunks(
                center,
                renderRadius,
                entityRenderRadius,
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

        private ChunkCoordinate ResolveCenterChunk(
            Vector3 targetPosition,
            WorldData world)
        {
            var localPosition = worldOrigin != null
                ? worldOrigin.InverseTransformPoint(targetPosition)
                : targetPosition;
            var chunkWorldSize = world.ChunkSizeX * world.CellSize;
            var chunkX = Mathf.FloorToInt(localPosition.x / chunkWorldSize);
            var chunkZ = Mathf.FloorToInt(localPosition.z / chunkWorldSize);
            if (world.IsInfinite)
            {
                return new ChunkCoordinate(chunkX, chunkZ);
            }

            return new ChunkCoordinate(
                Math.Clamp(chunkX, world.MinimumChunkX, world.MaximumChunkX),
                Math.Clamp(chunkZ, world.MinimumChunkZ, world.MaximumChunkZ));
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
