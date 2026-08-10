using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Nature
{
    public sealed class TreeEntity : global::MiniCivilization.World.Entities.NatureEntity
    {
        private static readonly EntityActivityId IdleActivity = new("Idle");

        public TreeEntity(EntityData data) : base(data)
        {
        }

        public override EntityActivityId Activity => IdleActivity;
        internal override bool RequiresTick => false;

        internal override void Tick(
            EntityRuntime runtime,
            float deltaTime)
        {
        }
    }
}
