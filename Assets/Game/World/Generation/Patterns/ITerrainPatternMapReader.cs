namespace MiniCivilization.World.Generation.Patterns
{
    public interface ITerrainPatternMapReader
    {
        TerrainPatternCell GetCell(int absoluteX, int absoluteZ);
    }
}
