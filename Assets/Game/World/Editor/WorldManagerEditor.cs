using MiniCivilization.World.Runtime;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldManager))]
    public sealed class WorldManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var manager = (WorldManager)target;

            EditorGUILayout.Space();
            if (manager.EditController == null
                || manager.WaterFlowController == null
                || manager.Renderer == null
                || manager.EntityManager == null)
            {
                EditorGUILayout.HelpBox(
                    "Editing, Water Flow, Renderer, and Entity Manager references must be assigned.",
                    MessageType.Error);
                return;
            }

            if (!manager.HasWorld)
            {
                EditorGUILayout.HelpBox(
                    "World Generation Settings and the Streaming Target must be assigned before play mode creates the World Runtime.",
                    MessageType.Info);
                return;
            }

            var world = manager.CurrentWorldData;
            EditorGUILayout.HelpBox(
                $"Active world: {world.Size} x {world.Size} x {world.Height}\n" +
                $"Seed: {world.Seed}\n" +
                $"Prepared cache chunks: {manager.CurrentWorldRuntime.SurfaceCache.PreparedChunkCount}\n" +
                $"Rendered/Pooled patches: {manager.Renderer.RenderedPatchCount}/" +
                $"{manager.Renderer.PooledPatchCount}\n" +
                $"Change ID: {manager.CurrentWorldRuntime.CurrentChangeId}\n" +
                $"Dirty: {(manager.IsDirty ? "Yes" : "No")}",
                MessageType.None);

        }
    }
}
