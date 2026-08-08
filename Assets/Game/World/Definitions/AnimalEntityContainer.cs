using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(
        fileName = "AnimalEntities",
        menuName = "Mini Civilization/Entities/Animal Container")]
    public sealed class AnimalEntityContainer : EntityDefinitionContainer
    {
        public override EntityCategory Category => EntityCategory.Animal;
    }
}
