using System.Collections.Generic;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshCollider))]
    public sealed class WorldChunkInteractionSurface : MonoBehaviour
    {
        private MeshCollider meshCollider;
        [SerializeField] private Mesh interactionMesh;
        [SerializeField] private InteractionTriangleMetadata[] triangleMetadata =
            System.Array.Empty<InteractionTriangleMetadata>();
        [SerializeField, HideInInspector] private bool ownsInteractionMesh = true;
        private readonly Dictionary<int, List<int>> cellTriangleIndices = new();
        private readonly Stack<List<int>> cellTriangleListPool = new();

        public Mesh InteractionMesh => interactionMesh;
        public InteractionTriangleMetadata[] TriangleMetadata => triangleMetadata;
        public uint GeometryVersion { get; private set; }

        public Mesh ReusableMesh => InteractionMesh;

        public void Bind(
            Mesh interactionMesh,
            InteractionTriangleMetadata[] metadata,
            bool ownsMesh = true)
        {
            if (meshCollider == null)
            {
                meshCollider = GetComponent<MeshCollider>();
            }

            this.interactionMesh = interactionMesh;
            ownsInteractionMesh = ownsMesh;
            metadata ??= System.Array.Empty<InteractionTriangleMetadata>();
            if (triangleMetadata == null
                || triangleMetadata.Length != metadata.Length)
            {
                triangleMetadata = new InteractionTriangleMetadata[
                    metadata.Length];
            }

            System.Array.Copy(
                metadata,
                triangleMetadata,
                metadata.Length);
            RebuildCellTriangleIndex();

            meshCollider.sharedMesh = null;
            if (interactionMesh != null && triangleMetadata.Length > 0)
            {
                meshCollider.sharedMesh = interactionMesh;
            }

            GeometryVersion++;
        }

        public bool TryResolveMetadata(
            int triangleIndex,
            out InteractionTriangleMetadata metadata)
        {
            if ((uint)triangleIndex < triangleMetadata.Length)
            {
                metadata = triangleMetadata[triangleIndex];
                return metadata.OwnerCellIndex >= 0;
            }

            metadata = default;
            return false;
        }

        public bool TryGetOwnedTriangleIndices(
            int cellIndex,
            out IReadOnlyList<int> triangleIndices)
        {
            if (cellTriangleIndices.TryGetValue(
                    cellIndex,
                    out var indices))
            {
                triangleIndices = indices;
                return true;
            }

            triangleIndices = null;
            return false;
        }

        public void Release()
        {
            if (meshCollider == null)
            {
                meshCollider = GetComponent<MeshCollider>();
            }

            if (meshCollider != null)
            {
                meshCollider.sharedMesh = null;
            }

            if (ownsInteractionMesh)
            {
                ReleaseObject(interactionMesh);
            }

            interactionMesh = null;
            triangleMetadata =
                System.Array.Empty<InteractionTriangleMetadata>();
            ClearCellTriangleIndex();
            ownsInteractionMesh = true;
            GeometryVersion++;
        }

        public void MarkPrepared()
        {
            ownsInteractionMesh = false;
        }

        public void RestorePreparedBinding()
        {
            if (meshCollider == null)
            {
                meshCollider = GetComponent<MeshCollider>();
            }

            if (meshCollider != null)
            {
                meshCollider.sharedMesh = interactionMesh != null
                    && triangleMetadata.Length > 0
                        ? interactionMesh
                        : null;
            }

            RebuildCellTriangleIndex();
            GeometryVersion++;
        }

        private void RebuildCellTriangleIndex()
        {
            ClearCellTriangleIndex();
            if (triangleMetadata == null || triangleMetadata.Length == 0)
            {
                return;
            }

            for (var triangleIndex = 0;
                 triangleIndex < triangleMetadata.Length;
                 triangleIndex++)
            {
                var owner = triangleMetadata[triangleIndex].OwnerCellIndex;
                if (owner < 0)
                {
                    continue;
                }

                if (!cellTriangleIndices.TryGetValue(
                        owner,
                        out var ownedTriangles))
                {
                    ownedTriangles = cellTriangleListPool.Count > 0
                        ? cellTriangleListPool.Pop()
                        : new List<int>();
                    cellTriangleIndices.Add(owner, ownedTriangles);
                }

                ownedTriangles.Add(triangleIndex);
            }
        }

        private void ClearCellTriangleIndex()
        {
            foreach (var pair in cellTriangleIndices)
            {
                pair.Value.Clear();
                cellTriangleListPool.Push(pair.Value);
            }

            cellTriangleIndices.Clear();
        }

        private void OnDestroy()
        {
            Release();
        }

        private static void ReleaseObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
