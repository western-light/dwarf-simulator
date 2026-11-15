using DwarfSimulator.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Car.Controller
{
    [DisallowMultipleComponent]
    public class PlayerCarControl : DriverBase
    {
        [System.Flags]
        public enum ControlMode
        {
            None = 0,
            Steering = 1,
            Speed = 2,
            All = Steering | Speed
        }

        [SerializeField, Min(0.001f)] private float _steerTime = 0.1f;
        [SerializeField, Min(0.001f)] private float _steerReleaseTime = 0.1f;

        [SerializeField, Min(0.001f)] private float _throttleTime = 0.1f;
        [SerializeField, Min(0.001f)] private float _throttleReleaseTime = 0.1f;

        [SerializeField, Min(0.001f)] private float _brakeTime = 0.1f;
        [SerializeField, Min(0.001f)] private float _brakeReleaseTime = 0.1f;

        [SerializeField] private bool _steerLimitByFriction = false;
        [SerializeField, Min(0f)] private float _steerMu = 2f;

        [SerializeField] private bool _autoShiftToReverse = true;
        [SerializeField, Min(0f)] private float _switchToReverseSpeedKPH = 1f;

        [SerializeField] private VirtualPadButton _leftSteerButton;
        [SerializeField] private VirtualPadButton _rightSteerButton;
        [SerializeField] private VirtualPadButton _throttleButton;
        [SerializeField] private VirtualPadButton _brakeButton;

        [SerializeField] private bool _enableVirtualPad = true;
        private ControlMode _controlMode = ControlMode.All;

        // Inputs fed by the network (server side). The local keyboard / virtual pad is only read through
        // ReadSteerInput / ReadThrottleInput / ReadBrakeInput by the client that occupies a seat.
        private float _remoteSteer;
        private float _remoteThrottle;
        private float _remoteBrake;
        private bool _useRemoteInput;

        public bool SteerLimitByFriction
        {
            get => _steerLimitByFriction;
            set => _steerLimitByFriction = value;
        }

        public bool AutoSwitchToReverse
        {
            get => _autoShiftToReverse;
            set => _autoShiftToReverse = value;
        }

        public bool EnableVirtualPad
        {
            get => _enableVirtualPad;
            set => _enableVirtualPad = value;
        }

        /// <summary>
        /// When enabled, driving inputs come from <see cref="SetRemoteInput"/> instead of the local devices.
        /// </summary>
        public bool UseRemoteInput
        {
            get => _useRemoteInput;
            set => _useRemoteInput = value;
        }

        public void SetControlMode(ControlMode controlMode)
        {
            _controlMode = controlMode;

            if ((_controlMode & ControlMode.Steering) == 0)
            {
                _remoteSteer = 0f;
                _carController.SteerInput = 0f;
            }

            if ((_controlMode & ControlMode.Speed) == 0)
            {
                _remoteThrottle = 0f;
                _remoteBrake = 0f;
                _carController.ThrottleInput = 0f;
                _carController.BrakeInput = 1f;
                _carController.Reverse = false;
            }
        }

        public void SetRemoteSteer(float steer)
        {
            _remoteSteer = Mathf.Clamp(steer, -1f, 1f);
        }

        public void SetRemotePedals(float throttle, float brake)
        {
            _remoteThrottle = Mathf.Clamp01(throttle);
            _remoteBrake = Mathf.Clamp01(brake);
        }

        public float ReadSteerInput() => GetLocalSteerInput();
        public float ReadThrottleInput() => GetLocalThrottleInput();
        public float ReadBrakeInput() => GetLocalBrakeInput();

        protected override void Drive()
        {
            if ((_controlMode & ControlMode.Steering) != 0)
                UpdateSteerInput();
            if ((_controlMode & ControlMode.Speed) != 0)
                UpdateThrottleAndBrakeInput();
        }

        protected override void Stop()
        {
            if (_controlMode == ControlMode.None)
                return;

            if (_controlMode == ControlMode.Steering)
            {
                _carController.SteerInput = 0f;
                return;
            }

            _carController.BrakeInput = 1f;
            var throttleInput = GetRawThrottleInput();

            var throttleTime = throttleInput != 0f ? _throttleTime : _throttleReleaseTime;
            _carController.ThrottleInput = Mathf.MoveTowards(_carController.ThrottleInput, throttleInput, Time.deltaTime / throttleTime);
        }

        private float GetRawSteerInput() => _useRemoteInput ? _remoteSteer : GetLocalSteerInput();
        private float GetRawThrottleInput() => _useRemoteInput ? _remoteThrottle : GetLocalThrottleInput();
        private float GetRawBrakeInput() => _useRemoteInput ? _remoteBrake : GetLocalBrakeInput();

        private float GetLocalSteerInput()
        {
            if (_enableVirtualPad && _leftSteerButton != null && _rightSteerButton != null)
            {
                if (_leftSteerButton.Pressed)
                {
                    return -1f;
                }
                if (_rightSteerButton.Pressed)
                {
                    return 1f;
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return 0f;

            if (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed)
            {
                return -1f;
            }
            if (keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed)
            {
                return 1f;
            }

            return 0f;
        }

        private float GetLocalThrottleInput()
        {
            if (_enableVirtualPad && _throttleButton != null)
            {
                if (_throttleButton.Pressed)
                {
                    return 1f;
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed || keyboard.cKey.isPressed))
            {
                return 1f;
            }

            return 0f;
        }

        private float GetLocalBrakeInput()
        {
            if (_enableVirtualPad && _brakeButton != null)
            {
                if (_brakeButton.Pressed)
                {
                    return 1f;
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed || keyboard.xKey.isPressed))
            {
                return 1f;
            }

            return 0f;
        }

        private void UpdateSteerInput()
        {
            var maxSteerInput = 1f;
            if (_steerLimitByFriction)
            {
                var speed = _carController.Speed;
                var minTurnR = (speed * speed) / (_steerMu * UnityEngine.Physics.gravity.magnitude);
                if (minTurnR > 0f)
                {
                    var optimalSteerAngle = Mathf.Asin(_carController.Wheelbase / minTurnR) * Mathf.Rad2Deg;
                    maxSteerInput = Mathf.Min(optimalSteerAngle / _carController.MaxSteerAngle, 1f);
                }
            }

            var steerInput = GetRawSteerInput();

            steerInput = Mathf.Clamp(steerInput, -maxSteerInput, maxSteerInput);

            var steerTime = steerInput != 0f ? _steerTime : _steerReleaseTime;

            if (steerInput != 0f && Mathf.Sign(steerInput) != Mathf.Sign(_carController.SteerInput))
            {
                _carController.SteerInput = 0f;
            }

            _carController.SteerInput = Mathf.MoveTowards(_carController.SteerInput, steerInput, Time.deltaTime / steerTime);
        }

        private void UpdateThrottleAndBrakeInput()
        {
            var throttleInput = GetRawThrottleInput();

            var brakeInput = GetRawBrakeInput();

            if (_autoShiftToReverse)
            {
                if (_carController.IsGrounded())
                {
                    var speedKPH = _carController.ForwardSpeed * Math.MPSToKPH;
                    if (_carController.Reverse)
                    {
                        if (throttleInput > 0f && speedKPH > -_switchToReverseSpeedKPH)
                        {
                            _carController.Reverse = false;
                        }
                    }
                    else
                    {
                        if (brakeInput > 0f && speedKPH < _switchToReverseSpeedKPH)
                        {
                            _carController.Reverse = true;
                        }
                    }
                }

                if (_carController.Reverse)
                {
                    (throttleInput, brakeInput) = (brakeInput, throttleInput);
                }
            }

            var throttleTime = throttleInput != 0f ? _throttleTime : _throttleReleaseTime;
            _carController.ThrottleInput = Mathf.MoveTowards(_carController.ThrottleInput, throttleInput, Time.deltaTime / throttleTime);

            var brakeTime = brakeInput != 0f ? _brakeTime : _brakeReleaseTime;
            _carController.BrakeInput = Mathf.MoveTowards(_carController.BrakeInput, brakeInput, Time.deltaTime / brakeTime);
        }
    }
}