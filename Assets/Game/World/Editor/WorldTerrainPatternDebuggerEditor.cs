using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation;
using MiniCivilization.World.Generation.Streaming;
using MiniCivilization.World.Runtime;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldTerrainPatternDebugger))]
    public sealed class WorldTerrainPatternDebuggerEditor : UnityEditor.Editor
    {
        private enum PatternMapLayer : byte
        {
            Terrain,
            Hydrology,
            Combined
        }

        private readonly struct PatternMapRow
        {
            public PatternMapRow(
                int row,
                StreamingPatternMapSample[] samples)
            {
                Row = row;
                Samples = samples;
            }

            public int Row { get; }
            public StreamingPatternMapSample[] Samples { get; }
        }

        private sealed class PatternMapBuildOperation : IDisposable
        {
            private readonly StreamingPatternMapSession session;
            private readonly WorldCellRectangle viewport;
            private readonly WorldCellRectangle plannedRectangle;
            private Task<PatternMapRow> activeRow;
            private int nextRow;
            private bool isCancelled;
            private bool isDisposed;

            public PatternMapBuildOperation(
                StreamingPatternMapSession session,
                in WorldCellRectangle viewport,
                in WorldCellRectangle plannedRectangle,
                int width,
                int height)
            {
                this.session = session ?? throw new ArgumentNullException(
                    nameof(session));
                if (width <= 0 || height <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(width));
                }

                this.viewport = viewport;
                this.plannedRectangle = plannedRectangle;
                Width = width;
                Height = height;
            }

            public int Width { get; }
            public int Height { get; }
            public bool IsCancelled => isCancelled;
            public bool HasActiveRow => activeRow != null;
            public bool IsComplete => !isCancelled
                && nextRow >= Height
                && activeRow == null;

            public void StartNextRow()
            {
                if (isDisposed || isCancelled || activeRow != null
                    || nextRow >= Height)
                {
                    return;
                }

                var row = ResolveRowIndex(nextRow, Height);
                nextRow++;
                activeRow = Task.Run(() => BuildRow(row));
            }

            public bool TryTakeCompleted(
                out PatternMapRow row,
                out Exception error)
            {
                row = default;
                error = null;
                if (activeRow == null || !activeRow.IsCompleted)
                {
                    return false;
                }

                var completed = activeRow;
                activeRow = null;
                try
                {
                    row = completed.GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    error = exception;
                }

                return true;
            }

            public void Cancel() => isCancelled = true;

            public void Dispose()
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
                isCancelled = true;
                if (activeRow != null && !activeRow.IsCompleted)
                {
                    activeRow.ContinueWith(_ => session.Dispose());
                    return;
                }

                session.Dispose();
            }

            private PatternMapRow BuildRow(int row)
            {
                var result = new StreamingPatternMapSample[Width];
                var worldZ = ResolveSampleCoordinate(
                    viewport.MinimumZ,
                    viewport.MaximumZExclusive,
                    row,
                    Height);
                for (var ordinal = 0; ordinal < Width; ordinal++)
                {
                    var x = ResolveCenterFirstIndex(ordinal, Width);
                    var worldX = ResolveSampleCoordinate(
                        viewport.MinimumX,
                        viewport.MaximumXExclusive,
                        x,
                        Width);
                    if (plannedRectangle.Contains(worldX, worldZ))
                    {
                        result[x] = session.Sample(worldX, worldZ);
                    }
                }

                return new PatternMapRow(row, result);
            }

            private static int ResolveRowIndex(int ordinal, int height) =>
                ResolveCenterFirstIndex(ordinal, height);

            private static int ResolveCenterFirstIndex(int ordinal, int length)
            {
                var center = length / 2;
                if (ordinal == 0)
                {
                    return center;
                }

                var distance = (ordinal + 1) / 2;
                return ordinal % 2 == 1
                    ? center - distance
                    : center + distance;
            }
        }

        private const int MapResolution = 256;
        private const int MinimumPixelsPerChunk = 2;
        private static readonly Vector2Int[] targetCrossOffsets =
        {
            Vector2Int.zero,
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.down
        };

        private WorldTerrainPatternDebugger debugger;
        private readonly List<PatternMapBuildOperation> retiredOperations = new();
        private PatternMapBuildOperation mapOperation;
        private StreamingPatternMapSample[] samples;
        private bool[] completedMapRows;
        private Texture2D mapTexture;
        private WorldCellRectangle mapViewport;
        private int mapPixelsPerChunk;
        private int mapLevel;
        private int completedRows;
        private Vector2Int selectedCenterChunk;
        private bool hasSelectedCenter;
        private PatternMapLayer layer;
        private string buildError;

        private void OnEnable()
        {
            debugger = (WorldTerrainPatternDebugger)target;
            EditorApplication.update += AdvanceMapBuild;
        }

        private void OnDisable()
        {
            EditorApplication.update -= AdvanceMapBuild;
            RetireCurrentOperation();
            for (var index = 0; index < retiredOperations.Count; index++)
            {
                retiredOperations[index].Dispose();
            }

            retiredOperations.Clear();
            DestroyMapTexture();
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                ClearMap();
            }

            var controller = debugger.GenerationController;
            var runtime = debugger.WorldManager != null
                ? debugger.WorldManager.CurrentWorldRuntime
                : null;
            if (controller == null || runtime == null)
            {
                EditorGUILayout.HelpBox(
                    controller == null
                        ? "WorldGenerationController is not assigned."
                        : "A prepared WorldRuntime is required.",
                    MessageType.Error);
                return;
            }

            var settingsError = string.Empty;
            if (controller.Settings == null
                || !controller.Settings.TryValidate(out settingsError))
            {
                EditorGUILayout.HelpBox(
                    controller.Settings == null
                        ? "WorldGenerationSettings is not assigned."
                        : settingsError,
                    MessageType.Error);
                return;
            }

            if (debugger.PatternMapPalette == null)
            {
                EditorGUILayout.HelpBox(
                    "WorldPatternMapPalette is not assigned.",
                    MessageType.Error);
                return;
            }

            var settings = runtime.Data.Settings;
            var levels = BuildMapLevels(settings.ChunkCellCountXZ);
            if (levels.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No Map Level can represent the current Chunk XZ Cell count.",
                    MessageType.Error);
                return;
            }

            EnsureSelectedCenter(settings);
            mapLevel = Math.Clamp(mapLevel, 0, levels.Count - 1);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Pattern Map Inspector",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "고정 출력",
                $"{MapResolution} x {MapResolution} Pixel");

            EditorGUI.BeginChangeCheck();
            selectedCenterChunk = EditorGUILayout.Vector2IntField(
                "중앙 선택 Chunk XZ",
                selectedCenterChunk);
            var nextLevel = GUILayout.Toolbar(mapLevel, BuildLevelLabels(levels));
            if (EditorGUI.EndChangeCheck())
            {
                mapLevel = nextLevel;
                ClearMap();
            }

            if (!IsSelectedCenterValid(settings))
            {
                EditorGUILayout.HelpBox(
                    "Finite 월드에서는 월드 경계 안의 Chunk를 선택해야 합니다.",
                    MessageType.Error);
                return;
            }

            if (GUILayout.Button("현재 Streaming Target을 중앙 선택으로"))
            {
                SelectCurrentStreamingTarget(settings);
            }

            if (!TryResolveViewport(
                    settings,
                    levels[mapLevel],
                    out var viewport,
                    out var plannedRectangle,
                    out var viewportError))
            {
                EditorGUILayout.HelpBox(viewportError, MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField(
                "현재 Level",
                $"Chunk 한 변 {levels[mapLevel]} Pixel");
            if (mapOperation == null || mapOperation.IsCancelled
                || mapOperation.IsComplete)
            {
                if (GUILayout.Button("패턴 맵 생성"))
                {
                    StartMapBuild(runtime, viewport, plannedRectangle,
                        levels[mapLevel]);
                }
            }
            else if (GUILayout.Button("생성 취소"))
            {
                mapOperation.Cancel();
            }

            if (!string.IsNullOrEmpty(buildError))
            {
                EditorGUILayout.HelpBox(buildError, MessageType.Error);
            }

            if (mapTexture == null || samples == null)
            {
                return;
            }

            var selectedLayer = GUILayout.Toolbar(
                (int)layer,
                new[] { "지형 패턴", "수문 패턴", "통합 패턴" });
            if (selectedLayer != (int)layer)
            {
                layer = (PatternMapLayer)selectedLayer;
                RenderCompletedRows();
            }

            var completePixels = completedRows * MapResolution;
            var status = mapOperation != null && !mapOperation.IsCancelled
                && !mapOperation.IsComplete
                ? "생성 중"
                : mapOperation != null && mapOperation.IsCancelled
                    ? "생성 취소"
                    : "생성 완료";
            EditorGUILayout.LabelField(
                $"{status} · {completePixels:N0} / "
                + $"{MapResolution * MapResolution:N0} Pixel");

            var width = Mathf.Clamp(
                EditorGUIUtility.currentViewWidth - 40f,
                128f,
                480f);
            var rect = GUILayoutUtility.GetRect(width, width);
            EditorGUI.DrawPreviewTexture(
                rect,
                mapTexture,
                null,
                ScaleMode.ScaleToFit);
            DrawChunkGrid(rect);
            DrawSelectionOverlay(rect, runtime);
            DrawStreamingTargetCross(rect);
            HandleMapSelection(rect, settings, runtime);

            if (GUILayout.Button("중앙 선택 영역으로 Streaming Target 이동"))
            {
                MoveStreamingTarget(settings);
            }
        }

        private void AdvanceMapBuild()
        {
            AdvanceRetiredOperations();
            if (mapOperation == null)
            {
                return;
            }

            if (mapOperation.TryTakeCompleted(out var row, out var error))
            {
                if (error != null)
                {
                    buildError = error.Message;
                    mapOperation.Cancel();
                }
                else if (!mapOperation.IsCancelled)
                {
                    ApplyRow(row);
                }
            }

            if (!mapOperation.IsCancelled
                && !mapOperation.IsComplete
                && !mapOperation.HasActiveRow)
            {
                mapOperation.StartNextRow();
            }

            Repaint();
        }

        private void StartMapBuild(
            WorldRuntime runtime,
            in WorldCellRectangle viewport,
            in WorldCellRectangle plannedRectangle,
            int pixelsPerChunk)
        {
            try
            {
                var nextOperation = new PatternMapBuildOperation(
                    runtime.OpenPatternMapSession(plannedRectangle),
                    viewport,
                    plannedRectangle,
                    MapResolution,
                    MapResolution);
                RetireCurrentOperation();
                DestroyMapTexture();
                samples = new StreamingPatternMapSample[
                    MapResolution * MapResolution];
                completedMapRows = new bool[MapResolution];
                mapTexture = new Texture2D(
                    MapResolution,
                    MapResolution,
                    TextureFormat.RGBA32,
                    false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                mapTexture.SetPixels(new Color[MapResolution * MapResolution]);
                mapTexture.Apply(false, false);
                mapOperation = nextOperation;
                mapViewport = viewport;
                mapPixelsPerChunk = pixelsPerChunk;
                completedRows = 0;
                buildError = string.Empty;
                mapOperation.StartNextRow();
            }
            catch (Exception exception)
            {
                buildError = exception.Message;
            }
        }

        private void ApplyRow(in PatternMapRow row)
        {
            if (samples == null || mapTexture == null
                || completedMapRows == null
                || completedMapRows[row.Row])
            {
                return;
            }

            var colors = new Color[MapResolution];
            var start = row.Row * MapResolution;
            for (var x = 0; x < MapResolution; x++)
            {
                var sample = row.Samples[x];
                samples[start + x] = sample;
                colors[x] = ResolveColor(sample);
            }

            mapTexture.SetPixels(0, row.Row, MapResolution, 1, colors);
            mapTexture.Apply(false, false);
            completedMapRows[row.Row] = true;
            completedRows++;
        }

        private void RenderCompletedRows()
        {
            if (samples == null || mapTexture == null)
            {
                return;
            }

            if (completedMapRows == null)
            {
                return;
            }

            for (var z = 0; z < MapResolution; z++)
            {
                if (!completedMapRows[z])
                {
                    continue;
                }

                var colors = new Color[MapResolution];
                var start = z * MapResolution;
                for (var x = 0; x < MapResolution; x++)
                {
                    colors[x] = ResolveColor(samples[start + x]);
                }

                mapTexture.SetPixels(0, z, MapResolution, 1, colors);
            }

            mapTexture.Apply(false, false);
            Repaint();
        }

        private Color ResolveColor(in StreamingPatternMapSample sample)
        {
            if (!sample.IsAvailable)
            {
                return Color.black;
            }

            var palette = debugger.PatternMapPalette;
            var terrain = palette.ResolveTerrain(sample.Terrain.DominantPattern);
            if (layer == PatternMapLayer.Terrain)
            {
                return terrain;
            }

            var hydrology = palette.ResolveHydrology(sample.HydrologyType);
            if (layer == PatternMapLayer.Hydrology)
            {
                var color = Color.Lerp(
                    Color.black,
                    hydrology,
                    sample.HydrologyMembership);
                color.a = 1f;
                return color;
            }

            return Color.Lerp(
                terrain,
                hydrology,
                hydrology.a * sample.HydrologyMembership);
        }

        private void DrawChunkGrid(Rect rect)
        {
            if (mapPixelsPerChunk <= 0)
            {
                return;
            }

            var color = new Color(0f, 0f, 0f, 0.6f);
            for (var pixel = 0;
                 pixel <= MapResolution;
                 pixel += mapPixelsPerChunk)
            {
                var position = rect.x + rect.width * pixel / MapResolution;
                EditorGUI.DrawRect(new Rect(position, rect.y, 1f, rect.height), color);
                position = rect.y + rect.height * pixel / MapResolution;
                EditorGUI.DrawRect(new Rect(rect.x, position, rect.width, 1f), color);
            }
        }

        private void DrawSelectionOverlay(Rect rect, WorldRuntime runtime)
        {
            var streaming = debugger.StreamingController;
            if (streaming == null
                || !TryGetTerrainRenderRectangle(
                    runtime.Data,
                    streaming,
                    selectedCenterChunk,
                    out var selection))
            {
                return;
            }

            DrawWorldRectangle(rect, selection, new Color(1f, 0.85f, 0f, 1f));
        }

        private void DrawStreamingTargetCross(Rect rect)
        {
            if (!debugger.TryGetStreamingTargetCell(out var cell)
                || !TryResolveMapPixel(cell.x, cell.y, out var pixelX,
                    out var pixelZ))
            {
                return;
            }

            for (var index = 0; index < targetCrossOffsets.Length; index++)
            {
                var pixel = new Vector2Int(pixelX, pixelZ)
                    + targetCrossOffsets[index];
                if ((uint)pixel.x >= MapResolution
                    || (uint)pixel.y >= MapResolution)
                {
                    continue;
                }

                DrawMapPixel(rect, pixel.x, pixel.y, Color.red);
            }
        }

        private void DrawWorldRectangle(
            Rect rect,
            in WorldCellRectangle worldRectangle,
            Color color)
        {
            var width = mapViewport.MaximumXExclusive - mapViewport.MinimumX;
            var height = mapViewport.MaximumZExclusive - mapViewport.MinimumZ;
            var minimumX = (worldRectangle.MinimumX - mapViewport.MinimumX)
                / (float)width;
            var maximumX = (worldRectangle.MaximumXExclusive - mapViewport.MinimumX)
                / (float)width;
            var minimumZ = (worldRectangle.MinimumZ - mapViewport.MinimumZ)
                / (float)height;
            var maximumZ = (worldRectangle.MaximumZExclusive - mapViewport.MinimumZ)
                / (float)height;
            if (maximumX <= 0f || minimumX >= 1f
                || maximumZ <= 0f || minimumZ >= 1f)
            {
                return;
            }

            minimumX = Mathf.Clamp01(minimumX);
            maximumX = Mathf.Clamp01(maximumX);
            minimumZ = Mathf.Clamp01(minimumZ);
            maximumZ = Mathf.Clamp01(maximumZ);
            var overlay = new Rect(
                rect.x + rect.width * minimumX,
                rect.yMax - rect.height * maximumZ,
                rect.width * (maximumX - minimumX),
                rect.height * (maximumZ - minimumZ));
            EditorGUI.DrawRect(new Rect(overlay.x, overlay.y, overlay.width, 1f), color);
            EditorGUI.DrawRect(new Rect(overlay.x, overlay.yMax - 1f, overlay.width, 1f), color);
            EditorGUI.DrawRect(new Rect(overlay.x, overlay.y, 1f, overlay.height), color);
            EditorGUI.DrawRect(new Rect(overlay.xMax - 1f, overlay.y, 1f, overlay.height), color);
        }

        private void DrawMapPixel(Rect rect, int x, int z, Color color)
        {
            var width = rect.width / MapResolution;
            var height = rect.height / MapResolution;
            EditorGUI.DrawRect(new Rect(
                rect.x + x * width,
                rect.yMax - (z + 1) * height,
                width,
                height), color);
        }

        private void HandleMapSelection(
            Rect rect,
            WorldSettingsData settings,
            WorldRuntime runtime)
        {
            var current = Event.current;
            if (current.type != EventType.MouseDown
                || current.button != 0
                || !rect.Contains(current.mousePosition))
            {
                return;
            }

            var normalizedX = Mathf.Clamp01(
                (current.mousePosition.x - rect.x) / rect.width);
            var normalizedZ = Mathf.Clamp01(
                1f - (current.mousePosition.y - rect.y) / rect.height);
            var pixelX = Math.Clamp(
                Mathf.FloorToInt(normalizedX * MapResolution),
                0,
                MapResolution - 1);
            var pixelZ = Math.Clamp(
                Mathf.FloorToInt(normalizedZ * MapResolution),
                0,
                MapResolution - 1);
            var cellX = ResolveSampleCoordinate(
                mapViewport.MinimumX,
                mapViewport.MaximumXExclusive,
                pixelX,
                MapResolution);
            var cellZ = ResolveSampleCoordinate(
                mapViewport.MinimumZ,
                mapViewport.MaximumZExclusive,
                pixelZ,
                MapResolution);
            var chunk = new Vector2Int(
                WorldCoordinateUtility.FloorDivide(
                    cellX,
                    settings.ChunkCellCountXZ),
                WorldCoordinateUtility.FloorDivide(
                    cellZ,
                    settings.ChunkCellCountXZ));
            if (settings.WorldType == WorldType.Finite
                && (chunk.x < settings.MinimumChunkCoordinate
                    || chunk.x > settings.MaximumChunkCoordinate
                    || chunk.y < settings.MinimumChunkCoordinate
                    || chunk.y > settings.MaximumChunkCoordinate))
            {
                return;
            }

            selectedCenterChunk = chunk;
            hasSelectedCenter = true;
            var levels = BuildMapLevels(settings.ChunkCellCountXZ);
            StartMapBuildForCurrentSelection(runtime, settings, levels[mapLevel]);
            current.Use();
        }

        private void MoveStreamingTarget(WorldSettingsData settings)
        {
            var cellX = checked(selectedCenterChunk.x
                * settings.ChunkCellCountXZ
                + settings.ChunkCellCountXZ / 2);
            var cellZ = checked(selectedCenterChunk.y
                * settings.ChunkCellCountXZ
                + settings.ChunkCellCountXZ / 2);
            var targetTransform = debugger.ResolveStreamingTarget();
            if (targetTransform != null && !Application.isPlaying)
            {
                Undo.RecordObject(
                    targetTransform,
                    "Move Streaming Target To Pattern Map Selection");
            }

            if (!debugger.TryMoveStreamingTargetToCell(cellX, cellZ))
            {
                buildError = "Streaming Target을 중앙 선택 영역으로 이동할 수 없습니다.";
            }
        }

        private void SelectCurrentStreamingTarget(WorldSettingsData settings)
        {
            if (!debugger.TryGetStreamingTargetCell(out var cell))
            {
                buildError = "Streaming Target의 Cell 좌표를 확인할 수 없습니다.";
                return;
            }

            selectedCenterChunk = new Vector2Int(
                WorldCoordinateUtility.FloorDivide(
                    cell.x,
                    settings.ChunkCellCountXZ),
                WorldCoordinateUtility.FloorDivide(
                    cell.y,
                    settings.ChunkCellCountXZ));
            hasSelectedCenter = true;
            var runtime = debugger.WorldManager.CurrentWorldRuntime;
            var levels = BuildMapLevels(settings.ChunkCellCountXZ);
            StartMapBuildForCurrentSelection(runtime, settings, levels[mapLevel]);
        }

        private void StartMapBuildForCurrentSelection(
            WorldRuntime runtime,
            WorldSettingsData settings,
            int pixelsPerChunk)
        {
            if (!TryResolveViewport(
                    settings,
                    pixelsPerChunk,
                    out var viewport,
                    out var plannedRectangle,
                    out var error))
            {
                buildError = error;
                return;
            }

            StartMapBuild(runtime, viewport, plannedRectangle, pixelsPerChunk);
        }

        private bool TryResolveViewport(
            WorldSettingsData settings,
            int pixelsPerChunk,
            out WorldCellRectangle viewport,
            out WorldCellRectangle plannedRectangle,
            out string error)
        {
            viewport = default;
            plannedRectangle = default;
            if (pixelsPerChunk <= 0 || MapResolution % pixelsPerChunk != 0)
            {
                error = "Map Level이 고정 출력 해상도를 나누어야 합니다.";
                return false;
            }

            try
            {
                var chunksPerSide = MapResolution / pixelsPerChunk;
                var minimumChunkX = checked(selectedCenterChunk.x
                    - chunksPerSide / 2);
                var minimumChunkZ = checked(selectedCenterChunk.y
                    - chunksPerSide / 2);
                var size = settings.ChunkCellCountXZ;
                viewport = new WorldCellRectangle(
                    checked(minimumChunkX * size),
                    checked(minimumChunkZ * size),
                    checked((minimumChunkX + chunksPerSide) * size),
                    checked((minimumChunkZ + chunksPerSide) * size));
                var minimumX = viewport.MinimumX;
                var minimumZ = viewport.MinimumZ;
                var maximumX = viewport.MaximumXExclusive;
                var maximumZ = viewport.MaximumZExclusive;
                if (settings.WorldType == WorldType.Finite)
                {
                    minimumX = Math.Max(minimumX, settings.MinimumCellCoordinate);
                    minimumZ = Math.Max(minimumZ, settings.MinimumCellCoordinate);
                    maximumX = Math.Min(maximumX,
                        settings.MaximumCellCoordinateExclusive);
                    maximumZ = Math.Min(maximumZ,
                        settings.MaximumCellCoordinateExclusive);
                }

                if (maximumX <= minimumX || maximumZ <= minimumZ)
                {
                    error = "선택한 Map 영역이 월드 경계와 겹치지 않습니다.";
                    return false;
                }

                plannedRectangle = new WorldCellRectangle(
                    minimumX,
                    minimumZ,
                    maximumX,
                    maximumZ);
                error = string.Empty;
                return true;
            }
            catch (OverflowException)
            {
                error = "선택한 Map 중심 좌표가 지원 범위를 초과합니다.";
                return false;
            }
        }

        private void EnsureSelectedCenter(WorldSettingsData settings)
        {
            if (hasSelectedCenter)
            {
                return;
            }

            if (debugger.StreamingController != null
                && debugger.StreamingController.HasCenter)
            {
                var center = debugger.StreamingController.CurrentCenter;
                selectedCenterChunk = new Vector2Int(center.X, center.Z);
            }
            else
            {
                selectedCenterChunk = Vector2Int.zero;
            }

            if (settings.WorldType == WorldType.Finite)
            {
                selectedCenterChunk = new Vector2Int(
                    Math.Clamp(selectedCenterChunk.x,
                        settings.MinimumChunkCoordinate,
                        settings.MaximumChunkCoordinate),
                    Math.Clamp(selectedCenterChunk.y,
                        settings.MinimumChunkCoordinate,
                        settings.MaximumChunkCoordinate));
            }

            hasSelectedCenter = true;
        }

        private bool IsSelectedCenterValid(WorldSettingsData settings) =>
            settings.WorldType != WorldType.Finite
            || selectedCenterChunk.x >= settings.MinimumChunkCoordinate
            && selectedCenterChunk.x <= settings.MaximumChunkCoordinate
            && selectedCenterChunk.y >= settings.MinimumChunkCoordinate
            && selectedCenterChunk.y <= settings.MaximumChunkCoordinate;

        private static List<int> BuildMapLevels(int chunkCellCountXZ)
        {
            var levels = new List<int>();
            if (chunkCellCountXZ < MinimumPixelsPerChunk)
            {
                return levels;
            }

            var pixels = MinimumPixelsPerChunk;
            while (true)
            {
                if (MapResolution % pixels == 0)
                {
                    levels.Add(pixels);
                }

                if (pixels == chunkCellCountXZ)
                {
                    return levels;
                }

                var doubled = checked(pixels * 2);
                pixels = doubled >= chunkCellCountXZ
                    ? chunkCellCountXZ
                    : doubled;
            }
        }

        private static string[] BuildLevelLabels(IReadOnlyList<int> levels)
        {
            var labels = new string[levels.Count];
            for (var index = 0; index < labels.Length; index++)
            {
                labels[index] = $"Level {index + 1} · {levels[index]}px";
            }

            return labels;
        }

        private static bool TryGetTerrainRenderRectangle(
            WorldData world,
            WorldChunkStreamingController streaming,
            Vector2Int center,
            out WorldCellRectangle rectangle)
        {
            var demand = StreamingChunkDemandBuilder.Build(
                world,
                new StreamingRequest(
                    new ChunkCoordinate(center.x, center.y),
                    streaming.RenderRadius,
                    streaming.EntityRenderRadius,
                    streaming.SimulationRadius));
            var hasChunk = false;
            var minimumX = 0;
            var minimumZ = 0;
            var maximumX = 0;
            var maximumZ = 0;
            foreach (var chunk in demand.TerrainRenderChunks)
            {
                if (!hasChunk)
                {
                    minimumX = maximumX = chunk.X;
                    minimumZ = maximumZ = chunk.Z;
                    hasChunk = true;
                    continue;
                }

                minimumX = Math.Min(minimumX, chunk.X);
                minimumZ = Math.Min(minimumZ, chunk.Z);
                maximumX = Math.Max(maximumX, chunk.X);
                maximumZ = Math.Max(maximumZ, chunk.Z);
            }

            if (!hasChunk)
            {
                rectangle = default;
                return false;
            }

            rectangle = new WorldCellRectangle(
                checked(minimumX * world.ChunkSizeX),
                checked(minimumZ * world.ChunkSizeZ),
                checked((maximumX + 1) * world.ChunkSizeX),
                checked((maximumZ + 1) * world.ChunkSizeZ));
            return true;
        }

        private bool TryResolveMapPixel(
            int worldX,
            int worldZ,
            out int pixelX,
            out int pixelZ)
        {
            pixelX = 0;
            pixelZ = 0;
            if (!mapViewport.Contains(worldX, worldZ))
            {
                return false;
            }

            var width = mapViewport.MaximumXExclusive - mapViewport.MinimumX;
            var height = mapViewport.MaximumZExclusive - mapViewport.MinimumZ;
            pixelX = (int)((long)(worldX - mapViewport.MinimumX)
                * MapResolution / width);
            pixelZ = (int)((long)(worldZ - mapViewport.MinimumZ)
                * MapResolution / height);
            return true;
        }

        private static int ResolveSampleCoordinate(
            int minimum,
            int maximumExclusive,
            int pixel,
            int pixelCount)
        {
            var cellCount = maximumExclusive - minimum;
            var offset = (long)(2 * pixel + 1) * cellCount
                / checked(2L * pixelCount);
            return checked(minimum + (int)offset);
        }

        private void RetireCurrentOperation()
        {
            if (mapOperation == null)
            {
                return;
            }

            mapOperation.Cancel();
            if (mapOperation.HasActiveRow)
            {
                retiredOperations.Add(mapOperation);
            }
            else
            {
                mapOperation.Dispose();
            }

            mapOperation = null;
        }

        private void AdvanceRetiredOperations()
        {
            for (var index = retiredOperations.Count - 1; index >= 0; index--)
            {
                var operation = retiredOperations[index];
                if (operation.TryTakeCompleted(out _, out _))
                {
                    operation.Dispose();
                    retiredOperations.RemoveAt(index);
                }
            }
        }

        private void ClearMap()
        {
            RetireCurrentOperation();
            samples = null;
            completedMapRows = null;
            completedRows = 0;
            mapPixelsPerChunk = 0;
            mapViewport = default;
            buildError = string.Empty;
            DestroyMapTexture();
            Repaint();
        }

        private void DestroyMapTexture()
        {
            if (mapTexture == null)
            {
                return;
            }

            DestroyImmediate(mapTexture);
            mapTexture = null;
        }
    }
}
