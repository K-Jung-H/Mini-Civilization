using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation;
using MiniCivilization.World.Runtime;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldTerrainPatternDebugger))]
    public sealed class WorldTerrainPatternDebuggerEditor : UnityEditor.Editor
    {
        private enum PatternView : byte
        {
            Dominant,
            Blend,
            Smooth,
            Rugged,
            Mountain,
            Canyon,
            Sea,
            SeaArea,
            SeaDepth,
            SeaWater,
            RiverArea,
            RiverDepth,
            RiverWater
        }

        private readonly struct PatternDebugSample
        {
            public PatternDebugSample(
                in WorldPatternWeights weights,
                in WorldPatternResult result,
                float finalSurfaceUnits,
                float riverMaximumDepthUnits)
            {
                Weights = weights;
                Result = result;
                FinalSurfaceUnits = finalSurfaceUnits;
                RiverMaximumDepthUnits = riverMaximumDepthUnits;
            }

            public WorldPatternWeights Weights { get; }
            public WorldPatternResult Result { get; }
            public float FinalSurfaceUnits { get; }
            public float RiverMaximumDepthUnits { get; }
        }

        private static readonly Color SmoothColor = new(0.25f, 0.8f, 0.3f);
        private static readonly Color RuggedColor = new(0.9f, 0.5f, 0.15f);
        private static readonly Color MountainColor = new(0.9f, 0.9f, 0.9f);
        private static readonly Color CanyonColor = new(0.65f, 0.15f, 0.75f);
        private static readonly Color SeaColor = new(0.1f, 0.45f, 0.9f);
        private static readonly Color RiverColor = new(0.1f, 0.85f, 0.95f);

        private Vector2Int previewCenter;
        private int previewAreaCells = 512;
        private int previewResolution = 128;
        private PatternView patternView;
        private PatternDebugSample[] samples;
        private Vector2Int[] sampleCells;
        private PatternDebugSample[] detailSamples;
        private Texture2D previewTexture;
        private Texture2D detailTexture;
        private string statistics;
        private string selectedCellDetails;
        private Vector2Int? selectedCell;
        private bool showWorldOverlay = true;
        private int worldOverlayRadius = 8;
        private WorldTerrainPatternDebugger debugger;
        private WorldManager worldManager;

        private void OnEnable()
        {
            debugger = (WorldTerrainPatternDebugger)target;
            SceneView.duringSceneGui += DrawWorldOverlay;
        }

        public override bool RequiresConstantRepaint() => false;

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                ClearPreview();
            }

            var controller = debugger.GenerationController;
            EditorGUILayout.Space();

            if (controller == null)
            {
                EditorGUILayout.HelpBox(
                    "WorldGenerationController is not assigned.",
                    MessageType.Error);
                return;
            }

            if (debugger.WorldManager == null)
            {
                EditorGUILayout.HelpBox(
                    "WorldManager is not assigned.",
                    MessageType.Warning);
            }

            if (controller.Settings == null)
            {
                EditorGUILayout.HelpBox(
                    "WorldGenerationSettings is not assigned.",
                    MessageType.Error);
            }
            else if (!controller.Settings.TryValidate(out var error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            if (controller.Settings == null
                || !controller.Settings.TryValidate(out _))
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "지형 패턴 테스트",
                EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            previewCenter = EditorGUILayout.Vector2IntField(
                "중심 절대 Cell XZ",
                previewCenter);
            previewAreaCells = EditorGUILayout.IntSlider(
                "전체 분포 범위 Cell 수",
                previewAreaCells,
                32,
                2048);
            previewResolution = EditorGUILayout.IntSlider(
                "전체 분포 Sampling 해상도",
                previewResolution,
                32,
                256);
            if (EditorGUI.EndChangeCheck())
            {
                ClearPreview();
            }

            var selectedView = (PatternView)EditorGUILayout.EnumPopup(
                "표시 값",
                patternView);
            if (selectedView != patternView)
            {
                patternView = selectedView;
                RenderPreview();
                RenderDetailPreview();
            }

            if (GUILayout.Button("패턴 맵 생성"))
            {
                BuildPreview(controller);
            }

            if (GUILayout.Button("Streaming Target 중심으로 재생성"))
            {
                BuildPreviewAtStreamingTarget(controller);
            }

            if (previewTexture != null)
            {
                EditorGUILayout.HelpBox(
                    "전체 분포 탐색용입니다. 초록: 완만 / 주황: 거친 / 흰색: 산맥 / 자주: 협곡 / 파랑: 바다 / 청록: 강",
                    MessageType.None);
                var width = Mathf.Clamp(
                    EditorGUIUtility.currentViewWidth - 40f,
                    128f,
                    320f);
                var rect = GUILayoutUtility.GetRect(width, width);
                EditorGUI.DrawPreviewTexture(
                    rect,
                    previewTexture,
                    null,
                    ScaleMode.ScaleToFit);
                DrawStreamingTargetMarker(
                    rect,
                    previewCenter.x - previewAreaCells * 0.5f,
                    previewCenter.y - previewAreaCells * 0.5f,
                    previewAreaCells);
                HandlePreviewSelection(rect, controller);
                EditorGUILayout.HelpBox(statistics, MessageType.Info);

                if (selectedCell.HasValue)
                {
                    EditorGUILayout.HelpBox(
                        selectedCellDetails,
                        MessageType.None);
                    showWorldOverlay = EditorGUILayout.Toggle(
                        "실제 지형에 색상 표시",
                        showWorldOverlay);
                    var nextOverlayRadius = EditorGUILayout.IntSlider(
                        "표시 반경 Cell",
                        worldOverlayRadius,
                        1,
                        32);
                    if (nextOverlayRadius != worldOverlayRadius)
                    {
                        worldOverlayRadius = nextOverlayRadius;
                        BuildDetailPreview(controller);
                    }

                    if (detailTexture != null)
                    {
                        EditorGUILayout.LabelField(
                            "선택 영역 상세: 1 Pixel = 1 Cell, 실제 지형 표시와 동일 범위");
                        var detailWidth = Mathf.Clamp(
                            EditorGUIUtility.currentViewWidth - 40f,
                            128f,
                            320f);
                        var detailRect = GUILayoutUtility.GetRect(
                            detailWidth,
                            detailWidth);
                        EditorGUI.DrawPreviewTexture(
                            detailRect,
                            detailTexture,
                            null,
                            ScaleMode.ScaleToFit);
                        DrawStreamingTargetMarker(
                            detailRect,
                            selectedCell.Value.x - worldOverlayRadius,
                            selectedCell.Value.y - worldOverlayRadius,
                            worldOverlayRadius * 2 + 1);
                    }

                    if (GUILayout.Button("선택 Cell로 Scene View 이동"))
                    {
                        FocusSelectedCell();
                    }
                }
            }
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawWorldOverlay;
            ClearPreview();
        }

        private void BuildPreview(WorldGenerationController controller)
        {
            var settings = ResolveSettings(controller);
            var router = new WorldNoiseRouter(settings);
            var resolution = previewResolution;
            samples = new PatternDebugSample[resolution * resolution];
            sampleCells = new Vector2Int[resolution * resolution];
            var dominantCounts = new int[5];
            var strongCounts = new int[5];
            var maximumWeights = new float[5];
            var weightSums = new float[5];
            var riverCount = 0;
            var riverInfluenceSum = 0f;
            var halfArea = previewAreaCells * 0.5;
            var unitsPerPixel = previewAreaCells / (double)resolution;
            selectedCell = null;
            selectedCellDetails = string.Empty;

            for (var z = 0; z < resolution; z++)
            for (var x = 0; x < resolution; x++)
            {
                var worldX = checked((int)Math.Floor(
                    previewCenter.x - halfArea
                    + (x + 0.5) * unitsPerPixel));
                var worldZ = checked((int)Math.Floor(
                    previewCenter.y - halfArea
                    + (z + 0.5) * unitsPerPixel));
                var profile = RiverPatternResolver.Resolve(
                    router,
                    worldX,
                    worldZ,
                    router.Sample(worldX, worldZ),
                    settings,
                    out var weights);
                var sampleIndex = x + resolution * z;
                samples[sampleIndex] = new PatternDebugSample(
                    weights,
                    profile,
                    settings.TerrainBaseHeightUnits
                        + profile.SurfaceOffsetUnits,
                    settings.WorldPatterns.River.DepthUnits.Maximum);
                sampleCells[sampleIndex] = new Vector2Int(worldX, worldZ);
                for (var index = 0; index < 5; index++)
                {
                    var weight = GetWeight(weights, index);
                    weightSums[index] += weight;
                    maximumWeights[index] = Math.Max(
                        maximumWeights[index],
                        weight);
                    if (weight >= 0.35f)
                    {
                        strongCounts[index]++;
                    }

                }

                dominantCounts[(int)profile.DominantPattern]++;
                if (profile.RiverInfluence > 0f)
                {
                    riverCount++;
                    riverInfluenceSum += profile.RiverInfluence;
                }
            }

            var count = samples.Length;
            statistics =
                $"우세 영역: 완만 {Percent(dominantCounts[0], count)} / "
                + $"거친 {Percent(dominantCounts[1], count)} / "
                + $"산맥 {Percent(dominantCounts[2], count)} / "
                + $"협곡 {Percent(dominantCounts[3], count)} / "
                + $"바다 {Percent(dominantCounts[4], count)}\n"
                + $"강한 가중치(0.35 이상): 완만 {Percent(strongCounts[0], count)} / "
                + $"거친 {Percent(strongCounts[1], count)} / "
                + $"산맥 {Percent(strongCounts[2], count)} / "
                + $"협곡 {Percent(strongCounts[3], count)} / "
                + $"바다 {Percent(strongCounts[4], count)}\n"
                + $"평균 가중치: 완만 {weightSums[0] / count:0.000} / "
                + $"거친 {weightSums[1] / count:0.000} / "
                + $"산맥 {weightSums[2] / count:0.000} / "
                + $"협곡 {weightSums[3] / count:0.000} / "
                + $"바다 {weightSums[4] / count:0.000}\n"
                + $"최대 가중치: 완만 {maximumWeights[0]:0.000} / "
                + $"거친 {maximumWeights[1]:0.000} / "
                + $"산맥 {maximumWeights[2]:0.000} / "
                + $"협곡 {maximumWeights[3]:0.000} / "
                + $"바다 {maximumWeights[4]:0.000}\n"
                + $"강 영역 {Percent(riverCount, count)} / "
                + $"평균 강 단면 진행 {(riverCount > 0 ? riverInfluenceSum / riverCount : 0f):0.000}";
            RenderPreview();
        }

        private void BuildPreviewAtStreamingTarget(
            WorldGenerationController controller)
        {
            if (!debugger.TryGetStreamingTargetCell(out var targetCell))
            {
                Debug.LogWarning(
                    "Streaming Target의 Cell 좌표를 확인할 수 없습니다.",
                    debugger);
                return;
            }

            previewCenter = targetCell;
            BuildPreview(controller);
        }

        private void HandlePreviewSelection(
            Rect rect,
            WorldGenerationController controller)
        {
            var current = Event.current;
            if (current.type != EventType.MouseDown
                || current.button != 0
                || !rect.Contains(current.mousePosition))
            {
                return;
            }

            var normalizedX = Math.Clamp(
                (current.mousePosition.x - rect.x) / rect.width,
                0f,
                1f);
            var normalizedZ = Math.Clamp(
                1f - (current.mousePosition.y - rect.y) / rect.height,
                0f,
                1f);
            var pixelX = Math.Clamp(
                (int)Math.Floor(normalizedX * previewResolution),
                0,
                previewResolution - 1);
            var pixelZ = Math.Clamp(
                (int)Math.Floor(normalizedZ * previewResolution),
                0,
                previewResolution - 1);
            var sampleIndex = pixelX + previewResolution * pixelZ;
            if (sampleCells == null || sampleIndex >= sampleCells.Length)
            {
                return;
            }

            selectedCell = sampleCells[sampleIndex];
            if (!IsColumnLoaded(selectedCell.Value))
            {
                MoveStreamingTarget(selectedCell.Value);
            }

            UpdateSelectedCellDetails(controller);
            BuildDetailPreview(controller);
            current.Use();
            Repaint();
            SceneView.RepaintAll();
        }

        private void UpdateSelectedCellDetails(
            WorldGenerationController controller)
        {
            if (!selectedCell.HasValue)
            {
                selectedCellDetails = string.Empty;
                return;
            }

            var cell = selectedCell.Value;
            var settings = ResolveSettings(controller);
            var router = new WorldNoiseRouter(settings);
            var field = router.Sample(cell.x, cell.y);
            var profile = RiverPatternResolver.Resolve(
                router,
                cell.x,
                cell.y,
                field,
                settings,
                out var weights);
            var hasActualSurface = TryGetActualSurface(
                cell.x,
                cell.y,
                out var heightUnits);
            var actualHeight = hasActualSurface
                ? $"{heightUnits / (float)WorldGrid.HeightStepsPerCell:0.00} Cell"
                : "현재 미로드";
            var contributionText = "현재 미로드";
            if (hasActualSurface)
            {
                new WorldDensityField(settings).Sample(
                    cell.x,
                    heightUnits,
                    cell.y,
                    field,
                    profile,
                    out var contributions);
                contributionText =
                    $"수직 {contributions.VerticalGradient:+0.00;-0.00;0.00} / "
                    + $"표면 {contributions.SurfaceOffset:+0.00;-0.00;0.00} / "
                    + $"표면 세부 {contributions.SurfaceDetail:+0.00;-0.00;0.00} / "
                    + $"3D 세부 {contributions.DensityDetail:+0.00;-0.00;0.00}";
            }

            selectedCellDetails =
                $"절대 Cell: ({cell.x}, {cell.y})\n"
                + $"가중치: 완만 {weights.Smooth:0.000} / "
                + $"거친 {weights.Rugged:0.000} / "
                + $"산맥 {weights.Mountain:0.000} / "
                + $"협곡 {weights.Canyon:0.000} / "
                + $"바다 {weights.Sea:0.000}\n"
                + $"패턴: {profile.DominantPattern} / Region {profile.RegionKey} / "
                + $"내부 진행 {profile.InteriorProgress:0.000}\n"
                + $"공통 Field: 대륙 {field.Continentalness:0.000} / "
                + $"침식 {field.Erosion:0.000} / "
                + $"변형 {field.Weirdness:0.000}\n"
                + $"봉우리·계곡 {field.PeaksValleys:0.000} / "
                + $"거칠기 {field.Roughness:0.000} / "
                + $"세부 {field.Detail:+0.000;-0.000;0.000}\n"
                + $"Density Profile: 표면 {profile.SurfaceOffsetUnits:+0.00;-0.00;0.00} / "
                + $"수직 {profile.VerticalFactor:0.00} / "
                + $"세부 굴곡 {profile.DetailUnits:0.00}\n"
                + $"패턴 깊이 {profile.PatternDepthUnits / WorldGrid.HeightStepsPerCell:0.00} Cell / "
                + $"깊이 진행 {profile.PatternDepthProgress:0.000} / "
                + $"패턴 세부 {profile.PatternDetailUnits / WorldGrid.HeightStepsPerCell:+0.00;-0.00;0.00} Cell\n"
                + $"강 단면 진행 {profile.RiverInfluence:0.000} / "
                + $"강 수심 {profile.RiverDepthUnits / WorldGrid.HeightStepsPerCell:0.00} Cell\n"
                + $"수면 {profile.WaterTopUnits / (float)WorldGrid.HeightStepsPerCell:0.00} Cell / "
                + $"합성 표면 {(settings.TerrainBaseHeightUnits + profile.SurfaceOffsetUnits) / WorldGrid.HeightStepsPerCell:0.00} Cell\n"
                + $"실제 지형 표면: {actualHeight}\n"
                + $"해당 표면 Density 기여: {contributionText}";
        }

        private void FocusSelectedCell()
        {
            if (!selectedCell.HasValue
                || !TryGetActualSurface(
                    selectedCell.Value.x,
                    selectedCell.Value.y,
                    out var heightUnits))
            {
                Debug.LogWarning(
                    "선택한 Cell의 Chunk가 현재 준비되어 있지 않습니다.");
                return;
            }

            var runtime = worldManager.CurrentWorldRuntime;
            var cell = selectedCell.Value;
            var position = ToWorldPosition(new Vector3(
                (cell.x + 0.5f) * runtime.Data.CellSize,
                heightUnits * runtime.Data.HeightStep,
                (cell.y + 0.5f) * runtime.Data.CellSize));
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return;
            }

            sceneView.LookAt(
                position,
                Quaternion.Euler(40f, 0f, 0f),
                runtime.Data.CellSize * 12f);
            sceneView.Repaint();
        }

        private void DrawWorldOverlay(SceneView sceneView)
        {
            if (!showWorldOverlay
                || !selectedCell.HasValue
                || !TryResolveWorldManager()
                || worldManager.CurrentWorldRuntime == null)
            {
                return;
            }

            var runtime = worldManager.CurrentWorldRuntime;
            var world = runtime.Data;
            var center = selectedCell.Value;
            var resolution = worldOverlayRadius * 2 + 1;
            if (detailSamples == null
                || detailSamples.Length != resolution * resolution)
            {
                return;
            }

            var cellSize = world.CellSize;
            var verticalOffset = world.HeightStep * 0.04f;
            var corners = new Vector3[4];

            for (var z = center.y - worldOverlayRadius;
                 z <= center.y + worldOverlayRadius;
                 z++)
            for (var x = center.x - worldOverlayRadius;
                 x <= center.x + worldOverlayRadius;
                 x++)
            {
                if (!world.IsColumnLoaded(x, z))
                {
                    continue;
                }

                var surface = runtime.SurfaceCache.GetSurfaceHeight(x, z);
                if (!surface.HasGround)
                {
                    continue;
                }

                var sampleX = x - (center.x - worldOverlayRadius);
                var sampleZ = z - (center.y - worldOverlayRadius);
                var color = GetColor(
                    detailSamples[sampleX + resolution * sampleZ],
                    patternView);
                color.a = 0.3f;
                var y = surface.GroundHeight * world.HeightStep
                    + verticalOffset;
                corners[0] = ToWorldPosition(
                    new Vector3(x * cellSize, y, z * cellSize));
                corners[1] = ToWorldPosition(
                    new Vector3((x + 1) * cellSize, y, z * cellSize));
                corners[2] = ToWorldPosition(new Vector3(
                    (x + 1) * cellSize,
                    y,
                    (z + 1) * cellSize));
                corners[3] = ToWorldPosition(
                    new Vector3(x * cellSize, y, (z + 1) * cellSize));
                Handles.DrawSolidRectangleWithOutline(
                    corners,
                    color,
                    new Color(color.r, color.g, color.b, 0.75f));
            }

            if (TryGetActualSurface(center.x, center.y, out var selectedHeight))
            {
                var labelPosition = ToWorldPosition(new Vector3(
                    (center.x + 0.5f) * cellSize,
                    selectedHeight * world.HeightStep + world.HeightStep,
                    (center.y + 0.5f) * cellSize));
                Handles.Label(
                    labelPosition,
                    $"Cell ({center.x}, {center.y})");
            }
        }

        private bool TryGetActualSurface(
            int x,
            int z,
            out int heightUnits)
        {
            heightUnits = 0;
            if (!TryResolveWorldManager()
                || worldManager.CurrentWorldRuntime == null
                || !worldManager.CurrentWorldData.IsColumnLoaded(x, z))
            {
                return false;
            }

            var surface = worldManager.CurrentWorldRuntime.SurfaceCache
                .GetSurfaceHeight(x, z);
            heightUnits = surface.GroundHeight;
            return surface.HasGround;
        }

        private bool TryResolveWorldManager()
        {
            worldManager = debugger != null ? debugger.WorldManager : null;
            return worldManager != null;
        }

        private Vector3 ToWorldPosition(Vector3 localPosition)
        {
            var origin = debugger != null
                ? debugger.StreamingController?.WorldOrigin
                : null;
            return origin != null
                ? origin.TransformPoint(localPosition)
                : localPosition;
        }

        private WorldSettingsData ResolveSettings(
            WorldGenerationController controller) =>
            TryResolveWorldManager()
            && worldManager.CurrentWorldRuntime != null
                ? worldManager.CurrentWorldRuntime.Data.Settings
                : controller.Settings.CreateData(controller.Seed);

        private bool IsColumnLoaded(Vector2Int cell) =>
            TryResolveWorldManager()
            && worldManager.CurrentWorldData != null
            && worldManager.CurrentWorldData.IsColumnLoaded(cell.x, cell.y);

        private void MoveStreamingTarget(Vector2Int cell)
        {
            var targetTransform = debugger.ResolveStreamingTarget();
            if (targetTransform != null && !Application.isPlaying)
            {
                Undo.RecordObject(
                    targetTransform,
                    "Move Streaming Target To Terrain Cell");
            }

            if (!debugger.TryMoveStreamingTargetToCell(cell.x, cell.y))
            {
                Debug.LogWarning(
                    $"Streaming Target을 Cell ({cell.x}, {cell.y})로 이동할 수 없습니다.",
                    debugger);
            }
        }

        private void DrawStreamingTargetMarker(
            Rect rect,
            float minimumX,
            float minimumZ,
            int areaCells)
        {
            if (areaCells <= 0
                || !debugger.TryGetStreamingTargetCell(out var targetCell))
            {
                return;
            }

            var normalizedX = (targetCell.x + 0.5f - minimumX)
                / areaCells;
            var normalizedZ = (targetCell.y + 0.5f - minimumZ)
                / areaCells;
            if (normalizedX < 0f || normalizedX > 1f
                || normalizedZ < 0f || normalizedZ > 1f)
            {
                return;
            }

            const float markerSize = 7f;
            var markerRect = new Rect(
                rect.x + normalizedX * rect.width - markerSize * 0.5f,
                rect.yMax - normalizedZ * rect.height - markerSize * 0.5f,
                markerSize,
                markerSize);
            EditorGUI.DrawRect(markerRect, Color.red);
        }

        private void RenderPreview()
        {
            if (samples == null || samples.Length == 0)
            {
                return;
            }

            var resolution = (int)Math.Sqrt(samples.Length);
            if (previewTexture == null
                || previewTexture.width != resolution
                || previewTexture.height != resolution)
            {
                DestroyPreviewTexture();
                previewTexture = new Texture2D(
                    resolution,
                    resolution,
                    TextureFormat.RGBA32,
                    false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var colors = new Color[samples.Length];
            for (var index = 0; index < samples.Length; index++)
            {
                colors[index] = GetColor(samples[index], patternView);
            }

            previewTexture.SetPixels(colors);
            previewTexture.Apply(false, false);
        }

        private void BuildDetailPreview(
            WorldGenerationController controller)
        {
            if (!selectedCell.HasValue)
            {
                detailSamples = null;
                DestroyDetailTexture();
                return;
            }

            var settings = ResolveSettings(controller);
            var router = new WorldNoiseRouter(settings);
            var resolution = worldOverlayRadius * 2 + 1;
            detailSamples = new PatternDebugSample[
                resolution * resolution];
            var center = selectedCell.Value;
            for (var z = 0; z < resolution; z++)
            for (var x = 0; x < resolution; x++)
            {
                var profile = RiverPatternResolver.Resolve(
                    router,
                    center.x - worldOverlayRadius + x,
                    center.y - worldOverlayRadius + z,
                    router.Sample(
                        center.x - worldOverlayRadius + x,
                        center.y - worldOverlayRadius + z),
                    settings,
                    out var weights);
                detailSamples[x + resolution * z] = new PatternDebugSample(
                    weights,
                    profile,
                    settings.TerrainBaseHeightUnits
                        + profile.SurfaceOffsetUnits,
                    settings.WorldPatterns.River.DepthUnits.Maximum);
            }

            RenderDetailPreview();
        }

        private void RenderDetailPreview()
        {
            if (detailSamples == null || detailSamples.Length == 0)
            {
                return;
            }

            var resolution = (int)Math.Sqrt(detailSamples.Length);
            if (detailTexture == null
                || detailTexture.width != resolution
                || detailTexture.height != resolution)
            {
                DestroyDetailTexture();
                detailTexture = new Texture2D(
                    resolution,
                    resolution,
                    TextureFormat.RGBA32,
                    false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var colors = new Color[detailSamples.Length];
            for (var index = 0; index < detailSamples.Length; index++)
            {
                colors[index] = GetColor(detailSamples[index], patternView);
            }

            detailTexture.SetPixels(colors);
            detailTexture.Apply(false, false);
        }

        private static Color GetColor(
            in PatternDebugSample sample,
            PatternView view)
        {
            if (view == PatternView.SeaArea)
            {
                return Color.Lerp(
                    Color.black,
                    SeaColor,
                    sample.Result.DominantPattern == WorldPatternType.Sea
                        ? sample.Result.InteriorProgress
                        : 0f);
            }

            if (view == PatternView.SeaDepth)
            {
                return Color.Lerp(
                    Color.black,
                    SeaColor,
                    sample.Result.DominantPattern == WorldPatternType.Sea
                        ? sample.Result.PatternDepthProgress
                        : 0f);
            }

            if (view == PatternView.SeaWater)
            {
                var waterDepthUnits = Math.Max(
                    0f,
                    sample.Result.WaterTopUnits
                        - sample.FinalSurfaceUnits);
                var ratio = sample.Result.PatternDepthUnits > 0f
                    ? Math.Clamp(
                        waterDepthUnits / sample.Result.PatternDepthUnits,
                        0f,
                        1f)
                    : 0f;
                return Color.Lerp(Color.black, SeaColor, ratio);
            }

            if (view == PatternView.RiverArea)
            {
                return Color.Lerp(
                    Color.black,
                    RiverColor,
                    sample.Result.RiverInfluence);
            }

            if (view == PatternView.RiverDepth)
            {
                var ratio = sample.RiverMaximumDepthUnits > 0f
                    ? Math.Clamp(
                        sample.Result.RiverDepthUnits
                            / sample.RiverMaximumDepthUnits,
                        0f,
                        1f)
                    : 0f;
                return Color.Lerp(Color.black, RiverColor, ratio);
            }

            if (view == PatternView.RiverWater)
            {
                var waterDepthUnits = sample.Result.WaterType == WaterType.River
                    ? Math.Max(
                        0f,
                        sample.Result.WaterTopUnits
                            - sample.FinalSurfaceUnits)
                    : 0f;
                var ratio = sample.RiverMaximumDepthUnits > 0f
                    ? Math.Clamp(
                        waterDepthUnits / sample.RiverMaximumDepthUnits,
                        0f,
                        1f)
                    : 0f;
                return Color.Lerp(Color.black, RiverColor, ratio);
            }

            var weights = sample.Weights;
            if (view == PatternView.Blend)
            {
                return SmoothColor * weights.Smooth
                    + RuggedColor * weights.Rugged
                    + MountainColor * weights.Mountain
                    + CanyonColor * weights.Canyon
                    + SeaColor * weights.Sea;
            }

            if (view >= PatternView.Smooth && view <= PatternView.Sea)
            {
                var weight = GetWeight(weights, (int)view - 2);
                return new Color(weight, weight, weight, 1f);
            }

            var dominant = (int)sample.Result.DominantPattern;
            var color = sample.Result.DominantPattern switch
            {
                WorldPatternType.Smooth => SmoothColor,
                WorldPatternType.Rugged => RuggedColor,
                WorldPatternType.Mountain => MountainColor,
                WorldPatternType.Canyon => CanyonColor,
                WorldPatternType.Sea => SeaColor,
                _ => Color.magenta
            };
            return Color.Lerp(
                Color.black,
                color,
                GetWeight(weights, dominant));
        }

        private static float GetWeight(
            in WorldPatternWeights sample,
            int index) => index switch
            {
                0 => sample.Smooth,
                1 => sample.Rugged,
                2 => sample.Mountain,
                3 => sample.Canyon,
                4 => sample.Sea,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };

        private static string Percent(int value, int total) =>
            $"{value * 100f / total:0.0}%";

        private void ClearPreview()
        {
            samples = null;
            sampleCells = null;
            detailSamples = null;
            statistics = string.Empty;
            selectedCell = null;
            selectedCellDetails = string.Empty;
            DestroyPreviewTexture();
            DestroyDetailTexture();
            SceneView.RepaintAll();
        }

        private void DestroyPreviewTexture()
        {
            if (previewTexture == null)
            {
                return;
            }

            DestroyImmediate(previewTexture);
            previewTexture = null;
        }

        private void DestroyDetailTexture()
        {
            if (detailTexture == null)
            {
                return;
            }

            DestroyImmediate(detailTexture);
            detailTexture = null;
        }
    }
}
