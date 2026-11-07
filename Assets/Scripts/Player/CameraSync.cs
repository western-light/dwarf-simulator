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
        
        
        private void Update()
        {
            if (!isOwner)
                return;

            if (playerCameras == null)
                Follow();

            if (!toggleViewAction.WasPressedThisFrame())
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

        protected override void OnDespawned()
        {
            if (playerCameras == null)
                return;

            playerCameras = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Follow()
        {
            playerCameras = FindObjectsByType<CinemachineCamera>();
            foreach (CinemachineCamera playerCamera in playerCameras)
                playerCamera.Target.TrackingTarget = cameraPos;

            FindAnyObjectByType<CinemachinePanTilt>().VirtualCamera.Prioritize();

            toggleViewAction = InputSystem.actions.FindAction("ToggleView");
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
