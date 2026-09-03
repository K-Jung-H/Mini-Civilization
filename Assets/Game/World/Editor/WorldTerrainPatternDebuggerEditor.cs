using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;
using MiniCivilization.World.Presentation;
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

        private readonly struct PatternMapPixel
        {
            public PatternMapPixel(int x, int z, int worldX, int worldZ)
            {
                X = x;
                Z = z;
                WorldX = worldX;
                WorldZ = worldZ;
            }

            public int X { get; }
            public int Z { get; }
            public int WorldX { get; }
            public int WorldZ { get; }
        }

        private const int MapResolution = 256;
        private const int MinimumPixelsPerChunk = 2;
        private static readonly Vector2Int[] TargetCrossOffsets =
        {
            Vector2Int.zero,
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.down
        };

        private WorldTerrainPatternDebugger debugger;
        private Dictionary<PatternTileKey, List<PatternMapPixel>> pixelsByTile;
        private TerrainPatternCell[] terrainSamples;
        private HydrologyPatternCell[] hydrologySamples;
        private bool[] terrainAvailable;
        private bool[] hydrologyAvailable;
        private Color[] mapColors;
        private Texture2D mapTexture;
        private PatternTileBounds viewport;
        private int pixelsPerChunk;
        private int mapLevel;
        private Vector2Int selectedCenterChunk;
        private bool hasSelectedCenter;
        private PatternMapLayer layer;
        private string mapError;
        private long observedRevision = -1;
        private int terrainTileCount;
        private int hydrologyTileCount;
        private int combinedTileCount;

        private void OnEnable()
        {
            debugger = (WorldTerrainPatternDebugger)target;
            EditorApplication.update += RefreshPatternMap;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RefreshPatternMap;
            debugger?.WorldManager?.ClearDebuggerPatternMapDemand();
            DestroyMapTexture();
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                ClearMap();
            }

            if (!debugger.TryCreateConfiguration(
                    out var configuration,
                    out var configurationError))
            {
                EditorGUILayout.HelpBox(configurationError, MessageType.Info);
                return;
            }

            if (debugger.PatternMapPalette == null)
            {
                EditorGUILayout.HelpBox(
                    "Pattern Map Palette is not assigned.",
                    MessageType.Error);
                return;
            }

            var levels = BuildMapLevels(
                configuration.World.ChunkCellCountXZ);
            if (levels.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Chunk XZ Cell count must support a 2 Pixel minimum Map Level.",
                    MessageType.Error);
                return;
            }

            EnsureSelectedCenter(configuration);
            mapLevel = Math.Clamp(mapLevel, 0, levels.Count - 1);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Pattern Map",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "출력 해상도",
                $"{MapResolution} × {MapResolution} Pixel");

            EditorGUI.BeginChangeCheck();
            var nextCenter = EditorGUILayout.Vector2IntField(
                "선택 중심 Chunk XZ",
                selectedCenterChunk);
            var nextLevel = GUILayout.Toolbar(
                mapLevel,
                BuildLevelLabels(levels));
            if (EditorGUI.EndChangeCheck())
            {
                selectedCenterChunk = nextCenter;
                mapLevel = nextLevel;
                hasSelectedCenter = true;
                StartMapPreparation(configuration, levels[mapLevel]);
            }

            if (!IsSelectedCenterValid(configuration.World))
            {
                EditorGUILayout.HelpBox(
                    "Finite 월드에서는 월드 경계 안의 Chunk를 선택해야 합니다.",
                    MessageType.Error);
                return;
            }

            if (GUILayout.Button("현재 Streaming Target을 선택 중심으로"))
            {
                SelectCurrentStreamingTarget(configuration, levels[mapLevel]);
            }

            EditorGUILayout.LabelField(
                "현재 Level",
                $"Chunk 한 변 {levels[mapLevel]} Pixel");
            if (GUILayout.Button("패턴 맵 준비"))
            {
                StartMapPreparation(configuration, levels[mapLevel]);
            }

            if (!string.IsNullOrEmpty(mapError))
            {
                EditorGUILayout.HelpBox(mapError, MessageType.Error);
            }

            if (mapTexture == null || terrainSamples == null
                || hydrologySamples == null || terrainAvailable == null
                || hydrologyAvailable == null || mapColors == null)
            {
                return;
            }

            var selectedLayer = GUILayout.Toolbar(
                (int)layer,
                new[] { "지형 패턴", "수문 패턴", "통합 패턴" });
            if (selectedLayer != (int)layer)
            {
                layer = (PatternMapLayer)selectedLayer;
                RenderEntireMap();
            }

            EditorGUILayout.LabelField(
                $"Terrain {terrainTileCount:N0} · Hydrology "
                + $"{hydrologyTileCount:N0} · Combined "
                + $"{combinedTileCount:N0} / {pixelsByTile.Count:N0} Tile");

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
            DrawSelectionOverlay(rect, configuration);
            DrawStreamingRenderRangeOverlay(rect, configuration);
            DrawStreamingTargetCross(rect, configuration);
            HandleMapSelection(rect, configuration, levels[mapLevel]);

            if (GUILayout.Button("선택 영역으로 Streaming Target 이동"))
            {
                MoveStreamingTarget(configuration);
            }
        }

        private void RefreshPatternMap()
        {
            if (pixelsByTile == null)
            {
                return;
            }

            var revision = debugger.PatternMapRevision;
            if (revision == observedRevision)
            {
                return;
            }

            observedRevision = revision;
            RefreshStoredTiles();
            Repaint();
        }

        private void StartMapPreparation(
            WorldGenerationConfiguration configuration,
            int nextPixelsPerChunk)
        {
            if (!TryResolveViewport(
                    configuration.World,
                    nextPixelsPerChunk,
                    out var nextViewport,
                    out var nextPixels,
                    out var error))
            {
                mapError = error;
                return;
            }

            var nextGroups = BuildPixelGroups(configuration, nextPixels);
            if (!debugger.TryRequestMapPreparation(nextViewport, out error))
            {
                mapError = error;
                return;
            }

            DestroyMapTexture();
            terrainSamples = new TerrainPatternCell[MapResolution * MapResolution];
            hydrologySamples = new HydrologyPatternCell[MapResolution * MapResolution];
            terrainAvailable = new bool[MapResolution * MapResolution];
            hydrologyAvailable = new bool[MapResolution * MapResolution];
            mapColors = new Color[MapResolution * MapResolution];
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
            mapTexture.SetPixels(mapColors);
            mapTexture.Apply(false, false);
            pixelsByTile = nextGroups;
            viewport = nextViewport;
            pixelsPerChunk = nextPixelsPerChunk;
            observedRevision = -1;
            terrainTileCount = 0;
            hydrologyTileCount = 0;
            combinedTileCount = 0;
            mapError = string.Empty;
            RefreshStoredTiles();
        }

        private Dictionary<PatternTileKey, List<PatternMapPixel>>
            BuildPixelGroups(
                WorldGenerationConfiguration configuration,
                IReadOnlyList<PatternMapPixel> pixels)
        {
            var result = new Dictionary<PatternTileKey, List<PatternMapPixel>>();
            for (var index = 0; index < pixels.Count; index++)
            {
                var pixel = pixels[index];
                var key = configuration.PatternTiles.GetKeyForCell(
                    pixel.WorldX,
                    pixel.WorldZ);
                if (!configuration.PatternTiles.IsOutputAllowed(key))
                {
                    continue;
                }

                if (!result.TryGetValue(key, out var group))
                {
                    group = new List<PatternMapPixel>();
                    result.Add(key, group);
                }

                group.Add(pixel);
            }

            return result;
        }

        private void RefreshStoredTiles()
        {
            if (pixelsByTile == null || terrainSamples == null
                || hydrologySamples == null || terrainAvailable == null
                || hydrologyAvailable == null || mapColors == null)
            {
                return;
            }

            terrainTileCount = 0;
            hydrologyTileCount = 0;
            combinedTileCount = 0;
            var changedGroups = new List<List<PatternMapPixel>>();
            foreach (var pair in pixelsByTile)
            {
                var hasTerrain = debugger.TryGetTerrainPatternTile(
                    pair.Key,
                    out var terrain);
                var hasHydrology = debugger.TryGetHydrologyPatternTile(
                    pair.Key,
                    out var hydrology);
                if (hasTerrain)
                {
                    terrainTileCount++;
                }

                if (hasHydrology)
                {
                    hydrologyTileCount++;
                }

                if (hasTerrain && hasHydrology)
                {
                    combinedTileCount++;
                }

                if (!hasTerrain && !hasHydrology)
                {
                    continue;
                }

                var pixels = pair.Value;
                var changed = false;
                for (var index = 0; index < pixels.Count; index++)
                {
                    var pixel = pixels[index];
                    var mapIndex = pixel.X + MapResolution * pixel.Z;
                    if (hasTerrain && !terrainAvailable[mapIndex])
                    {
                        terrainSamples[mapIndex] = terrain.GetCell(
                            pixel.WorldX,
                            pixel.WorldZ);
                        terrainAvailable[mapIndex] = true;
                        changed = true;
                    }

                    if (hasHydrology && !hydrologyAvailable[mapIndex])
                    {
                        hydrologySamples[mapIndex] = hydrology.GetCell(
                            pixel.WorldX,
                            pixel.WorldZ);
                        hydrologyAvailable[mapIndex] = true;
                        changed = true;
                    }
                }

                if (changed)
                {
                    changedGroups.Add(pixels);
                }
            }

            RenderChangedGroups(changedGroups);
        }

        private void RenderChangedGroups(
            IReadOnlyList<List<PatternMapPixel>> groups)
        {
            if (mapTexture == null || terrainSamples == null
                || hydrologySamples == null || terrainAvailable == null
                || hydrologyAvailable == null || mapColors == null
                || groups == null || groups.Count == 0)
            {
                return;
            }

            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                var minimumX = MapResolution;
                var minimumZ = MapResolution;
                var maximumX = -1;
                var maximumZ = -1;
                for (var index = 0; index < group.Count; index++)
                {
                    var pixel = group[index];
                    var mapIndex = pixel.X + MapResolution * pixel.Z;
                    mapColors[mapIndex] = ResolveColor(
                        terrainSamples[mapIndex],
                        terrainAvailable[mapIndex],
                        hydrologySamples[mapIndex],
                        hydrologyAvailable[mapIndex]);
                    minimumX = Math.Min(minimumX, pixel.X);
                    minimumZ = Math.Min(minimumZ, pixel.Z);
                    maximumX = Math.Max(maximumX, pixel.X);
                    maximumZ = Math.Max(maximumZ, pixel.Z);
                }

                var width = maximumX - minimumX + 1;
                var height = maximumZ - minimumZ + 1;
                var colors = new Color[width * height];
                for (var z = 0; z < height; z++)
                for (var x = 0; x < width; x++)
                {
                    var mapIndex = minimumX + x
                        + MapResolution * (minimumZ + z);
                    colors[x + width * z] = mapColors[mapIndex];
                }

                mapTexture.SetPixels(minimumX, minimumZ, width, height, colors);
            }

            mapTexture.Apply(false, false);
        }

        private void RenderEntireMap()
        {
            if (mapTexture == null || terrainSamples == null
                || hydrologySamples == null || terrainAvailable == null
                || hydrologyAvailable == null || mapColors == null)
            {
                return;
            }

            for (var index = 0; index < mapColors.Length; index++)
            {
                mapColors[index] = ResolveColor(
                    terrainSamples[index],
                    terrainAvailable[index],
                    hydrologySamples[index],
                    hydrologyAvailable[index]);
            }

            mapTexture.SetPixels(mapColors);
            mapTexture.Apply(false, false);
        }

        private Color ResolveColor(
            in TerrainPatternCell terrain,
            bool hasTerrain,
            in HydrologyPatternCell hydrology,
            bool hasHydrology)
        {
            if (layer == PatternMapLayer.Terrain)
            {
                return hasTerrain
                    ? debugger.PatternMapPalette.ResolveTerrain(terrain.Type)
                    : Color.black;
            }

            if (layer == PatternMapLayer.Hydrology)
            {
                if (!hasHydrology)
                {
                    return Color.black;
                }

                var hydrologyColor = debugger.PatternMapPalette.ResolveHydrology(
                    hydrology.WaterType);
                hydrologyColor.a = 1f;
                return hydrologyColor;
            }

            if (!hasTerrain || !hasHydrology)
            {
                return Color.black;
            }

            var terrainColor = debugger.PatternMapPalette.ResolveTerrain(
                terrain.Type);
            if (!hydrology.HasWater)
            {
                return terrainColor;
            }

            var waterColor = debugger.PatternMapPalette.ResolveHydrology(
                hydrology.WaterType);
            var combined = Color.Lerp(terrainColor, waterColor, waterColor.a);
            combined.a = 1f;
            return combined;
        }

        private void DrawChunkGrid(Rect rect)
        {
            if (pixelsPerChunk <= 0)
            {
                return;
            }

            var color = new Color(0f, 0f, 0f, 0.6f);
            for (var pixel = 0;
                 pixel <= MapResolution;
                 pixel += pixelsPerChunk)
            {
                var position = rect.x + rect.width * pixel / MapResolution;
                EditorGUI.DrawRect(
                    new Rect(position, rect.y, 1f, rect.height),
                    color);
                position = rect.y + rect.height * pixel / MapResolution;
                EditorGUI.DrawRect(
                    new Rect(rect.x, position, rect.width, 1f),
                    color);
            }
        }

        private void DrawSelectionOverlay(
            Rect rect,
            WorldGenerationConfiguration configuration)
        {
            var selection = ResolveSelectionBounds(configuration);
            DrawWorldRectangle(rect, selection, new Color(1f, 0.55f, 0f, 1f));
        }

        private void DrawStreamingRenderRangeOverlay(
            Rect rect,
            WorldGenerationConfiguration configuration)
        {
            if (!debugger.TryGetStreamingTargetCell(configuration, out var cell))
            {
                return;
            }

            var world = configuration.World;
            var targetChunkX = WorldCoordinateUtility.FloorDivide(
                cell.x,
                world.ChunkCellCountXZ);
            var targetChunkZ = WorldCoordinateUtility.FloorDivide(
                cell.y,
                world.ChunkCellCountXZ);
            var minimumChunkX = targetChunkX - configuration.RenderRangeChunks;
            var maximumChunkX = targetChunkX + configuration.RenderRangeChunks;
            var minimumChunkZ = targetChunkZ - configuration.RenderRangeChunks;
            var maximumChunkZ = targetChunkZ + configuration.RenderRangeChunks;
            if (world.WorldType == WorldType.Finite)
            {
                minimumChunkX = Math.Max(
                    minimumChunkX,
                    world.MinimumChunkCoordinate);
                maximumChunkX = Math.Min(
                    maximumChunkX,
                    world.MaximumChunkCoordinate);
                minimumChunkZ = Math.Max(
                    minimumChunkZ,
                    world.MinimumChunkCoordinate);
                maximumChunkZ = Math.Min(
                    maximumChunkZ,
                    world.MaximumChunkCoordinate);
            }

            var size = world.ChunkCellCountXZ;
            var renderRange = new PatternTileBounds(
                checked(minimumChunkX * size),
                checked(minimumChunkZ * size),
                checked((maximumChunkX + 1) * size),
                checked((maximumChunkZ + 1) * size));
            DrawWorldRectangle(rect, renderRange, Color.yellow);
        }

        private void DrawStreamingTargetCross(
            Rect rect,
            WorldGenerationConfiguration configuration)
        {
            if (!debugger.TryGetStreamingTargetCell(
                    configuration,
                    out var cell)
                || !TryResolveMapPixel(
                    cell.x,
                    cell.y,
                    out var pixelX,
                    out var pixelZ))
            {
                return;
            }

            for (var index = 0; index < TargetCrossOffsets.Length; index++)
            {
                var pixel = new Vector2Int(pixelX, pixelZ)
                    + TargetCrossOffsets[index];
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
            PatternTileBounds worldRectangle,
            Color color)
        {
            var width = viewport.Width;
            var height = viewport.Height;
            var minimumX = (worldRectangle.MinimumX - viewport.MinimumX)
                / (float)width;
            var maximumX = (worldRectangle.MaximumXExclusive - viewport.MinimumX)
                / (float)width;
            var minimumZ = (worldRectangle.MinimumZ - viewport.MinimumZ)
                / (float)height;
            var maximumZ = (worldRectangle.MaximumZExclusive - viewport.MinimumZ)
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
            EditorGUI.DrawRect(
                new Rect(overlay.x, overlay.y, overlay.width, 1f),
                color);
            EditorGUI.DrawRect(
                new Rect(overlay.x, overlay.yMax - 1f, overlay.width, 1f),
                color);
            EditorGUI.DrawRect(
                new Rect(overlay.x, overlay.y, 1f, overlay.height),
                color);
            EditorGUI.DrawRect(
                new Rect(overlay.xMax - 1f, overlay.y, 1f, overlay.height),
                color);
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
            WorldGenerationConfiguration configuration,
            int nextPixelsPerChunk)
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
                viewport.MinimumX,
                viewport.MaximumXExclusive,
                pixelX,
                MapResolution);
            var cellZ = ResolveSampleCoordinate(
                viewport.MinimumZ,
                viewport.MaximumZExclusive,
                pixelZ,
                MapResolution);
            var chunk = new Vector2Int(
                WorldCoordinateUtility.FloorDivide(
                    cellX,
                    configuration.World.ChunkCellCountXZ),
                WorldCoordinateUtility.FloorDivide(
                    cellZ,
                    configuration.World.ChunkCellCountXZ));
            if (!IsChunkWithinBounds(configuration.World, chunk))
            {
                return;
            }

            selectedCenterChunk = chunk;
            hasSelectedCenter = true;
            StartMapPreparation(configuration, nextPixelsPerChunk);
            current.Use();
        }

        private void MoveStreamingTarget(
            WorldGenerationConfiguration configuration)
        {
            var streamingTarget = debugger.WorldManager != null
                ? debugger.WorldManager.StreamingTarget
                : null;
            if (streamingTarget != null && !Application.isPlaying)
            {
                Undo.RecordObject(
                    streamingTarget,
                    "Move Streaming Target To Pattern Map Selection");
            }

            if (!debugger.TryMoveStreamingTargetToChunk(
                    configuration,
                    selectedCenterChunk))
            {
                mapError = "Streaming Target을 선택 영역으로 이동할 수 없습니다.";
            }
        }

        private void SelectCurrentStreamingTarget(
            WorldGenerationConfiguration configuration,
            int nextPixelsPerChunk)
        {
            if (!debugger.TryGetStreamingTargetCell(configuration, out var cell))
            {
                mapError = "Streaming Target의 Cell 좌표를 확인할 수 없습니다.";
                return;
            }

            selectedCenterChunk = new Vector2Int(
                WorldCoordinateUtility.FloorDivide(
                    cell.x,
                    configuration.World.ChunkCellCountXZ),
                WorldCoordinateUtility.FloorDivide(
                    cell.y,
                    configuration.World.ChunkCellCountXZ));
            hasSelectedCenter = true;
            StartMapPreparation(configuration, nextPixelsPerChunk);
        }

        private bool TryResolveViewport(
            WorldSettingsData settings,
            int nextPixelsPerChunk,
            out PatternTileBounds nextViewport,
            out List<PatternMapPixel> pixels,
            out string error)
        {
            nextViewport = default;
            pixels = null;
            error = string.Empty;
            if (nextPixelsPerChunk <= 0
                || MapResolution % nextPixelsPerChunk != 0)
            {
                error = "Map Level은 고정 출력 해상도를 나누어야 합니다.";
                return false;
            }

            try
            {
                var chunksPerSide = MapResolution / nextPixelsPerChunk;
                var minimumChunkX = checked(selectedCenterChunk.x
                    - chunksPerSide / 2);
                var minimumChunkZ = checked(selectedCenterChunk.y
                    - chunksPerSide / 2);
                var span = settings.ChunkCellCountXZ;
                nextViewport = new PatternTileBounds(
                    checked(minimumChunkX * span),
                    checked(minimumChunkZ * span),
                    checked((minimumChunkX + chunksPerSide) * span),
                    checked((minimumChunkZ + chunksPerSide) * span));
                pixels = new List<PatternMapPixel>(
                    MapResolution * MapResolution);
                for (var z = 0; z < MapResolution; z++)
                {
                    var worldZ = ResolveSampleCoordinate(
                        nextViewport.MinimumZ,
                        nextViewport.MaximumZExclusive,
                        z,
                        MapResolution);
                    for (var x = 0; x < MapResolution; x++)
                    {
                        var worldX = ResolveSampleCoordinate(
                            nextViewport.MinimumX,
                            nextViewport.MaximumXExclusive,
                            x,
                            MapResolution);
                        if (settings.WorldType == WorldType.Finite
                            && (worldX < settings.MinimumCellCoordinate
                                || worldX >= settings.MaximumCellCoordinateExclusive
                                || worldZ < settings.MinimumCellCoordinate
                                || worldZ >= settings.MaximumCellCoordinateExclusive))
                        {
                            continue;
                        }

                        pixels.Add(new PatternMapPixel(x, z, worldX, worldZ));
                    }
                }

                return true;
            }
            catch (OverflowException)
            {
                error = "선택한 Map 중심 좌표가 지원 범위를 초과합니다.";
                return false;
            }
        }

        private void EnsureSelectedCenter(
            WorldGenerationConfiguration configuration)
        {
            if (hasSelectedCenter)
            {
                return;
            }

            if (debugger.TryGetStreamingTargetCell(configuration, out var cell))
            {
                selectedCenterChunk = new Vector2Int(
                    WorldCoordinateUtility.FloorDivide(
                        cell.x,
                        configuration.World.ChunkCellCountXZ),
                    WorldCoordinateUtility.FloorDivide(
                        cell.y,
                        configuration.World.ChunkCellCountXZ));
            }
            else
            {
                selectedCenterChunk = Vector2Int.zero;
            }

            if (configuration.World.WorldType == WorldType.Finite)
            {
                selectedCenterChunk = new Vector2Int(
                    Math.Clamp(
                        selectedCenterChunk.x,
                        configuration.World.MinimumChunkCoordinate,
                        configuration.World.MaximumChunkCoordinate),
                    Math.Clamp(
                        selectedCenterChunk.y,
                        configuration.World.MinimumChunkCoordinate,
                        configuration.World.MaximumChunkCoordinate));
            }

            hasSelectedCenter = true;
        }

        private bool IsSelectedCenterValid(WorldSettingsData settings) =>
            IsChunkWithinBounds(settings, selectedCenterChunk);

        private static bool IsChunkWithinBounds(
            WorldSettingsData settings,
            Vector2Int chunk) => settings.WorldType == WorldType.Infinite
            || chunk.x >= settings.MinimumChunkCoordinate
            && chunk.x <= settings.MaximumChunkCoordinate
            && chunk.y >= settings.MinimumChunkCoordinate
            && chunk.y <= settings.MaximumChunkCoordinate;

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

        private PatternTileBounds ResolveSelectionBounds(
            WorldGenerationConfiguration configuration)
        {
            var world = configuration.World;
            if (world.WorldType == WorldType.Finite)
            {
                return new PatternTileBounds(
                    world.MinimumCellCoordinate,
                    world.MinimumCellCoordinate,
                    world.MaximumCellCoordinateExclusive,
                    world.MaximumCellCoordinateExclusive);
            }

            var radius = configuration.RenderRangeChunks;
            var size = world.ChunkCellCountXZ;
            return new PatternTileBounds(
                checked((selectedCenterChunk.x - radius) * size),
                checked((selectedCenterChunk.y - radius) * size),
                checked((selectedCenterChunk.x + radius + 1) * size),
                checked((selectedCenterChunk.y + radius + 1) * size));
        }

        private bool TryResolveMapPixel(
            int worldX,
            int worldZ,
            out int pixelX,
            out int pixelZ)
        {
            pixelX = 0;
            pixelZ = 0;
            if (!viewport.Contains(worldX, worldZ))
            {
                return false;
            }

            pixelX = (int)((long)(worldX - viewport.MinimumX)
                * MapResolution / viewport.Width);
            pixelZ = (int)((long)(worldZ - viewport.MinimumZ)
                * MapResolution / viewport.Height);
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

        private void ClearMap()
        {
            debugger?.WorldManager?.ClearDebuggerPatternMapDemand();
            pixelsByTile = null;
            terrainSamples = null;
            hydrologySamples = null;
            terrainAvailable = null;
            hydrologyAvailable = null;
            mapColors = null;
            viewport = default;
            pixelsPerChunk = 0;
            mapError = string.Empty;
            observedRevision = -1;
            terrainTileCount = 0;
            hydrologyTileCount = 0;
            combinedTileCount = 0;
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
