using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Entities.Building;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    public enum BuildingEntityType : ushort
    {
        None = 0,
        House = 1
    }

    public sealed class BuildingEntityController : EntityController
    {
        [SerializeField] private BuildingEntityType entityType;

        public override EntityTypeKey TypeKey => new(
            EntityCategory.Building,
            (ushort)entityType);
        public override string EntityTypeName => entityType.ToString();
        public override bool HasValidEntityType =>
            entityType is BuildingEntityType.House;

        public override Entity CreateStateMachine(EntityData data)
        {
            return entityType switch
            {
                BuildingEntityType.House => new HouseEntity(data),
                _ => throw new InvalidOperationException(
                    $"Unsupported Building Entity type: {entityType}.")
            };
        }
    }
}
