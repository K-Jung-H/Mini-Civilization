using UnityEngine;

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

        [SerializeField]
        private BuildingCellRole buildingRole;

        public BuildingCellRole BuildingRole => buildingRole;
        public override Color WireColor => buildingRole switch
        {
            BuildingCellRole.None => NoneWireColor,
            BuildingCellRole.TerrainAnchor => TerrainAnchorWireColor,
            _ => base.WireColor
        };
        public override Color TerrainColor => buildingRole switch
        {
            BuildingCellRole.None => Color.clear,
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

            return changed;
        }
    }
}
