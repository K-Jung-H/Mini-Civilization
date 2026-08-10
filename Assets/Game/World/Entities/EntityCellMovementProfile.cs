using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using UnityEngine;
using UnityEngine.Serialization;

namespace MiniCivilization.World.Entities
{
    public sealed class EntityCellMovementRules
    {
        private readonly CellOffset[] neighborOffsets;

        public IReadOnlyList<CellOffset> NeighborOffsets => neighborOffsets;
        public float MaxUpHeight { get; }
        public float MaxDownHeight { get; }
        public float WalkDuration { get; }
        public float HeightTransitionDuration { get; }
        public float SwimDuration { get; }
        public bool CanEnterWater { get; }

        public EntityCellMovementRules(
            IReadOnlyList<CellOffset> neighborOffsets,
            float maxUpHeight,
            float maxDownHeight,
            float walkDuration,
            float heightTransitionDuration,
            float swimDuration,
            bool canEnterWater)
        {
            if (neighborOffsets == null || neighborOffsets.Count == 0)
            {
                throw new ArgumentException(
                    "Cell movement requires at least one neighbor offset.",
                    nameof(neighborOffsets));
            }

            if (maxUpHeight < 0f || !float.IsFinite(maxUpHeight))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxUpHeight));
            }

            if (maxDownHeight < 0f || !float.IsFinite(maxDownHeight))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDownHeight));
            }

            ValidateDuration(walkDuration, nameof(walkDuration));
            ValidateDuration(
                heightTransitionDuration,
                nameof(heightTransitionDuration));
            ValidateDuration(swimDuration, nameof(swimDuration));

            this.neighborOffsets = new CellOffset[neighborOffsets.Count];
            var uniqueOffsets = new HashSet<CellOffset>();
            for (var index = 0; index < neighborOffsets.Count; index++)
            {
                var offset = neighborOffsets[index];
                if (offset == default || !uniqueOffsets.Add(offset))
                {
                    throw new ArgumentException(
                        "Neighbor offsets must be unique and non-zero.",
                        nameof(neighborOffsets));
                }

                this.neighborOffsets[index] = offset;
            }

            MaxUpHeight = maxUpHeight;
            MaxDownHeight = maxDownHeight;
            WalkDuration = walkDuration;
            HeightTransitionDuration = heightTransitionDuration;
            SwimDuration = swimDuration;
            CanEnterWater = canEnterWater;
        }

        public bool Allows(
            CellCoordinate current,
            float currentSurfaceHeight,
            CellCoordinate next,
            float nextSurfaceHeight)
        {
            var heightDifference = nextSurfaceHeight
                - currentSurfaceHeight;
            if (heightDifference > MaxUpHeight
                || -heightDifference > MaxDownHeight)
            {
                return false;
            }

            var differenceX = next.X - current.X;
            var differenceZ = next.Z - current.Z;
            for (var index = 0; index < neighborOffsets.Length; index++)
            {
                var offset = neighborOffsets[index];
                if (offset.X == differenceX && offset.Z == differenceZ)
                {
                    return true;
                }
            }

            return false;
        }

        public float GetDuration(EntityMoveType moveType) => moveType switch
        {
            EntityMoveType.Walk => WalkDuration,
            EntityMoveType.HeightTransition => HeightTransitionDuration,
            EntityMoveType.Swim => SwimDuration,
            _ => throw new ArgumentOutOfRangeException(nameof(moveType))
        };

        private static void ValidateDuration(float value, string name)
        {
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    [CreateAssetMenu(
        fileName = "Entity Cell Movement Profile",
        menuName = "Mini Civilization/Entities/Cell Movement Profile")]
    public sealed class EntityCellMovementProfile : ScriptableObject
    {
        [Serializable]
        private struct NeighborOffset
        {
            [SerializeField] private int x;
            [SerializeField] private int z;

            public CellOffset ToCellOffset() => new(x, 0, z);
        }

        [SerializeField] private NeighborOffset[] neighborOffsets;
        [FormerlySerializedAs("maxUpHeightUnits")]
        [SerializeField, Min(0f)]
        [Tooltip("이동 가능한 최대 상승 표면 높이 차이입니다. 단위는 월드 좌표입니다.")]
        private float maxUpHeight = 1f;
        [FormerlySerializedAs("maxDownHeightUnits")]
        [SerializeField, Min(0f)]
        [Tooltip("이동 가능한 최대 하강 표면 높이 차이입니다. 단위는 월드 좌표입니다.")]
        private float maxDownHeight = 1f;
        [SerializeField, Min(0.01f)] private float walkDuration = 1f;
        [SerializeField, Min(0.01f)]
        private float heightTransitionDuration = 1f;
        [SerializeField, Min(0.01f)] private float swimDuration = 2f;
        [SerializeField] private bool canEnterWater;

        private EntityCellMovementRules cachedRules;

        public EntityCellMovementRules GetRuntimeRules()
        {
            if (cachedRules != null)
            {
                return cachedRules;
            }

            var offsets = new CellOffset[neighborOffsets?.Length ?? 0];
            for (var index = 0; index < offsets.Length; index++)
            {
                offsets[index] = neighborOffsets[index].ToCellOffset();
            }

            cachedRules = new EntityCellMovementRules(
                offsets,
                maxUpHeight,
                maxDownHeight,
                walkDuration,
                heightTransitionDuration,
                swimDuration,
                canEnterWater);
            return cachedRules;
        }

        private void OnValidate()
        {
            cachedRules = null;
        }
    }
}
