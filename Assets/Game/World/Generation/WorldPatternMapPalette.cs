using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Generation
{
    [CreateAssetMenu(
        fileName = "WorldPatternMapPalette",
        menuName = "Mini Civilization/World Pattern Map Palette")]
    public sealed class WorldPatternMapPalette : ScriptableObject
    {
        [Header("Terrain Pattern")]
        [SerializeField] private Color smooth = new(0.25f, 0.8f, 0.3f, 1f);
        [SerializeField] private Color rugged = new(0.9f, 0.5f, 0.15f, 1f);
        [SerializeField] private Color mountain = new(0.9f, 0.9f, 0.9f, 1f);
        [SerializeField] private Color canyon = new(0.65f, 0.15f, 0.75f, 1f);
        [SerializeField] private Color sea = new(0.1f, 0.45f, 0.9f, 1f);

        [Header("Hydrology Pattern")]
        [SerializeField] private Color noHydrology = Color.black;
        [SerializeField] private Color pond = new(0.15f, 0.75f, 0.7f, 0.65f);
        [SerializeField] private Color lake = new(0.08f, 0.55f, 0.95f, 0.65f);
        [SerializeField] private Color hydrologySea = new(0.1f, 0.45f, 0.9f, 0.65f);
        [SerializeField] private Color river = new(0.1f, 0.85f, 0.95f, 0.65f);

        internal Color ResolveTerrain(WorldPatternType pattern) => pattern switch
        {
            WorldPatternType.Smooth => smooth,
            WorldPatternType.Rugged => rugged,
            WorldPatternType.Mountain => mountain,
            WorldPatternType.Canyon => canyon,
            WorldPatternType.Sea => sea,
            _ => throw new ArgumentOutOfRangeException(nameof(pattern))
        };

        internal Color ResolveHydrology(WaterType type) => type switch
        {
            WaterType.None => noHydrology,
            WaterType.Pond => pond,
            WaterType.Lake => lake,
            WaterType.Sea => hydrologySea,
            WaterType.River => river,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
}
