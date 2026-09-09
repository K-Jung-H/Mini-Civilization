using System;
using MiniCivilization.World.Presentation;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    public abstract class NamedProfileEntryDrawer : PropertyDrawer
    {
        protected abstract string[] ChildNames { get; }
        protected abstract string ResolveLabel(SerializedProperty property);

        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            var height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            var names = ChildNames;
            for (var index = 0; index < names.Length; index++)
            {
                var child = property.FindPropertyRelative(names[index]);
                if (child == null)
                {
                    continue;
                }

                height += EditorGUIUtility.standardVerticalSpacing
                    + EditorGUI.GetPropertyHeight(child, true);
            }

            return height;
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(
                line,
                property.isExpanded,
                ResolveLabel(property),
                true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                var names = ChildNames;
                for (var index = 0; index < names.Length; index++)
                {
                    var child = property.FindPropertyRelative(names[index]);
                    if (child == null)
                    {
                        continue;
                    }

                    line.y += line.height
                        + EditorGUIUtility.standardVerticalSpacing;
                    line.height = EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(line, child, true);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        protected static string ReadName(
            SerializedProperty property,
            string childName,
            string fallback)
        {
            var value = property.FindPropertyRelative(childName)?.stringValue;
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        protected static string ReadEnum(
            SerializedProperty property,
            string childName)
        {
            var child = property.FindPropertyRelative(childName);
            if (child == null
                || child.enumValueIndex < 0
                || child.enumValueIndex >= child.enumDisplayNames.Length)
            {
                return "Unknown";
            }

            return child.enumDisplayNames[child.enumValueIndex];
        }
    }

    [CustomPropertyDrawer(
        typeof(EntityVisualMotionProfile.StateVisual))]
    public sealed class StateVisualDrawer : NamedProfileEntryDrawer
    {
        private static readonly string[] Names =
        {
            "stateName",
            "phase",
            "variants"
        };

        protected override string[] ChildNames => Names;

        protected override string ResolveLabel(SerializedProperty property) =>
            $"{ReadName(property, "stateName", "<State>")} / "
            + ReadEnum(property, "phase");
    }

    [CustomPropertyDrawer(
        typeof(EntityVisualMotionProfile.VisualVariant))]
    public sealed class VisualVariantDrawer : NamedProfileEntryDrawer
    {
        private static readonly string[] Names =
        {
            "name",
            "selectWeight",
            "settings"
        };

        protected override string[] ChildNames => Names;

        protected override string ResolveLabel(SerializedProperty property) =>
            ReadName(property, "name", "<Variant>");
    }

    [CustomPropertyDrawer(
        typeof(EntityVisualMotionProfile.CellMoveVisual))]
    public sealed class CellMoveVisualDrawer : NamedProfileEntryDrawer
    {
        private static readonly string[] Names =
        {
            "moveType",
            "settings"
        };

        protected override string[] ChildNames => Names;

        protected override string ResolveLabel(SerializedProperty property) =>
            ReadEnum(property, "moveType");
    }

}
