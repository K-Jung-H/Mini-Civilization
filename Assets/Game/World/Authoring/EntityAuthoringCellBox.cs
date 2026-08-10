using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Authoring
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class EntityAuthoringCellBox : MonoBehaviour
    {
        [SerializeField]
        [Range(0, WorldGrid.HeightStepsPerCell)]
        private int terrainHeight;

        [SerializeField]
        private Color wireColor = new(0.72f, 1f, 0f, 1f);

        [SerializeField]
        private Color terrainColor = new(0.05f, 0.8f, 0.25f, 0.24f);

        [SerializeField]
        [HideInInspector]
        private Vector3Int localOffset;

        [SerializeField]
        [HideInInspector]
        private EntityAuthoringSystem authoringSystem;

        public int TerrainHeight => terrainHeight;
        public float CellSize => authoringSystem != null
            ? authoringSystem.CellSize
            : 1f;
        public float TerrainSurfaceHeight =>
            terrainHeight * (CellSize / WorldGrid.HeightStepsPerCell);
        public Color WireColor => wireColor;
        public Color TerrainColor => terrainColor;
        public Vector3Int LocalOffset => localOffset;
        public EntityAuthoringSystem AuthoringSystem => authoringSystem;

        internal bool SetAuthoringContext(
            EntityAuthoringSystem owner,
            Vector3Int offset)
        {
            if (authoringSystem == owner && localOffset == offset)
            {
                return false;
            }

            authoringSystem = owner;
            localOffset = offset;
            return true;
        }

        internal bool SetTerrainHeight(int value)
        {
            value = Mathf.Clamp(value, 0, WorldGrid.HeightStepsPerCell);
            if (terrainHeight == value)
            {
                return false;
            }

            terrainHeight = value;
            return true;
        }

        private void OnValidate()
        {
            SetTerrainHeight(terrainHeight);
        }
    }
}
