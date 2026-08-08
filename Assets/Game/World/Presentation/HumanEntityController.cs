using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Entities.Human;
using UnityEngine;
using HumanStateMachine = MiniCivilization.World.Entities.Human.HumanEntity;

namespace MiniCivilization.World.Presentation
{
    public enum HumanEntityType : ushort
    {
        None = 0,
        Human = 1
    }

    public sealed class HumanEntityController : EntityController
    {
        [SerializeField] private HumanEntityType entityType;

        public override EntityTypeKey TypeKey => new(
            EntityCategory.Human,
            (ushort)entityType);
        public override string EntityTypeName => entityType.ToString();
        public override bool HasValidEntityType =>
            entityType is HumanEntityType.Human;

        public override Entity CreateStateMachine(EntityData data)
        {
            return entityType switch
            {
                HumanEntityType.Human => new HumanStateMachine(data),
                _ => throw new InvalidOperationException(
                    $"Unsupported Human Entity type: {entityType}.")
            };
        }
    }
}
