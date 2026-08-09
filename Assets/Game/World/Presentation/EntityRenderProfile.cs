using System;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    [CreateAssetMenu(
        fileName = "Entity Render Profile",
        menuName = "Mini Civilization/Entities/Render Profile")]
    public sealed class EntityRenderProfile : ScriptableObject
    {
        public enum LocalMotionMode : byte
        {
            Hold,
            Circle,
            Wander,
            ReturnToAnchor
        }

        [Serializable]
        public struct LocalMotionSettings
        {
            [SerializeField] private LocalMotionMode mode;
            [SerializeField, Min(0f)] private float radius;
            [SerializeField, Min(0f)] private float moveSpeed;
            [SerializeField, Min(0f)] private float turnInterval;
            [SerializeField, Min(0f)] private float returnSpeed;

            public LocalMotionMode Mode => mode;
            public float Radius => radius;
            public float MoveSpeed => moveSpeed;
            public float TurnInterval => turnInterval;
            public float ReturnSpeed => returnSpeed;
        }

        [Serializable]
        public struct RenderStateSettings
        {
            [SerializeField] private int animationStateKey;
            [SerializeField] private float blend;
            [SerializeField] private LocalMotionSettings localMotion;

            public int AnimationStateKey => animationStateKey;
            public float Blend => blend;
            public LocalMotionSettings LocalMotion => localMotion;
        }

        [SerializeField] private RenderStateSettings[] renderStates;

        public RenderStateSettings Resolve(int renderStateKey)
        {
            if (renderStates == null
                || renderStateKey < 0
                || renderStateKey >= renderStates.Length)
            {
                return default;
            }

            return renderStates[renderStateKey];
        }
    }
}
