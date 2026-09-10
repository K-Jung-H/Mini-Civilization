using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Human
{
    public sealed class HumanEntityFSM : global::MiniCivilization.World.Entities.HumanEntityFSM
    {
        private static readonly EntityActivityId IdleActivity = new("Idle");

        public HumanEntityFSM(EntityData data) : base(data)
        {
        }

        public override EntityActivityId Activity => IdleActivity;

        internal override void Tick(
            EntitySystem runtime,
            float deltaTime)
        {
        }

        public override bool CanEnterWorld(
            WorldRuntime runtime,
            CellCoordinate currentCell,
            CellCoordinate nextCell) => false;
    }
}
