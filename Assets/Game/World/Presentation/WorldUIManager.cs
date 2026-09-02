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
        [SerializeField] private WorldEditToolbarView toolbarView;
        [SerializeField] private WorldEntityCatalogView entityCatalogView;
        [SerializeField] private WorldRoadCatalogView roadCatalogView;
        [SerializeField] private RoadVisualCatalog roadVisualCatalog;
        [SerializeField] private WorldEditConfirmationView editConfirmationView;
        [SerializeField] private WorldTileInfoPresenter tileInfoPresenter;
        [SerializeField] private WorldTileInfoPanel tileInfoPanel;
        [SerializeField] private WorldStreamingProgressView streamingProgressView;

        private WorldManager worldManager;

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
            if (entityCatalogView != null)
            {
                entityCatalogView.Initialize(
                    manager.EntityManager?.Catalog,
                    toolbarView);
                entityEditController?.Configure(
                    manager.EntityManager,
                    manager.EditController);
            }

            if (toolbarView != null)
            {
                roadCatalogView?.Initialize(roadVisualCatalog);
                editToolState?.Configure(
                    toolbarView,
                    entityCatalogView,
                    roadCatalogView);
                editInputController?.Configure(
                    manager,
                    editToolState,
                    selectionState,
                    editConfirmationView,
                    toolbarView);
                editApplyController?.Configure(
                    manager.EditController,
                    selectionState,
                    toolbarView,
                    editToolState,
                    editInputController,
                    entityEditController,
                    manager.EntityManager);
            }

            if (tileInfoPresenter != null && tileInfoPanel != null)
            {
                tileInfoPresenter.Configure(
                    manager,
                    selectionState,
                    infoProvider,
                    tileInfoPanel);
            }
        }

    }
}
