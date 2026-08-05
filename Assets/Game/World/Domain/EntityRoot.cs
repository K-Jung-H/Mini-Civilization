using System;

namespace MiniCivilization.World.Domain
{
    [Serializable]
    public readonly struct MovementProfile : IEquatable<MovementProfile>
    {
        public readonly ushort Height;
        public readonly ushort Step;
        public readonly ushort WaterRange;

        public MovementProfile(
            int height,
            int step,
            int waterRange)
        {
            Height = checked((ushort)Math.Clamp(
                height,
                1,
                ushort.MaxValue));
            Step = checked((ushort)Math.Clamp(
                step,
                0,
                ushort.MaxValue));
            WaterRange = checked((ushort)Math.Clamp(
                waterRange,
                0,
                ushort.MaxValue - 1));
        }

        public bool CanStand(CellView cell)
        {
            var surface = cell.SurfaceHeight;
            var path = cell.Path;
            return cell.HasTerrain
                && cell.Position.Y == surface.GroundCellY
                && path.OpenHeight >= Height
                && path.WaterDistance != ushort.MaxValue
                && path.WaterDistance <= WaterRange;
        }

        public bool CanMove(CellView from, CellView to)
        {
            var distance = Math.Abs(from.Position.X - to.Position.X)
                + Math.Abs(from.Position.Z - to.Position.Z);
            return distance == 1
                && CanStand(from)
                && CanStand(to)
                && Math.Abs(
                    from.SurfaceHeight.GroundHeight
                    - to.SurfaceHeight.GroundHeight) <= Step;
        }

        public bool Equals(MovementProfile other) =>
            Height == other.Height
            && Step == other.Step
            && WaterRange == other.WaterRange;

        public override bool Equals(object obj) =>
            obj is MovementProfile other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            Height,
            Step,
            WaterRange);
    }

    public abstract class EntityRoot
    {
        public int Id { get; }
        public CellCoordinate Cell { get; private set; }
        public abstract MovementProfile Movement { get; }

        protected EntityRoot(int id, CellCoordinate cell)
        {
            if (id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            Id = id;
            Cell = cell;
        }

        public bool CanMove(WorldContext context, CellCoordinate target)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (!context.World.Contains(Cell.X, Cell.Y, Cell.Z)
                || !context.World.Contains(target.X, target.Y, target.Z))
            {
                return false;
            }

            return Movement.CanMove(
                context.GetCell(Cell),
                context.GetCell(target));
        }

        public bool TryMove(WorldContext context, CellCoordinate target)
        {
            if (!CanMove(context, target))
            {
                return false;
            }

            Cell = target;
            return true;
        }
    }
}
