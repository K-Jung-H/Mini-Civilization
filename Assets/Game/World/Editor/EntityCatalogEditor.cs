using System;
using System.Linq;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(EntityCatalog))]
    public sealed class EntityCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawGroup("Nature", "nature", EntityCategory.Nature);
            DrawGroup("Animal", "animals", EntityCategory.Animal);
            DrawGroup("Human", "humans", EntityCategory.Human);
            DrawGroup("Building", "buildings", EntityCategory.Building);

            if (serializedObject.ApplyModifiedProperties())
            {
                ((EntityCatalog)target).InvalidateRuntimeIndex();
            }
        }

        private void DrawGroup(
            string label,
            string propertyName,
            EntityCategory category)
        {
            var group = serializedObject.FindProperty(propertyName);
            var definitions = group.FindPropertyRelative("definitions");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            for (var index = 0; index < definitions.arraySize; index++)
            {
                var definition = definitions.GetArrayElementAtIndex(index);
                DrawDefinition(definition);
            }

            if (GUILayout.Button($"Add {label} Entity"))
            {
                ShowAddMenu(category);
            }
        }

        private void DrawDefinition(SerializedProperty definition)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var typeId = definition.FindPropertyRelative("entityTypeId").intValue;
                EditorGUILayout.PropertyField(
                    definition.FindPropertyRelative("displayName"),
                    new GUIContent("Entity Name"));
                EditorGUILayout.PropertyField(
                    definition.FindPropertyRelative("thumbnail"));
                EditorGUILayout.PropertyField(
                    definition.FindPropertyRelative("prefab"));

                if (GUILayout.Button("Remove"))
                {
                    RemoveDefinition(new EntityTypeId((ushort)typeId));
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void ShowAddMenu(EntityCategory category)
        {
            var menu = new GenericMenu();
            var types = TypeCache.GetTypesDerivedFrom<Entity>()
                .Where(type =>
                    type.IsSealed
                    && !type.ContainsGenericParameters
                    && EntityCategoryInfo.Supports(category, type)
                    && type.GetConstructor(new[] { typeof(EntityData) }) != null)
                .OrderBy(type => type.FullName)
                .ToArray();
            if (types.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No sealed Entity type"));
            }
            else
            {
                for (var index = 0; index < types.Length; index++)
                {
                    var entityType = types[index];
                    menu.AddItem(
                        new GUIContent(entityType.FullName.Replace('.', '/')),
                        false,
                        () => AddDefinition(category, entityType));
                }
            }

            menu.ShowAsContext();
        }

        private void AddDefinition(EntityCategory category, Type entityType)
        {
            var catalog = (EntityCatalog)target;
            Undo.RecordObject(catalog, "Add Entity Definition");
            catalog.AddDefinition(category, entityType);
            EditorUtility.SetDirty(catalog);
        }

        private void RemoveDefinition(EntityTypeId typeId)
        {
            var catalog = (EntityCatalog)target;
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(catalog, "Remove Entity Definition");
            catalog.RemoveDefinition(typeId);
            EditorUtility.SetDirty(catalog);
        }
    }
}
