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
        private const float TargetThreshold = 0.01f;
        private const float RotationSpeed = 360f;

        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform cellScaleRoot;
        [SerializeField] private Transform localMotionRoot;
        [SerializeField] private EntityVisualMotionProfile visualMotionProfile;

        private EntityVisualMotionProfile.VisualMotionSettings
            currentVisualMotion;
        private WorldEntityRenderer rendererContext;
        private Vector3 wanderTarget;
        private float circleAngle;
        private float wanderElapsed;
        private float localMotionElapsed;
        private EntityActivityId visualMotionActivity;
        private EntityActivityPhase visualMotionPhase;
        private WorldEntityId visualMotionInteractionTarget;
        private EntityMoveType visualMotionMoveType;
        private bool currentUsesMoveVisual;
        private bool wayConstrained;
        private uint randomState;
        private float worldCellScale = 1f;

        public abstract EntityTypeKey TypeKey { get; }
        public abstract string EntityTypeName { get; }
        public abstract bool HasValidEntityType { get; }
        public EntityCategory Category => TypeKey.Category;
        public Entity BoundEntity { get; private set; }
        public WorldEntityId BoundEntityId =>
            BoundEntity?.Id ?? WorldEntityId.None;
        public Transform VisualRoot => visualRoot;
        public EntityVisualMotionProfile.RenderHeightBasis RenderHeightBasis =>
            currentVisualMotion.HeightBasis;
        public float JumpHeight => currentVisualMotion.JumpHeight
            * worldCellScale;
        protected EntityVisualMotionProfile.AnimationMode
            PresentationAnimationMode => currentVisualMotion.AnimatorMode;
        protected float PresentationAnimatorBlendValue =>
            currentVisualMotion.AnimatorBlendValue;
        public bool HasValidVisualRoot => visualRoot != null
            && visualRoot != transform
            && visualRoot.IsChildOf(transform)
            && HasValidCellScaleRoot
            && visualRoot.IsChildOf(cellScaleRoot);
        public bool HasValidCellScaleRoot => cellScaleRoot != null
            && cellScaleRoot != transform
            && cellScaleRoot.IsChildOf(transform);

        protected virtual void Update()
        {
            if (BoundEntity == null
                || localMotionRoot == null
                || visualRoot == null
                || wayConstrained
                || !visualRoot.gameObject.activeInHierarchy)
            {
                return;
            }

            UpdateLocalMotion(Time.deltaTime);
        }

        public void Bind(
            Entity entity,
            WorldEntityRenderer renderer)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            if (!HasValidEntityType || entity.TypeKey != TypeKey)
            {
                throw new InvalidOperationException(
                    $"Entity Controller '{name}' cannot bind entity type key "
                    + $"{entity.TypeKey}.");
            }

            if (!HasValidVisualRoot)
            {
                throw new InvalidOperationException(
                    $"Entity Controller '{name}' requires a CellScaleRoot with VisualRoot below it.");
            }

            if (BoundEntity != null)
            {
                Unbind();
            }

            BoundEntity = entity;
            rendererContext = renderer;
            worldCellScale = renderer.Runtime.Data.CellSize;
            cellScaleRoot.localScale = Vector3.one * worldCellScale;
            InitializeRandom(entity);
            ResetVisualMotion();
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
            rendererContext = null;
            worldCellScale = 1f;
            if (cellScaleRoot != null)
            {
                cellScaleRoot.localScale = Vector3.one;
            }
            OnUnbound(entity);
            ResetVisualMotion();
            SetVisualVisible(true);
        }

        public void ApplyRenderPose(
            Vector3 localPosition,
            EntityDirection direction)
        {
            transform.localPosition = localPosition;
            transform.localRotation = ToRotation(direction);
        }

        public void ApplyRenderPose(
            Vector3 localPosition,
            Quaternion localRotation)
        {
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
        }

        public void PreparePlacementPreview(float cellScale)
        {
            if (!HasValidVisualRoot)
            {
                throw new InvalidOperationException(
                    $"Entity Controller '{name}' requires a CellScaleRoot with VisualRoot below it.");
            }

            cellScaleRoot.localScale = Vector3.one * cellScale;
            if (localMotionRoot != null)
            {
                localMotionRoot.localPosition = Vector3.zero;
                localMotionRoot.localRotation = Quaternion.identity;
            }
        }

        public void SetWayConstrained(bool constrained)
        {
            if (wayConstrained == constrained)
            {
                return;
            }

            wayConstrained = constrained;
            if (constrained && localMotionRoot != null)
            {
                localMotionRoot.localPosition = Vector3.zero;
                localMotionRoot.localRotation = Quaternion.identity;
            }
        }

        public void RefreshState()
        {
            var entity = BoundEntity;
            if (entity == null)
            {
                return;
            }

            RefreshVisualMotion(entity);
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

        public Vector3 GetInteractionWorldPosition()
        {
            if (localMotionRoot != null)
            {
                return localMotionRoot.position;
            }

            return visualRoot != null ? visualRoot.position : transform.position;
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

        private void RefreshVisualMotion(Entity entity)
        {
            var moving = entity as DynamicEntity;
            var usesMoveVisual = moving is { IsMoving: true };
            var moveType = usesMoveVisual ? moving.MoveType : default;
            if (!usesMoveVisual
                && rendererContext != null
                && rendererContext.IsWaterSurface(entity.AnchorCell))
            {
                usesMoveVisual = true;
                moveType = EntityMoveType.Swim;
            }
            if (visualMotionActivity == entity.Activity
                && visualMotionPhase == entity.ActivityPhase
                && visualMotionInteractionTarget == entity.InteractionTargetId
                && currentUsesMoveVisual == usesMoveVisual
                && (!usesMoveVisual || visualMotionMoveType == moveType))
            {
                return;
            }

            visualMotionActivity = entity.Activity;
            visualMotionPhase = entity.ActivityPhase;
            visualMotionInteractionTarget = entity.InteractionTargetId;
            currentUsesMoveVisual = usesMoveVisual;
            visualMotionMoveType = moveType;
            currentVisualMotion = ResolveVisualMotion(entity, usesMoveVisual, moveType);
            localMotionElapsed = 0f;

            switch (currentVisualMotion.LocalMotion.Mode)
            {
                case EntityVisualMotionProfile.LocalMotionMode.Circle:
                    InitializeCircleAngle();
                    break;
                case EntityVisualMotionProfile.LocalMotionMode.Wander:
                    SelectWanderTarget();
                    break;
            }
        }

        private EntityVisualMotionProfile.VisualMotionSettings ResolveVisualMotion(
            Entity entity,
            bool usesMoveVisual,
            EntityMoveType moveType)
        {
            if (visualMotionProfile == null)
            {
                return default;
            }

            return usesMoveVisual
                ? visualMotionProfile.ResolveCellMove(moveType)
                : visualMotionProfile.ResolveState(
                    entity.Activity,
                    entity.ActivityPhase,
                    NextRandom());
        }

        private void UpdateLocalMotion(float deltaTime)
        {
            switch (currentVisualMotion.LocalMotion.Mode)
            {
                case EntityVisualMotionProfile.LocalMotionMode.Hold:
                    UpdateHold();
                    break;
                case EntityVisualMotionProfile.LocalMotionMode.ReturnToAnchor:
                    UpdateReturnToAnchor(deltaTime);
                    break;
                case EntityVisualMotionProfile.LocalMotionMode.Circle:
                    UpdateCircle(deltaTime);
                    break;
                case EntityVisualMotionProfile.LocalMotionMode.Wander:
                    UpdateWander(deltaTime);
                    break;
                case EntityVisualMotionProfile.LocalMotionMode.Wave:
                    UpdateWave(deltaTime);
                    break;
                case EntityVisualMotionProfile.LocalMotionMode.ApproachTarget:
                    UpdateApproachTarget(deltaTime);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void InitializeCircleAngle()
        {
            if (localMotionRoot == null)
            {
                return;
            }

            var position = localMotionRoot.localPosition;
            circleAngle = position.x * position.x + position.z * position.z
                > TargetThreshold * TargetThreshold
                ? Mathf.Atan2(position.x, position.z)
                : NextRandom01() * Mathf.PI * 2f;
        }

        private void UpdateCircle(float deltaTime)
        {
            var localMotion = currentVisualMotion.LocalMotion;
            if (localMotion.Radius <= 0f || localMotion.MoveSpeed <= 0f)
            {
                UpdateReturnToAnchor(deltaTime);
                return;
            }

            circleAngle = Mathf.Repeat(
                circleAngle + localMotion.MoveSpeed / localMotion.Radius * deltaTime,
                Mathf.PI * 2f);
            var target = new Vector3(
                Mathf.Sin(circleAngle) * localMotion.Radius,
                currentVisualMotion.VisualYOffset,
                Mathf.Cos(circleAngle) * localMotion.Radius);
            MoveLocalRoot(target, localMotion.MoveSpeed, deltaTime);

            var tangent = new Vector3(
                Mathf.Cos(circleAngle),
                0f,
                -Mathf.Sin(circleAngle));
            RotateTowardLocal(tangent, deltaTime);
        }

        private void UpdateWander(float deltaTime)
        {
            var localMotion = currentVisualMotion.LocalMotion;
            wanderElapsed += deltaTime;
            var difference = wanderTarget - localMotionRoot.localPosition;
            if (difference.sqrMagnitude <= TargetThreshold * TargetThreshold
                || localMotion.TurnInterval > 0f
                && wanderElapsed >= localMotion.TurnInterval)
            {
                SelectWanderTarget();
                difference = wanderTarget - localMotionRoot.localPosition;
            }

            MoveLocalRoot(
                wanderTarget,
                localMotion.MoveSpeed,
                deltaTime);
            if (difference.sqrMagnitude > 0f)
            {
                RotateTowardLocal(difference, deltaTime);
            }
        }

        private void SelectWanderTarget()
        {
            var angle = NextRandom01() * Mathf.PI * 2f;
            var distance = Mathf.Sqrt(NextRandom01())
                * currentVisualMotion.LocalMotion.Radius;
            wanderTarget = new Vector3(
                Mathf.Sin(angle) * distance,
                currentVisualMotion.VisualYOffset,
                Mathf.Cos(angle) * distance);
            wanderElapsed = 0f;
        }

        private void UpdateReturnToAnchor(float deltaTime)
        {
            var target = new Vector3(
                0f,
                currentVisualMotion.VisualYOffset,
                0f);
            MoveLocalRoot(
                target,
                currentVisualMotion.LocalMotion.ReturnSpeed,
                deltaTime);
            localMotionRoot.localRotation = Quaternion.RotateTowards(
                localMotionRoot.localRotation,
                Quaternion.identity,
                RotationSpeed * deltaTime);
        }

        private void UpdateHold()
        {
            var position = localMotionRoot.localPosition;
            position.y = currentVisualMotion.VisualYOffset;
            localMotionRoot.localPosition = position;
        }

        private void UpdateWave(float deltaTime)
        {
            localMotionElapsed += deltaTime;
            var localMotion = currentVisualMotion.LocalMotion;
            var position = localMotionRoot.localPosition;
            if (localMotion.ReturnSpeed > 0f)
            {
                var horizontal = Vector3.MoveTowards(
                    new Vector3(position.x, 0f, position.z),
                    Vector3.zero,
                    localMotion.ReturnSpeed * deltaTime);
                position.x = horizontal.x;
                position.z = horizontal.z;
                localMotionRoot.localRotation = Quaternion.RotateTowards(
                    localMotionRoot.localRotation,
                    Quaternion.identity,
                    RotationSpeed * deltaTime);
            }

            position.y = currentVisualMotion.VisualYOffset
                + Mathf.Sin(
                    localMotionElapsed
                    * localMotion.WaveFrequency
                    * Mathf.PI
                    * 2f)
                * localMotion.WaveAmplitude;
            localMotionRoot.localPosition = position;
        }

        private void UpdateApproachTarget(float deltaTime)
        {
            if (!TryGetInteractionTarget(out var target))
            {
                UpdateReturnToAnchor(deltaTime);
                return;
            }

            var parent = localMotionRoot.parent;
            var targetPosition = parent != null
                ? parent.InverseTransformPoint(target.GetInteractionWorldPosition())
                : target.GetInteractionWorldPosition();
            targetPosition.y = currentVisualMotion.VisualYOffset;

            var current = localMotionRoot.localPosition;
            var difference = targetPosition - current;
            difference.y = 0f;
            Vector3 separationDirection;
            if (difference.sqrMagnitude
                <= TargetThreshold * TargetThreshold)
            {
                var angle = DeterministicInteractionAngle(
                    BoundEntityId,
                    target.BoundEntityId);
                separationDirection = new Vector3(
                    Mathf.Sin(angle),
                    0f,
                    Mathf.Cos(angle));
            }
            else
            {
                separationDirection = -difference.normalized;
            }

            var localMotion = currentVisualMotion.LocalMotion;
            var destination = targetPosition
                + separationDirection * localMotion.InteractionDistance;
            if (localMotion.Radius > 0f)
            {
                var horizontal = new Vector2(destination.x, destination.z);
                if (horizontal.sqrMagnitude
                    > localMotion.Radius * localMotion.Radius)
                {
                    horizontal = horizontal.normalized * localMotion.Radius;
                    destination.x = horizontal.x;
                    destination.z = horizontal.y;
                }
            }

            MoveLocalRoot(
                destination,
                localMotion.MoveSpeed,
                deltaTime);
            if (localMotion.FaceTarget)
            {
                RotateTowardWorld(
                    target.GetInteractionWorldPosition()
                    - localMotionRoot.position,
                    deltaTime);
            }
        }

        private bool TryGetInteractionTarget(out EntityController target)
        {
            target = null;
            var entity = BoundEntity;
            if (entity == null
                || !entity.InteractionTargetId.IsValid
                || rendererContext == null
                || !rendererContext.TryGetView(entity.InteractionTargetId, out target)
                || target == null
                || target.BoundEntity == null
                || !target.BoundEntity.AnchorCell.Equals(entity.AnchorCell))
            {
                target = null;
                return false;
            }

            return true;
        }

        private void MoveLocalRoot(
            Vector3 target,
            float speed,
            float deltaTime)
        {
            var difference = target - localMotionRoot.localPosition;
            if (difference.sqrMagnitude
                <= TargetThreshold * TargetThreshold)
            {
                localMotionRoot.localPosition = target;
                return;
            }

            if (speed <= 0f)
            {
                return;
            }

            localMotionRoot.localPosition = Vector3.MoveTowards(
                localMotionRoot.localPosition,
                target,
                speed * deltaTime);
        }

        private void RotateTowardLocal(Vector3 direction, float deltaTime)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0f)
            {
                return;
            }

            localMotionRoot.localRotation = Quaternion.RotateTowards(
                localMotionRoot.localRotation,
                Quaternion.LookRotation(direction, Vector3.up),
                RotationSpeed * deltaTime);
        }

        private void RotateTowardWorld(Vector3 direction, float deltaTime)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0f)
            {
                return;
            }

            localMotionRoot.rotation = Quaternion.RotateTowards(
                localMotionRoot.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                RotationSpeed * deltaTime);
        }

        private void InitializeRandom(Entity entity)
        {
            var seed = entity.Id.Value
                ^ ((ulong)entity.TypeKey.Value << 32);
            randomState = (uint)(seed ^ (seed >> 32));
            if (randomState == 0)
            {
                randomState = 0x9E3779B9u;
            }
        }

        private uint NextRandom()
        {
            var state = randomState;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            randomState = state;
            return state;
        }

        private float NextRandom01() =>
            (NextRandom() & 0x00FFFFFFu) / 16777216f;

        private void ResetVisualMotion()
        {
            visualMotionActivity = EntityActivityId.None;
            visualMotionPhase = default;
            visualMotionInteractionTarget = WorldEntityId.None;
            visualMotionMoveType = default;
            currentUsesMoveVisual = false;
            currentVisualMotion = default;
            wanderElapsed = 0f;
            localMotionElapsed = 0f;
            if (localMotionRoot != null)
            {
                localMotionRoot.localPosition = Vector3.zero;
                localMotionRoot.localRotation = Quaternion.identity;
            }
        }

        private static float DeterministicInteractionAngle(
            WorldEntityId source,
            WorldEntityId target)
        {
            var hash = source.Value * 11400714819323198485ul
                ^ target.Value * 14029467366897019727ul;
            return (hash & 0x00FFFFFFul)
                / 16777216f
                * Mathf.PI
                * 2f;
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
