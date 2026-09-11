using MiniCivilization.World.Runtime;
using TMPro;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class WorldStreamingProgressView : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Text stageText;
        [SerializeField] private TMP_Text completedText;
        [SerializeField] private RectTransform progressFill;

        private WorldManager worldManager;
        private bool subscribed;

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
            if (!subscribed && isActiveAndEnabled && worldManager != null)
            {
                worldManager.StreamingProgressChanged += OnProgressChanged;
                subscribed = true;
            }
        }

        private void Unsubscribe()
        {
            if (subscribed && worldManager != null)
            {
                worldManager.StreamingProgressChanged -= OnProgressChanged;
            }
            subscribed = false;
        }

        private void OnProgressChanged(WorldStreamingProgress progress) =>
            Refresh(progress);

        private void Refresh() => Refresh(
            worldManager == null
                ? default
                : worldManager.StreamingProgress);

        private void Refresh(WorldStreamingProgress progress)
        {
            if (progressFill != null)
            {
                var fraction = progress.RequestedChunkCount == 0
                    ? 0f
                    : progress.CompletedChunkCount / (float)progress.RequestedChunkCount;
                progressFill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            }

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
