using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshCollider))]
    public sealed class WorldChunkInteractionSurface : MonoBehaviour
    {
        private MeshCollider meshCollider;
        private InteractionTriangleMetadata[] triangleMetadata =
            System.Array.Empty<InteractionTriangleMetadata>();

        public Mesh InteractionMesh { get; private set; }
        public uint GeometryVersion { get; private set; }

        public Mesh ReusableMesh => InteractionMesh;

        public void Bind(
            Mesh interactionMesh,
            InteractionTriangleMetadata[] metadata)
        {
            if (meshCollider == null)
            {
                meshCollider = GetComponent<MeshCollider>();
            }

            InteractionMesh = interactionMesh;
            triangleMetadata = metadata
                ?? System.Array.Empty<InteractionTriangleMetadata>();

            meshCollider.sharedMesh = null;
            if (InteractionMesh != null && triangleMetadata.Length > 0)
            {
                meshCollider.sharedMesh = InteractionMesh;
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

            ReleaseObject(InteractionMesh);
            InteractionMesh = null;
            triangleMetadata =
                System.Array.Empty<InteractionTriangleMetadata>();
            GeometryVersion++;
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
