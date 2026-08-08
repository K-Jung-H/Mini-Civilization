using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(
        fileName = "NatureEntities",
        menuName = "Mini Civilization/Entities/Nature Container")]
    public sealed class NatureEntityContainer : EntityDefinitionContainer
    {
        public override EntityCategory Category => EntityCategory.Nature;
    }
}
