using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class WorldEditToolbarGroup : MonoBehaviour
    {
        [SerializeField] private RectTransform content;
        [SerializeField] private bool startExpanded;

        private Button expandButton;
        private bool isExpanded;

        public Button ExpandButton =>
            expandButton != null
                ? expandButton
                : expandButton = GetComponent<Button>();
        public RectTransform Content => content;
        public bool IsExpanded => isExpanded;

        public void Initialize()
        {
            isExpanded = startExpanded;
        }

        public bool SetExpanded(bool expanded)
        {
            if (isExpanded == expanded)
            {
                return false;
            }

            isExpanded = expanded;
            return true;
        }

        public void Toggle()
        {
            isExpanded = !isExpanded;
        }

        public void RefreshVisibility(bool toolbarExpanded)
        {
            if (content != null)
            {
                content.gameObject.SetActive(toolbarExpanded && isExpanded);
            }
        }
    }
}
