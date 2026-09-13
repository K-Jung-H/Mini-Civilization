using MiniCivilization.World.Definitions;
using MiniCivilization.World.Editing;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class WorldUIManager : MonoBehaviour
    {
        [Header("Runtime Systems")]
        [SerializeField] private WorldEditToolState editToolState;
        [SerializeField] private WorldEditInputController editInputController;
        [SerializeField] private WorldEditApplyController editApplyController;
        [SerializeField] private EntityEditController entityEditController;
        [SerializeField] private WorldTileSelectionState selectionState;
        [SerializeField] private WorldCellInfoProvider infoProvider;

        [Header("World UI")]
        [SerializeField] private RoadVisualCatalog roadVisualCatalog;
        [SerializeField] private WorldEditConfirmationView editConfirmationView;
        [SerializeField] private WorldStreamingProgressView streamingProgressView;

        public void Initialize(WorldManager manager)
        {
            if (manager == null)
            {
                return;
            }

            streamingProgressView?.SetWorldManager(manager);
            entityEditController?.Configure(manager.EntityManager, manager.EditController);
            editInputController?.Configure(manager, editToolState, selectionState, editConfirmationView);
            editApplyController?.Configure(manager.EditController, selectionState, editToolState,
                editInputController, entityEditController, manager.EntityManager);
            var mainSceneView = GetComponent<MainSceneUIView>();
            var inspectorView = GetComponent<WorldInspectorView>();
            if (mainSceneView == null || inspectorView == null)
            {
                Debug.LogError(
                    $"{nameof(WorldUIManager)} requires serialized MainSceneUIView and WorldInspectorView components.",
                    this);
                return;
            }
            inspectorView.Configure(manager, selectionState, infoProvider);
            mainSceneView.Configure(
                manager,
                editToolState,
                editApplyController,
                roadVisualCatalog);
        }

    }
}


