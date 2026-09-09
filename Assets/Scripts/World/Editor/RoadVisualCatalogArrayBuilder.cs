using System;
using System.Collections.Generic;
using System.Text;
using MiniCivilization.World.Definitions;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(RoadVisualCatalog))]
    public sealed class RoadVisualCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            var changed = EditorGUI.EndChangeCheck();
            var catalog = (RoadVisualCatalog)target;
            if (changed)
            {
                RoadVisualCatalogArrayBuilder.RebuildIfNeeded(catalog);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Shape Masks",
                catalog.ShapeMaskArrayAvailable
                    ? "Ready (Clamp / Bilinear / No Mip Maps)"
                    : "None");
            var surface = catalog.SurfaceArrayAvailability;
            EditorGUILayout.LabelField(
                "Road Surfaces",
                $"Albedo: {Format(surface.Albedo)}, "
                + $"Normal: {Format(surface.Normal)}, "
                + $"Surface: {Format(surface.Mask)}");
            if (GUILayout.Button("Rebuild Texture Arrays"))
            {
                RoadVisualCatalogArrayBuilder.RebuildIfNeeded(catalog, force: true);
            }
        }

        private static string Format(bool available) => available ? "Ready" : "None";
    }

    internal static class RoadVisualCatalogArrayBuilder
    {
        private const int BakeFormatVersion = 2;

        internal static bool RebuildIfNeeded(
            RoadVisualCatalog catalog,
            bool force = false)
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

            var roads = catalog.Roads;
            var signature = ComputeSignature(catalog, roads);
            if (!force && signature == catalog.BakedTextureArraySignature)
            {
                return false;
            }

            var shapes = CollectShapeSources(roads);
            var shapeMasks = BuildShapeMaskArray(
                catalog,
                shapes,
                "Road Shape Mask Array");
            var albedo = BuildSurfaceArray(
                catalog,
                roads,
                RoadSurfaceChannel.Albedo,
                "Road Albedo Array");
            var normal = BuildSurfaceArray(
                catalog,
                roads,
                RoadSurfaceChannel.Normal,
                "Road Normal Array");
            var surface = BuildSurfaceArray(
                catalog,
                roads,
                RoadSurfaceChannel.Surface,
                "Road Surface Array");
            var oldArrays = new[]
            {
                catalog.ShapeMaskArray,
                catalog.AlbedoArray,
                catalog.NormalArray,
                catalog.SurfaceArray
            };
            var newArrays = new[] { shapeMasks, albedo, normal, surface };
            for (var index = 0; index < newArrays.Length; index++)
            {
                if (newArrays[index] == null)
                {
                    continue;
                }

                AssetDatabase.AddObjectToAsset(newArrays[index], catalog);
                EditorUtility.SetDirty(newArrays[index]);
            }

            catalog.AssignBakedTextureArrays(
                shapeMasks,
                albedo,
                normal,
                surface,
                signature);
            EditorUtility.SetDirty(catalog);
            for (var index = 0; index < oldArrays.Length; index++)
            {
                DestroyOwnedArray(oldArrays[index], catalogPath);
            }

            AssetDatabase.SaveAssetIfDirty(catalog);
            return true;
        }

        private static List<Texture2D> CollectShapeSources(
            IReadOnlyList<RoadVisualDefinition> roads)
        {
            var sources = new List<Texture2D> { null };
            for (var index = 0; index < roads.Count; index++)
            {
                var definition = roads[index];
                sources.Add(definition?.CornerMask);
                sources.Add(definition?.QuarterMask);
                sources.Add(definition?.StraightMask);
            }

            return sources;
        }

        private static string ComputeSignature(
            RoadVisualCatalog catalog,
            IReadOnlyList<RoadVisualDefinition> roads)
        {
            var value = new StringBuilder(256);
            value.Append(BakeFormatVersion).Append('|')
                .Append(catalog.TextureResolution).Append('|')
                .Append((int)catalog.TextureArrayFormat).Append('|')
                .Append(catalog.GenerateSurfaceTextureArrayMipMaps ? 1 : 0).Append('|')
                .Append(roads.Count);
            for (var index = 0; index < roads.Count; index++)
            {
                var definition = roads[index];
                value.Append('|').Append((int)(definition?.Type ?? 0));
                AppendTexture(value, definition?.CornerMask);
                AppendTexture(value, definition?.QuarterMask);
                AppendTexture(value, definition?.StraightMask);
                AppendSurface(value, definition?.Surface);
            }

            return Hash128.Compute(value.ToString()).ToString();
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

        private static void AppendSurface(
            StringBuilder value,
            SurfaceTextureProfile profile)
        {
            if (profile == null)
            {
                value.Append("|surface-null");
                return;
            }

            value.Append('|').Append(profile.Tint)
                .Append('|').Append(profile.Tiling)
                .Append('|').Append(profile.Metallic)
                .Append('|').Append(profile.Smoothness)
                .Append('|').Append(profile.Occlusion);
            AppendTexture(value, profile.AlbedoTexture);
            AppendTexture(value, profile.NormalTexture);
            AppendTexture(value, profile.MaskTexture);
        }

        private static Texture2DArray BuildShapeMaskArray(
            RoadVisualCatalog catalog,
            IReadOnlyList<Texture2D> sources,
            string arrayName)
        {
            if (!ContainsTexture(sources))
            {
                return null;
            }

            var array = new Texture2DArray(
                catalog.TextureResolution,
                catalog.TextureResolution,
                sources.Count,
                catalog.TextureArrayFormat,
                mipChain: false,
                linear: true)
            {
                name = arrayName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };
            var defaults = CreateSolidPixels(
                catalog.TextureResolution,
                new Color(0f, 0f, 0f, 0f));
            for (var layer = 0; layer < sources.Count; layer++)
            {
                array.SetPixels(
                    sources[layer] != null
                        ? ReadTexturePixels(
                            sources[layer],
                            catalog.TextureResolution,
                            linear: true)
                        : defaults,
                    layer,
                    0);
            }

            array.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return array;
        }

        private static Texture2DArray BuildSurfaceArray(
            RoadVisualCatalog catalog,
            IReadOnlyList<RoadVisualDefinition> roads,
            RoadSurfaceChannel channel,
            string arrayName)
        {
            if (channel == RoadSurfaceChannel.Normal
                && !ContainsNormalTexture(roads))
            {
                return null;
            }

            var linear = channel != RoadSurfaceChannel.Albedo;
            var array = new Texture2DArray(
                catalog.TextureResolution,
                catalog.TextureResolution,
                roads.Count + 1,
                catalog.TextureArrayFormat,
                catalog.GenerateSurfaceTextureArrayMipMaps,
                linear)
            {
                name = arrayName,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2
            };
            array.SetPixels(
                CreateSolidPixels(
                    catalog.TextureResolution,
                    GetDefaultColor(null, channel)),
                0,
                0);
            for (var index = 0; index < roads.Count; index++)
            {
                var profile = roads[index]?.Surface;
                var source = GetTexture(profile, channel);
                var multiplier = GetMultiplier(profile, channel);
                var pixels = source != null
                    ? ReadTexturePixels(
                        source,
                        catalog.TextureResolution,
                        linear,
                        multiplier)
                    : CreateSolidPixels(
                        catalog.TextureResolution,
                        GetDefaultColor(profile, channel));
                array.SetPixels(pixels, index + 1, 0);
            }

            array.Apply(
                catalog.GenerateSurfaceTextureArrayMipMaps,
                makeNoLongerReadable: true);
            return array;
        }

        private static bool ContainsTexture(IReadOnlyList<Texture2D> sources)
        {
            for (var index = 0; index < sources.Count; index++)
            {
                if (sources[index] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static Color[] ReadTexturePixels(
            Texture2D source,
            int resolution,
            bool linear) =>
            ReadTexturePixels(
                source,
                resolution,
                linear,
                Color.white,
                applyMultiplier: false);

        private static Color[] ReadTexturePixels(
            Texture2D source,
            int resolution,
            bool linear,
            Color multiplier) =>
            ReadTexturePixels(
                source,
                resolution,
                linear,
                multiplier,
                applyMultiplier: true);

        private static Color[] ReadTexturePixels(
            Texture2D source,
            int resolution,
            bool linear,
            Color multiplier,
            bool applyMultiplier)
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
                var pixels = readable.GetPixels();
                if (!applyMultiplier)
                {
                    return pixels;
                }

                for (var index = 0; index < pixels.Length; index++)
                {
                    pixels[index] *= multiplier;
                }

                return pixels;
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

        private static bool ContainsNormalTexture(
            IReadOnlyList<RoadVisualDefinition> roads)
        {
            for (var index = 0; index < roads.Count; index++)
            {
                if (roads[index]?.Surface?.NormalTexture != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static Texture2D GetTexture(
            SurfaceTextureProfile profile,
            RoadSurfaceChannel channel) =>
            channel switch
            {
                RoadSurfaceChannel.Albedo => profile?.AlbedoTexture,
                RoadSurfaceChannel.Normal => profile?.NormalTexture,
                _ => profile?.MaskTexture
            };

        private static Color GetMultiplier(
            SurfaceTextureProfile profile,
            RoadSurfaceChannel channel) =>
            channel switch
            {
                RoadSurfaceChannel.Albedo => profile?.Tint ?? Color.white,
                RoadSurfaceChannel.Surface => new Color(
                    profile?.Metallic ?? 0f,
                    profile?.Occlusion ?? 1f,
                    1f,
                    profile?.Smoothness ?? 0.2f),
                _ => Color.white
            };

        private static Color GetDefaultColor(
            SurfaceTextureProfile profile,
            RoadSurfaceChannel channel) =>
            channel switch
            {
                RoadSurfaceChannel.Albedo => profile?.Tint ?? Color.white,
                RoadSurfaceChannel.Normal => new Color(0.5f, 0.5f, 1f, 1f),
                _ => GetMultiplier(profile, channel)
            };

        private enum RoadSurfaceChannel : byte
        {
            Albedo,
            Normal,
            Surface
        }
    }
}
