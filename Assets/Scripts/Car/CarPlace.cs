using DwarfSimulator.Car.Controller;
using DwarfSimulator.Player;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

namespace DwarfSimulator.Car
{
    public class CarPlace : NetworkBehaviour
    {
        [SerializeField] private Transform top;
        [SerializeField] private Transform bottom;

        private SyncVar<PlayerID?> _topDriver = new();
        private SyncVar<PlayerID?> _bottomDriver = new();

        private CarControllerBase _carController;
        private PlayerCarControl _playerCarControl;

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

            // Only the server runs the physics.
            GetComponent<Rigidbody>().isKinematic = !isServer;
            _carController.enabled = isServer;
            _playerCarControl.enabled = isServer;

            _topDriver.onChanged += OnTopChanged;
            _bottomDriver.onChanged += OnBottomChanged;
            networkManager.onTick += OnTick;

            if (isServer)
            {
                _playerCarControl.SetControlMode(PlayerCarControl.ControlMode.None);
                networkManager.playerModule.onPlayerLeft += OnPlayerLeft;
            }
        }

        protected override void OnDespawned()
        {
            _topDriver.onChanged -= OnTopChanged;
            _bottomDriver.onChanged -= OnBottomChanged;

            if (networkManager == null)
                return;

            networkManager.onTick -= OnTick;
            if (isServer)
                networkManager.playerModule.onPlayerLeft -= OnPlayerLeft;
        }

        // Called by the interaction menu on the client that picked the seat.
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

            if (otherSeat.value == info.sender)
                otherSeat.value = null;

            seat.value = info.sender;
            UpdateControlMode();
        }

        // Inputs: seat occupant -> server. Unreliable and sent every tick, so a lost packet is simply replaced.
        private void OnTick(bool asServer)
        {
            if (asServer || !localPlayer.HasValue)
                return;

            bool isTop = _topDriver.value == localPlayer;
            bool isBottom = _bottomDriver.value == localPlayer;
            if (!isTop && !isBottom)
                return;

            SendInputRpc(
                isTop ? _playerCarControl.ReadSteerInput() : 0f,
                isBottom ? _playerCarControl.ReadThrottleInput() : 0f,
                isBottom ? _playerCarControl.ReadBrakeInput() : 0f);
        }

        [ServerRpc(Channel.Unreliable, requireOwnership: false)]
        private void SendInputRpc(float steer, float throttle, float brake, RPCInfo info = default)
        {
            if (_topDriver.value == info.sender)
                _playerCarControl.SetRemoteSteer(steer);
            if (_bottomDriver.value == info.sender)
                _playerCarControl.SetRemotePedals(throttle, brake);
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
            if (_bottomDriver.value.HasValue)
                mode |= PlayerCarControl.ControlMode.Speed;

            _playerCarControl.SetControlMode(mode);
        }

        private void OnTopChanged(PlayerID? driver) => Seat(driver, top);
        private void OnBottomChanged(PlayerID? driver) => Seat(driver, bottom);

        private void Seat(PlayerID? driver, Transform seat)
        {
            if (!driver.HasValue)
                return;

            foreach (var player in FindObjectsByType<Player.Player>())
            {
                if (player.owner != driver)
                    continue;

                if (isServer)
                {
                    player.GetComponent<Collider>().enabled = false;
                    player.GetComponent<Rigidbody>().isKinematic = true;
                }

                if (player.isOwner)
                {
                    var movement = player.GetComponent<PlayerMouvement>();
                    movement.Freeze(seat.position, seat.rotation);
                    movement.SyncTransform(seat);
                }
            }
        }
    }
}
