using MiniCivilization.World.Domain;
using MiniCivilization.World.Editing;
using MiniCivilization.World.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MiniCivilization.World.Interaction
{
    [DisallowMultipleComponent]
    public sealed class WorldInteractionController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera interactionCamera;
        [SerializeField] private WorldManager worldManager;
        [SerializeField] private WorldTileSelectionState selectionState;
        [SerializeField] private WorldEditToolState editToolState;

        [Header("Raycast")]
        [SerializeField, Min(1f)] private float maxDistance = 1000f;

        private WorldRuntime observedRuntime;

        private void OnEnable()
        {
            if (worldManager != null) worldManager.WorldChanged += SynchronizeSelection;
            SynchronizeSelection();
        }

        private void SynchronizeSelection()
        {
            if (selectionState == null) return;
            var runtime = worldManager != null ? worldManager.CurrentWorldRuntime : null;
            if (!ReferenceEquals(observedRuntime, runtime))
            {
                observedRuntime = runtime;
                selectionState.SetHovered(null);
                selectionState.SetSelected(null);
            }
            if (selectionState.Selected.HasValue)
            {
                var cell = selectionState.Selected.Value.Cell;
                var world = runtime?.Data;
                if (world == null || !world.Contains(cell.X, cell.Y, cell.Z)
                    || !world.IsChunkLoaded(cell.X, cell.Z))
                    selectionState.SetSelected(null);
            }
        }

        private void Update()
        {
            SynchronizeSelection();
            var mouse = Mouse.current;
            if (mouse == null
                || interactionCamera == null
                || worldManager == null
                || selectionState == null
                || !worldManager.HasWorld)
            {
                selectionState?.SetHovered(null);
                return;
            }

            var blocksCellSelection = editToolState != null
                && editToolState.BlocksCellSelection;
            if (blocksCellSelection && selectionState.Selected.HasValue)
            {
                selectionState.SetSelected(null);
            }

            if (EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject())
            {
                selectionState.SetHovered(null);
                if (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
                {
                    selectionState.SetSelected(null);
                }

                return;
            }

            var ray = interactionCamera.ScreenPointToRay(
                mouse.position.ReadValue());
            var world = worldManager.CurrentWorldData;
            var effectiveMaxDistance = world.IsInfinite
                ? maxDistance
                : Mathf.Max(
                    maxDistance,
                    world.Size * world.CellSize * 4f);
            if (WorldDdaTilePicker.TryPick(
                    world,
                    worldManager.Renderer,
                    ray,
                    effectiveMaxDistance,
                    out var pick))
            {
                selectionState.SetHovered(pick);
            }
            else
            {
                selectionState.SetHovered(null);
            }

            if (mouse.leftButton.wasPressedThisFrame
                && !blocksCellSelection)
            {
                selectionState.SelectHovered();
            }

            if (mouse.rightButton.wasPressedThisFrame
                || (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false))
            {
                selectionState.SetSelected(null);
            }
        }

        public void Configure(
            Camera camera,
            WorldManager manager,
            WorldTileSelectionState state,
            WorldEditToolState toolState,
            float rayDistance)
        {
            if (worldManager != null) worldManager.WorldChanged -= SynchronizeSelection;
            interactionCamera = camera;
            worldManager = manager;
            selectionState = state;
            editToolState = toolState;
            maxDistance = Mathf.Max(1f, rayDistance);
            if (isActiveAndEnabled)
            {
                if (worldManager != null) worldManager.WorldChanged += SynchronizeSelection;
                SynchronizeSelection();
            }
        }

        private void OnDisable()
        {
            if (worldManager != null) worldManager.WorldChanged -= SynchronizeSelection;
            observedRuntime = null;
            selectionState?.SetHovered(null);
            selectionState?.SetSelected(null);
        }
    }
}
