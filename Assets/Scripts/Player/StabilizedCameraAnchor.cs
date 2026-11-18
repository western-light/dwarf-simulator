using UnityEngine;

namespace DwarfSimulator.Player
{
    [DefaultExecutionOrder(-100)]
    public class StabilizedCameraAnchor : MonoBehaviour
    {
        private Transform _target;
        private Vector3 _offset;
        private float _heightSmoothTime;
        private float _maxHeightDeviation;
        private float _yawSmoothTime;
        private float _heightVelocity;
        private float _yawVelocity;
        private float _yaw;

        public float Yaw => _yaw;

        public void Follow(Transform target, Vector3 offset, float heightSmoothTime, float maxHeightDeviation,
            float yawSmoothTime)
        {
            _target = target;
            _offset = offset;
            _heightSmoothTime = heightSmoothTime;
            _maxHeightDeviation = maxHeightDeviation;
            _yawSmoothTime = yawSmoothTime;
            _heightVelocity = 0f;
            _yawVelocity = 0f;
            _yaw = TargetYaw();
            transform.SetPositionAndRotation(TargetPosition(_yaw), Quaternion.Euler(0f, _yaw, 0f));
        }

        public void Stop() => _target = null;

        private void LateUpdate()
        {
            if (_target == null)
                return;

            float targetYaw = TargetYaw();
            Vector3 targetPosition = TargetPosition(targetYaw);

            float height = Mathf.SmoothDamp(transform.position.y, targetPosition.y, ref _heightVelocity,
                _heightSmoothTime);
            height = Mathf.Clamp(height, targetPosition.y - _maxHeightDeviation,
                targetPosition.y + _maxHeightDeviation);

            _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, _yawSmoothTime);

            transform.SetPositionAndRotation(new Vector3(targetPosition.x, height, targetPosition.z),
                Quaternion.Euler(0f, _yaw, 0f));
        }

        private Vector3 TargetPosition(float yaw) => _target.position + Quaternion.Euler(0f, yaw, 0f) * _offset;

        private float TargetYaw()
        {
            Vector3 forward = Vector3.ProjectOnPlane(_target.forward, Vector3.up);
            return forward.sqrMagnitude > 0.0001f ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : _yaw;
        }
    }
}
