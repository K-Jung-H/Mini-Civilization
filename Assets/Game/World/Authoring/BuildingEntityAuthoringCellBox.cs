using MiniCivilization.World.Domain;
using UnityEngine;
using UnityEngine.Serialization;

namespace MiniCivilization.World.Authoring
{
    public enum BuildingCellRole : byte
    {
        None = 0,
        Building = 1,
        TerrainAnchor = 2
    }

    [DisallowMultipleComponent]
    public sealed class BuildingEntityAuthoringCellBox
        : EntityAuthoringCellBox
    {
        private static readonly Color NoneWireColor = new(
            0.5f,
            0.5f,
            0.5f,
            1f);
        private static readonly Color TerrainAnchorWireColor = new(
            1f,
            0.25f,
            0.65f,
            1f);
        private static readonly Color TerrainAnchorTerrainColor = new(
            1f,
            0f,
            0f,
            0.24f);
        private static readonly Color InvalidTerrainAnchorWireColor = new(
            1f,
            0.05f,
            0.05f,
            1f);
        private static readonly Color InvalidTerrainAnchorTerrainColor = new(
            1f,
            0f,
            0f,
            0.42f);

        [SerializeField]
        private BuildingCellRole buildingRole;

        [SerializeField, FormerlySerializedAs("maxTerrainCorrectionSteps"), Min(0)]
        private int maxHeightAdjustmentSteps;

        public BuildingCellRole BuildingRole => buildingRole;
        public int MaxHeightAdjustmentSteps => maxHeightAdjustmentSteps;
        public bool HasValidTerrainAnchor =>
            buildingRole != BuildingCellRole.TerrainAnchor
            || TerrainHeight == WorldGrid.HeightStepsPerCell;
        public override Color WireColor => buildingRole switch
        {
            BuildingCellRole.None => NoneWireColor,
            BuildingCellRole.TerrainAnchor
                when !HasValidTerrainAnchor =>
                InvalidTerrainAnchorWireColor,
            BuildingCellRole.TerrainAnchor => TerrainAnchorWireColor,
            _ => base.WireColor
        };
        public override Color TerrainColor => buildingRole switch
        {
            BuildingCellRole.None => Color.clear,
            BuildingCellRole.TerrainAnchor
                when !HasValidTerrainAnchor =>
                InvalidTerrainAnchorTerrainColor,
            BuildingCellRole.TerrainAnchor => TerrainAnchorTerrainColor,
            _ => base.TerrainColor
        };

        internal override bool CopyAuthoringValuesFrom(
            EntityAuthoringCellBox source)
        {
            var changed = base.CopyAuthoringValuesFrom(source);
            var sourceRole = source is BuildingEntityAuthoringCellBox building
                ? building.BuildingRole
                : BuildingCellRole.None;
            if (buildingRole != sourceRole)
            {
                buildingRole = sourceRole;
                changed = true;
            }

            var sourceCorrectionSteps =
                source is BuildingEntityAuthoringCellBox sourceBuilding
                    ? sourceBuilding.MaxHeightAdjustmentSteps
                    : 0;
            if (maxHeightAdjustmentSteps != sourceCorrectionSteps)
            {
                maxHeightAdjustmentSteps = sourceCorrectionSteps;
                changed = true;
            }

            if (buildingRole == BuildingCellRole.TerrainAnchor
                && SetTerrainHeight(WorldGrid.HeightStepsPerCell))
            {
                changed = true;
            }

            return changed;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            maxHeightAdjustmentSteps = Mathf.Max(
                0,
                maxHeightAdjustmentSteps);
            if (buildingRole == BuildingCellRole.TerrainAnchor)
            {
                SetTerrainHeight(WorldGrid.HeightStepsPerCell);
            }
        }
    }
}
