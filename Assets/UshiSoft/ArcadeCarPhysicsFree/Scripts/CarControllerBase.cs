using UnityEngine;
using UshiSoft.Common;

namespace UshiSoft.UACPF
{
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public abstract class CarControllerBase : MonoBehaviour
    {
        [SerializeField] Wheel[] _steerableWheels;

        [SerializeField, Min(0f)] protected float _maxSteerAngle = 30f;

        [SerializeField, Min(0f)] protected float _maxTurnSpeed = 60f;

        [SerializeField, Min(0f)] protected float _peakFrictionSlipAngle = 5f;
        [SerializeField, Min(0f)] protected float _mu = 2f;

        [SerializeField] protected bool _useAddTorque = false;

        [SerializeField, Min(0.001f)] protected float _wheelRadius = 0.3f;

        [SerializeField] protected float _centerOfMassHeight = 0.3f;
        
        [SerializeField, Min(0f)] protected float _maxBrakeTorque = 1000f;

        [SerializeField, Min(0f)] protected float _rollingResistanceCoef = 0.015f;
        [SerializeField, Min(0f)] protected float _airResistanceCoef = 1.5f;
        [SerializeField, Min(0f)] protected float _downforceCoef = 0f;

        [SerializeField, Range(0f, 1f)] protected float _airResistanceReduction = 0f;

        [SerializeField] protected bool _autoAdjustSuspension = true;
        [SerializeField, Min(0.001f)] protected float _suspensionStroke = 0.1f;
        [SerializeField, Min(0f)] protected float _suspensionNaturalFrequency = 2f;
        [SerializeField, Range(0f, 1f)] protected float _suspensionDampingRatio = 0.35f;

        [SerializeField] protected float _addForceOffset = -0.1f;

        protected Rigidbody _rigidbody;
        protected Collider _collider;
        protected Wheel[] _wheels;

        protected float _wheelbase;

        protected float _steerInput;
        protected float _throttleInput;
        protected float _brakeInput;

        protected float _angularVelocity;

        protected Vector3 _groundNormal;
        protected Vector3 _groundForward;
        protected Vector3 _groundSideways;

        protected float _forwardSpeed;
        protected float _sidewaysSpeed;

        protected float _speed;

        protected float _normalForce;

        protected Vector3 _addForcePosition;

        protected float _slipAngle;
        protected float _tiltAngle;

        protected Vector3 _totalForce;

        public abstract float MaxSpeedKPH { get; }

        public float Weight
        {
            get
            {
                if (_rigidbody == null)
                {
                    _rigidbody = GetComponent<Rigidbody>();
                }
                return _rigidbody.mass;
            }
        }

        public Wheel[] SteerableWheels
        {
            get => _steerableWheels;
            set => _steerableWheels = value;
        }

        public float Wheelbase => _wheelbase;

        public float SlipAngle => _slipAngle;

        public float SteerInput
        {
            get => _steerInput;
            set => _steerInput = Mathf.Clamp(value, -1f, 1f);
        }

        public float ThrottleInput
        {
            get => _throttleInput;
            set => _throttleInput = Mathf.Clamp(value, 0f, 1f);
        }

        public float BrakeInput
        {
            get => _brakeInput;
            set => _brakeInput = Mathf.Clamp(value, 0f, 1f);
        }

        public abstract bool Reverse { get; set; }

        public float ForwardSpeed => Vector3.Dot(_rigidbody.linearVelocity, transform.forward);
        public float ForwardSpeedKPH => ForwardSpeed * UshiMath.MPSToKPH;

        public float Speed => _speed;
        public float SpeedKPH => Speed * UshiMath.MPSToKPH;

        public Vector3 Velocity => _rigidbody.linearVelocity;

        public float MaxSteerAngle => _maxSteerAngle;

        public float MaxTurnSpeed
        {
            get => _maxTurnSpeed;
            set => _maxTurnSpeed = Mathf.Max(value, 0f);
        }


        public float PeakFrictionSlipAngle
        {
            get => _peakFrictionSlipAngle;
            set => _peakFrictionSlipAngle = Mathf.Max(value, 0f);
        }

        public float Mu
        {
            get => _mu;
            set => _mu = Mathf.Max(value, 0f);
        }

        public float WheelRadius
        {
            get => _wheelRadius;
            set => _wheelRadius = Mathf.Max(value, 0.001f);
        }

        public float RollingResistanceCoef
        {
            get => _rollingResistanceCoef;
            set => _rollingResistanceCoef = Mathf.Max(value, 0f);
        }

        public float AirResistanceReduction
        {
            get => _airResistanceReduction;
            set => _airResistanceReduction = Mathf.Clamp01(value);
        }

        public float MaxBrakeTorque => _maxBrakeTorque;

        public Rigidbody Rigidbody => _rigidbody;

        public Collider Collider => _collider;

        public Wheel[] Wheels => _wheels;

        public abstract float MotorRevolutionRate { get; }

        protected virtual void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _collider = GetComponentInChildren<Collider>();
            _wheels = GetComponentsInChildren<Wheel>();

            CalcWheelbase();

            AdjustCenterOfMass();

            if (_autoAdjustSuspension)
            {
                AdjustSuspension();
            }
        }

        protected virtual void FixedUpdate()
        {
            _groundNormal = Vector3.zero;
            _groundForward = Vector3.zero;
            _groundSideways = Vector3.zero;
            _forwardSpeed = 0f;
            _sidewaysSpeed = 0f;
            _slipAngle = 0f;
            _normalForce = 0f;
            _addForcePosition = Vector3.zero;
            _totalForce = Vector3.zero;

            _speed = _rigidbody.linearVelocity.magnitude;

            UpdateSteerAngle();

            AddAirResistanceForce();
            AddDownforce();

            if (!IsGrounded())
            {
                return;
            }

            _groundNormal = GetGroundNormal();
            _groundForward = Vector3.ProjectOnPlane(transform.forward, _groundNormal).normalized;
            _groundSideways = Vector3.ProjectOnPlane(transform.right, _groundNormal).normalized;

            _forwardSpeed = Vector3.Dot(_rigidbody.linearVelocity, _groundForward);
            _sidewaysSpeed = Vector3.Dot(_rigidbody.linearVelocity, _groundSideways);

            var denom = Mathf.Max(Mathf.Abs(_forwardSpeed), 1f);
            _slipAngle = Mathf.Atan2(_sidewaysSpeed, denom) * Mathf.Rad2Deg;
            _tiltAngle = Vector3.Angle(_groundNormal, transform.up);

            _normalForce = _rigidbody.mass * Physics.gravity.magnitude;

            _addForcePosition = _rigidbody.worldCenterOfMass + transform.up * _addForceOffset;

            Turn();
            AddFrictionForce();

            AddRollingResistanceForce();
            AddBrakeForce();
        }

        private void OnDrawGizmosSelected()
        {
            if (_rigidbody != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawRay(transform.TransformPoint(_rigidbody.centerOfMass), _totalForce * 0.0001f);
            }
        }

        private void CalcWheelbase()
        {
            var minLocalZ = float.MaxValue;
            var maxLocalZ = float.MinValue;
            foreach (var wheel in _wheels)
            {
                minLocalZ = Mathf.Min(wheel.transform.localPosition.z, minLocalZ);
                maxLocalZ = Mathf.Max(wheel.transform.localPosition.z, maxLocalZ);
            }
            _wheelbase = Mathf.Abs(minLocalZ - maxLocalZ);
        }

        private void AdjustCenterOfMass()
        {
            if (_wheels.Length == 0)
            {
                return;
            }

            var com = Vector3.zero;
            foreach (var wheel in _wheels)
            {
                com += wheel.transform.localPosition;
            }
            com /= (float)_wheels.Length;

            com.y = _centerOfMassHeight;

            _rigidbody.centerOfMass = com;
        }
        
        private void AdjustSuspension()
        {
            var mass = _rigidbody.mass / _wheels.Length;

            var spring = 4f * Mathf.PI * Mathf.PI * _suspensionNaturalFrequency * _suspensionNaturalFrequency * mass;
            var damper = 2f * Mathf.Sqrt(mass * spring) * _suspensionDampingRatio;

            foreach (var wheel in _wheels)
            {
                wheel.SuspensionStroke = _suspensionStroke;
                wheel.SuspensionSpring = spring;
                wheel.SuspensionDamper = damper;
            }
        }
        
        private void UpdateSteerAngle()
        {
            var steerAngle = _maxSteerAngle * _steerInput;
            foreach (var wheel in _steerableWheels)
            {
                wheel.SteerAngle = steerAngle;
            }
        }

        public bool IsGrounded()
        {
            foreach (var wheel in _wheels)
            {
                if (wheel.Grounded)
                {
                    return true;
                }
            }
            return false;
        }

        private Vector3 GetGroundNormal()
        {
            var normal = Vector3.zero;
            var count = 0;
            foreach (var wheel in _wheels)
            {
                if (!wheel.Grounded)
                {
                    continue;
                }
                normal += wheel.HitInfo.normal;
                count++;
            }

            if (count == 0)
            {
                return Vector3.zero;
            }

            normal /= (float)count;
            return normal.normalized;
        }

        private void Turn()
        {
            var steerAngle = _maxSteerAngle * _steerInput;

            var targetAngVel = 0f;
            if (steerAngle != 0f)
            {
                var turnR = _wheelbase / Mathf.Sin(steerAngle * Mathf.Deg2Rad);
                targetAngVel = _forwardSpeed / turnR;

                var minTurnR = (_speed * _speed) / (_mu * Physics.gravity.magnitude);
                var maxAngVel1 = _speed / minTurnR;

                var maxAngVel2 = _maxTurnSpeed * Mathf.Deg2Rad;

                var maxAngVel = Mathf.Max(maxAngVel1, maxAngVel2);

                targetAngVel = Mathf.Clamp(targetAngVel, -maxAngVel, maxAngVel);
            }

            var currAngVel = _useAddTorque ? _rigidbody.angularVelocity.y : _angularVelocity;
            var angVelDiff = targetAngVel - currAngVel;
            var velDiff = (_wheelbase / 2f) * angVelDiff;
            var torque = ((_normalForce / 2f) * velDiff) * 2f;
            var maxFriction = _normalForce * _mu;
            torque = Mathf.Clamp(torque, -maxFriction, maxFriction);

            if (_useAddTorque)
            {
                _rigidbody.AddTorque(transform.up * torque);

                _angularVelocity = _rigidbody.angularVelocity.y;
            }
            else
            {
                _angularVelocity += torque / _rigidbody.inertiaTensor.y * Time.deltaTime;

                var angVel = _rigidbody.angularVelocity;
                angVel.y = _angularVelocity;
                _rigidbody.angularVelocity = angVel;
            }
        }

        private void AddFrictionForce()
        {
            var friForce = (_rigidbody.mass * -_sidewaysSpeed) / Time.fixedDeltaTime;

            var fri = Mathf.InverseLerp(0f, _peakFrictionSlipAngle, Mathf.Abs(_slipAngle)) * _mu;
            var tilt = Mathf.Cos(_tiltAngle * Mathf.Deg2Rad);
            var maxFriForce = _normalForce * fri * tilt;
            friForce = Mathf.Clamp(friForce, -maxFriForce, maxFriForce);

            var friForceVec = _groundSideways * friForce;
            _rigidbody.AddForceAtPosition(friForceVec, _addForcePosition);

            _totalForce += friForceVec;
        }

        protected void AddDriveTorque(float driveTorque)
        {
            var driveForce = driveTorque / _wheelRadius;

            var maxDriveForce = _normalForce * _mu;
            driveForce = Mathf.Clamp(driveForce, -maxDriveForce, maxDriveForce);

            var driveForceVec = _groundForward * driveForce;
            _rigidbody.AddForceAtPosition(driveForceVec, _addForcePosition);

            _totalForce += driveForceVec;
        }

        protected void AddBrakeTorque(float brakeTorque)
        {
            var brakeForce = -Mathf.Sign(_forwardSpeed) * Mathf.Abs(brakeTorque / _wheelRadius);

            var maxBrakeForce1 = (_rigidbody.mass * Mathf.Abs(_forwardSpeed)) / Time.fixedDeltaTime;
            var maxBrakeForce2 = _normalForce * _mu;
            var maxBrakeForce = Mathf.Min(maxBrakeForce1, maxBrakeForce2);
            brakeForce = Mathf.Clamp(brakeForce, -maxBrakeForce, maxBrakeForce);

            var brakeForceVec = _groundForward * brakeForce;
            _rigidbody.AddForceAtPosition(brakeForceVec, _addForcePosition);

            _totalForce += brakeForceVec;
        }

        private void AddRollingResistanceForce()
        {
            var rollResForce = _normalForce * _rollingResistanceCoef * _wheelRadius;
            AddBrakeTorque(rollResForce);
        }

        private void AddBrakeForce()
        {
            var brakeTorque = _maxBrakeTorque * _brakeInput;
            var totalBrakeForce = brakeTorque * _wheels.Length;
            AddBrakeTorque(totalBrakeForce);
        }

        private void AddAirResistanceForce()
        {
            var vel = _rigidbody.linearVelocity;
            var force = -vel.normalized * vel.sqrMagnitude * _airResistanceCoef * (1f - _airResistanceReduction);
            _rigidbody.AddForce(force);

            _totalForce += force;
        }

        private void AddDownforce()
        {
            var forwardVel = Vector3.Dot(_rigidbody.linearVelocity, transform.forward);
            var force = -transform.up * forwardVel * forwardVel * _downforceCoef * (1f - _airResistanceReduction);
            _rigidbody.AddForce(force);

            _totalForce += force;
        }
    }
}