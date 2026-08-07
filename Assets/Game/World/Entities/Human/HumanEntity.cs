using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Human
{
    public sealed class HumanEntity : global::MiniCivilization.World.Entities.HumanEntity
    {
        public HumanEntity(EntityData data) : base(data)
        {
        }

        public override bool CanEnterWorld(
            WorldRuntime runtime,
            CellCoordinate currentCell,
            CellCoordinate nextCell) => false;
    }
}
