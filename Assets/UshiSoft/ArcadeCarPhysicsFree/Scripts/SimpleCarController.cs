using UnityEngine;
using UshiSoft.Common;

namespace UshiSoft.UACPF
{
    public class SimpleCarController : CarControllerBase
    {
        [SerializeField, Min(0f)] private float _maxForwardSpeedKPH = 180f;
        [SerializeField, Min(0f)] private float _maxBackwardSpeedKPH = 60f;
        [SerializeField, Min(0f)] private float _maxMotorTorque = 300f;
        [SerializeField, Min(0f)] private float _minMotorFrictionTorque = 15f;
        [SerializeField, Min(0f)] private float _maxMotorFrictionTorque = 75f;
        [SerializeField, Min(0.001f)] private float _motorInertia = 0.1f;
        [SerializeField, Min(0f)] private float _finalGearRatio = 8f;

        private float _maxMotorForwardRPM;
        private float _maxMotorBackwardRPM;

        private float _motorRPM;

        private bool _reverse;

        public override bool Reverse
        {
            get => _reverse;
            set => _reverse = value;
        }

        public override float MotorRevolutionRate => _motorRPM / Mathf.Max(_maxMotorForwardRPM, _maxMotorBackwardRPM);

        public float MotorRPM => _motorRPM;

        private bool IsExceedMaxMotorRPM
        {
            get
            {
                var maxRPM = _reverse ? _maxMotorBackwardRPM : _maxMotorForwardRPM;
                var rpm = Mathf.Abs(_motorRPM);
                return rpm > maxRPM;
            }
        }

        public override float MaxSpeedKPH => Mathf.Max(_maxForwardSpeedKPH, _maxBackwardSpeedKPH);

        protected override void Awake()
        {
            base.Awake();

            _maxMotorForwardRPM = CalcMotorRPMFromSpeedKPH(_maxForwardSpeedKPH);
            _maxMotorBackwardRPM = CalcMotorRPMFromSpeedKPH(_maxBackwardSpeedKPH);
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            AddDriveTorque();
        }

        private void AddDriveTorque()
        {
            var throttleInput = ThrottleInput;
            if (IsExceedMaxMotorRPM)
            {
                throttleInput = 0f;
            }

            if (IsGrounded())
            {
                _motorRPM = CalcMotorRPMFromSpeedKPH(SpeedKPH);
                var motorTorque = GetMotorTorque() * throttleInput;
                var motorFriTorque = GetMotorFrictionTorque() * (1f - throttleInput);

                var driveTorque = motorTorque * _finalGearRatio;
                var friTorque = motorFriTorque * _finalGearRatio;

                AddDriveTorque(driveTorque);
                AddBrakeTorque(friTorque);
            }
            else
            {
                var motorTorque = GetMotorFrictionTorque() * throttleInput;
                var motorFiTorque = GetMotorFrictionTorque() * (1f - throttleInput);

                var totalBrakeTorque = MaxBrakeTorque * BrakeInput * Wheels.Length;

                var driveTorque = motorTorque * _finalGearRatio;
                var drivetrainI = _finalGearRatio * _finalGearRatio * _motorInertia;

                var friTorque = motorFiTorque * _finalGearRatio;

                var brakeTorque = totalBrakeTorque * _finalGearRatio;

                _motorRPM += (driveTorque / drivetrainI) * Time.fixedDeltaTime * UshiMath.RPSToRPM;
                DecelerateMotor(friTorque, drivetrainI);
                DecelerateMotor(brakeTorque, drivetrainI);
            }
        }

        public float CalcMotorRPMFromSpeedKPH(float speedKPH)
        {
            return UshiMath.SpeedKPHToEngineRPM(speedKPH, 1f, _finalGearRatio, _wheelRadius);
        }

        private float GetMotorTorque()
        {
            if (IsExceedMaxMotorRPM)
            {
                return 0f;
            }

            var revRate = Mathf.Clamp01(MotorRevolutionRate);

            var coef = 1f;
            if (revRate >= 0.5f)
            {
                coef = (1f - revRate) * 2f;
                coef *= coef;
            }

            var sign = _reverse ? -1f : 1f;

            return sign * _maxMotorTorque * coef;
        }

        private float GetMotorFrictionTorque()
        {
            var motorRevRate = MotorRevolutionRate;
            return Mathf.Lerp(_minMotorFrictionTorque, _maxMotorFrictionTorque, motorRevRate * motorRevRate);
        }

        private void DecelerateMotor(float torque, float inertia)
        {
            var acc = -Mathf.Sign(_motorRPM) * (torque / inertia) * Time.fixedDeltaTime * UshiMath.RPSToRPM;
            if (Mathf.Abs(acc) > _motorRPM)
            {
                _motorRPM = 0f;
            }
            else
            {
                _motorRPM += acc;
            }
        }
    }
}