using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using UnityEngine;
using WorldEntityId = MiniCivilization.World.Domain.EntityId;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public abstract class EntityController : MonoBehaviour
    {
        [SerializeField, HideInInspector] private string entityClassName;

        public abstract EntityCategory Category { get; }
        public Entity BoundEntity { get; private set; }
        public WorldEntityId BoundEntityId => BoundEntity?.Id ?? WorldEntityId.None;
        public Type EntityClass => ResolveEntityClass();

        public void Bind(Entity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (!SupportsEntityType(entity.GetType()))
            {
                throw new InvalidOperationException(
                    $"Entity Controller '{name}' cannot bind entity type " +
                    $"{entity.GetType().Name}.");
            }

            if (BoundEntity != null)
            {
                Unbind();
            }

            BoundEntity = entity;
            Refresh();
            OnBound(entity);
        }

        public void Unbind()
        {
            var entity = BoundEntity;
            if (entity == null)
            {
                return;
            }

            BoundEntity = null;
            OnUnbound(entity);
        }

        public void Refresh()
        {
            var entity = BoundEntity;
            if (entity == null)
            {
                return;
            }

            var anchor = entity.AnchorCell;
            transform.localPosition = new Vector3(
                anchor.X,
                anchor.Y,
                anchor.Z);
            transform.localRotation = ToRotation(entity.Direction);
            OnRefreshed(entity);
        }

        public bool SupportsEntityType(Type entityType)
        {
            var configuredEntityClass = EntityClass;
            return configuredEntityClass != null
                && configuredEntityClass == entityType
                && EntityCategoryInfo.Supports(Category, entityType);
        }

        private Type ResolveEntityClass()
        {
            return string.IsNullOrWhiteSpace(entityClassName)
                ? null
                : Type.GetType(entityClassName, throwOnError: false);
        }

        protected virtual void OnBound(Entity entity)
        {
        }

        protected virtual void OnUnbound(Entity entity)
        {
        }

        protected virtual void OnRefreshed(Entity entity)
        {
        }

        private static Quaternion ToRotation(EntityDirection direction)
        {
            return direction switch
            {
                EntityDirection.North => Quaternion.identity,
                EntityDirection.East => Quaternion.Euler(0f, 90f, 0f),
                EntityDirection.South => Quaternion.Euler(0f, 180f, 0f),
                EntityDirection.West => Quaternion.Euler(0f, 270f, 0f),
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };
        }
    }
}
