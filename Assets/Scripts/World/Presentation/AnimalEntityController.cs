using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Entities.Animal;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    public enum AnimalEntityType : ushort
    {
        None = 0,
        Dog = 1
    }

    public sealed class AnimalEntityController : AnimatedEntityController
    {
        [SerializeField] private AnimalEntityType entityType;
        [SerializeField] private EntityCellMovementProfile cellMovementProfile;
        [SerializeField] private AnimalDecisionProfile decisionProfile;

        public override EntityTypeKey TypeKey => new(
            EntityCategory.Animal,
            (ushort)entityType);
        public override string EntityTypeName => entityType.ToString();
        public override bool HasValidEntityType =>
            entityType is AnimalEntityType.Dog;

        public override Entity CreateStateMachine(EntityData data)
        {
            if (cellMovementProfile == null || decisionProfile == null)
            {
                throw new InvalidOperationException(
                    $"Animal Entity Controller '{name}' requires assigned "
                    + "Cell Movement and Decision Profiles.");
            }

            return entityType switch
            {
                AnimalEntityType.Dog => new DogEntity(
                    data,
                    cellMovementProfile.GetRuntimeRules(),
                    decisionProfile.GetRuntimeRules()),
                _ => throw new InvalidOperationException(
                    $"Unsupported Animal Entity type: {entityType}.")
            };
        }
    }
}
