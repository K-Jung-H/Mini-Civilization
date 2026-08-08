using System.Collections.Generic;
using UnityEngine;

namespace MiniCivilization.World.Authoring
{
    [DisallowMultipleComponent]
    public sealed class EntityAuthoringSystem : MonoBehaviour
    {
        [SerializeField]
        private EntityAuthoringCellBox cellBoxPrefab;

        [SerializeField]
        private Vector3Int gridSize = Vector3Int.one;

        [SerializeField]
        [HideInInspector]
        private EntityAuthoringCellBox pooledPrefab;

        [SerializeField]
        [HideInInspector]
        private List<EntityAuthoringCellBox> pooledCells = new();

        public EntityAuthoringCellBox CellBoxPrefab => cellBoxPrefab;
        public Vector3Int GridSize => gridSize;

        internal EntityAuthoringCellBox PooledPrefab
        {
            get => pooledPrefab;
            set => pooledPrefab = value;
        }

        internal List<EntityAuthoringCellBox> PooledCells => pooledCells;

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
            NormalizeSettings();
        }
    }
}
