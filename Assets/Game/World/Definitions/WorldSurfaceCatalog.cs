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
        public BiomeType Biome = BiomeType.Grassland;

        [Tooltip("바이옴 전용 Ground, Cliff, Road, Riverbed 등의 표현입니다.")]
        public List<TerrainSurfaceDefinition> Surfaces = new();
    }

    [Serializable]
    public sealed class WaterSurfaceDefinition
    {
        public WaterType Type = WaterType.Fresh;
        public SurfaceTextureProfile Appearance = new();
    }

    public readonly struct SurfaceAppearance
    {
        public readonly Color Albedo;
        public readonly float Metallic;
        public readonly float Smoothness;
        public readonly float Occlusion;
        public readonly Vector4 TextureLayers;
        public readonly Vector4 TextureWeights;
        public readonly Vector4 TextureScales;

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
            TextureLayers = new Vector4(textureLayer, 0f, 0f, 0f);
            TextureWeights = new Vector4(1f, 0f, 0f, 0f);
            TextureScales = new Vector4(textureScale, 1f, 1f, 1f);
        }

        private SurfaceAppearance(
            Color albedo,
            float metallic,
            float smoothness,
            float occlusion,
            Vector4 textureLayers,
            Vector4 textureWeights,
            Vector4 textureScales)
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
            var layers = Vector4.zero;
            var weights = Vector4.zero;
            var scales = Vector4.one;
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
            ref Vector4 layers,
            ref Vector4 weights,
            ref Vector4 scales,
            ref int count)
        {
            if (multiplier <= 0f)
            {
                return;
            }

            for (var sourceIndex = 0; sourceIndex < 4; sourceIndex++)
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

                if (count < 4)
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

        private static int FindLayer(Vector4 layers, int count, float layer)
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

        private static int FindSmallestWeight(Vector4 weights)
        {
            var result = 0;
            for (var i = 1; i < 4; i++)
            {
                if (weights[i] < weights[result])
                {
                    result = i;
                }
            }

            return result;
        }

        private static void NormalizeWeights(ref Vector4 weights)
        {
            var sum = weights.x + weights.y + weights.z + weights.w;
            if (sum <= 0.00001f)
            {
                weights = new Vector4(1f, 0f, 0f, 0f);
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

        [SerializeField, Min(1)]
        [Tooltip("카탈로그의 모든 텍스처를 통일할 Texture2DArray 한 레이어의 해상도입니다.")]
        private int textureResolution = 256;

        [SerializeField]
        [Tooltip("특정 바이옴 프로필이 없을 때 사용하는 SurfaceType별 공통 표현입니다.")]
        private List<TerrainSurfaceDefinition> commonTerrain = new();

        [SerializeField]
        [Tooltip("BiomeType별 Ground, Cliff, Road, Riverbed 등의 재질 프로필입니다.")]
        private List<BiomeSurfaceSet> biomes = new();

        [SerializeField]
        [Tooltip("Fresh, Sea, Marsh 수면 타입별 재질 프로필입니다.")]
        private List<WaterSurfaceDefinition> water = new();

        private readonly Dictionary<SurfaceType, SurfaceAppearance> commonCache = new();
        private readonly Dictionary<TerrainSurfaceKey, SurfaceAppearance> biomeCache = new();
        private readonly Dictionary<WaterType, SurfaceAppearance> waterCache = new();
        private Texture2DArray terrainAlbedoArray;
        private Texture2DArray terrainNormalArray;
        private Texture2DArray terrainMaskArray;
        private Texture2DArray waterAlbedoArray;
        private Texture2DArray waterNormalArray;
        private Texture2DArray waterMaskArray;
        private bool cacheValid;

        public SurfaceAppearance ResolveTerrain(BiomeType biome, SurfaceType type)
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

        public SurfaceAppearance ResolveWater(WaterType type)
        {
            EnsureRuntimeCache();
            return waterCache.TryGetValue(type, out var appearance)
                ? appearance
                : DefaultSurfacePalette.ResolveWater(type);
        }

        public void ApplyToMaterials(Material terrainMaterial, Material waterMaterial)
        {
            EnsureRuntimeCache();
            ApplyArrays(terrainMaterial, terrainAlbedoArray, terrainNormalArray, terrainMaskArray);
            ApplyArrays(waterMaterial, waterAlbedoArray, waterNormalArray, waterMaskArray);
        }

        private void OnEnable()
        {
            if (commonTerrain.Count == 0 && biomes.Count == 0 && water.Count == 0)
            {
                PopulateDefaultProfiles();
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
            waterCache.Clear();

            var terrainProfiles = new List<SurfaceTextureProfile> { null };
            AddCommonProfiles(terrainProfiles);
            AddBiomeProfiles(terrainProfiles);

            var waterProfiles = new List<SurfaceTextureProfile> { null };
            AddWaterProfiles(waterProfiles);

            terrainAlbedoArray = BuildTextureArray(terrainProfiles, TextureChannel.Albedo);
            terrainNormalArray = BuildTextureArray(terrainProfiles, TextureChannel.Normal);
            terrainMaskArray = BuildTextureArray(terrainProfiles, TextureChannel.Mask);
            waterAlbedoArray = BuildTextureArray(waterProfiles, TextureChannel.Albedo);
            waterNormalArray = BuildTextureArray(waterProfiles, TextureChannel.Normal);
            waterMaskArray = BuildTextureArray(waterProfiles, TextureChannel.Mask);
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
                CreateBiomeSet(BiomeType.Grassland, new Color(0.25f, 0.5f, 0.18f)),
                CreateBiomeSet(BiomeType.Forest, new Color(0.16f, 0.38f, 0.12f)),
                CreateBiomeSet(BiomeType.Desert, new Color(0.76f, 0.64f, 0.38f)),
                CreateBiomeSet(BiomeType.Snow, new Color(0.9f, 0.94f, 0.98f)),
                CreateBiomeSet(BiomeType.Wetland, new Color(0.25f, 0.19f, 0.12f)),
                CreateBiomeSet(BiomeType.Mountain, new Color(0.34f, 0.35f, 0.37f))
            };

            biomes[(int)BiomeType.Snow - 1].Surfaces.Add(
                CreateTerrainDefinition(SurfaceType.Cliff, new Color(0.62f, 0.67f, 0.72f), 0f, 0.3f));
            biomes[(int)BiomeType.Desert - 1].Surfaces.Add(
                CreateTerrainDefinition(SurfaceType.Road, new Color(0.46f, 0.34f, 0.19f), 0f, 0.14f));
            biomes[(int)BiomeType.Forest - 1].Surfaces.Add(
                CreateTerrainDefinition(SurfaceType.Riverbed, new Color(0.19f, 0.24f, 0.18f), 0f, 0.12f));

            water = new List<WaterSurfaceDefinition>
            {
                CreateWaterDefinition(WaterType.Fresh, new Color(0.08f, 0.42f, 0.68f, 0.72f), 0.9f),
                CreateWaterDefinition(WaterType.Sea, new Color(0.05f, 0.25f, 0.52f, 0.78f), 0.88f),
                CreateWaterDefinition(WaterType.Marsh, new Color(0.18f, 0.31f, 0.17f, 0.8f), 0.72f)
            };
        }

        private static BiomeSurfaceSet CreateBiomeSet(BiomeType biome, Color tint)
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

        private static WaterSurfaceDefinition CreateWaterDefinition(
            WaterType type,
            Color tint,
            float smoothness)
        {
            return new WaterSurfaceDefinition
            {
                Type = type,
                Appearance = new SurfaceTextureProfile
                {
                    Tint = tint,
                    Metallic = 0f,
                    Smoothness = smoothness,
                    Occlusion = 0.9f
                }
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

        private void AddWaterProfiles(List<SurfaceTextureProfile> profiles)
        {
            for (var i = 0; i < water.Count; i++)
            {
                var definition = water[i];
                if (definition == null || definition.Appearance == null)
                {
                    continue;
                }

                var layer = profiles.Count;
                profiles.Add(definition.Appearance);
                waterCache[definition.Type] = CreateAppearance(definition.Appearance, layer);
            }
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

        private Texture2DArray BuildTextureArray(
            IReadOnlyList<SurfaceTextureProfile> profiles,
            TextureChannel channel)
        {
            var linear = channel != TextureChannel.Albedo;
            var array = new Texture2DArray(
                textureResolution,
                textureResolution,
                Mathf.Max(1, profiles.Count),
                TextureFormat.RGBA32,
                false,
                linear)
            {
                name = $"{name} {channel} Array",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 2,
                hideFlags = HideFlags.DontSave
            };

            var defaultColor = channel switch
            {
                TextureChannel.Normal => new Color(0.5f, 0.5f, 1f, 1f),
                _ => Color.white
            };
            var defaultPixels = CreateSolidPixels(defaultColor);

            for (var layer = 0; layer < array.depth; layer++)
            {
                var profile = layer < profiles.Count ? profiles[layer] : null;
                var texture = GetTexture(profile, channel);
                array.SetPixels(texture != null ? ReadTexturePixels(texture, linear) : defaultPixels, layer);
            }

            array.Apply(false, true);
            return array;
        }

        private Color[] CreateSolidPixels(Color color)
        {
            var pixels = new Color[textureResolution * textureResolution];
            Array.Fill(pixels, color);
            return pixels;
        }

        private Color[] ReadTexturePixels(Texture2D source, bool linear)
        {
            var readWrite = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
            var temporary = RenderTexture.GetTemporary(
                textureResolution,
                textureResolution,
                0,
                RenderTextureFormat.ARGB32,
                readWrite);
            var previous = RenderTexture.active;
            var readable = new Texture2D(
                textureResolution,
                textureResolution,
                TextureFormat.RGBA32,
                false,
                linear);

            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                readable.ReadPixels(new Rect(0f, 0f, textureResolution, textureResolution), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                ReleaseObject(readable);
            }
        }

        private static Texture2D GetTexture(SurfaceTextureProfile profile, TextureChannel channel)
        {
            if (profile == null)
            {
                return null;
            }

            return channel switch
            {
                TextureChannel.Normal => profile.NormalTexture,
                TextureChannel.Mask => profile.MaskTexture,
                _ => profile.AlbedoTexture
            };
        }

        private static void ApplyArrays(
            Material material,
            Texture2DArray albedo,
            Texture2DArray normal,
            Texture2DArray mask)
        {
            if (material == null)
            {
                return;
            }

            material.SetTexture(AlbedoArrayProperty, albedo);
            material.SetTexture(NormalArrayProperty, normal);
            material.SetTexture(MaskArrayProperty, mask);
        }

        private void InvalidateRuntimeCache()
        {
            cacheValid = false;
            ReleaseObject(terrainAlbedoArray);
            ReleaseObject(terrainNormalArray);
            ReleaseObject(terrainMaskArray);
            ReleaseObject(waterAlbedoArray);
            ReleaseObject(waterNormalArray);
            ReleaseObject(waterMaskArray);
            terrainAlbedoArray = null;
            terrainNormalArray = null;
            terrainMaskArray = null;
            waterAlbedoArray = null;
            waterNormalArray = null;
            waterMaskArray = null;
        }

        private static void ReleaseObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private readonly struct TerrainSurfaceKey : IEquatable<TerrainSurfaceKey>
        {
            private readonly BiomeType biome;
            private readonly SurfaceType surface;

            public TerrainSurfaceKey(BiomeType biome, SurfaceType surface)
            {
                this.biome = biome;
                this.surface = surface;
            }

            public bool Equals(TerrainSurfaceKey other) => biome == other.biome && surface == other.surface;
            public override bool Equals(object obj) => obj is TerrainSurfaceKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine((ushort)biome, (ushort)surface);
        }

        private enum TextureChannel : byte
        {
            Albedo,
            Normal,
            Mask
        }
    }

    public static class DefaultSurfacePalette
    {
        public static SurfaceAppearance ResolveTerrain(BiomeType biome, SurfaceType type)
        {
            if (type == SurfaceType.Cliff)
            {
                var cliff = biome == BiomeType.Snow
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
                BiomeType.Forest => new SurfaceAppearance(new Color(0.16f, 0.38f, 0.12f), 0f, 0.14f, 1f),
                BiomeType.Desert => new SurfaceAppearance(new Color(0.76f, 0.64f, 0.38f), 0f, 0.18f, 1f),
                BiomeType.Snow => new SurfaceAppearance(new Color(0.9f, 0.94f, 0.98f), 0f, 0.42f, 1f),
                BiomeType.Wetland => new SurfaceAppearance(new Color(0.25f, 0.19f, 0.12f), 0f, 0.12f, 0.9f),
                BiomeType.Mountain => new SurfaceAppearance(new Color(0.34f, 0.35f, 0.37f), 0.02f, 0.25f, 0.95f),
                BiomeType.Grassland => new SurfaceAppearance(new Color(0.25f, 0.5f, 0.18f), 0f, 0.16f, 1f),
                _ => new SurfaceAppearance(new Color(0.38f, 0.28f, 0.16f), 0f, 0.2f, 1f)
            };
        }

        public static SurfaceAppearance ResolveWater(WaterType type)
        {
            return type switch
            {
                WaterType.Sea => new SurfaceAppearance(new Color(0.05f, 0.25f, 0.52f, 0.78f), 0f, 0.88f, 0.9f),
                WaterType.Marsh => new SurfaceAppearance(new Color(0.18f, 0.31f, 0.17f, 0.8f), 0f, 0.72f, 0.82f),
                _ => new SurfaceAppearance(new Color(0.08f, 0.42f, 0.68f, 0.72f), 0f, 0.9f, 0.92f)
            };
        }
    }
}
