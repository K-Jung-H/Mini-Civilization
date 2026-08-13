using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Building
{
    public sealed class HouseEntity : global::MiniCivilization.World.Entities.BuildingEntity
    {
        private static readonly EntityActivityId IdleActivity = new("Idle");

        private readonly BuildingLayout layout;

        public HouseEntity(EntityData data, BuildingLayout layout) : base(data)
        {
            this.layout = layout
                ?? throw new System.ArgumentNullException(nameof(layout));
        }

        public override BuildingLayout Layout => layout;
        public override EntityActivityId Activity => IdleActivity;
        internal override bool RequiresTick => false;

        internal override void Tick(
            EntityRuntime runtime,
            float deltaTime)
        {
        }

        public override bool ValidatePlacement(
            in BuildingPlacementContext context)
        {
            return true;
        }
    }
}
