using System;
using System.Linq;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Presentation;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(EntityController), true)]
    public sealed class EntityControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var controller = (EntityController)target;
            var entityClassName = serializedObject.FindProperty("entityClassName");
            var entityTypes = FindEntityTypes(controller.Category);
            var selectedIndex = Array.FindIndex(
                entityTypes,
                type => type.AssemblyQualifiedName == entityClassName.stringValue);

            serializedObject.Update();
            var labels = entityTypes
                .Select(type => type.FullName)
                .Prepend("None")
                .ToArray();
            var selectedLabelIndex = EditorGUILayout.Popup(
                "Entity Class",
                selectedIndex + 1,
                labels);
            entityClassName.stringValue = selectedLabelIndex == 0
                ? string.Empty
                : entityTypes[selectedLabelIndex - 1].AssemblyQualifiedName;

            serializedObject.ApplyModifiedProperties();
        }

        private static Type[] FindEntityTypes(EntityCategory category)
        {
            return TypeCache.GetTypesDerivedFrom<Entity>()
                .Where(type =>
                    type.IsSealed
                    && !type.ContainsGenericParameters
                    && EntityCategoryInfo.Supports(category, type)
                    && type.GetConstructor(new[] { typeof(EntityData) }) != null)
                .OrderBy(type => type.FullName)
                .ToArray();
        }
    }
}
