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
