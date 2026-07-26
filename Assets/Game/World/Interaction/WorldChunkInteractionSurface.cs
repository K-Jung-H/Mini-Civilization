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
        private readonly Dictionary<int, int[]> cellTriangleIndices = new();

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
            triangleMetadata = metadata
                ?? System.Array.Empty<InteractionTriangleMetadata>();
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
            out int[] triangleIndices)
        {
            return cellTriangleIndices.TryGetValue(
                cellIndex,
                out triangleIndices);
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
            cellTriangleIndices.Clear();
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
            cellTriangleIndices.Clear();
            if (triangleMetadata == null || triangleMetadata.Length == 0)
            {
                return;
            }

            var builders = new Dictionary<int, List<int>>();
            for (var triangleIndex = 0;
                 triangleIndex < triangleMetadata.Length;
                 triangleIndex++)
            {
                var owner = triangleMetadata[triangleIndex].OwnerCellIndex;
                if (owner < 0)
                {
                    continue;
                }

                if (!builders.TryGetValue(owner, out var ownedTriangles))
                {
                    ownedTriangles = new List<int>();
                    builders.Add(owner, ownedTriangles);
                }

                ownedTriangles.Add(triangleIndex);
            }

            foreach (var pair in builders)
            {
                cellTriangleIndices.Add(pair.Key, pair.Value.ToArray());
            }
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
