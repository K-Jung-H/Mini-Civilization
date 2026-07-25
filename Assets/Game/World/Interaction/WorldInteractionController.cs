using MiniCivilization.World.Runtime;
using MiniCivilization.World.Domain;
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

        [Header("Raycast")]
        [SerializeField] private LayerMask interactionMask = 1 << 8;
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

            if (observedWorld != worldManager.CurrentWorld.Data)
            {
                observedWorld = worldManager.CurrentWorld.Data;
                selectionState.Clear();
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
            if (WorldTilePicker.TryPick(
                    worldManager.CurrentWorld.Data,
                    ray,
                    maxDistance,
                    interactionMask,
                    out var pick))
            {
                selectionState.SetHovered(pick);
            }
            else
            {
                selectionState.SetHovered(null);
            }

            if (mouse.leftButton.wasPressedThisFrame)
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
            LayerMask mask,
            float rayDistance)
        {
            interactionCamera = camera;
            worldManager = manager;
            selectionState = state;
            interactionMask = mask;
            maxDistance = Mathf.Max(1f, rayDistance);
        }

        private void OnDisable()
        {
            observedWorld = null;
            selectionState?.Clear();
        }
    }
}
