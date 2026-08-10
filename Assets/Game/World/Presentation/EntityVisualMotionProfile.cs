using System;
using System.Collections.Generic;
using MiniCivilization.World.Entities;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    [CreateAssetMenu(
        fileName = "Entity Visual Motion Profile",
        menuName = "Mini Civilization/Entities/Visual Motion Profile")]
    public sealed class EntityVisualMotionProfile : ScriptableObject
    {
        public enum RenderHeightBasis : byte
        {
            GroundSurface,
            WaterSurface
        }

        public enum AnimationMode : byte
        {
            Ground,
            Air,
            Water
        }

        public enum LocalMotionMode : byte
        {
            Hold,
            Circle,
            Wander,
            ReturnToAnchor,
            Wave,
            ApproachTarget
        }

        [Serializable]
        public struct LocalMotionSettings
        {
            [SerializeField] private LocalMotionMode mode;
            [SerializeField, Min(0f)] private float radius;
            [SerializeField, Min(0f)] private float moveSpeed;
            [SerializeField, Min(0f)] private float turnInterval;
            [SerializeField, Min(0f)] private float returnSpeed;
            [SerializeField, Min(0f)] private float waveAmplitude;
            [SerializeField, Min(0f)] private float waveFrequency;
            [SerializeField, Min(0f)] private float interactionDistance;
            [SerializeField] private bool faceTarget;

            public LocalMotionMode Mode => mode;
            public float Radius => radius;
            public float MoveSpeed => moveSpeed;
            public float TurnInterval => turnInterval;
            public float ReturnSpeed => returnSpeed;
            public float WaveAmplitude => waveAmplitude;
            public float WaveFrequency => waveFrequency;
            public float InteractionDistance => interactionDistance;
            public bool FaceTarget => faceTarget;
        }

        [Serializable]
        public struct VisualMotionSettings
        {
            [SerializeField] private AnimationMode animatorMode;
            [SerializeField, Min(0f)] private float animatorBlendValue;
            [SerializeField] private RenderHeightBasis heightBasis;
            [SerializeField] private float visualYOffset;
            [SerializeField, Min(0f)] private float jumpHeight;
            [SerializeField] private LocalMotionSettings localMotion;

            public AnimationMode AnimatorMode => animatorMode;
            public float AnimatorBlendValue => animatorBlendValue;
            public RenderHeightBasis HeightBasis => heightBasis;
            public float VisualYOffset => visualYOffset;
            public float JumpHeight => jumpHeight;
            public LocalMotionSettings LocalMotion => localMotion;
        }

        [Serializable]
        public struct VisualVariant
        {
            [SerializeField] private string name;
            [SerializeField, Min(0)] private int selectWeight;
            [SerializeField] private VisualMotionSettings settings;

            public string Name => name;
            public int SelectWeight => selectWeight;
            public VisualMotionSettings Settings => settings;
        }

        [Serializable]
        public struct StateVisual
        {
            [SerializeField] private string stateName;
            [SerializeField] private EntityActivityPhase phase;
            [SerializeField] private VisualVariant[] variants;

            public string StateName => stateName;
            public EntityActivityPhase Phase => phase;
            public VisualVariant[] Variants => variants;
        }

        [Serializable]
        public struct CellMoveVisual
        {
            [SerializeField] private EntityMoveType moveType;
            [SerializeField] private VisualMotionSettings settings;

            public EntityMoveType MoveType => moveType;
            public VisualMotionSettings Settings => settings;
        }

        [SerializeField] private StateVisual[] stateVisuals;
        [SerializeField] private CellMoveVisual[] cellMoveVisuals;

        private Dictionary<ActivityPhaseKey, VisualVariant[]> stateLookup;
        private VisualMotionSettings[] cellMoveLookup;
        private bool[] definedCellMoveTypes;

        public VisualMotionSettings ResolveState(
            EntityActivityId activity,
            EntityActivityPhase phase,
            uint selection)
        {
            EnsureLookup();
            var key = new ActivityPhaseKey(activity, phase);
            if (!stateLookup.TryGetValue(key, out var variants))
            {
                throw new InvalidOperationException(
                    $"Visual Motion Profile '{name}' does not define state "
                    + $"'{activity}', phase {phase}.");
            }

            return Select(variants, selection, activity, phase);
        }

        public VisualMotionSettings ResolveCellMove(EntityMoveType moveType)
        {
            EnsureLookup();
            var index = (int)moveType;
            if (index < 0
                || index >= definedCellMoveTypes.Length
                || !definedCellMoveTypes[index])
            {
                throw new InvalidOperationException(
                    $"Visual Motion Profile '{name}' does not define Cell move "
                    + $"type {moveType}.");
            }

            return cellMoveLookup[index];
        }

        private void OnEnable() => ClearLookup();
        private void OnValidate() => ClearLookup();

        private void EnsureLookup()
        {
            if (stateLookup != null)
            {
                return;
            }

            stateLookup = new Dictionary<
                ActivityPhaseKey,
                VisualVariant[]>(stateVisuals?.Length ?? 0);
            if (stateVisuals != null)
            {
                for (var index = 0; index < stateVisuals.Length; index++)
                {
                    var entry = stateVisuals[index];
                    var activity = new EntityActivityId(entry.StateName);
                    var key = new ActivityPhaseKey(activity, entry.Phase);
                    ValidateVariants(entry.Variants, activity, entry.Phase);
                    if (!stateLookup.TryAdd(key, entry.Variants))
                    {
                        throw new InvalidOperationException(
                            $"Visual Motion Profile '{name}' contains duplicate "
                            + $"state '{activity}', phase {entry.Phase}.");
                    }
                }
            }

            var moveTypeCount = Enum.GetValues(typeof(EntityMoveType)).Length;
            cellMoveLookup = new VisualMotionSettings[moveTypeCount];
            definedCellMoveTypes = new bool[moveTypeCount];
            if (cellMoveVisuals == null)
            {
                return;
            }

            for (var index = 0; index < cellMoveVisuals.Length; index++)
            {
                var entry = cellMoveVisuals[index];
                if (!Enum.IsDefined(typeof(EntityMoveType), entry.MoveType))
                {
                    throw new InvalidOperationException(
                        $"Visual Motion Profile '{name}' contains an invalid "
                        + "move type.");
                }

                var moveIndex = (int)entry.MoveType;
                if (definedCellMoveTypes[moveIndex])
                {
                    throw new InvalidOperationException(
                        $"Visual Motion Profile '{name}' contains duplicate "
                        + $"move type {entry.MoveType}.");
                }

                cellMoveLookup[moveIndex] = entry.Settings;
                definedCellMoveTypes[moveIndex] = true;
            }
        }

        private void ClearLookup()
        {
            stateLookup = null;
            cellMoveLookup = null;
            definedCellMoveTypes = null;
        }

        private void ValidateVariants(
            VisualVariant[] variants,
            EntityActivityId activity,
            EntityActivityPhase phase)
        {
            if (variants == null || variants.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Visual Motion Profile '{name}' has no variants for "
                    + $"activity '{activity}', phase {phase}.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var totalWeight = 0;
            for (var index = 0; index < variants.Length; index++)
            {
                var variant = variants[index];
                if (string.IsNullOrWhiteSpace(variant.Name))
                {
                    throw new InvalidOperationException(
                        $"Visual Motion Profile '{name}' requires a name for "
                        + $"every '{activity}' variant.");
                }

                if (!names.Add(variant.Name))
                {
                    throw new InvalidOperationException(
                        $"Visual Motion Profile '{name}' contains duplicate "
                        + $"variant '{variant.Name}' for '{activity}'.");
                }

                totalWeight = checked(totalWeight + variant.SelectWeight);
            }

            if (totalWeight <= 0)
            {
                throw new InvalidOperationException(
                    $"Visual Motion Profile '{name}' requires a positive "
                    + $"variant weight for activity '{activity}', phase {phase}.");
            }
        }

        private VisualMotionSettings Select(
            VisualVariant[] variants,
            uint selection,
            EntityActivityId activity,
            EntityActivityPhase phase)
        {
            var totalWeight = 0;
            for (var index = 0; index < variants.Length; index++)
            {
                totalWeight += variants[index].SelectWeight;
            }

            var selected = (int)(selection % (uint)totalWeight);
            for (var index = 0; index < variants.Length; index++)
            {
                if (selected < variants[index].SelectWeight)
                {
                    return variants[index].Settings;
                }

                selected -= variants[index].SelectWeight;
            }

            throw new InvalidOperationException(
                $"Visual motion selection failed for '{activity}', phase {phase}.");
        }

        private readonly struct ActivityPhaseKey : IEquatable<ActivityPhaseKey>
        {
            private readonly EntityActivityId activity;
            private readonly EntityActivityPhase phase;

            public ActivityPhaseKey(
                EntityActivityId activity,
                EntityActivityPhase phase)
            {
                if (!activity.IsValid)
                {
                    throw new ArgumentOutOfRangeException(nameof(activity));
                }

                this.activity = activity;
                this.phase = phase;
            }

            public bool Equals(ActivityPhaseKey other) =>
                activity == other.activity && phase == other.phase;
            public override bool Equals(object obj) =>
                obj is ActivityPhaseKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(activity, phase);
        }
    }
}
