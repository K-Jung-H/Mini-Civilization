using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Runtime
{
    public sealed class EntityTypeRegistry
    {
        private readonly Dictionary<EntityTypeKey, EntityDefinition>
            definitions = new();

        public static EntityTypeRegistry Shared { get; } = new();

        public void Clear() => definitions.Clear();

        public void Register(
            EntityTypeKey typeKey,
            EntityDefinition definition)
        {
            if (!typeKey.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(typeKey));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (definition.TypeKey != typeKey)
            {
                throw new InvalidOperationException(
                    $"Entity definition '{definition.name}' owns type key "
                    + $"{definition.TypeKey}, not {typeKey}.");
            }

            if (!definitions.TryAdd(typeKey, definition))
            {
                throw new InvalidOperationException(
                    $"Entity type key {typeKey} is already registered.");
            }
        }

        public EntityRuntime Create(
            EntityData data,
            EntityRuntime reusableSlot = null)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (!definitions.TryGetValue(data.TypeKey, out var definition))
            {
                throw new InvalidOperationException(
                    $"Entity type key {data.TypeKey} is not registered.");
            }

            var fsm = definition.CreateFSM(data);
            if (fsm == null
                || !ReferenceEquals(fsm.Data, data)
                || fsm.TypeKey != data.TypeKey)
            {
                throw new InvalidOperationException(
                    $"Entity definition for type key {data.TypeKey} returned an invalid FSM.");
            }

            if (reusableSlot == null)
            {
                return new EntityRuntime(data, fsm);
            }

            reusableSlot.Bind(data, fsm);
            return reusableSlot;
        }

        public EntityData CreateData(
            EntityId id,
            EntityTypeKey typeKey,
            CellCoordinate anchorCell,
            EntityDirection direction)
        {
            if (!definitions.TryGetValue(typeKey, out var definition))
            {
                throw new InvalidOperationException(
                    $"Entity type key {typeKey} is not registered.");
            }

            return definition.CreateData(
                id,
                anchorCell,
                direction);
        }
    }
}
