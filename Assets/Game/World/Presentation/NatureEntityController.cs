using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Entities.Nature;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    public enum NatureEntityType : ushort
    {
        None = 0,
        Tree = 1
    }

    public sealed class NatureEntityController : EntityController
    {
        [SerializeField] private NatureEntityType entityType;

        public override EntityTypeKey TypeKey => new(
            EntityCategory.Nature,
            (ushort)entityType);
        public override string EntityTypeName => entityType.ToString();
        public override bool HasValidEntityType =>
            entityType is NatureEntityType.Tree;

        public override Entity CreateStateMachine(EntityData data)
        {
            return entityType switch
            {
                NatureEntityType.Tree => new TreeEntity(data),
                _ => throw new InvalidOperationException(
                    $"Unsupported Nature Entity type: {entityType}.")
            };
        }
    }
}
