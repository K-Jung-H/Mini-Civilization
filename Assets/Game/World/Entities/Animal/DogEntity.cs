using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Animal
{
    public sealed class DogEntity : global::MiniCivilization.World.Entities.AnimalEntity
    {
        private enum State : byte
        {
            Idle
        }

        private State currentState = State.Idle;

        public DogEntity(EntityData data) : base(data)
        {
        }

        public override int RenderStateKey => (int)currentState;

        internal override void Tick(
            EntityRuntime runtime,
            float deltaTime)
        {
        }

        public override bool CanEnterWorld(
            WorldRuntime runtime,
            CellCoordinate currentCell,
            CellCoordinate nextCell) => false;
    }
}
