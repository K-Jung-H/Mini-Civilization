using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [Serializable]
    public sealed class SurfaceTextureProfile
    {
        [Tooltip("표면의 기본 색상 텍스처입니다. 비어 있으면 흰색 텍스처와 Tint만 사용합니다.")]
        public Texture2D AlbedoTexture;

        [Tooltip("표면의 탄젠트 공간 노멀 텍스처입니다. 비어 있으면 평면 노멀을 사용합니다.")]
        public Texture2D NormalTexture;

        [Tooltip("R=Metallic, G=Occlusion, A=Smoothness로 해석되는 마스크 텍스처입니다.")]
        public Texture2D MaskTexture;

        [Tooltip("Albedo 텍스처에 곱해지는 색상입니다.")]
        public Color Tint = Color.white;

        [Min(0.01f)]
        [Tooltip("월드 좌표 기준 텍스처 반복 배율입니다.")]
        public float Tiling = 1f;

        [Range(0f, 1f)] public float Metallic;
        [Range(0f, 1f)] public float Smoothness = 0.2f;
        [Range(0f, 1f)] public float Occlusion = 1f;
    }

    [Serializable]
    public sealed class TerrainSurfaceDefinition
    {
        [Tooltip("이 프로필이 표현할 지형 면의 역할입니다.")]
        public SurfaceType Type = SurfaceType.Ground;

        public SurfaceTextureProfile Appearance = new();
    }

    [Serializable]
    public sealed class BiomeSurfaceSet
    {
        [Tooltip("아래 Surface 프로필들이 적용될 바이옴입니다.")]
        public TerrainBiome Biome = TerrainBiome.Field;

        [Tooltip("바이옴 전용 Ground, Cliff, Road, Riverbed 등의 표현입니다.")]
        public List<TerrainSurfaceDefinition> Surfaces = new();
    }

    [Serializable]
    public struct SurfaceTextureArrayAvailability
    {
        [SerializeField] private bool albedo;
        [SerializeField] private bool normal;
        [SerializeField] private bool mask;

        public readonly bool Albedo => albedo;
        public readonly bool Normal => normal;
        public readonly bool Mask => mask;

        internal SurfaceTextureArrayAvailability(bool albedo, bool normal, bool mask)
        {
            this.albedo = albedo;
            this.normal = normal;
            this.mask = mask;
        }
    }

    public readonly struct SurfaceAppearance
    {
        public readonly Color Albedo;
        public readonly float Metallic;
        public readonly float Smoothness;
        public readonly float Occlusion;
        public readonly Vector2 TextureLayers;
        public readonly Vector2 TextureWeights;
        public readonly Vector2 TextureScales;

        public SurfaceAppearance(
            Color albedo,
            float metallic,
            float smoothness,
            float occlusion,
            float textureLayer = 0f,
            float textureScale = 1f)
        {
            Albedo = albedo;
            Metallic = metallic;
            Smoothness = smoothness;
            Occlusion = occlusion;
            TextureLayers = new Vector2(textureLayer, 0f);
            TextureWeights = new Vector2(1f, 0f);
            TextureScales = new Vector2(textureScale, 1f);
        }

        private SurfaceAppearance(
            Color albedo,
            float metallic,
            float smoothness,
            float occlusion,
            Vector2 textureLayers,
            Vector2 textureWeights,
            Vector2 textureScales)
        {
            Albedo = albedo;
            Metallic = metallic;
            Smoothness = smoothness;
            Occlusion = occlusion;
            TextureLayers = textureLayers;
            TextureWeights = textureWeights;
            TextureScales = textureScales;
        }

        public static SurfaceAppearance Lerp(in SurfaceAppearance a, in SurfaceAppearance b, float t)
        {
            t = Mathf.Clamp01(t);
            var layers = Vector2.zero;
            var weights = Vector2.zero;
            var scales = Vector2.one;
            var count = 0;
            AddLayers(in a, 1f - t, ref layers, ref weights, ref scales, ref count);
            AddLayers(in b, t, ref layers, ref weights, ref scales, ref count);
            NormalizeWeights(ref weights);

            return new SurfaceAppearance(
                Color.Lerp(a.Albedo, b.Albedo, t),
                Mathf.Lerp(a.Metallic, b.Metallic, t),
                Mathf.Lerp(a.Smoothness, b.Smoothness, t),
                Mathf.Lerp(a.Occlusion, b.Occlusion, t),
                layers,
                weights,
                scales);
        }

        public float GetLayer(int index) => TextureLayers[index];
        public float GetWeight(int index) => TextureWeights[index];
        public float GetScale(int index) => TextureScales[index];

        private static void AddLayers(
            in SurfaceAppearance source,
            float multiplier,
            ref Vector2 layers,
            ref Vector2 weights,
            ref Vector2 scales,
            ref int count)
        {
            if (multiplier <= 0f)
            {
                return;
            }

            for (var sourceIndex = 0; sourceIndex < 2; sourceIndex++)
            {
                var weight = source.GetWeight(sourceIndex) * multiplier;
                if (weight <= 0.00001f)
                {
                    continue;
                }

                var layer = source.GetLayer(sourceIndex);
                var existingIndex = FindLayer(layers, count, layer);
                if (existingIndex >= 0)
                {
                    weights[existingIndex] += weight;
                    continue;
                }

                if (count < 2)
                {
                    layers[count] = layer;
                    weights[count] = weight;
                    scales[count] = source.GetScale(sourceIndex);
                    count++;
                    continue;
                }

                var smallestIndex = FindSmallestWeight(weights);
                if (weight > weights[smallestIndex])
                {
                    layers[smallestIndex] = layer;
                    weights[smallestIndex] = weight;
                    scales[smallestIndex] = source.GetScale(sourceIndex);
                }
            }
        }

        private static int FindLayer(Vector2 layers, int count, float layer)
        {
            for (var i = 0; i < count; i++)
            {
                if (Mathf.Abs(layers[i] - layer) < 0.001f)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindSmallestWeight(Vector2 weights)
        {
            return weights.x <= weights.y ? 0 : 1;
        }

        private static void NormalizeWeights(ref Vector2 weights)
        {
            var sum = weights.x + weights.y;
            if (sum <= 0.00001f)
            {
                weights = new Vector2(1f, 0f);
                return;
            }

            weights /= sum;
        }
    }

    [CreateAssetMenu(fileName = "WorldSurfaceCatalog", menuName = "Mini Civilization/World Surface Catalog")]
    public sealed class WorldSurfaceCatalog : ScriptableObject
    {
        private static readonly int AlbedoArrayProperty = Shader.PropertyToID("_SurfaceAlbedoArray");
        private static readonly int NormalArrayProperty = Shader.PropertyToID("_SurfaceNormalArray");
        private static readonly int MaskArrayProperty = Shader.PropertyToID("_SurfaceMaskArray");
        private const string AlbedoArrayKeyword = "_WORLD_ALBEDO_ARRAY";
        private const string NormalArrayKeyword = "_WORLD_NORMAL_ARRAY";
        private const string MaskArrayKeyword = "_WORLD_MASK_ARRAY";

        [SerializeField, Min(1)]
        [Tooltip("카탈로그의 모든 텍스처를 통일할 Texture2DArray 한 레이어의 해상도입니다.")]
        private int textureResolution = 256;

        [SerializeField]
        [Tooltip("모든 Albedo/Normal/Mask Texture2DArray가 공통으로 사용하는 픽셀 포맷입니다.")]
        private TextureFormat textureArrayFormat = TextureFormat.RGBA32;

        [SerializeField]
        [Tooltip("모든 Texture2DArray에 MipMap을 동일하게 생성합니다.")]
        private bool textureArrayMipMaps = true;

        [SerializeField]
        [Tooltip("특정 바이옴 프로필이 없을 때 사용하는 SurfaceType별 공통 표현입니다.")]
        private List<TerrainSurfaceDefinition> commonTerrain = new();

        [SerializeField]
        [Tooltip("TerrainBiome별 Ground, Cliff, Road, Riverbed 등의 재질 프로필입니다.")]
        private List<BiomeSurfaceSet> biomes = new();

        [SerializeField]
        [Tooltip("수면에 공통으로 사용하는 재질 프로필입니다.")]
        private SurfaceTextureProfile waterSurface = new()
        {
            Tint = new Color(0.08f, 0.42f, 0.68f, 0.72f),
            Metallic = 0f,
            Smoothness = 0.72f,
            Occlusion = 0.9f
        };

        private readonly Dictionary<SurfaceType, SurfaceAppearance> commonCache = new();
        private readonly Dictionary<TerrainSurfaceKey, SurfaceAppearance> biomeCache = new();
        private SurfaceAppearance waterAppearance;
        [SerializeField, HideInInspector] private Texture2DArray terrainAlbedoArray;
        [SerializeField, HideInInspector] private Texture2DArray terrainNormalArray;
        [SerializeField, HideInInspector] private Texture2DArray terrainMaskArray;
        [SerializeField, HideInInspector] private Texture2DArray waterAlbedoArray;
        [SerializeField, HideInInspector] private Texture2DArray waterNormalArray;
        [SerializeField, HideInInspector] private Texture2DArray waterMaskArray;
        [SerializeField, HideInInspector] private SurfaceTextureArrayAvailability terrainArrayAvailability;
        [SerializeField, HideInInspector] private SurfaceTextureArrayAvailability waterArrayAvailability;
        [SerializeField, HideInInspector] private string bakedTextureArraySignature;
        private bool cacheValid;

        public SurfaceTextureArrayAvailability TerrainArrayAvailability => terrainArrayAvailability;
        public SurfaceTextureArrayAvailability WaterArrayAvailability => waterArrayAvailability;

        internal int TextureResolution => textureResolution;
        internal TextureFormat TextureArrayFormat => textureArrayFormat;
        internal bool TextureArrayMipMaps => textureArrayMipMaps;
        internal IReadOnlyList<TerrainSurfaceDefinition> CommonTerrainDefinitions => commonTerrain;
        internal IReadOnlyList<BiomeSurfaceSet> BiomeSurfaceSets => biomes;
        internal SurfaceTextureProfile WaterSurfaceProfile => waterSurface;
        internal string BakedTextureArraySignature => bakedTextureArraySignature;
        internal Texture2DArray TerrainAlbedoArray => terrainAlbedoArray;
        internal Texture2DArray TerrainNormalArray => terrainNormalArray;
        internal Texture2DArray TerrainMaskArray => terrainMaskArray;
        internal Texture2DArray WaterAlbedoArray => waterAlbedoArray;
        internal Texture2DArray WaterNormalArray => waterNormalArray;
        internal Texture2DArray WaterMaskArray => waterMaskArray;

        internal void AssignBakedTextureArrays(
            Texture2DArray bakedTerrainAlbedo,
            Texture2DArray bakedTerrainNormal,
            Texture2DArray bakedTerrainMask,
            Texture2DArray bakedWaterAlbedo,
            Texture2DArray bakedWaterNormal,
            Texture2DArray bakedWaterMask,
            string signature)
        {
            terrainAlbedoArray = bakedTerrainAlbedo;
            terrainNormalArray = bakedTerrainNormal;
            terrainMaskArray = bakedTerrainMask;
            waterAlbedoArray = bakedWaterAlbedo;
            waterNormalArray = bakedWaterNormal;
            waterMaskArray = bakedWaterMask;
            terrainArrayAvailability = new SurfaceTextureArrayAvailability(
                bakedTerrainAlbedo != null,
                bakedTerrainNormal != null,
                bakedTerrainMask != null);
            waterArrayAvailability = new SurfaceTextureArrayAvailability(
                bakedWaterAlbedo != null,
                bakedWaterNormal != null,
                bakedWaterMask != null);
            bakedTextureArraySignature = signature;
        }

        public SurfaceAppearance ResolveTerrain(TerrainBiome biome, SurfaceType type)
        {
            EnsureRuntimeCache();
            if (biomeCache.TryGetValue(new TerrainSurfaceKey(biome, type), out var exact))
            {
                return exact;
            }

            if (commonCache.TryGetValue(type, out var common))
            {
                return common;
            }

            if (type != SurfaceType.Ground
                && biomeCache.TryGetValue(new TerrainSurfaceKey(biome, SurfaceType.Ground), out var biomeGround))
            {
                return biomeGround;
            }

            if (commonCache.TryGetValue(SurfaceType.Ground, out var commonGround))
            {
                return commonGround;
            }

            return DefaultSurfacePalette.ResolveTerrain(biome, type);
        }

        public SurfaceAppearance ResolveWater()
        {
            EnsureRuntimeCache();
            return waterSurface != null
                ? waterAppearance
                : DefaultSurfacePalette.ResolveWater();
        }

        public void ApplyToMaterials(
            Material terrainMaterial,
            Material waterMaterial)
        {
            EnsureRuntimeCache();
            ApplyArrays(
                terrainMaterial,
                terrainAlbedoArray,
                terrainNormalArray,
                terrainMaskArray,
                terrainArrayAvailability);
            ApplyArrays(
                waterMaterial,
                waterAlbedoArray,
                waterNormalArray,
                waterMaskArray,
                waterArrayAvailability);
        }

        private void OnEnable()
        {
            if (commonTerrain.Count == 0 && biomes.Count == 0)
            {
                PopulateDefaultProfiles();
            }
            else if (waterSurface == null)
            {
                waterSurface = CreateWaterProfile(
                    new Color(0.08f, 0.42f, 0.68f, 0.72f),
                    0.72f);
            }

            cacheValid = false;
        }

        private void Reset()
        {
            PopulateDefaultProfiles();
            InvalidateRuntimeCache();
        }

        [ContextMenu("Restore Default Surface Profiles")]
        private void RestoreDefaultProfiles()
        {
            PopulateDefaultProfiles();
            InvalidateRuntimeCache();
        }

        private void OnValidate()
        {
            textureResolution = Mathf.Max(1, textureResolution);
            if (textureArrayFormat is not TextureFormat.RGBA32 and not TextureFormat.RGBAHalf)
            {
                textureArrayFormat = TextureFormat.RGBA32;
            }

            InvalidateRuntimeCache();
        }

        private void OnDisable() => InvalidateRuntimeCache();

        private void EnsureRuntimeCache()
        {
            if (cacheValid)
            {
                return;
            }

            commonCache.Clear();
            biomeCache.Clear();
            waterAppearance = default;

            var terrainProfiles = new List<SurfaceTextureProfile> { null };
            AddCommonProfiles(terrainProfiles);
            AddBiomeProfiles(terrainProfiles);

            var waterProfiles = new List<SurfaceTextureProfile> { null };
            AddWaterProfile(waterProfiles);

            cacheValid = true;
        }

        private void PopulateDefaultProfiles()
        {
            commonTerrain = new List<TerrainSurfaceDefinition>
            {
                CreateTerrainDefinition(SurfaceType.Cliff, new Color(0.34f, 0.35f, 0.37f), 0.02f, 0.25f),
                CreateTerrainDefinition(SurfaceType.Road, new Color(0.34f, 0.24f, 0.14f), 0f, 0.15f),
                CreateTerrainDefinition(SurfaceType.Riverbed, new Color(0.25f, 0.27f, 0.25f), 0f, 0.16f),
                CreateTerrainDefinition(SurfaceType.Lakebed, new Color(0.25f, 0.19f, 0.12f), 0f, 0.12f),
                CreateTerrainDefinition(SurfaceType.Seabed, new Color(0.76f, 0.64f, 0.38f), 0f, 0.18f),
                CreateTerrainDefinition(SurfaceType.Shore, new Color(0.76f, 0.64f, 0.38f), 0f, 0.18f)
            };

            biomes = new List<BiomeSurfaceSet>
            {
                CreateBiomeSet(TerrainBiome.Field, new Color(0.25f, 0.5f, 0.18f)),
                CreateBiomeSet(TerrainBiome.Forest, new Color(0.16f, 0.38f, 0.12f)),
                CreateBiomeSet(TerrainBiome.Desert, new Color(0.76f, 0.64f, 0.38f)),
                CreateBiomeSet(TerrainBiome.Snow, new Color(0.9f, 0.94f, 0.98f)),
                CreateBiomeSet(TerrainBiome.Wetland, new Color(0.25f, 0.19f, 0.12f)),
                CreateBiomeSet(TerrainBiome.Mountain, new Color(0.34f, 0.35f, 0.37f))
            };

            biomes[(int)TerrainBiome.Snow - 1].Surfaces.Add(
                CreateTerrainDefinition(SurfaceType.Cliff, new Color(0.62f, 0.67f, 0.72f), 0f, 0.3f));
            biomes[(int)TerrainBiome.Desert - 1].Surfaces.Add(
                CreateTerrainDefinition(SurfaceType.Road, new Color(0.46f, 0.34f, 0.19f), 0f, 0.14f));
            biomes[(int)TerrainBiome.Forest - 1].Surfaces.Add(
                CreateTerrainDefinition(SurfaceType.Riverbed, new Color(0.19f, 0.24f, 0.18f), 0f, 0.12f));

            waterSurface = CreateWaterProfile(
                new Color(0.08f, 0.42f, 0.68f, 0.72f),
                0.72f);
        }

        private static BiomeSurfaceSet CreateBiomeSet(TerrainBiome biome, Color tint)
        {
            return new BiomeSurfaceSet
            {
                Biome = biome,
                Surfaces = new List<TerrainSurfaceDefinition>
                {
                    CreateTerrainDefinition(SurfaceType.Ground, tint, 0f, 0.16f)
                }
            };
        }

        private static TerrainSurfaceDefinition CreateTerrainDefinition(
            SurfaceType type,
            Color tint,
            float metallic,
            float smoothness)
        {
            return new TerrainSurfaceDefinition
            {
                Type = type,
                Appearance = new SurfaceTextureProfile
                {
                    Tint = tint,
                    Metallic = metallic,
                    Smoothness = smoothness,
                    Occlusion = 1f
                }
            };
        }

        private static SurfaceTextureProfile CreateWaterProfile(
            Color tint,
            float smoothness)
        {
            return new SurfaceTextureProfile
            {
                Tint = tint,
                Metallic = 0f,
                Smoothness = smoothness,
                Occlusion = 0.9f
            };
        }

        private void AddCommonProfiles(List<SurfaceTextureProfile> profiles)
        {
            for (var i = 0; i < commonTerrain.Count; i++)
            {
                var definition = commonTerrain[i];
                if (definition == null || definition.Appearance == null)
                {
                    continue;
                }

                var layer = profiles.Count;
                profiles.Add(definition.Appearance);
                commonCache[definition.Type] = CreateAppearance(definition.Appearance, layer);
            }
        }

        private void AddBiomeProfiles(List<SurfaceTextureProfile> profiles)
        {
            for (var biomeIndex = 0; biomeIndex < biomes.Count; biomeIndex++)
            {
                var set = biomes[biomeIndex];
                if (set?.Surfaces == null)
                {
                    continue;
                }

                for (var surfaceIndex = 0; surfaceIndex < set.Surfaces.Count; surfaceIndex++)
                {
                    var definition = set.Surfaces[surfaceIndex];
                    if (definition == null || definition.Appearance == null)
                    {
                        continue;
                    }

                    var layer = profiles.Count;
                    profiles.Add(definition.Appearance);
                    biomeCache[new TerrainSurfaceKey(set.Biome, definition.Type)] =
                        CreateAppearance(definition.Appearance, layer);
                }
            }
        }

        private void AddWaterProfile(List<SurfaceTextureProfile> profiles)
        {
            if (waterSurface == null)
            {
                return;
            }

            var layer = profiles.Count;
            profiles.Add(waterSurface);
            waterAppearance = CreateAppearance(waterSurface, layer);
        }

        private static SurfaceAppearance CreateAppearance(SurfaceTextureProfile profile, int layer)
        {
            return new SurfaceAppearance(
                profile.Tint,
                profile.Metallic,
                profile.Smoothness,
                profile.Occlusion,
                layer,
                profile.Tiling);
        }

        private static void ApplyArrays(
            Material material,
            Texture2DArray albedo,
            Texture2DArray normal,
            Texture2DArray mask,
            SurfaceTextureArrayAvailability availability)
        {
            if (material == null)
            {
                return;
            }

            material.SetTexture(AlbedoArrayProperty, albedo);
            material.SetTexture(NormalArrayProperty, normal);
            material.SetTexture(MaskArrayProperty, mask);
            SetKeyword(material, AlbedoArrayKeyword, availability.Albedo && albedo != null);
            SetKeyword(material, NormalArrayKeyword, availability.Normal && normal != null);
            SetKeyword(material, MaskArrayKeyword, availability.Mask && mask != null);
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        private void InvalidateRuntimeCache()
        {
            cacheValid = false;
        }

        private readonly struct TerrainSurfaceKey : IEquatable<TerrainSurfaceKey>
        {
            private readonly TerrainBiome biome;
            private readonly SurfaceType surface;

            public TerrainSurfaceKey(TerrainBiome biome, SurfaceType surface)
            {
                this.biome = biome;
                this.surface = surface;
            }

            public bool Equals(TerrainSurfaceKey other) => biome == other.biome && surface == other.surface;
            public override bool Equals(object obj) => obj is TerrainSurfaceKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine((ushort)biome, (ushort)surface);
        }

    }

    public static class DefaultSurfacePalette
    {
        public static SurfaceAppearance ResolveTerrain(TerrainBiome biome, SurfaceType type)
        {
            if (type == SurfaceType.Cliff)
            {
                var cliff = biome == TerrainBiome.Snow
                    ? new Color(0.62f, 0.67f, 0.72f)
                    : new Color(0.34f, 0.35f, 0.37f);
                return new SurfaceAppearance(cliff, 0.02f, 0.25f, 0.95f);
            }

            if (type == SurfaceType.Road)
            {
                return new SurfaceAppearance(new Color(0.34f, 0.24f, 0.14f), 0f, 0.15f, 0.95f);
            }

            if (type == SurfaceType.Riverbed)
            {
                return new SurfaceAppearance(new Color(0.25f, 0.27f, 0.25f), 0f, 0.16f, 0.92f);
            }

            if (type == SurfaceType.Lakebed)
            {
                return new SurfaceAppearance(new Color(0.25f, 0.19f, 0.12f), 0f, 0.12f, 0.9f);
            }

            if (type is SurfaceType.Seabed or SurfaceType.Shore)
            {
                return new SurfaceAppearance(new Color(0.76f, 0.64f, 0.38f), 0f, 0.18f, 1f);
            }

            return biome switch
            {
                TerrainBiome.Forest => new SurfaceAppearance(new Color(0.16f, 0.38f, 0.12f), 0f, 0.14f, 1f),
                TerrainBiome.Desert => new SurfaceAppearance(new Color(0.76f, 0.64f, 0.38f), 0f, 0.18f, 1f),
                TerrainBiome.Snow => new SurfaceAppearance(new Color(0.9f, 0.94f, 0.98f), 0f, 0.42f, 1f),
                TerrainBiome.Wetland => new SurfaceAppearance(new Color(0.25f, 0.19f, 0.12f), 0f, 0.12f, 0.9f),
                TerrainBiome.Mountain => new SurfaceAppearance(new Color(0.34f, 0.35f, 0.37f), 0.02f, 0.25f, 0.95f),
                TerrainBiome.Field => new SurfaceAppearance(new Color(0.25f, 0.5f, 0.18f), 0f, 0.16f, 1f),
                _ => new SurfaceAppearance(new Color(0.38f, 0.28f, 0.16f), 0f, 0.2f, 1f)
            };
        }

        public static SurfaceAppearance ResolveWater() =>
            new(
                new Color(0.08f, 0.42f, 0.68f, 0.72f),
                0f,
                0.72f,
                0.92f);
    }
}
