using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(
        fileName = "BuildingEntities",
        menuName = "Mini Civilization/Entities/Building Container")]
    public sealed class BuildingEntityContainer : EntityDefinitionContainer
    {
        public override EntityCategory Category => EntityCategory.Building;
    }
}
