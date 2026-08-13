using MiniCivilization.World.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Editor
{
    internal static class EntityAuthoringCellBoxGizmo
    {
        private const float TerrainCoreSizeRatio = 0.6f;

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
            var cellSize = cellBox.CellSize;
            Handles.DrawWireCube(
                new Vector3(0f, cellSize * 0.5f, 0f),
                Vector3.one * cellSize);

            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        private static void DrawTerrain(EntityAuthoringCellBox cellBox)
        {
            var topY = cellBox.TerrainSurfaceHeight;
            var halfSize = cellBox.CellSize * 0.5f;
            var color = cellBox.TerrainColor;
            var noOutline = Color.clear;

            var bottomBackLeft = new Vector3(-halfSize, 0f, -halfSize);
            var bottomBackRight = new Vector3(halfSize, 0f, -halfSize);
            var bottomFrontRight = new Vector3(halfSize, 0f, halfSize);
            var bottomFrontLeft = new Vector3(-halfSize, 0f, halfSize);

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

            DrawTerrainCore(cellBox, topY, color);

            if (topY <= 0f)
            {
                return;
            }

            var topBackLeft = new Vector3(-halfSize, topY, -halfSize);
            var topBackRight = new Vector3(halfSize, topY, -halfSize);
            var topFrontRight = new Vector3(halfSize, topY, halfSize);
            var topFrontLeft = new Vector3(-halfSize, topY, halfSize);

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

        private static void DrawTerrainCore(
            EntityAuthoringCellBox cellBox,
            float surfaceHeight,
            Color terrainColor)
        {
            if (terrainColor.a <= 0f)
            {
                return;
            }

            var halfCoreSize = cellBox.CellSize * TerrainCoreSizeRatio * 0.5f;
            var surfaceOffset = Mathf.Max(cellBox.CellSize * 0.0001f, 0.0001f);
            var y = surfaceHeight + surfaceOffset;
            var fillColor = new Color(
                terrainColor.r * 0.65f,
                terrainColor.g * 0.65f,
                terrainColor.b * 0.65f,
                Mathf.Clamp01(terrainColor.a + 0.2f));
            var outlineColor = cellBox.WireColor;
            outlineColor.a = 1f;

            Handles.DrawSolidRectangleWithOutline(
                new[]
                {
                    new Vector3(-halfCoreSize, y, -halfCoreSize),
                    new Vector3(-halfCoreSize, y, halfCoreSize),
                    new Vector3(halfCoreSize, y, halfCoreSize),
                    new Vector3(halfCoreSize, y, -halfCoreSize)
                },
                fillColor,
                outlineColor);
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
