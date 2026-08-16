using MiniCivilization.World.Runtime;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldDataAsset))]
    public sealed class WorldDataAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var asset = (WorldDataAsset)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Data Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Has Data", asset.HasData ? "Yes" : "No");
            EditorGUILayout.LabelField("Seed", asset.Seed.ToString());
            EditorGUILayout.LabelField(
                "Dimensions",
                $"{asset.WorldSize} x {asset.WorldSize} x {asset.WorldHeight}");
            EditorGUILayout.LabelField("Cell Size", asset.CellSize.ToString("g"));
            EditorGUILayout.LabelField(
                "Serialized Bytes",
                EditorUtility.FormatBytes(asset.SerializedByteCount));
            using (new EditorGUI.DisabledScope(!asset.HasData))
            {
                if (GUILayout.Button("Export To World File..."))
                {
                    var path = EditorUtility.SaveFilePanel(
                        "Export World Data",
                        Application.persistentDataPath,
                        asset.name,
                        "mcw");
                    if (!string.IsNullOrEmpty(path))
                    {
                        System.IO.File.WriteAllBytes(path, asset.ExportBytes());
                    }
                }
            }
        }
    }
}
