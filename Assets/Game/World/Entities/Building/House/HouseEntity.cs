using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Building
{
    public sealed class HouseEntity : global::MiniCivilization.World.Entities.BuildingEntity
    {
        private static readonly EntityActivityId IdleActivity = new("Idle");

        private static readonly CellOffset SupportOffset = new(0, 0, 0);
        private static readonly BuildingLayout HouseLayout = new(
            new[]
            {
                new BuildingOccupiedCell(
                    new CellOffset(0, 1, 0),
                    Array.Empty<CellOffset>())
            },
            new[] { SupportOffset });
        public HouseEntity(EntityData data) : base(data)
        {
        }

        public override BuildingLayout Layout => HouseLayout;
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
            return context.TryGetCell(SupportOffset, out var support)
                && support.HasTerrain;
        }
    }
}
