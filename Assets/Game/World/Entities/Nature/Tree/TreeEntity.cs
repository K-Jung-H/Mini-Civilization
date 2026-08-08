using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Nature
{
    public sealed class TreeEntity : global::MiniCivilization.World.Entities.NatureEntity
    {
        private enum State : byte
        {
            Idle
        }

        private State currentState = State.Idle;

        public TreeEntity(EntityData data) : base(data)
        {
        }

        public override int RenderStateKey => (int)currentState;
        internal override bool RequiresTick => false;

        internal override void Tick(
            EntityRuntime runtime,
            float deltaTime)
        {
        }
    }
}
