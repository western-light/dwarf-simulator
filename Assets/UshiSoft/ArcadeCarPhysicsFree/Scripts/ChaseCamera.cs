using UnityEngine;

namespace UshiSoft.UACPF
{
    public class ChaseCamera : CameraBase
    {
        [SerializeField, Min(0f)] private float _distance = 6f;
        [SerializeField, Min(0f)] private float _height = 2f;
        [SerializeField, Min(0f)] private float _lookAtHeight = 1f;

        [SerializeField] private bool _followVelocity = false;

        [SerializeField, Min(1f)] private float _flipSpeedKPH = 5f;

        [SerializeField, Min(0f)] private float _rotationDamping = 5f;
        [SerializeField, Min(0f)] private float _heightDamping = 5f;
        [SerializeField, Min(0f)] private float _velocityDamping = 5f;
        
        private bool _flip;

        private Vector3 _velocityDirection;

        public bool FollowVelocity
        {
            get => _followVelocity;
            set => _followVelocity = value;
        }

        private void LateUpdate()
        {
            if (_targetCar == null)
            {
                return;
            }

            var carPos = _targetCar.transform.TransformPoint(_targetCar.Rigidbody.centerOfMass);

            var targetAngleY = _targetCar.transform.eulerAngles.y;

            if (_followVelocity)
            {
                var carDir = carPos - transform.position;
                carDir.y = 0f;
                carDir.Normalize();

                var carVelDir = _targetCar.Velocity;
                carVelDir.y = 0f;
                carVelDir.Normalize();

                if (_targetCar.SpeedKPH >= _flipSpeedKPH)
                {
                    _velocityDirection = Vector3.Lerp(_velocityDirection, carVelDir, _velocityDamping * Time.deltaTime);
                }
                else
                {
                    _velocityDirection = carDir;
                }

                targetAngleY = Mathf.Atan2(_velocityDirection.x, _velocityDirection.z) * Mathf.Rad2Deg;
            }
            else
            {
                if (_flip)
                {
                    if (!_targetCar.Reverse && _targetCar.ForwardSpeedKPH >= _flipSpeedKPH)
                    {
                        _flip = false;
                    }
                }
                else
                {
                    if (_targetCar.Reverse && _targetCar.ForwardSpeedKPH <= -_flipSpeedKPH)
                    {
                        _flip = true;
                    }
                }

                if (_flip)
                {
                    targetAngleY += 180f;
                }
            }

            var newAngleY = Mathf.LerpAngle(transform.eulerAngles.y, targetAngleY, _rotationDamping * Time.deltaTime);

            var currY = transform.position.y;
            var targetY = carPos.y + _height;
            var newY = Mathf.Lerp(currY, targetY, _heightDamping * Time.deltaTime);

            var rot = Quaternion.Euler(0f, newAngleY, 0f);
            var camPos = carPos + rot * Vector3.back * _distance;
            camPos.y = newY;
            transform.position = camPos;

            var lookAtPos = carPos + Vector3.up * _lookAtHeight;
            transform.LookAt(lookAtPos);
        }
    }
}