using System;

namespace MiniCivilization.World.Domain
{
    public sealed class WorldContext
    {
        public WorldData World { get; }

        public WorldContext(WorldData world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
        }

        public CellView GetCell(CellCoordinate position)
        {
            if (!World.Contains(position.X, position.Y, position.Z))
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            return new CellView(World, position);
        }
    }

    public readonly struct CellView
    {
        private readonly WorldData world;

        public CellCoordinate Position { get; }
        public CellData Data => Current;
        public TerrainData Terrain => Current.Terrain;
        public WaterData Water => Current.Water;
        public bool HasTerrain => Current.HasTerrain;
        public bool HasWater => Current.HasWater;
        public byte WaterHeight => Current.WaterHeight;
        public EnvironmentData Environment => world.GetEnvironment(
            Position.X,
            Position.Z);
        public SurfaceHeightData SurfaceHeight =>
            world.Cache.GetSurfaceHeight(Position.X, Position.Z);
        public PathData Path => world.Cache.GetPathData(
            Position.X,
            Position.Y,
            Position.Z);

        private CellData Current => world.GetCell(
            Position.X,
            Position.Y,
            Position.Z);

        internal CellView(WorldData world, CellCoordinate position)
        {
            this.world = world;
            Position = position;
        }
    }
}
