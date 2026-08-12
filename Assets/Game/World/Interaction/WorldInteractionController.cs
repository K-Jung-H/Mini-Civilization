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

        private WorldData observedWorld;

        private void Update()
        {
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

            if (observedWorld != worldManager.CurrentWorldData)
            {
                observedWorld = worldManager.CurrentWorldData;
                selectionState.Clear();
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
            var effectiveMaxDistance = Mathf.Max(
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
            interactionCamera = camera;
            worldManager = manager;
            selectionState = state;
            editToolState = toolState;
            maxDistance = Mathf.Max(1f, rayDistance);
        }

        private void OnDisable()
        {
            observedWorld = null;
            selectionState?.Clear();
        }
    }
}
