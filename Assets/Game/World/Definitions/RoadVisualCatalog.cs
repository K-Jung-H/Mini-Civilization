using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using UnityEngine;
using UnityEngine.Serialization;

namespace MiniCivilization.World.Definitions
{
    [Serializable]
    public sealed class RoadVisualDefinition
    {
        public RoadType Type = RoadType.Basic;
        public string DisplayName;
        public Sprite Thumbnail;
        [FormerlySerializedAs("CenterMask")]
        [FormerlySerializedAs("Turn2Mask")]
        public Texture2D CornerMask;
        [FormerlySerializedAs("Turn1Mask")]
        public Texture2D QuarterMask;
        [FormerlySerializedAs("HalfMask")]
        public Texture2D StraightMask;
        public SurfaceTextureProfile Surface = new();

        public bool HasShapeMasks => CornerMask != null
            && QuarterMask != null
            && StraightMask != null;

        public string Name => string.IsNullOrWhiteSpace(DisplayName)
            ? Type.ToString()
            : DisplayName;
    }

    public readonly struct RoadVisualAppearance
    {
        public readonly int CornerMaskLayer;
        public readonly int QuarterMaskLayer;
        public readonly int StraightMaskLayer;
        public readonly SurfaceAppearance Surface;

        internal RoadVisualAppearance(
            int cornerMaskLayer,
            int quarterMaskLayer,
            int straightMaskLayer,
            SurfaceAppearance surface)
        {
            CornerMaskLayer = cornerMaskLayer;
            QuarterMaskLayer = quarterMaskLayer;
            StraightMaskLayer = straightMaskLayer;
            Surface = surface;
        }
    }

    [CreateAssetMenu(fileName = "RoadVisualCatalog", menuName = "Mini Civilization/Road Visual Catalog")]
    public sealed class RoadVisualCatalog : ScriptableObject
    {
        private static readonly int ShapeMaskArrayProperty = Shader.PropertyToID(
            "_RoadShapeMaskArray");
        private static readonly int AlbedoArrayProperty = Shader.PropertyToID(
            "_RoadAlbedoArray");
        private static readonly int NormalArrayProperty = Shader.PropertyToID(
            "_RoadNormalArray");
        private static readonly int SurfaceArrayProperty = Shader.PropertyToID(
            "_RoadSurfaceArray");
        private const string ShapeMaskArrayKeyword = "_ROAD_SHAPE_MASK_ARRAY";
        private const string AlbedoArrayKeyword = "_ROAD_ALBEDO_ARRAY";
        private const string NormalArrayKeyword = "_ROAD_NORMAL_ARRAY";
        private const string SurfaceArrayKeyword = "_ROAD_SURFACE_ARRAY";

        [SerializeField, Min(1)] private int textureResolution = 256;
        [SerializeField] private TextureFormat textureArrayFormat = TextureFormat.RGBA32;
        [FormerlySerializedAs("textureArrayMipMaps")]
        [SerializeField] private bool surfaceTextureArrayMipMaps = true;
        [SerializeField] private List<RoadVisualDefinition> roads = new();
        [SerializeField, HideInInspector] private Texture2DArray shapeMaskArray;
        [SerializeField, HideInInspector] private Texture2DArray albedoArray;
        [SerializeField, HideInInspector] private Texture2DArray normalArray;
        [SerializeField, HideInInspector] private Texture2DArray surfaceArray;
        [SerializeField, HideInInspector] private SurfaceTextureArrayAvailability surfaceArrayAvailability;
        [SerializeField, HideInInspector] private bool shapeMaskArrayAvailable;
        [SerializeField, HideInInspector] private string bakedTextureArraySignature;

        private readonly Dictionary<RoadType, RoadVisualAppearance> cache = new();
        private bool cacheValid;

        public SurfaceTextureArrayAvailability SurfaceArrayAvailability => surfaceArrayAvailability;
        public bool ShapeMaskArrayAvailable => shapeMaskArrayAvailable;

        internal int TextureResolution => textureResolution;
        internal TextureFormat TextureArrayFormat => textureArrayFormat;
        internal bool SurfaceTextureArrayMipMaps => surfaceTextureArrayMipMaps;
        internal IReadOnlyList<RoadVisualDefinition> Roads => roads;
        internal Texture2DArray ShapeMaskArray => shapeMaskArray;
        internal Texture2DArray AlbedoArray => albedoArray;
        internal Texture2DArray NormalArray => normalArray;
        internal Texture2DArray SurfaceArray => surfaceArray;
        internal string BakedTextureArraySignature => bakedTextureArraySignature;

        public bool TryResolve(
            RoadType type,
            out RoadVisualAppearance appearance)
        {
            EnsureCache();
            return cache.TryGetValue(type, out appearance);
        }

        public void ApplyToMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            material.SetTexture(ShapeMaskArrayProperty, shapeMaskArray);
            material.SetTexture(AlbedoArrayProperty, albedoArray);
            material.SetTexture(NormalArrayProperty, normalArray);
            material.SetTexture(SurfaceArrayProperty, surfaceArray);
            SetKeyword(
                material,
                ShapeMaskArrayKeyword,
                shapeMaskArrayAvailable && shapeMaskArray != null);
            SetKeyword(
                material,
                AlbedoArrayKeyword,
                surfaceArrayAvailability.Albedo && albedoArray != null);
            SetKeyword(
                material,
                NormalArrayKeyword,
                surfaceArrayAvailability.Normal && normalArray != null);
            SetKeyword(
                material,
                SurfaceArrayKeyword,
                surfaceArrayAvailability.Mask && surfaceArray != null);
        }

        internal void AssignBakedTextureArrays(
            Texture2DArray bakedShapeMask,
            Texture2DArray bakedAlbedo,
            Texture2DArray bakedNormal,
            Texture2DArray bakedSurface,
            string signature)
        {
            shapeMaskArray = bakedShapeMask;
            albedoArray = bakedAlbedo;
            normalArray = bakedNormal;
            surfaceArray = bakedSurface;
            shapeMaskArrayAvailable = bakedShapeMask != null;
            surfaceArrayAvailability = new SurfaceTextureArrayAvailability(
                bakedAlbedo != null,
                bakedNormal != null,
                bakedSurface != null);
            bakedTextureArraySignature = signature;
        }

        private void OnEnable()
        {
            if (roads.Count == 0)
            {
                PopulateDefaultRoad();
            }

            cacheValid = false;
        }

        private void Reset()
        {
            PopulateDefaultRoad();
            cacheValid = false;
        }

        private void OnValidate()
        {
            textureResolution = Mathf.Max(1, textureResolution);
            if (textureArrayFormat is not TextureFormat.RGBA32 and not TextureFormat.RGBAHalf)
            {
                textureArrayFormat = TextureFormat.RGBA32;
            }

            cacheValid = false;
        }

        private void OnDisable()
        {
            cacheValid = false;
        }

        private void EnsureCache()
        {
            if (cacheValid)
            {
                return;
            }

            cache.Clear();
            for (var index = 0; index < roads.Count; index++)
            {
                var definition = roads[index];
                if (definition == null
                    || definition.Type == RoadType.None
                    || !definition.HasShapeMasks
                    || cache.ContainsKey(definition.Type))
                {
                    continue;
                }

                var layer = index + 1;
                cache.Add(definition.Type, new RoadVisualAppearance(
                    1 + index * 3,
                    2 + index * 3,
                    3 + index * 3,
                    CreateAppearance(definition.Surface, layer)));
            }

            cacheValid = true;
        }

        private void PopulateDefaultRoad()
        {
            roads = new List<RoadVisualDefinition>
            {
                new()
                {
                    Type = RoadType.Basic,
                    DisplayName = "기본 도로",
                    Surface = new SurfaceTextureProfile
                    {
                        Tint = new Color(0.34f, 0.24f, 0.14f),
                        Metallic = 0f,
                        Smoothness = 0.15f,
                        Occlusion = 1f
                    }
                }
            };
        }

        private static SurfaceAppearance CreateAppearance(
            SurfaceTextureProfile profile,
            int layer)
        {
            if (profile == null)
            {
                return new SurfaceAppearance(
                    Color.white,
                    0f,
                    0.2f,
                    1f,
                    layer,
                    1f);
            }

            return new SurfaceAppearance(
                profile.Tint,
                profile.Metallic,
                profile.Smoothness,
                profile.Occlusion,
                layer,
                profile.Tiling);
        }

        private static void SetKeyword(
            Material material,
            string keyword,
            bool enabled)
        {
            if (enabled) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }
    }
}
