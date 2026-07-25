using System.Collections.Generic;
using MiniCivilization.World.Presentation;
using MiniCivilization.World.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    internal static class WorldPreparedRenderCacheUtility
    {
        public static WorldDataAsset EnsurePersistentAsset(
            WorldManager manager,
            string assetPath)
        {
            var current = manager.CurrentWorldDataAsset;
            if (current == null || !current.HasData)
            {
                throw new System.InvalidOperationException(
                    "There is no current world data to convert.");
            }

            if (AssetDatabase.Contains(current))
            {
                current.CaptureSerializedData();
                EditorUtility.SetDirty(current);
                return current;
            }

            var asset = ScriptableObject.CreateInstance<WorldDataAsset>();
            asset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            asset.InitializeFromBytes(current.ExportBytes());
            AssetDatabase.CreateAsset(asset, assetPath);
            manager.SetCurrentWorldAsset(
                asset,
                preferPreparedScene: false,
                markDirty: manager.IsDirty);
            EditorUtility.SetDirty(manager);
            return asset;
        }

        public static void Prepare(
            WorldManager manager,
            WorldDataAsset asset)
        {
            if (manager == null
                || manager.Renderer == null
                || asset == null
                || !asset.HasData)
            {
                throw new System.InvalidOperationException(
                    "WorldManager, Renderer, and a populated WorldDataAsset are required.");
            }

            RemoveMeshSubAssets(asset);
            asset.CaptureSerializedData();
            manager.Renderer.PrepareWorldInScene(asset.Data);

            var meshes = new List<Mesh>();
            foreach (var view in manager.Renderer.EnumerateChunkViews())
            {
                foreach (var mesh in view.EnumerateMeshes())
                {
                    if (mesh == null || meshes.Contains(mesh))
                    {
                        continue;
                    }

                    mesh.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(mesh, asset);
                    meshes.Add(mesh);
                }

                view.MarkPrepared();
                EditorUtility.SetDirty(view);
            }

            asset.SetPreparedRenderCache(
                manager.Renderer.ActiveRenderPatchSize,
                manager.Renderer.RenderedPatchCount,
                meshes);
            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(manager.Renderer);
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            AssetDatabase.SaveAssets();
        }

        public static void Remove(
            WorldManager manager,
            WorldDataAsset asset)
        {
            manager?.Renderer?.Unbind();
            RemoveMeshSubAssets(asset);
            asset?.ClearPreparedRenderCache();
            if (asset != null)
            {
                EditorUtility.SetDirty(asset);
            }

            if (manager != null)
            {
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            }

            AssetDatabase.SaveAssets();
        }

        private static void RemoveMeshSubAssets(WorldDataAsset asset)
        {
            if (asset == null || !AssetDatabase.Contains(asset))
            {
                return;
            }

            var meshes = new List<Mesh>(asset.PreparedMeshes);
            asset.ClearPreparedRenderCache();
            for (var index = 0; index < meshes.Count; index++)
            {
                var mesh = meshes[index];
                if (mesh != null && AssetDatabase.IsSubAsset(mesh))
                {
                    Object.DestroyImmediate(mesh, allowDestroyingAssets: true);
                }
            }
        }
    }
}
