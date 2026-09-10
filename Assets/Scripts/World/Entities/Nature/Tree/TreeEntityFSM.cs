using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Nature
{
    public sealed class TreeEntityFSM : global::MiniCivilization.World.Entities.NatureEntityFSM
    {
        private static readonly EntityActivityId IdleActivity = new("Idle");

        public TreeEntityFSM(EntityData data) : base(data)
        {
        }

        public override EntityActivityId Activity => IdleActivity;
        internal override bool RequiresTick => false;

        internal override void Tick(
            EntitySystem runtime,
            float deltaTime)
        {
        }
    }
}
