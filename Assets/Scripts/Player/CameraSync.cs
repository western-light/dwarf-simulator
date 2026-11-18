using PurrNet;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player
{
    public class CameraSync: NetworkIdentity
    {
        [SerializeField] private Transform cameraPos;
        [SerializeField, Min(0f)] private float stabilizedHeightSmoothTime = 0.15f;
        [SerializeField, Min(0f)] private float stabilizedMaxHeightDeviation = 0.3f;
        [SerializeField, Min(0f)] private float stabilizedYawSmoothTime = 0.1f;

        private CinemachineCamera _fpsCamera;
        private CinemachineCamera _tpsCamera;
        private CinemachinePanTilt _fpsPanTilt;
        private CinemachineCamera _activeCamera;
        private InputAction _toggleViewAction;
        private bool _initialized;

        private bool _viewLocked;
        private CinemachineCamera _cameraBeforeLock;
        private StabilizedCameraAnchor _stabilizedAnchor;
        private bool _stabilized;
        private CinemachinePanTilt.ReferenceFrames _panTiltFrameBeforeLock;

        private void Update()
        {
            if (!isOwner)
                return;

            if (!_initialized)
                Follow();

            if (_viewLocked || !_toggleViewAction.WasPressedThisFrame())
                return;

            Activate(_activeCamera == _fpsCamera ? _tpsCamera : _fpsCamera);
        }

        protected override void OnDespawned()
        {
            if (_stabilizedAnchor != null)
                Destroy(_stabilizedAnchor.gameObject);

            if (!_initialized)
                return;

            _initialized = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void LockFirstPersonStabilized(Transform seat)
        {
            if (!BeginLock())
                return;

            if (_stabilizedAnchor == null)
                _stabilizedAnchor = new GameObject("StabilizedCameraAnchor").AddComponent<StabilizedCameraAnchor>();

            Vector3 offset = Quaternion.Inverse(seat.rotation) * (cameraPos.position - seat.position);
            _stabilizedAnchor.Follow(seat, offset, stabilizedHeightSmoothTime, stabilizedMaxHeightDeviation,
                stabilizedYawSmoothTime);

            _stabilized = true;
            _panTiltFrameBeforeLock = _fpsPanTilt.ReferenceFrame;
            _fpsPanTilt.ReferenceFrame = CinemachinePanTilt.ReferenceFrames.TrackingTarget;
            _fpsPanTilt.PanAxis.Value = 0f;
            _fpsPanTilt.TiltAxis.Value = 0f;
            _fpsCamera.Target.TrackingTarget = _stabilizedAnchor.transform;
            Activate(_fpsCamera);
        }

        public void LockThirdPerson()
        {
            if (!BeginLock())
                return;

            Activate(_tpsCamera);
        }

        public void UnlockView()
        {
            if (!_viewLocked)
                return;

            ReleaseStabilization();
            _viewLocked = false;
            Activate(_cameraBeforeLock);
        }

        private bool BeginLock()
        {
            if (!isOwner)
                return false;

            if (!_initialized)
                Follow();

            if (_viewLocked)
                ReleaseStabilization();
            else
                _cameraBeforeLock = _activeCamera;

            _viewLocked = true;
            return true;
        }

        private void ReleaseStabilization()
        {
            if (!_stabilized)
                return;

            _stabilized = false;
            float worldPan = _stabilizedAnchor.Yaw + _fpsPanTilt.PanAxis.Value;
            _stabilizedAnchor.Stop();
            _fpsPanTilt.ReferenceFrame = _panTiltFrameBeforeLock;
            _fpsPanTilt.PanAxis.Value = Mathf.DeltaAngle(0f, worldPan);
            _fpsCamera.Target.TrackingTarget = cameraPos;
        }

        private void Activate(CinemachineCamera playerCamera)
        {
            _activeCamera = playerCamera;
            playerCamera.Prioritize();
        }

        private void Follow()
        {
            _fpsPanTilt = FindAnyObjectByType<CinemachinePanTilt>();
            _fpsCamera = _fpsPanTilt.VirtualCamera as CinemachineCamera;
            _tpsCamera = FindAnyObjectByType<CinemachineOrbitalFollow>().VirtualCamera as CinemachineCamera;

            _fpsCamera.Target.TrackingTarget = cameraPos;
            _tpsCamera.Target.TrackingTarget = cameraPos;
            Activate(_fpsCamera);

            _toggleViewAction = InputSystem.actions.FindAction("ToggleView");
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _initialized = true;
        }
    }
}
