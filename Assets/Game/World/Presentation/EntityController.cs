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
        [SerializeField] private Transform visualRoot;

        public abstract EntityTypeKey TypeKey { get; }
        public abstract string EntityTypeName { get; }
        public abstract bool HasValidEntityType { get; }
        public EntityCategory Category => TypeKey.Category;
        public Entity BoundEntity { get; private set; }
        public WorldEntityId BoundEntityId => BoundEntity?.Id ?? WorldEntityId.None;
        public Transform VisualRoot => visualRoot;
        public bool HasValidVisualRoot => visualRoot != null
            && visualRoot != transform
            && visualRoot.IsChildOf(transform);

        public void Bind(Entity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (!HasValidEntityType || entity.TypeKey != TypeKey)
            {
                throw new InvalidOperationException(
                    $"Entity Controller '{name}' cannot bind entity type key "
                    + $"{entity.TypeKey}.");
            }

            if (BoundEntity != null)
            {
                Unbind();
            }

            BoundEntity = entity;
            OnBound(entity);
            RefreshState();
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
            SetVisualVisible(true);
        }

        public void ApplyRenderPose(
            Vector3 localPosition,
            EntityDirection direction)
        {
            transform.localPosition = localPosition;
            transform.localRotation = ToRotation(direction);
        }

        public void RefreshState()
        {
            var entity = BoundEntity;
            if (entity == null)
            {
                return;
            }

            OnRefreshed(entity);
        }

        public void SetVisualVisible(bool visible)
        {
            if (visualRoot != null
                && visualRoot.gameObject.activeSelf != visible)
            {
                visualRoot.gameObject.SetActive(visible);
            }
        }

        public abstract Entity CreateStateMachine(EntityData data);

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
