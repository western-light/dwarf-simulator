using DwarfSimulator.Car.Controller;
using DwarfSimulator.Player;
using PurrNet;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Car
{
    public class CarPlace : NetworkBehaviour
    {
        [SerializeField] private Transform top;
        [SerializeField] private Transform bottom;
        [SerializeField, Min(0f)] private float exitSideOffset = 1.2f;
        [SerializeField, Min(0.001f)] private float visualSteerTime = 0.1f;

        private SyncVar<PlayerID?> _topDriver = new();
        private SyncVar<PlayerID?> _bottomDriver = new();
        private SyncVar<float> _steer = new();

        private CarControllerBase _carController;
        private PlayerCarControl _playerCarControl;
        private InputAction _leaveAction;
        private Player.Player _seatedLocalPlayer;
        private Vector3 _lastInput;
        private float _visualSteer;

        private void Awake()
        {
            if (!TryGetComponent(out _carController))
            {
                Debug.LogError("CarPlace requires a CarControllerBase on the same GameObject.", this);
                enabled = false;
                return;
            }

            if (!TryGetComponent(out _playerCarControl))
                _playerCarControl = gameObject.AddComponent<PlayerCarControl>();

            _playerCarControl.UseRemoteInput = true;
        }

        protected override void OnSpawned()
        {
            if (_carController == null)
                return;

            GetComponent<Rigidbody>().isKinematic = !isServer;
            _carController.enabled = isServer;
            _playerCarControl.enabled = isServer;

            _leaveAction = InputSystem.actions?.FindAction("Jump");

            _topDriver.onChanged += OnSeatsChanged;
            _bottomDriver.onChanged += OnSeatsChanged;
            networkManager.onTick += OnTick;

            if (isServer)
            {
                UpdateControlMode();
                networkManager.playerModule.onPlayerLeft += OnPlayerLeft;
            }
        }

        protected override void OnDespawned()
        {
            _topDriver.onChanged -= OnSeatsChanged;
            _bottomDriver.onChanged -= OnSeatsChanged;
            GetOut();

            if (networkManager == null)
                return;

            networkManager.onTick -= OnTick;
            if (isServer)
                networkManager.playerModule.onPlayerLeft -= OnPlayerLeft;
        }

        public void PlaceTop(Player.Player player) => RequestSeat(player, true);
        public void PlaceBottom(Player.Player player) => RequestSeat(player, false);

        private void RequestSeat(Player.Player player, bool topSeat)
        {
            if (isSpawned && player != null && player.isOwner)
                RequestSeatRpc(topSeat);
        }

        [ServerRpc(requireOwnership: false)]
        private void RequestSeatRpc(bool topSeat, RPCInfo info = default)
        {
            var seat = topSeat ? _topDriver : _bottomDriver;
            var otherSeat = topSeat ? _bottomDriver : _topDriver;

            if (seat.value.HasValue && seat.value != info.sender)
                return;

            seat.value = info.sender;
            if (otherSeat.value == info.sender)
                otherSeat.value = null;

            UpdateControlMode();
        }

        [ServerRpc(requireOwnership: false)]
        private void LeaveSeatRpc(RPCInfo info = default)
        {
            if (_topDriver.value == info.sender)
                _topDriver.value = null;
            if (_bottomDriver.value == info.sender)
                _bottomDriver.value = null;
            UpdateControlMode();
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            if (!asServer)
                return;

            if (_topDriver.value == player)
                _topDriver.value = null;
            if (_bottomDriver.value == player)
                _bottomDriver.value = null;
            UpdateControlMode();
        }

        private void UpdateControlMode()
        {
            var mode = PlayerCarControl.ControlMode.None;
            if (_topDriver.value.HasValue)
                mode |= PlayerCarControl.ControlMode.Steering;
            else
                _steer.value = 0f;
            if (_bottomDriver.value.HasValue)
                mode |= PlayerCarControl.ControlMode.Speed;

            _playerCarControl.SetControlMode(mode);
        }

        private Transform LocalSeat
        {
            get
            {
                if (!localPlayer.HasValue)
                    return null;
                if (_topDriver.value == localPlayer)
                    return top;
                if (_bottomDriver.value == localPlayer)
                    return bottom;
                return null;
            }
        }

        private void OnSeatsChanged(PlayerID? _)
        {
            var seat = LocalSeat;
            if (seat == null)
            {
                GetOut();
                return;
            }

            if (_seatedLocalPlayer == null)
                _seatedLocalPlayer = FindLocalPlayer();
            if (_seatedLocalPlayer == null || _seatedLocalPlayer.transform.parent == seat)
                return;

            _lastInput = Vector3.zero;
            if (_seatedLocalPlayer.TryGetComponent(out PlayerMouvement movement))
                movement.Freeze();

            _seatedLocalPlayer.transform.SetParent(seat, false);
            _seatedLocalPlayer.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        private void GetOut()
        {
            var player = _seatedLocalPlayer;
            _seatedLocalPlayer = null;
            if (player == null)
                return;

            player.transform.SetParent(null, true);
            if (player.TryGetComponent(out PlayerMouvement movement))
                movement.Release(GetExitPosition(), transform.forward);
        }

        private void Update()
        {
            if (!isSpawned)
                return;

            if (_seatedLocalPlayer != null && _leaveAction != null && _leaveAction.WasPressedThisFrame())
                LeaveSeatRpc();

            if (!isServer)
            {
                _visualSteer = Mathf.MoveTowards(_visualSteer, _steer.value, Time.deltaTime / visualSteerTime);
                var steerAngle = _carController.MaxSteerAngle * _visualSteer;
                foreach (var wheel in _carController.SteerableWheels)
                {
                    if (wheel != null)
                        wheel.SteerAngle = steerAngle;
                }
            }
        }

        private void OnTick(bool asServer)
        {
            if (asServer || !localPlayer.HasValue)
                return;

            bool isTop = _topDriver.value == localPlayer;
            bool isBottom = _bottomDriver.value == localPlayer;
            if (!isTop && !isBottom)
                return;

            var input = new Vector3(
                isTop ? _playerCarControl.ReadSteerInput() : 0f,
                isBottom ? _playerCarControl.ReadThrottleInput() : 0f,
                isBottom ? _playerCarControl.ReadBrakeInput() : 0f);

            if (input == _lastInput)
                return;

            _lastInput = input;
            SendInputRpc(input.x, input.y, input.z);
        }

        [ServerRpc(requireOwnership: false)]
        private void SendInputRpc(float steer, float throttle, float brake, RPCInfo info = default)
        {
            if (_topDriver.value == info.sender)
            {
                _playerCarControl.SetRemoteSteer(steer);
                _steer.value = Mathf.Clamp(steer, -1f, 1f);
            }
            if (_bottomDriver.value == info.sender)
                _playerCarControl.SetRemotePedals(throttle, brake);
        }
        
        private Vector3 GetExitPosition()
        {
            float halfWidth = 0f;
            foreach (var wheel in _carController.Wheels)
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(wheel.transform.localPosition.x) + wheel.Width);

            var side = Vector3.ProjectOnPlane(-transform.right, Vector3.up).normalized;
            return transform.position + side * (halfWidth + exitSideOffset) + Vector3.up * 0.5f;
        }

        private static Player.Player FindLocalPlayer()
        {
            foreach (var player in FindObjectsByType<Player.Player>())
            {
                if (player.isOwner)
                    return player;
            }
            return null;
        }
    }
}
