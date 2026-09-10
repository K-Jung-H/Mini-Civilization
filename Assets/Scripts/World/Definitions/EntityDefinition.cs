using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Presentation;
using UnityEngine;
using WorldEntityId = MiniCivilization.World.Domain.EntityId;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(
        fileName = "EntityDefinition",
        menuName = "Mini Civilization/Entities/Entity Definition")]
    public sealed class EntityDefinition : ScriptableObject
    {
        [Serializable]
        private struct TraitRange
        {
            [SerializeField] private string id;
            [SerializeField] private float minimum;
            [SerializeField] private float maximum;

            public string Id => id;

            public EntityTrait Create(ref uint randomState)
            {
                var value = minimum
                    + (maximum - minimum) * NextRandom01(ref randomState);
                return new EntityTrait(id, value);
            }

            public void Validate(string definitionName)
            {
                if (string.IsNullOrWhiteSpace(id)
                    || !float.IsFinite(minimum)
                    || !float.IsFinite(maximum)
                    || maximum < minimum)
                {
                    throw new InvalidOperationException(
                        $"Entity definition '{definitionName}' has an invalid trait range.");
                }
            }
        }

        [SerializeField] private EntityCategory category;
        [SerializeField, Min(1)] private int typeId = 1;
        [SerializeField] private EntityView prefab;
        [SerializeField] private EntityFSMDefinition fsmDefinition;
        [SerializeField] private Sprite thumbnail;
        [SerializeField] private string displayName;
        [SerializeField] private string[] names = Array.Empty<string>();
        [SerializeField, Min(0)] private int minimumAge;
        [SerializeField, Min(0)] private int maximumAge;
        [SerializeField] private TraitRange[] traitRanges =
            Array.Empty<TraitRange>();

        public EntityView Prefab => prefab;
        public EntityFSMDefinition FSMDefinition => fsmDefinition;
        public EntityTypeKey TypeKey => typeId > 0
            && typeId <= ushort.MaxValue
            ? new EntityTypeKey(category, (ushort)typeId)
            : EntityTypeKey.None;
        public Sprite Thumbnail => thumbnail;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;

        public EntityData CreateData(
            WorldEntityId id,
            CellCoordinate anchorCell,
            EntityDirection direction)
        {
            ValidateInitialization();
            var typeKey = TypeKey;
            var randomState = CreateSeed(id, typeKey);
            var selectedName = SelectName(ref randomState);
            var age = NextRandomInclusive(
                ref randomState,
                minimumAge,
                maximumAge);
            var traits = new EntityTrait[traitRanges?.Length ?? 0];
            for (var index = 0; index < traits.Length; index++)
            {
                traits[index] = traitRanges[index].Create(ref randomState);
            }

            return new EntityData(
                id,
                typeKey,
                anchorCell,
                direction,
                new EntityAttributes(selectedName, age, traits));
        }

        public EntityFSM CreateFSM(EntityData data)
        {
            if (fsmDefinition == null)
            {
                throw new InvalidOperationException(
                    $"Entity definition '{name}' has no FSM Definition.");
            }

            return fsmDefinition.Create(data);
        }

        public void ValidateInitialization()
        {
            if (!TypeKey.IsValid || prefab == null || fsmDefinition == null)
            {
                throw new InvalidOperationException(
                    $"Entity definition '{name}' requires a valid Type Key, Prefab, and FSM Definition.");
            }

            fsmDefinition.ValidateInitialization();
            if (minimumAge < 0 || maximumAge < minimumAge)
            {
                throw new InvalidOperationException(
                    $"Entity definition '{name}' has an invalid age range.");
            }

            var ids = new System.Collections.Generic.HashSet<string>(
                StringComparer.Ordinal);
            for (var index = 0; index < (traitRanges?.Length ?? 0); index++)
            {
                var range = traitRanges[index];
                range.Validate(name);
                if (!ids.Add(range.Id))
                {
                    throw new InvalidOperationException(
                        $"Entity definition '{name}' contains duplicated trait ID '{range.Id}'.");
                }
            }
        }

        private string SelectName(ref uint randomState)
        {
            if (names == null || names.Length == 0)
            {
                return DisplayName;
            }

            var index = NextRandom(ref randomState, names.Length);
            return string.IsNullOrWhiteSpace(names[index])
                ? DisplayName
                : names[index];
        }

        private static uint CreateSeed(WorldEntityId id, EntityTypeKey typeKey)
        {
            var seed = id.Value
                ^ ((ulong)typeKey.Value << 32)
                ^ ((ulong)typeKey.Category << 56);
            var state = (uint)(seed ^ (seed >> 32));
            return state == 0 ? 0x9E3779B9u : state;
        }

        private static int NextRandomInclusive(
            ref uint state,
            int minimum,
            int maximum)
        {
            var range = checked(maximum - minimum + 1);
            return minimum + NextRandom(ref state, range);
        }

        private static int NextRandom(ref uint state, int maximum)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (int)(state % (uint)maximum);
        }

        private static float NextRandom01(ref uint state) =>
            (uint)NextRandom(ref state, 1 << 24) / 16777216f;

        private void OnValidate()
        {
            typeId = Mathf.Max(1, typeId);
            minimumAge = Mathf.Max(0, minimumAge);
            maximumAge = Mathf.Max(minimumAge, maximumAge);
        }
    }
}
