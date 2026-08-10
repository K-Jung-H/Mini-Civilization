using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Human
{
    public sealed class HumanEntity : global::MiniCivilization.World.Entities.HumanEntity
    {
        private static readonly EntityActivityId IdleActivity = new("Idle");

        public HumanEntity(EntityData data) : base(data)
        {
        }

        public override EntityActivityId Activity => IdleActivity;

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
