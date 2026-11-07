using PurrNet;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player
{
    public class CameraSync: NetworkIdentity
    {
        [SerializeField] private Transform cameraPos;

        private CinemachineCamera[] playerCameras;
        private InputAction toggleViewAction;

        // playerCameras is only set for the local player, so remote players never react to the key.
        private void Update()
        {
            if (playerCameras == null || !toggleViewAction.WasPressedThisFrame())
                return;

            foreach (CinemachineCamera playerCamera in playerCameras)
            {
                if (!playerCamera.IsLive)
                {
                    playerCamera.Prioritize();
                    break;
                }
            }
        }

        protected override void OnSpawned()
        {
            FollowIfOwner();
        }

        // PlayerSpawner gives ownership after the spawn, so isOwner can still be false in OnSpawned.
        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            FollowIfOwner();
        }

        protected override void OnDespawned()
        {
            if (playerCameras == null)
                return;

            playerCameras = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void FollowIfOwner()
        {
            if (!isOwner)
                return;

            // Both views stay enabled and read the mouse, so they keep looking the same way.
            playerCameras = FindObjectsByType<CinemachineCamera>();
            foreach (CinemachineCamera playerCamera in playerCameras)
                playerCamera.Target.TrackingTarget = cameraPos;

            // Start in first person.
            FindAnyObjectByType<CinemachinePanTilt>().VirtualCamera.Prioritize();

            toggleViewAction = InputSystem.actions.FindAction("ToggleView");
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
