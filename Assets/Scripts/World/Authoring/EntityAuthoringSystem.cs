using System.Collections.Generic;
using MiniCivilization.World.Presentation;
using UnityEngine;

namespace MiniCivilization.World.Authoring
{
    [DisallowMultipleComponent]
    public sealed class EntityAuthoringSystem : MonoBehaviour
    {
        [SerializeField]
        private EntityAuthoringCellBox cellBoxPrefab;

        [SerializeField, Min(float.Epsilon)]
        private float cellSize = 1f;

        [SerializeField]
        private EntityView entityPrefab;

        [SerializeField]
        private Vector3Int gridSize = Vector3Int.one;

        [SerializeField]
        [HideInInspector]
        private EntityAuthoringCellBox pooledPrefab;

        [SerializeField]
        [HideInInspector]
        private List<EntityAuthoringCellBox> pooledCells = new();

        [SerializeField]
        [HideInInspector]
        private EntityView previewPrefab;

        [SerializeField]
        [HideInInspector]
        private Transform previewScaleRoot;

        [SerializeField]
        [HideInInspector]
        private EntityView previewInstance;

        public EntityAuthoringCellBox CellBoxPrefab => cellBoxPrefab;
        public EntityView EntityPrefab => entityPrefab;
        public float CellSize => cellSize;
        public Vector3Int GridSize => gridSize;

        internal EntityAuthoringCellBox PooledPrefab
        {
            get => pooledPrefab;
            set => pooledPrefab = value;
        }

        internal List<EntityAuthoringCellBox> PooledCells => pooledCells;

        internal EntityView PreviewPrefab
        {
            get => previewPrefab;
            set => previewPrefab = value;
        }

        internal Transform PreviewScaleRoot
        {
            get => previewScaleRoot;
            set => previewScaleRoot = value;
        }

        internal EntityView PreviewInstance
        {
            get => previewInstance;
            set => previewInstance = value;
        }

        internal bool NormalizeSettings()
        {
            var normalizedSize = new Vector3Int(
                Mathf.Max(1, gridSize.x),
                Mathf.Max(1, gridSize.y),
                Mathf.Max(1, gridSize.z));
            var changed = gridSize != normalizedSize
                || pooledCells == null;

            gridSize = normalizedSize;
            pooledCells ??= new List<EntityAuthoringCellBox>();
            return changed;
        }

        private void OnValidate()
        {
            cellSize = Mathf.Max(float.Epsilon, cellSize);
            NormalizeSettings();
        }
    }
}
