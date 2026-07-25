using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Hydrology
{
    [DisallowMultipleComponent]
    public sealed class WorldHydrologyController : MonoBehaviour
    {
        private WorldData boundWorld;

        public WorldData BoundWorld => boundWorld;
        public HydrologyState State { get; private set; }
        public WorldChangeId LastAppliedChangeId { get; private set; }

        public event Action<HydrologyState> StateChanged;

        public void Bind(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            boundWorld = world;
            RebuildAll();
            LastAppliedChangeId = world.CurrentChangeId;
        }

        public void Unbind()
        {
            boundWorld = null;
            State = null;
            LastAppliedChangeId = WorldChangeId.None;
            StateChanged?.Invoke(null);
        }

        public void ApplyChanges(WorldChangeSet changeSet)
        {
            if (changeSet == null)
            {
                throw new ArgumentNullException(nameof(changeSet));
            }

            if (changeSet.World != boundWorld)
            {
                throw new InvalidOperationException(
                    "The change set belongs to a different world.");
            }

            if (changeSet.ChangeId <= LastAppliedChangeId)
            {
                return;
            }

            const WorldChangeType hydrologyChanges =
                WorldChangeType.CellStructure
                | WorldChangeType.WaterTopology;
            if ((changeSet.ChangeTypes & hydrologyChanges) != 0)
            {
                RebuildAll();
            }

            LastAppliedChangeId = changeSet.ChangeId;
        }

        public void RebuildAll()
        {
            if (boundWorld == null)
            {
                State = null;
                return;
            }

            State = new HydrologyState(
                boundWorld,
                WaterBodyResolver.Resolve(boundWorld));
            StateChanged?.Invoke(State);
        }
    }
}
