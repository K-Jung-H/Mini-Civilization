using System;
using System.Collections.Generic;
using System.Text;
using MiniCivilization.World.Definitions;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldSurfaceCatalog))]
    public sealed class WorldSurfaceCatalogEditor : UnityEditor.Editor
    {
        private void OnEnable()
        {
            WorldSurfaceCatalogArrayScheduler.RequestAll();
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var changed = EditorGUI.EndChangeCheck();
            var catalog = (WorldSurfaceCatalog)target;

            if (changed)
            {
                WorldSurfaceCatalogArrayBuilder.RebuildIfNeeded(catalog);
            }

            EditorGUILayout.Space();
            DrawAvailability("Terrain", catalog.TerrainArrayAvailability);
            DrawAvailability("Water", catalog.WaterArrayAvailability);

            if (GUILayout.Button("Rebuild Texture Arrays"))
            {
                WorldSurfaceCatalogArrayBuilder.RebuildIfNeeded(catalog, force: true);
            }
        }

        private static void DrawAvailability(
            string label,
            SurfaceTextureArrayAvailability availability)
        {
            EditorGUILayout.LabelField(
                $"{label} Arrays",
                $"Albedo: {Format(availability.Albedo)}, " +
                $"Normal: {Format(availability.Normal)}, " +
                $"Mask: {Format(availability.Mask)}");
        }

        private static string Format(bool present) => present ? "Ready" : "None";
    }

    internal static class WorldSurfaceCatalogArrayBuilder
    {
        private const int BakeFormatVersion = 1;

        internal static bool RebuildIfNeeded(WorldSurfaceCatalog catalog, bool force = false)
        {
            if (catalog == null)
            {
                return false;
            }

            var catalogPath = AssetDatabase.GetAssetPath(catalog);
            if (string.IsNullOrEmpty(catalogPath))
            {
                return false;
            }

            var terrainProfiles = CollectTerrainProfiles(catalog);
            var waterProfiles = CollectWaterProfiles(catalog);
            var signature = ComputeSignature(catalog, terrainProfiles, waterProfiles);
            if (!force
                && signature == catalog.BakedTextureArraySignature
                && BakedArraysAreValid(catalog, terrainProfiles, waterProfiles))
            {
                return false;
            }

            var terrainAlbedo = BuildArray(catalog, terrainProfiles, TextureChannel.Albedo, "Terrain Albedo Array");
            var terrainNormal = BuildArray(catalog, terrainProfiles, TextureChannel.Normal, "Terrain Normal Array");
            var terrainMask = BuildArray(catalog, terrainProfiles, TextureChannel.Mask, "Terrain Mask Array");
            var waterAlbedo = BuildArray(catalog, waterProfiles, TextureChannel.Albedo, "Water Albedo Array");
            var waterNormal = BuildArray(catalog, waterProfiles, TextureChannel.Normal, "Water Normal Array");
            var waterMask = BuildArray(catalog, waterProfiles, TextureChannel.Mask, "Water Mask Array");

            var oldArrays = new[]
            {
                catalog.TerrainAlbedoArray,
                catalog.TerrainNormalArray,
                catalog.TerrainMaskArray,
                catalog.WaterAlbedoArray,
                catalog.WaterNormalArray,
                catalog.WaterMaskArray
            };
            var newArrays = new[]
            {
                terrainAlbedo,
                terrainNormal,
                terrainMask,
                waterAlbedo,
                waterNormal,
                waterMask
            };

            for (var i = 0; i < newArrays.Length; i++)
            {
                if (newArrays[i] == null)
                {
                    continue;
                }

                AssetDatabase.AddObjectToAsset(newArrays[i], catalog);
                EditorUtility.SetDirty(newArrays[i]);
            }

            catalog.AssignBakedTextureArrays(
                terrainAlbedo,
                terrainNormal,
                terrainMask,
                waterAlbedo,
                waterNormal,
                waterMask,
                signature);
            EditorUtility.SetDirty(catalog);

            for (var i = 0; i < oldArrays.Length; i++)
            {
                DestroyOwnedArray(oldArrays[i], catalogPath);
            }

            AssetDatabase.SaveAssetIfDirty(catalog);
            return true;
        }

        private static List<SurfaceTextureProfile> CollectTerrainProfiles(
            WorldSurfaceCatalog catalog)
        {
            var profiles = new List<SurfaceTextureProfile> { null };
            var common = catalog.CommonTerrainDefinitions;
            for (var i = 0; i < common.Count; i++)
            {
                var definition = common[i];
                if (definition?.Appearance != null)
                {
                    profiles.Add(definition.Appearance);
                }
            }

            var biomes = catalog.BiomeSurfaceSets;
            for (var biomeIndex = 0; biomeIndex < biomes.Count; biomeIndex++)
            {
                var surfaces = biomes[biomeIndex]?.Surfaces;
                if (surfaces == null)
                {
                    continue;
                }

                for (var surfaceIndex = 0; surfaceIndex < surfaces.Count; surfaceIndex++)
                {
                    var definition = surfaces[surfaceIndex];
                    if (definition?.Appearance != null)
                    {
                        profiles.Add(definition.Appearance);
                    }
                }
            }

            return profiles;
        }

        private static List<SurfaceTextureProfile> CollectWaterProfiles(
            WorldSurfaceCatalog catalog)
        {
            var profiles = new List<SurfaceTextureProfile> { null };
            var water = catalog.WaterSurfaceDefinitions;
            for (var i = 0; i < water.Count; i++)
            {
                var definition = water[i];
                if (definition?.Appearance != null)
                {
                    profiles.Add(definition.Appearance);
                }
            }

            return profiles;
        }

        private static string ComputeSignature(
            WorldSurfaceCatalog catalog,
            IReadOnlyList<SurfaceTextureProfile> terrainProfiles,
            IReadOnlyList<SurfaceTextureProfile> waterProfiles)
        {
            var value = new StringBuilder(512);
            value.Append(BakeFormatVersion).Append('|')
                .Append(catalog.TextureResolution).Append('|')
                .Append((int)catalog.TextureArrayFormat).Append('|')
                .Append(catalog.TextureArrayMipMaps ? 1 : 0);
            AppendProfiles(value, terrainProfiles);
            AppendProfiles(value, waterProfiles);
            return Hash128.Compute(value.ToString()).ToString();
        }

        private static void AppendProfiles(
            StringBuilder value,
            IReadOnlyList<SurfaceTextureProfile> profiles)
        {
            value.Append('|').Append(profiles.Count);
            for (var i = 0; i < profiles.Count; i++)
            {
                AppendTexture(value, profiles[i]?.AlbedoTexture);
                AppendTexture(value, profiles[i]?.NormalTexture);
                AppendTexture(value, profiles[i]?.MaskTexture);
            }
        }

        private static void AppendTexture(StringBuilder value, Texture2D texture)
        {
            if (texture == null)
            {
                value.Append("|null");
                return;
            }

            var path = AssetDatabase.GetAssetPath(texture);
            value.Append('|')
                .Append(AssetDatabase.AssetPathToGUID(path))
                .Append(':')
                .Append(AssetDatabase.GetAssetDependencyHash(path));
        }

        private static bool BakedArraysAreValid(
            WorldSurfaceCatalog catalog,
            IReadOnlyList<SurfaceTextureProfile> terrainProfiles,
            IReadOnlyList<SurfaceTextureProfile> waterProfiles)
        {
            var terrainAvailability = catalog.TerrainArrayAvailability;
            var waterAvailability = catalog.WaterArrayAvailability;
            return terrainAvailability.Albedo == ContainsTexture(terrainProfiles, TextureChannel.Albedo)
                && terrainAvailability.Normal == ContainsTexture(terrainProfiles, TextureChannel.Normal)
                && terrainAvailability.Mask == ContainsTexture(terrainProfiles, TextureChannel.Mask)
                && waterAvailability.Albedo == ContainsTexture(waterProfiles, TextureChannel.Albedo)
                && waterAvailability.Normal == ContainsTexture(waterProfiles, TextureChannel.Normal)
                && waterAvailability.Mask == ContainsTexture(waterProfiles, TextureChannel.Mask)
                && ArrayIsValid(catalog, catalog.TerrainAlbedoArray, terrainProfiles, TextureChannel.Albedo)
                && ArrayIsValid(catalog, catalog.TerrainNormalArray, terrainProfiles, TextureChannel.Normal)
                && ArrayIsValid(catalog, catalog.TerrainMaskArray, terrainProfiles, TextureChannel.Mask)
                && ArrayIsValid(catalog, catalog.WaterAlbedoArray, waterProfiles, TextureChannel.Albedo)
                && ArrayIsValid(catalog, catalog.WaterNormalArray, waterProfiles, TextureChannel.Normal)
                && ArrayIsValid(catalog, catalog.WaterMaskArray, waterProfiles, TextureChannel.Mask);
        }

        private static bool ArrayIsValid(
            WorldSurfaceCatalog catalog,
            Texture2DArray array,
            IReadOnlyList<SurfaceTextureProfile> profiles,
            TextureChannel channel)
        {
            var required = ContainsTexture(profiles, channel);
            if (!required)
            {
                return array == null;
            }

            var expectedMipCount = catalog.TextureArrayMipMaps
                ? CalculateMipCount(catalog.TextureResolution)
                : 1;
            return array != null
                && EditorUtility.IsPersistent(array)
                && array.width == catalog.TextureResolution
                && array.height == catalog.TextureResolution
                && array.depth == profiles.Count
                && array.format == catalog.TextureArrayFormat
                && array.mipmapCount == expectedMipCount;
        }

        private static Texture2DArray BuildArray(
            WorldSurfaceCatalog catalog,
            IReadOnlyList<SurfaceTextureProfile> profiles,
            TextureChannel channel,
            string arrayName)
        {
            if (!ContainsTexture(profiles, channel))
            {
                return null;
            }

            var linear = channel != TextureChannel.Albedo;
            var array = new Texture2DArray(
                catalog.TextureResolution,
                catalog.TextureResolution,
                profiles.Count,
                catalog.TextureArrayFormat,
                catalog.TextureArrayMipMaps,
                linear)
            {
                name = arrayName,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2
            };

            var defaultColor = channel == TextureChannel.Normal
                ? new Color(0.5f, 0.5f, 1f, 1f)
                : Color.white;
            var defaultPixels = CreateSolidPixels(catalog.TextureResolution, defaultColor);
            for (var layer = 0; layer < profiles.Count; layer++)
            {
                var source = GetTexture(profiles[layer], channel);
                var pixels = source != null
                    ? ReadTexturePixels(source, catalog.TextureResolution, linear)
                    : defaultPixels;
                array.SetPixels(pixels, layer, 0);
            }

            array.Apply(catalog.TextureArrayMipMaps, makeNoLongerReadable: true);
            return array;
        }

        private static Color[] ReadTexturePixels(Texture2D source, int resolution, bool linear)
        {
            var readWrite = linear
                ? RenderTextureReadWrite.Linear
                : RenderTextureReadWrite.sRGB;
            var temporary = RenderTexture.GetTemporary(
                resolution,
                resolution,
                0,
                RenderTextureFormat.ARGB32,
                readWrite);
            var previous = RenderTexture.active;
            var readable = new Texture2D(
                resolution,
                resolution,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: linear);

            try
            {
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                readable.ReadPixels(
                    new Rect(0f, 0f, resolution, resolution),
                    0,
                    0,
                    recalculateMipMaps: false);
                readable.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                return readable.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
                UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        private static Color[] CreateSolidPixels(int resolution, Color color)
        {
            var pixels = new Color[resolution * resolution];
            Array.Fill(pixels, color);
            return pixels;
        }

        private static bool ContainsTexture(
            IReadOnlyList<SurfaceTextureProfile> profiles,
            TextureChannel channel)
        {
            for (var i = 0; i < profiles.Count; i++)
            {
                if (GetTexture(profiles[i], channel) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static Texture2D GetTexture(
            SurfaceTextureProfile profile,
            TextureChannel channel)
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

        private static int CalculateMipCount(int resolution)
        {
            var count = 1;
            while (resolution > 1)
            {
                resolution >>= 1;
                count++;
            }

            return count;
        }

        private static void DestroyOwnedArray(Texture2DArray array, string catalogPath)
        {
            if (array == null)
            {
                return;
            }

            var path = AssetDatabase.GetAssetPath(array);
            if (string.IsNullOrEmpty(path) || path == catalogPath)
            {
                UnityEngine.Object.DestroyImmediate(array, allowDestroyingAssets: true);
            }
        }

        private enum TextureChannel : byte
        {
            Albedo,
            Normal,
            Mask
        }
    }

    [InitializeOnLoad]
    internal static class WorldSurfaceCatalogArrayScheduler
    {
        private static bool scheduled;

        static WorldSurfaceCatalogArrayScheduler()
        {
            RequestAll();
        }

        internal static void RequestAll()
        {
            if (scheduled)
            {
                return;
            }

            scheduled = true;
            EditorApplication.delayCall += RebuildChangedCatalogs;
        }

        private static void RebuildChangedCatalogs()
        {
            scheduled = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RequestAll();
                return;
            }

            var guids = AssetDatabase.FindAssets("t:WorldSurfaceCatalog");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var catalog = AssetDatabase.LoadAssetAtPath<WorldSurfaceCatalog>(path);
                WorldSurfaceCatalogArrayBuilder.RebuildIfNeeded(catalog);
            }
        }
    }

    internal sealed class WorldSurfaceCatalogAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            if (didDomainReload
                || ContainsTextureOrCatalog(importedAssets)
                || ContainsTextureOrCatalog(deletedAssets)
                || ContainsTextureOrCatalog(movedAssets)
                || ContainsTextureOrCatalog(movedFromAssetPaths))
            {
                WorldSurfaceCatalogArrayScheduler.RequestAll();
            }
        }

        private static bool ContainsTextureOrCatalog(IReadOnlyList<string> paths)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                var extension = System.IO.Path.GetExtension(paths[i]).ToLowerInvariant();
                if (extension is ".png" or ".jpg" or ".jpeg" or ".tga" or ".tif" or ".tiff"
                    or ".psd" or ".exr" or ".hdr")
                {
                    return true;
                }

                if (extension == ".asset"
                    && AssetDatabase.LoadAssetAtPath<WorldSurfaceCatalog>(paths[i]) != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
