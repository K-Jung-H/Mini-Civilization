using MiniCivilization.World.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Editor
{
    internal static class EntityAuthoringCellBoxGizmo
    {
        private static readonly Vector3 CellCenter = new(0f, 0.5f, 0f);

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawCellBox(
            EntityAuthoringCellBox cellBox,
            GizmoType gizmoType)
        {
            var previousMatrix = Handles.matrix;
            var previousColor = Handles.color;
            var previousZTest = Handles.zTest;

            Handles.matrix = cellBox.transform.localToWorldMatrix;
            Handles.zTest = CompareFunction.Always;

            DrawTerrain(cellBox);

            Handles.color = cellBox.WireColor;
            Handles.DrawWireCube(CellCenter, Vector3.one);

            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        private static void DrawTerrain(EntityAuthoringCellBox cellBox)
        {
            var topY = cellBox.TerrainSurfaceHeight;
            var color = cellBox.TerrainColor;
            var noOutline = Color.clear;

            var bottomBackLeft = new Vector3(-0.5f, 0f, -0.5f);
            var bottomBackRight = new Vector3(0.5f, 0f, -0.5f);
            var bottomFrontRight = new Vector3(0.5f, 0f, 0.5f);
            var bottomFrontLeft = new Vector3(-0.5f, 0f, 0.5f);

            Handles.DrawSolidRectangleWithOutline(
                new[]
                {
                    bottomBackLeft,
                    bottomBackRight,
                    bottomFrontRight,
                    bottomFrontLeft
                },
                color,
                noOutline);

            if (topY <= 0f)
            {
                return;
            }

            var topBackLeft = new Vector3(-0.5f, topY, -0.5f);
            var topBackRight = new Vector3(0.5f, topY, -0.5f);
            var topFrontRight = new Vector3(0.5f, topY, 0.5f);
            var topFrontLeft = new Vector3(-0.5f, topY, 0.5f);

            Handles.DrawSolidRectangleWithOutline(
                new[]
                {
                    topBackLeft,
                    topFrontLeft,
                    topFrontRight,
                    topBackRight
                },
                color,
                cellBox.WireColor);
            DrawFace(
                bottomBackLeft,
                topBackLeft,
                topBackRight,
                bottomBackRight,
                color);
            DrawFace(
                bottomFrontRight,
                topFrontRight,
                topFrontLeft,
                bottomFrontLeft,
                color);
            DrawFace(
                bottomFrontLeft,
                topFrontLeft,
                topBackLeft,
                bottomBackLeft,
                color);
            DrawFace(
                bottomBackRight,
                topBackRight,
                topFrontRight,
                bottomFrontRight,
                color);
        }

        private static void DrawFace(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth,
            Color color)
        {
            Handles.DrawSolidRectangleWithOutline(
                new[] { first, second, third, fourth },
                color,
                Color.clear);
        }
    }
}
