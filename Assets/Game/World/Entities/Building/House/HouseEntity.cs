using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Building
{
    public sealed class HouseEntity : global::MiniCivilization.World.Entities.BuildingEntity
    {
        private static readonly EntityActivityId IdleActivity = new("Idle");

        private readonly BuildingLayout layout;
        private readonly int maxTerrainCorrectionSteps;

        public HouseEntity(
            EntityData data,
            BuildingLayout layout,
            int maxTerrainCorrectionSteps) : base(data)
        {
            this.layout = layout
                ?? throw new System.ArgumentNullException(nameof(layout));
            this.maxTerrainCorrectionSteps = maxTerrainCorrectionSteps >= 0
                ? maxTerrainCorrectionSteps
                : throw new System.ArgumentOutOfRangeException(
                    nameof(maxTerrainCorrectionSteps));
        }

        public override BuildingLayout Layout => layout;
        public override int MaxTerrainCorrectionSteps =>
            maxTerrainCorrectionSteps;
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
