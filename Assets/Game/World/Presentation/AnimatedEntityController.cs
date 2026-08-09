using System;
using MiniCivilization.World.Entities;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    public abstract class AnimatedEntityController : EntityController
    {
        private static readonly int StateParameter =
            Animator.StringToHash("State");
        private static readonly int BlendParameter =
            Animator.StringToHash("Blend");
        private const float RotationSpeed = 360f;
        private const float TargetThreshold = 0.01f;
        private const float BlendDampTime = 0.15f;

        [SerializeField] private Animator animator;
        [SerializeField] private Transform localMotionRoot;
        [SerializeField] private EntityRenderProfile renderProfile;

        private EntityRenderProfile.RenderStateSettings currentRenderSettings;
        private Vector3 wanderTarget;
        private float circleAngle;
        private float wanderElapsed;
        private float targetBlend;
        private int currentRenderState = -1;
        private uint randomState;

        protected virtual void Update()
        {
            if (BoundEntity == null)
            {
                return;
            }

            var deltaTime = Time.deltaTime;
            if (animator != null)
            {
                animator.SetFloat(
                    BlendParameter,
                    targetBlend,
                    BlendDampTime,
                    deltaTime);
            }

            if (localMotionRoot == null
                || VisualRoot == null
                || !VisualRoot.gameObject.activeInHierarchy)
            {
                return;
            }

            switch (currentRenderSettings.LocalMotion.Mode)
            {
                case EntityRenderProfile.LocalMotionMode.Hold:
                    break;
                case EntityRenderProfile.LocalMotionMode.ReturnToAnchor:
                    UpdateReturnToAnchor(deltaTime);
                    break;
                case EntityRenderProfile.LocalMotionMode.Circle:
                    UpdateCircle(deltaTime);
                    break;
                case EntityRenderProfile.LocalMotionMode.Wander:
                    UpdateWander(deltaTime);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override void OnBound(Entity entity)
        {
            var seed = entity.Id.Value
                ^ ((ulong)entity.TypeKey.Value << 32);
            randomState = (uint)(seed ^ (seed >> 32));
            if (randomState == 0)
            {
                randomState = 0x9E3779B9u;
            }

            ResetRenderState();
        }

        protected override void OnUnbound(Entity entity)
        {
            ResetRenderState();
        }

        protected override void OnRefreshed(Entity entity)
        {
            if (currentRenderState == entity.RenderStateKey)
            {
                return;
            }

            currentRenderState = entity.RenderStateKey;
            currentRenderSettings = ResolveRenderSettings(currentRenderState);
            targetBlend = currentRenderSettings.Blend;

            if (animator != null)
            {
                animator.SetInteger(
                    StateParameter,
                    currentRenderSettings.AnimationStateKey);
            }

            switch (currentRenderSettings.LocalMotion.Mode)
            {
                case EntityRenderProfile.LocalMotionMode.Circle:
                    InitializeCircleAngle();
                    break;
                case EntityRenderProfile.LocalMotionMode.Wander:
                    SelectWanderTarget();
                    break;
            }
        }

        private void ResetRenderState()
        {
            currentRenderState = -1;
            currentRenderSettings = default;
            wanderElapsed = 0f;
            targetBlend = 0f;
            if (animator != null)
            {
                animator.SetInteger(StateParameter, 0);
                animator.SetFloat(BlendParameter, 0f);
            }

            if (localMotionRoot != null)
            {
                localMotionRoot.localPosition = Vector3.zero;
                localMotionRoot.localRotation = Quaternion.identity;
            }
        }

        private EntityRenderProfile.RenderStateSettings ResolveRenderSettings(
            int renderStateKey)
        {
            return renderProfile != null
                ? renderProfile.Resolve(renderStateKey)
                : default;
        }

        private void InitializeCircleAngle()
        {
            if (localMotionRoot == null)
            {
                return;
            }

            var position = localMotionRoot.localPosition;
            if (position.sqrMagnitude > TargetThreshold * TargetThreshold)
            {
                circleAngle = Mathf.Atan2(position.x, position.z);
            }
            else
            {
                circleAngle = NextRandom01() * Mathf.PI * 2f;
            }
        }

        private void UpdateCircle(float deltaTime)
        {
            var localMotion = currentRenderSettings.LocalMotion;
            var radius = localMotion.Radius;
            var moveSpeed = localMotion.MoveSpeed;
            if (radius <= 0f || moveSpeed <= 0f)
            {
                UpdateReturnToAnchor(deltaTime);
                return;
            }

            circleAngle = Mathf.Repeat(
                circleAngle + moveSpeed / radius * deltaTime,
                Mathf.PI * 2f);
            var target = new Vector3(
                Mathf.Sin(circleAngle) * radius,
                0f,
                Mathf.Cos(circleAngle) * radius);
            localMotionRoot.localPosition = Vector3.MoveTowards(
                localMotionRoot.localPosition,
                target,
                moveSpeed * deltaTime);

            var tangent = new Vector3(
                Mathf.Cos(circleAngle),
                0f,
                -Mathf.Sin(circleAngle));
            RotateToward(tangent, deltaTime);
        }

        private void UpdateWander(float deltaTime)
        {
            var localMotion = currentRenderSettings.LocalMotion;
            wanderElapsed += deltaTime;
            var difference = wanderTarget - localMotionRoot.localPosition;
            if (difference.sqrMagnitude
                    <= TargetThreshold * TargetThreshold
                || localMotion.TurnInterval > 0f
                && wanderElapsed >= localMotion.TurnInterval)
            {
                SelectWanderTarget();
                difference = wanderTarget - localMotionRoot.localPosition;
            }

            localMotionRoot.localPosition = Vector3.MoveTowards(
                localMotionRoot.localPosition,
                wanderTarget,
                localMotion.MoveSpeed * deltaTime);
            if (difference.sqrMagnitude > 0f)
            {
                RotateToward(difference, deltaTime);
            }
        }

        private void SelectWanderTarget()
        {
            var angle = NextRandom01() * Mathf.PI * 2f;
            var distance = Mathf.Sqrt(NextRandom01())
                * currentRenderSettings.LocalMotion.Radius;
            wanderTarget = new Vector3(
                Mathf.Sin(angle) * distance,
                0f,
                Mathf.Cos(angle) * distance);
            wanderElapsed = 0f;
        }

        private void UpdateReturnToAnchor(float deltaTime)
        {
            var returnSpeed = currentRenderSettings.LocalMotion.ReturnSpeed;
            if (returnSpeed <= 0f)
            {
                return;
            }

            localMotionRoot.localPosition = Vector3.MoveTowards(
                localMotionRoot.localPosition,
                Vector3.zero,
                returnSpeed * deltaTime);
            localMotionRoot.localRotation = Quaternion.RotateTowards(
                localMotionRoot.localRotation,
                Quaternion.identity,
                RotationSpeed * deltaTime);
        }

        private void RotateToward(
            Vector3 direction,
            float deltaTime)
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

        private float NextRandom01()
        {
            var state = randomState;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            randomState = state;
            return (state & 0x00FFFFFFu) / 16777216f;
        }
    }
}
