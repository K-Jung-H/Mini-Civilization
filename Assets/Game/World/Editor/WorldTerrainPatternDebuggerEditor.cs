using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation;
using MiniCivilization.World.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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
            BasinArea,
            BasinDepth,
            LakeWater,
            PondWater,
            HydrologyComponent,
            RiverArea,
            RiverDepth,
            RiverWater
        }

        private readonly struct PatternDebugSample
        {
            public PatternDebugSample(
                in WorldPatternWeights weights,
                in WorldPatternResult result,
                float riverMaximumDepthUnits)
            {
                Weights = weights;
                Result = result;
                RiverMaximumDepthUnits = riverMaximumDepthUnits;
                FinalSurfaceUnits = 0f;
                HasFinalSurface = false;
            }

            private PatternDebugSample(
                in WorldPatternWeights weights,
                in WorldPatternResult result,
                float riverMaximumDepthUnits,
                float finalSurfaceUnits)
            {
                Weights = weights;
                Result = result;
                RiverMaximumDepthUnits = riverMaximumDepthUnits;
                FinalSurfaceUnits = finalSurfaceUnits;
                HasFinalSurface = true;
            }

            public WorldPatternWeights Weights { get; }
            public WorldPatternResult Result { get; }
            public float FinalSurfaceUnits { get; }
            public float RiverMaximumDepthUnits { get; }
            public bool HasFinalSurface { get; }

            public PatternDebugSample WithFinalSurface(
                float finalSurfaceUnits) => new(
                Weights,
                Result,
                RiverMaximumDepthUnits,
                finalSurfaceUnits);
        }

        private static readonly Color SmoothColor = new(0.25f, 0.8f, 0.3f);
        private static readonly Color RuggedColor = new(0.9f, 0.5f, 0.15f);
        private static readonly Color MountainColor = new(0.9f, 0.9f, 0.9f);
        private static readonly Color CanyonColor = new(0.65f, 0.15f, 0.75f);
        private static readonly Color SeaColor = new(0.1f, 0.45f, 0.9f);
        private static readonly Color RiverColor = new(0.1f, 0.85f, 0.95f);
        private static readonly Color LakeColor = new(0.08f, 0.55f, 0.95f);
        private static readonly Color PondColor = new(0.15f, 0.75f, 0.7f);

        private Vector2Int previewCenter;
        private int previewAreaCells = 512;
        private int previewResolution = 128;
        private PatternView patternView;
        private PatternDebugSample[] samples;
        private Vector2Int[] sampleCells;
        private PatternDebugSample[] detailSamples;
        private WorldSettingsData previewSettings;
        private WorldHydrology previewHydrology;
        private Texture2D previewTexture;
        private Texture2D detailTexture;
        private Mesh worldOverlayMesh;
        private Material worldOverlayMaterial;
        private readonly System.Collections.Generic.List<Vector3>
            overlayVertices = new();
        private readonly System.Collections.Generic.List<Color>
            overlayColors = new();
        private readonly System.Collections.Generic.List<int>
            overlayTriangles = new();
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
            DestroyOverlayResources();
        }

        private void BuildPreview(WorldGenerationController controller)
        {
            var settings = ResolveSettings(controller);
            previewSettings = settings;
            previewHydrology = ResolveHydrology(settings);
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
            var lakeCount = 0;
            var pondCount = 0;
            var halfArea = previewAreaCells * 0.5;
            var unitsPerPixel = previewAreaCells / (double)resolution;
            selectedCell = null;
            selectedCellDetails = string.Empty;
            HydrologyPlanScope scope = RequiresHydrologyPlan(patternView)
                ? previewHydrology.BeginPlanScope()
                : null;
            try
            {
                for (var z = 0; z < resolution; z++)
                for (var x = 0; x < resolution; x++)
                {
                    var worldX = checked((int)Math.Floor(
                        previewCenter.x - halfArea
                        + (x + 0.5) * unitsPerPixel));
                    var worldZ = checked((int)Math.Floor(
                        previewCenter.y - halfArea
                        + (z + 0.5) * unitsPerPixel));
                    var terrain = previewHydrology.SampleBaseTerrain(worldX, worldZ);
                    var profile = scope == null
                        ? terrain.Terrain
                        : HydrologyPatternResolver.Resolve(
                            settings,
                            HydrologyBatchBuilder.Sample(
                                previewHydrology,
                                scope,
                                worldX,
                                worldZ),
                            terrain.Terrain);
                    var weights = WorldPatternResolver.SampleWeights(
                        router,
                        worldX,
                        worldZ);
                    var sampleIndex = x + resolution * z;
                    samples[sampleIndex] = new PatternDebugSample(
                        weights,
                        profile,
                        settings.Hydrology.RiverCorridor.DepthUnits.Maximum);
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
                    if (profile.WaterType == WaterType.Lake) lakeCount++;
                    if (profile.WaterType == WaterType.Pond) pondCount++;
                }
            }
            finally
            {
                scope?.Dispose();
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
                + $"평균 강 단면 진행 {(riverCount > 0 ? riverInfluenceSum / riverCount : 0f):0.000}\n"
                + $"Lake 수면 {Percent(lakeCount, count)} / "
                + $"Pond 수면 {Percent(pondCount, count)}";
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
            var settings = ResolvePreviewSettings(controller);
            var router = new WorldNoiseRouter(settings);
            var hydrology = ResolvePreviewHydrology(settings)
                ?? throw new InvalidOperationException(
                    "Preview Hydrology is not ready.");
            using var scope = hydrology.BeginPlanScope();
            var terrain = hydrology.SampleBaseTerrain(cell.x, cell.y);
            var field = terrain.Field;
            var profile = HydrologyPatternResolver.Resolve(
                settings,
                HydrologyBatchBuilder.Sample(hydrology, scope, cell.x, cell.y),
                terrain.Terrain);
            var weights = WorldPatternResolver.SampleWeights(
                router,
                cell.x,
                cell.y);
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
                + $"Hydrology: {profile.HydrologyType} / Component {profile.HydrologyComponentId} / "
                + $"영역 {profile.HydrologyMembership:0.000} / 내부 {profile.HydrologyInteriorProgress:0.000}\n"
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
            overlayVertices.Clear();
            overlayColors.Clear();
            overlayTriangles.Clear();

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
                var vertexStart = overlayVertices.Count;
                overlayVertices.Add(ToWorldPosition(
                    new Vector3(x * cellSize, y, z * cellSize)));
                overlayVertices.Add(ToWorldPosition(
                    new Vector3((x + 1) * cellSize, y, z * cellSize)));
                overlayVertices.Add(ToWorldPosition(new Vector3(
                    (x + 1) * cellSize,
                    y,
                    (z + 1) * cellSize)));
                overlayVertices.Add(ToWorldPosition(
                    new Vector3(x * cellSize, y, (z + 1) * cellSize)));
                overlayColors.Add(color);
                overlayColors.Add(color);
                overlayColors.Add(color);
                overlayColors.Add(color);
                overlayTriangles.Add(vertexStart);
                overlayTriangles.Add(vertexStart + 1);
                overlayTriangles.Add(vertexStart + 2);
                overlayTriangles.Add(vertexStart);
                overlayTriangles.Add(vertexStart + 2);
                overlayTriangles.Add(vertexStart + 3);
            }

            if (overlayVertices.Count > 0 && EnsureOverlayResources())
            {
                worldOverlayMesh.Clear();
                worldOverlayMesh.SetVertices(overlayVertices);
                worldOverlayMesh.SetColors(overlayColors);
                worldOverlayMesh.SetTriangles(overlayTriangles, 0);
                worldOverlayMesh.RecalculateBounds();
                worldOverlayMaterial.SetPass(0);
                Graphics.DrawMeshNow(worldOverlayMesh, Matrix4x4.identity);
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

        private bool EnsureOverlayResources()
        {
            if (worldOverlayMesh == null)
            {
                worldOverlayMesh = new Mesh
                {
                    name = "World Terrain Pattern Overlay",
                    hideFlags = HideFlags.HideAndDontSave
                };
                worldOverlayMesh.MarkDynamic();
            }

            if (worldOverlayMaterial != null)
            {
                return true;
            }

            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                return false;
            }

            worldOverlayMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            worldOverlayMaterial.SetInt(
                "_SrcBlend",
                (int)BlendMode.SrcAlpha);
            worldOverlayMaterial.SetInt(
                "_DstBlend",
                (int)BlendMode.OneMinusSrcAlpha);
            worldOverlayMaterial.SetInt("_Cull", (int)CullMode.Off);
            worldOverlayMaterial.SetInt("_ZWrite", 0);
            worldOverlayMaterial.SetInt(
                "_ZTest",
                (int)CompareFunction.LessEqual);
            return true;
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

        private WorldSettingsData ResolvePreviewSettings(
            WorldGenerationController controller) =>
            previewSettings ?? ResolveSettings(controller);

        private WorldHydrology ResolveHydrology(
            WorldSettingsData settings)
        {
            if (TryResolveWorldManager()
                && worldManager.CurrentWorldRuntime != null
                && ReferenceEquals(
                    worldManager.CurrentWorldRuntime.Data.Settings,
                    settings))
            {
                return worldManager.CurrentWorldRuntime.Hydrology;
            }

            return new WorldHydrology(settings);
        }

        private WorldHydrology ResolvePreviewHydrology(
            WorldSettingsData settings)
        {
            if (previewHydrology == null
                || !ReferenceEquals(
                    previewHydrology.Settings,
                    settings))
            {
                previewHydrology = ResolveHydrology(settings);
            }

            return previewHydrology;
        }

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

            if (RequiresFinalSurface(patternView))
            {
                EnsurePreviewFinalSurfaces();
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

        private static bool RequiresFinalSurface(PatternView view) =>
            view is PatternView.SeaWater or PatternView.RiverWater;

        private static bool RequiresHydrologyPlan(PatternView view) =>
            view is PatternView.Dominant
                or PatternView.BasinArea
                or PatternView.BasinDepth
                or PatternView.LakeWater
                or PatternView.PondWater
                or PatternView.HydrologyComponent
                or PatternView.RiverArea
                or PatternView.RiverDepth
                or PatternView.RiverWater;

        private static bool RequiresFinalSurface(
            in PatternDebugSample sample,
            PatternView view) =>
            view == PatternView.SeaWater
                && sample.Result.WaterType == WaterType.Sea
            || view == PatternView.RiverWater
                && sample.Result.WaterType == WaterType.River;

        private void EnsurePreviewFinalSurfaces()
        {
            if (samples == null || sampleCells == null || previewSettings == null)
            {
                return;
            }

            var settings = previewSettings;
            var hydrology = ResolvePreviewHydrology(settings);
            var density = new WorldDensityField(settings);
            for (var index = 0; index < samples.Length; index++)
            {
                var sample = samples[index];
                if (sample.HasFinalSurface
                    || !RequiresFinalSurface(sample, patternView))
                {
                    continue;
                }

                var cell = sampleCells[index];
                samples[index] = ResolveFinalSurface(
                    sample,
                    hydrology,
                    density,
                    settings,
                    cell.x,
                    cell.y);
            }
        }

        private void EnsureDetailFinalSurfaces()
        {
            if (detailSamples == null || !selectedCell.HasValue)
            {
                return;
            }

            var settings = previewSettings;
            if (settings == null)
            {
                return;
            }

            var hydrology = ResolvePreviewHydrology(settings);
            var density = new WorldDensityField(settings);
            var resolution = worldOverlayRadius * 2 + 1;
            var center = selectedCell.Value;
            for (var z = 0; z < resolution; z++)
            for (var x = 0; x < resolution; x++)
            {
                var index = x + resolution * z;
                var sample = detailSamples[index];
                if (sample.HasFinalSurface
                    || !RequiresFinalSurface(sample, patternView))
                {
                    continue;
                }

                detailSamples[index] = ResolveFinalSurface(
                    sample,
                    hydrology,
                    density,
                    settings,
                    center.x - worldOverlayRadius + x,
                    center.y - worldOverlayRadius + z);
            }
        }

        private static PatternDebugSample ResolveFinalSurface(
            in PatternDebugSample sample,
            WorldHydrology hydrology,
            in WorldDensityField density,
            WorldSettingsData settings,
            int worldX,
            int worldZ)
        {
            var terrain = hydrology.SampleBaseTerrain(
                worldX,
                worldZ);
            return sample.WithFinalSurface(
                TerrainSurfaceSampler.SampleResolved(
                    density,
                    settings,
                    worldX,
                    worldZ,
                    terrain.Field,
                    sample.Result).SurfaceUnits);
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

            var settings = ResolvePreviewSettings(controller);
            var hydrology = ResolvePreviewHydrology(settings);
            var router = new WorldNoiseRouter(settings);
            var resolution = worldOverlayRadius * 2 + 1;
            detailSamples = new PatternDebugSample[
                resolution * resolution];
            var center = selectedCell.Value;
            using var scope = hydrology.BeginPlanScope();
            using var hydrologyBatch = HydrologyBatchBuilder.Build(
                hydrology,
                scope,
                center.x - worldOverlayRadius,
                center.y - worldOverlayRadius,
                resolution,
                resolution);
            for (var z = 0; z < resolution; z++)
            for (var x = 0; x < resolution; x++)
            {
                var worldX = center.x - worldOverlayRadius + x;
                var worldZ = center.y - worldOverlayRadius + z;
                var terrain = hydrologyBatch.SampleBaseTerrainState(
                    worldX,
                    worldZ);
                var profile = HydrologyPatternResolver.Resolve(
                    worldX,
                    worldZ,
                    settings,
                    hydrologyBatch,
                    terrain.Terrain);
                var weights = WorldPatternResolver.SampleWeights(
                    router,
                    worldX,
                    worldZ);
                detailSamples[x + resolution * z] = new PatternDebugSample(
                    weights,
                    profile,
                    settings.Hydrology.RiverCorridor.DepthUnits.Maximum);
            }

            RenderDetailPreview();
        }

        private void RenderDetailPreview()
        {
            if (detailSamples == null || detailSamples.Length == 0)
            {
                return;
            }

            if (RequiresFinalSurface(patternView))
            {
                EnsureDetailFinalSurfaces();
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

            if (view == PatternView.BasinArea)
            {
                var basin = sample.Result.HydrologyType is WaterType.Lake
                    or WaterType.Pond;
                return Color.Lerp(
                    Color.black,
                    sample.Result.HydrologyType == WaterType.Lake
                        ? LakeColor
                        : PondColor,
                    basin ? sample.Result.HydrologyMembership : 0f);
            }

            if (view == PatternView.BasinDepth)
            {
                var basin = sample.Result.HydrologyType is WaterType.Lake
                    or WaterType.Pond;
                return Color.Lerp(
                    Color.black,
                    sample.Result.HydrologyType == WaterType.Lake
                        ? LakeColor
                        : PondColor,
                    basin ? sample.Result.HydrologyInteriorProgress : 0f);
            }

            if (view is PatternView.LakeWater or PatternView.PondWater)
            {
                var type = view == PatternView.LakeWater
                    ? WaterType.Lake
                    : WaterType.Pond;
                var waterColor = type == WaterType.Lake
                    ? LakeColor
                    : PondColor;
                return sample.Result.WaterType == type
                    ? waterColor
                    : Color.black;
            }

            if (view == PatternView.HydrologyComponent)
            {
                return sample.Result.HydrologyMembership > 0f
                    ? ComponentColor(sample.Result.HydrologyComponentId)
                    : Color.black;
            }

            if (view == PatternView.Dominant
                && sample.Result.HydrologyMembership > 0f)
            {
                var hydrologyColor = sample.Result.HydrologyType switch
                {
                    WaterType.River => RiverColor,
                    WaterType.Lake => LakeColor,
                    WaterType.Pond => PondColor,
                    WaterType.Sea => SeaColor,
                    _ => Color.black
                };
                return Color.Lerp(
                    Color.black,
                    hydrologyColor,
                    sample.Result.HydrologyMembership);
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

        private static Color ComponentColor(int componentId)
        {
            var hash = unchecked((uint)componentId * 2654435761u);
            return Color.HSVToRGB((hash & 0x00FFFFFFu) / 16777215f, 0.75f, 1f);
        }

        private static string Percent(int value, int total) =>
            $"{value * 100f / total:0.0}%";

        private void ClearPreview()
        {
            samples = null;
            sampleCells = null;
            detailSamples = null;
            previewSettings = null;
            previewHydrology = null;
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

        private void DestroyOverlayResources()
        {
            if (worldOverlayMesh != null)
            {
                DestroyImmediate(worldOverlayMesh);
                worldOverlayMesh = null;
            }

            if (worldOverlayMaterial != null)
            {
                DestroyImmediate(worldOverlayMaterial);
                worldOverlayMaterial = null;
            }

            overlayVertices.Clear();
            overlayColors.Clear();
            overlayTriangles.Clear();
        }
    }
}
