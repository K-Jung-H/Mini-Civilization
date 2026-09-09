using MiniCivilization.World.Entities;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    public abstract class AnimatedEntityController : EntityController
    {
        private static readonly int ModeParameter =
            Animator.StringToHash("Mode");
        private static readonly int BlendParameter =
            Animator.StringToHash("Blend");
        private static readonly int MoveProgressParameter =
            Animator.StringToHash("MoveProgress");
        private const float BlendDampTime = 0.15f;

        [SerializeField] private Animator animator;

        protected override void Update()
        {
            base.Update();
            if (BoundEntity == null
                || animator == null
                || !animator.isActiveAndEnabled
                || !animator.isInitialized)
            {
                return;
            }

            animator.SetInteger(
                ModeParameter,
                (int)PresentationAnimationMode);
            animator.SetFloat(
                BlendParameter,
                PresentationAnimatorBlendValue,
                BlendDampTime,
                Time.deltaTime);
            var moveProgress = BoundEntity is DynamicEntity moving
                && moving.IsMoving
                && moving.MoveType == EntityMoveType.HeightTransition
                    ? moving.MoveProgress
                    : 0f;
            animator.SetFloat(
                MoveProgressParameter,
                moveProgress);
        }
    }
}
