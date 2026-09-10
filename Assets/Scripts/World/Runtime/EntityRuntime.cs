using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;

namespace MiniCivilization.World.Runtime
{
    public sealed class EntityRuntime
    {
        internal EntityRuntime(EntityData data, EntityFSM fsm)
        {
            Bind(data, fsm);
        }

        public bool IsBound => Data != null;
        internal uint BindingVersion { get; private set; }
        private bool releasePrepared;
        public EntityData Data { get; private set; }
        public EntityFSM FSM { get; private set; }

        internal void Bind(EntityData data, EntityFSM fsm)
        {
            if (IsBound)
            {
                throw new InvalidOperationException(
                    "Entity Runtime slot is already bound.");
            }

            Data = data ?? throw new ArgumentNullException(nameof(data));
            FSM = fsm ?? throw new ArgumentNullException(nameof(fsm));
            releasePrepared = false;
            if (!ReferenceEquals(FSM.Data, Data))
            {
                Data = null;
                FSM = null;
                throw new ArgumentException(
                    "Entity FSM must use the Runtime's EntityData.",
                    nameof(fsm));
            }

            BindingVersion++;
        }

        internal void PrepareRelease()
        {
            if (!IsBound)
            {
                throw new InvalidOperationException(
                    "Entity Runtime slot is already released.");
            }

            if (releasePrepared)
            {
                return;
            }

            try
            {
                FSM.Release();
            }
            finally
            {
                releasePrepared = true;
            }
        }

        internal void CompleteRelease()
        {
            if (!IsBound || !releasePrepared)
            {
                throw new InvalidOperationException(
                    "Entity Runtime slot must finish FSM release before it is cleared.");
            }

            FSM = null;
            Data = null;
            releasePrepared = false;
            BindingVersion++;
        }

        internal bool IsBinding(EntityId id, uint version) =>
            IsBound && Id == id && BindingVersion == version;

        public EntityId Id => Data.Id;
        public EntityTypeKey TypeKey => Data.TypeKey;
        public CellCoordinate AnchorCell => Data.AnchorCell;
        public EntityDirection Direction => Data.Direction;
        public EntityActivityId Activity => FSM.Activity;
        public EntityActivityPhase ActivityPhase => FSM.ActivityPhase;
        public EntityId InteractionTargetId => FSM.InteractionTargetId;
        internal bool RequiresTick => FSM.RequiresTick;

        internal void Tick(EntitySystem system, float deltaTime) =>
            FSM.Tick(system, deltaTime);

        internal byte[] CapturePersistentPayload() =>
            FSM.CapturePersistentPayload();

        internal void RestorePersistentPayload(byte[] payload) =>
            FSM.RestorePersistentPayload(payload);
    }
}
