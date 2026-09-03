using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    [CreateAssetMenu(
        fileName = "PatternMapPalette",
        menuName = "Mini Civilization/World/Pattern Map Palette")]
    public sealed class PatternMapPalette : ScriptableObject
    {
        [Header("Terrain Pattern")]
        [SerializeField] private Color smooth = new(0.25f, 0.8f, 0.3f, 1f);
        [SerializeField] private Color rugged = new(0.9f, 0.5f, 0.15f, 1f);
        [SerializeField] private Color mountain = new(0.9f, 0.9f, 0.9f, 1f);
        [SerializeField] private Color canyon = new(0.65f, 0.15f, 0.75f, 1f);

        [Header("Hydrology Pattern")]
        [SerializeField] private Color noHydrology = Color.black;
        [SerializeField] private Color pond = new(0.15f, 0.75f, 0.7f, 0.65f);
        [SerializeField] private Color lake = new(0.08f, 0.55f, 0.95f, 0.65f);
        [SerializeField] private Color sea = new(0.1f, 0.45f, 0.9f, 0.65f);
        [SerializeField] private Color river = new(0.1f, 0.85f, 0.95f, 0.65f);

        public Color ResolveTerrain(TerrainPatternType type) => type switch
        {
            TerrainPatternType.Smooth => smooth,
            TerrainPatternType.Rugged => rugged,
            TerrainPatternType.Mountain => mountain,
            TerrainPatternType.Canyon => canyon,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        public Color ResolveHydrology(WaterType type) => type switch
        {
            WaterType.None => noHydrology,
            WaterType.Pond => pond,
            WaterType.Lake => lake,
            WaterType.Sea => sea,
            WaterType.River => river,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
}
