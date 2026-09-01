using MiniCivilization.World.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class WorldOperationProgressView : MonoBehaviour
    {
        private WorldManager worldManager;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text stageText;
        [SerializeField] private Text completedText;

        public WorldManager WorldManager => worldManager;

        private void OnEnable()
        {
            Subscribe();
            Refresh(worldManager?.CurrentOperationProgress ?? default);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void SetWorldManager(WorldManager manager)
        {
            Unsubscribe();
            worldManager = manager;
            if (isActiveAndEnabled)
            {
                Subscribe();
                Refresh(worldManager?.CurrentOperationProgress ?? default);
            }
        }

        private void Subscribe()
        {
            if (worldManager != null)
            {
                worldManager.OperationProgressChanged += Refresh;
            }
        }

        private void Unsubscribe()
        {
            if (worldManager != null)
            {
                worldManager.OperationProgressChanged -= Refresh;
            }
        }

        private void Refresh(WorldOperationProgress progress)
        {
            if (panelRoot == null)
            {
                return;
            }

            panelRoot.SetActive(progress.IsRunning);
            if (!progress.IsRunning)
            {
                return;
            }

            if (stageText != null)
            {
                stageText.text = $"{GetOperationName(progress.Kind)} · "
                    + $"{progress.CompletedStageCount + 1} / {progress.StageCount} · "
                    + GetStageName(progress.Stage);
            }

            if (completedText != null)
            {
                completedText.text = progress.WorkCount > 0
                    ? $"{progress.CompletedWorkCount} / {progress.WorkCount} "
                        + $"({GetWorkPercent(progress)}%)"
                    : string.Empty;
            }
        }

        private static string GetOperationName(WorldOperationKind kind) =>
            kind == WorldOperationKind.Load ? "월드 로드" : "월드 생성";

        private static string GetStageName(WorldOperationStage stage)
        {
            return stage switch
            {
                WorldOperationStage.Terrain => "지형 생성",
                WorldOperationStage.WaterFeatures => "수문·수역 계획",
                WorldOperationStage.Biome => "바이옴 계산",
                WorldOperationStage.BuildWorldData => "월드 데이터 조립",
                WorldOperationStage.PrepareRuntime => "런타임 준비",
                WorldOperationStage.ReadSave => "저장 파일 읽기",
                WorldOperationStage.Mesh => "Mesh 생성 중",
                WorldOperationStage.ChunkData => "Chunk 데이터 생성 중",
                _ => string.Empty
            };
        }

        private static int GetWorkPercent(WorldOperationProgress progress) =>
            Mathf.FloorToInt(100f * progress.CompletedWorkCount
                / progress.WorkCount);
    }
}
