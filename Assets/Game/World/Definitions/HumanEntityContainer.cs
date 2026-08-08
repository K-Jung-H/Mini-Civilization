using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(
        fileName = "HumanEntities",
        menuName = "Mini Civilization/Entities/Human Container")]
    public sealed class HumanEntityContainer : EntityDefinitionContainer
    {
        public override EntityCategory Category => EntityCategory.Human;
    }
}
