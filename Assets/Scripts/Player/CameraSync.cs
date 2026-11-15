using PurrNet;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player
{
    public class CameraSync: NetworkIdentity
    {
        [SerializeField] private Transform cameraPos;

        private CinemachineCamera[] _playerCameras;
        private InputAction _toggleViewAction;
        
        private void Update()
        {
            if (!isOwner)
                return;

            if (_playerCameras == null)
                Follow();

            if (!_toggleViewAction.WasPressedThisFrame())
                return;

            foreach (CinemachineCamera playerCamera in _playerCameras)
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
            if (_playerCameras == null)
                return;

            _playerCameras = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Follow()
        {
            _playerCameras = FindObjectsByType<CinemachineCamera>();
            foreach (CinemachineCamera playerCamera in _playerCameras)
                playerCamera.Target.TrackingTarget = cameraPos;

            FindAnyObjectByType<CinemachinePanTilt>().VirtualCamera.Prioritize();

            _toggleViewAction = InputSystem.actions.FindAction("ToggleView");
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
