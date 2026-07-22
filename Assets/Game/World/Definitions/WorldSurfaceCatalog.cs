using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [Serializable]
    public struct SurfaceAppearanceDefinition
    {
        public ushort Id;
        public Color Albedo;
        [Range(0f, 1f)] public float Metallic;
        [Range(0f, 1f)] public float Smoothness;
        [Range(0f, 1f)] public float Occlusion;
    }

    public readonly struct SurfaceAppearance
    {
        public readonly Color Albedo;
        public readonly float Metallic;
        public readonly float Smoothness;
        public readonly float Occlusion;

        public SurfaceAppearance(Color albedo, float metallic, float smoothness, float occlusion)
        {
            Albedo = albedo;
            Metallic = metallic;
            Smoothness = smoothness;
            Occlusion = occlusion;
        }

        public static SurfaceAppearance Lerp(in SurfaceAppearance a, in SurfaceAppearance b, float t)
        {
            return new SurfaceAppearance(
                Color.Lerp(a.Albedo, b.Albedo, t),
                Mathf.Lerp(a.Metallic, b.Metallic, t),
                Mathf.Lerp(a.Smoothness, b.Smoothness, t),
                Mathf.Lerp(a.Occlusion, b.Occlusion, t));
        }
    }

    [CreateAssetMenu(fileName = "WorldSurfaceCatalog", menuName = "Mini Civilization/World Surface Catalog")]
    public sealed class WorldSurfaceCatalog : ScriptableObject
    {
        [SerializeField] private List<SurfaceAppearanceDefinition> terrain = new();
        [SerializeField] private List<SurfaceAppearanceDefinition> water = new();

        public SurfaceAppearance ResolveTerrain(ushort id)
        {
            for (var i = 0; i < terrain.Count; i++)
            {
                if (terrain[i].Id == id)
                {
                    var item = terrain[i];
                    return new SurfaceAppearance(item.Albedo, item.Metallic, item.Smoothness, item.Occlusion);
                }
            }

            return DefaultSurfacePalette.ResolveTerrain(id);
        }

        public SurfaceAppearance ResolveWater(ushort id)
        {
            for (var i = 0; i < water.Count; i++)
            {
                if (water[i].Id == id)
                {
                    var item = water[i];
                    return new SurfaceAppearance(item.Albedo, item.Metallic, item.Smoothness, item.Occlusion);
                }
            }

            return DefaultSurfacePalette.ResolveWater(id);
        }
    }

    public static class DefaultSurfacePalette
    {
        public static SurfaceAppearance ResolveTerrain(ushort id)
        {
            return id switch
            {
                WorldMaterialIds.Rock => new SurfaceAppearance(new Color(0.34f, 0.35f, 0.37f), 0.02f, 0.25f, 0.95f),
                WorldMaterialIds.Sand => new SurfaceAppearance(new Color(0.76f, 0.64f, 0.38f), 0f, 0.18f, 1f),
                WorldMaterialIds.Snow => new SurfaceAppearance(new Color(0.9f, 0.94f, 0.98f), 0f, 0.42f, 1f),
                WorldMaterialIds.Mud => new SurfaceAppearance(new Color(0.25f, 0.19f, 0.12f), 0f, 0.12f, 0.9f),
                WorldMaterialIds.Grass => new SurfaceAppearance(new Color(0.25f, 0.5f, 0.18f), 0f, 0.16f, 1f),
                _ => new SurfaceAppearance(new Color(0.38f, 0.28f, 0.16f), 0f, 0.2f, 1f)
            };
        }

        public static SurfaceAppearance ResolveWater(ushort id)
        {
            return id switch
            {
                WorldMaterialIds.SeaWater => new SurfaceAppearance(new Color(0.05f, 0.25f, 0.52f, 0.78f), 0f, 0.88f, 0.9f),
                WorldMaterialIds.MarshWater => new SurfaceAppearance(new Color(0.18f, 0.31f, 0.17f, 0.8f), 0f, 0.72f, 0.82f),
                _ => new SurfaceAppearance(new Color(0.08f, 0.42f, 0.68f, 0.72f), 0f, 0.9f, 0.92f)
            };
        }
    }
}
