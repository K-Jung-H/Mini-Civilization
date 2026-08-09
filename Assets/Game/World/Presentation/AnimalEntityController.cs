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

        public override EntityTypeKey TypeKey => new(
            EntityCategory.Animal,
            (ushort)entityType);
        public override string EntityTypeName => entityType.ToString();
        public override bool HasValidEntityType =>
            entityType is AnimalEntityType.Dog;

        public override Entity CreateStateMachine(EntityData data)
        {
            return entityType switch
            {
                AnimalEntityType.Dog => new DogEntity(data),
                _ => throw new InvalidOperationException(
                    $"Unsupported Animal Entity type: {entityType}.")
            };
        }
    }
}
