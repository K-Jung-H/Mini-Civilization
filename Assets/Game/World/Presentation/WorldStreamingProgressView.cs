using MiniCivilization.World.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class WorldStreamingProgressView : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text stageText;
        [SerializeField] private Text completedText;

        private WorldManager worldManager;

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void SetWorldManager(WorldManager manager)
        {
            if (ReferenceEquals(worldManager, manager))
            {
                Refresh();
                return;
            }

            Unsubscribe();
            worldManager = manager;
            Subscribe();
            Refresh();
        }

        private void Subscribe()
        {
            if (worldManager != null)
            {
                worldManager.StreamingProgressChanged += OnProgressChanged;
            }
        }

        private void Unsubscribe()
        {
            if (worldManager != null)
            {
                worldManager.StreamingProgressChanged -= OnProgressChanged;
            }
        }

        private void OnProgressChanged(WorldStreamingProgress progress) =>
            Refresh(progress);

        private void Refresh() => Refresh(
            worldManager == null
                ? default
                : worldManager.StreamingProgress);

        private void Refresh(WorldStreamingProgress progress)
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(progress.IsGenerating);
            }

            if (!progress.IsGenerating)
            {
                return;
            }

            if (stageText != null)
            {
                stageText.text = "Chunk 데이터 생성 중";
            }

            if (completedText != null)
            {
                completedText.text = $"{progress.CompletedChunkCount} / "
                    + $"{progress.RequestedChunkCount} "
                    + $"({progress.PercentComplete}%)";
            }
        }
    }
}
