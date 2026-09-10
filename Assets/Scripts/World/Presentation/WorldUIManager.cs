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

        private WorldManager worldManager;
        private MainSceneUIView mainSceneView;

        public void Configure(
            WorldEditToolState toolState,
            WorldEditApplyController editApply,
            EntityEditController entityEdit,
            WorldTileSelectionState selections,
            WorldCellInfoProvider cellInfoProvider)
        {
            editToolState = toolState;
            editApplyController = editApply;
            entityEditController = entityEdit;
            selectionState = selections;
            infoProvider = cellInfoProvider;
        }

        public void Initialize(WorldManager manager)
        {
            if (manager == null)
            {
                return;
            }

            worldManager = manager;
            streamingProgressView?.SetWorldManager(manager);
            entityEditController?.Configure(manager.EntityManager, manager.EditController);
            editInputController?.Configure(manager, editToolState, selectionState, editConfirmationView);
            editApplyController?.Configure(manager.EditController, selectionState, editToolState,
                editInputController, entityEditController, manager.EntityManager);
            mainSceneView = GetComponent<MainSceneUIView>();
            if (mainSceneView == null)
            {
                Debug.LogError(
                    $"{nameof(WorldUIManager)} requires a serialized {nameof(MainSceneUIView)}.",
                    this);
                return;
            }
            mainSceneView.Configure(
                manager,
                editToolState,
                editApplyController,
                roadVisualCatalog,
                selectionState,
                infoProvider);
        }

    }
}

