using DwarfSimulator.Car.Controller;
using PurrNet;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Car
{
    [RequireComponent(typeof(CarControllerBase))]
    public class CarPlace : NetworkBehaviour
    {
        [SerializeField] private Transform top;
        [SerializeField] private Transform bottom;
        [SerializeField] private float exitDistance = 2.2f;

        private SyncLazyRef<Player.Player> _topSeat = new();
        private SyncLazyRef<Player.Player> _bottomSeat = new();
        private SyncVar<float> _steer = new();

        private CarControllerBase _car;
        private PlayerCarControl _driver;
        private InputAction _leaveAction;
        private (float steer, float throttle, float brake) _lastInput;

        private void Awake()
        {
            _car = GetComponent<CarControllerBase>();
            if (!TryGetComponent(out _driver))
                _driver = gameObject.AddComponent<PlayerCarControl>();
            _driver.UseRemoteInput = true;
        }

        protected override void OnSpawned()
        {
            _car.Rigidbody.isKinematic = !isServer;
            _car.enabled = isServer;
            _driver.enabled = isServer;
            _leaveAction = InputSystem.actions.FindAction("Jump");

            _topSeat.onChanged += (oldPlayer, newPlayer) => OnSeatChanged(oldPlayer, newPlayer, top);
            _bottomSeat.onChanged += (oldPlayer, newPlayer) => OnSeatChanged(oldPlayer, newPlayer, bottom);
            OnSeatChanged(null, _topSeat.value, top);
            OnSeatChanged(null, _bottomSeat.value, bottom);
        }

        private void Update()
        {
            if (!isSpawned)
                return;

            if (isServer)
                _steer.value = _car.SteerInput;
            else
                TurnWheels();

            bool onTop = IsLocal(_topSeat);
            bool onBottom = IsLocal(_bottomSeat);
            if (!onTop && !onBottom)
                return;

            if (_leaveAction.WasPressedThisFrame())
            {
                LeaveSeatRpc();
                return;
            }

            (float steer, float throttle, float brake) input = (
                onTop ? _driver.ReadSteerInput() : 0f,
                onBottom ? _driver.ReadThrottleInput() : 0f,
                onBottom ? _driver.ReadBrakeInput() : 0f);

            if (input == _lastInput)
                return;

            _lastInput = input;
            SendInputRpc(input.steer, input.throttle, input.brake);
        }

        public void PlaceTop(Player.Player player)
        {
            if (player.isOwner)
                RequestSeatRpc(true);
        }

        public void PlaceBottom(Player.Player player)
        {
            if (player.isOwner)
                RequestSeatRpc(false);
        }

        private void OnSeatChanged(Player.Player oldPlayer, Player.Player newPlayer, Transform seat)
        {
            if (oldPlayer != null && oldPlayer.Seat == seat)
            {
                oldPlayer.GetOut(transform.position - transform.right * exitDistance + Vector3.up * 0.5f,
                    Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
                if (oldPlayer.isOwner && oldPlayer.TryGetComponent(out Player.CameraSync oldCamera))
                    oldCamera.UnlockView();
            }

            if (newPlayer != null)
            {
                newPlayer.SitIn(seat);
                if (newPlayer.isOwner && newPlayer.TryGetComponent(out Player.CameraSync newCamera))
                {
                    if (seat == bottom)
                        newCamera.LockFirstPersonStabilized(seat);
                    else
                        newCamera.LockThirdPerson();
                }
            }

            _lastInput = default;
            if (isServer)
                RefreshControlMode();
        }

        private void TurnWheels()
        {
            float angle = _car.MaxSteerAngle * _steer.value;
            foreach (var wheel in _car.SteerableWheels)
                wheel.SteerAngle = angle;
        }

        private static bool IsLocal(SyncLazyRef<Player.Player> seat) => seat.value != null && seat.value.isOwner;

        private static bool IsSeatedBy(SyncLazyRef<Player.Player> seat, PlayerID player) =>
            seat.value != null && seat.value.owner == player;

        [ServerRpc(requireOwnership: false)]
        private void RequestSeatRpc(bool onTop, RPCInfo info = default)
        {
            var seat = onTop ? _topSeat : _bottomSeat;
            var otherSeat = onTop ? _bottomSeat : _topSeat;
            if (seat.value != null || !Player.Player.TryGetPlayer(info.sender, out var player))
                return;

            seat.value = player;
            if (otherSeat.value == player)
                otherSeat.value = null;
        }

        [ServerRpc(requireOwnership: false)]
        private void LeaveSeatRpc(RPCInfo info = default)
        {
            if (IsSeatedBy(_topSeat, info.sender))
                _topSeat.value = null;
            if (IsSeatedBy(_bottomSeat, info.sender))
                _bottomSeat.value = null;
        }

        [ServerRpc(requireOwnership: false)]
        private void SendInputRpc(float steer, float throttle, float brake, RPCInfo info = default)
        {
            if (IsSeatedBy(_topSeat, info.sender))
                _driver.SetRemoteSteer(steer);
            if (IsSeatedBy(_bottomSeat, info.sender))
                _driver.SetRemotePedals(throttle, brake);
        }

        private void RefreshControlMode()
        {
            var mode = PlayerCarControl.ControlMode.None;
            if (_topSeat.value != null)
                mode |= PlayerCarControl.ControlMode.Steering;
            if (_bottomSeat.value != null)
                mode |= PlayerCarControl.ControlMode.Speed;
            _driver.SetControlMode(mode);
        }
    }
}
