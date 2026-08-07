using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Animal
{
    public sealed class DogEntity : global::MiniCivilization.World.Entities.AnimalEntity
    {
        public DogEntity(EntityData data) : base(data)
        {
        }

        public override bool CanEnterWorld(
            WorldRuntime runtime,
            CellCoordinate currentCell,
            CellCoordinate nextCell) => false;
    }
}
