using MiniCivilization.World.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class WorldOperationProgressView : MonoBehaviour
    {
        [SerializeField] private WorldManager worldManager;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text stageText;
        [SerializeField] private Text completedText;

        private void OnEnable()
        {
            Subscribe();
            Refresh(worldManager?.CurrentOperationProgress ?? default);
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(
            WorldManager manager,
            GameObject root,
            Text stage,
            Text completed)
        {
            Unsubscribe();
            worldManager = manager;
            panelRoot = root;
            stageText = stage;
            completedText = completed;
            Subscribe();
            Refresh(worldManager?.CurrentOperationProgress ?? default);
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
                stageText.text = $"{GetOperationName(progress.Kind)} · {GetStageName(progress.Stage)}";
            }

            if (completedText != null)
            {
                completedText.text = $"완료 단계: {progress.CompletedStageCount} / {progress.StageCount}";
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
                _ => string.Empty
            };
        }
    }
}
