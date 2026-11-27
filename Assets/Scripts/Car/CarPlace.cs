using DwarfSimulator.Car.Controller;
using DwarfSimulator.Player.Interactor;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Car
{
    [RequireComponent(typeof(CarControllerBase))]
    public class CarPlace : NetworkBehaviour, IInteractionAvailability, IInteractionLabel
    {
        public struct SeatAssignments : IPackedAuto
        {
            public PlayerID? Top;
            public PlayerID? Bottom;
            public PlayerID? SwapRequester;

            public bool IsSwap(PlayerID player, bool onTop)
            {
                PlayerID? destination = onTop ? Top : Bottom;
                PlayerID? source = onTop ? Bottom : Top;
                return destination.HasValue && destination != player && source == player;
            }

            public bool TryAssign(PlayerID player, bool onTop, out SeatAssignments result)
            {
                result = this;
                result.SwapRequester = null;
                PlayerID? destination = onTop ? Top : Bottom;
                PlayerID? source = onTop ? Bottom : Top;
                if (destination == player || (destination.HasValue && source != player))
                    return false;

                if (onTop)
                {
                    result.Top = player;
                    if (source == player)
                        result.Bottom = destination;
                }
                else
                {
                    result.Bottom = player;
                    if (source == player)
                        result.Top = destination;
                }
                return true;
            }
        }

        [SerializeField] private Transform top;
        [SerializeField] private Transform bottom;
        [SerializeField] private float exitDistance = 2.2f;
        [SerializeField, Min(1f)] private float blackoutSeconds = 90f;
        [SerializeField, Min(1f)] private float recoverySeconds = 30f;

        private readonly SyncVar<SeatAssignments> _seats = new();
        private SyncVar<float> _steer = new();
        private readonly SyncVar<float> _throttle = new();
        private readonly SyncVar<float> _brake = new();

        private Player.Player _topPlayer;
        private Player.Player _bottomPlayer;
        private bool _inputNeedsSending = true;
        private CarControllerBase _car;
        private PlayerCarControl _driver;
        private CarCockpit _cockpit;
        private InputAction _leaveAction;
        private (float steer, float throttle, float brake) _lastInput;

        private void Awake()
        {
            _car = GetComponent<CarControllerBase>();
            if (!TryGetComponent(out _driver))
                _driver = gameObject.AddComponent<PlayerCarControl>();
            _driver.UseRemoteInput = true;
            if (!TryGetComponent(out _cockpit))
                _cockpit = gameObject.AddComponent<CarCockpit>();
            _cockpit.Build(bottom);
        }

        protected override void OnSpawned()
        {
            _car.Rigidbody.isKinematic = !isServer;
            _car.enabled = isServer;
            _driver.enabled = isServer;
            _leaveAction = InputSystem.actions.FindAction("Jump");

            _seats.onChanged += OnAssignmentsChanged;
            OnAssignmentsChanged(_seats.value);
        }

        protected override void OnDespawned()
        {
            _seats.onChanged -= OnAssignmentsChanged;
            OnSeatChanged(_topPlayer, null, top);
            OnSeatChanged(_bottomPlayer, null, bottom);
            _topPlayer = null;
            _bottomPlayer = null;
            base.OnDespawned();
        }

        private void Update()
        {
            if (!isSpawned)
                return;

            ReconcileSeats();
            float visualSteer;
            float visualThrottle;
            float visualBrake;
            if (isServer)
            {
                RemoveDisconnectedPlayers();
                visualSteer = _car.SteerInput;
                visualThrottle = _driver.RemoteThrottle;
                visualBrake = _driver.RemoteBrake;
                _steer.value = visualSteer;
                _throttle.value = visualThrottle;
                _brake.value = visualBrake;
            }
            else
            {
                visualSteer = _steer.value;
                visualThrottle = _throttle.value;
                visualBrake = _brake.value;
                TurnWheels();
            }
            _cockpit.SetInputs(visualSteer, visualThrottle, visualBrake);

            bool onTop = _topPlayer != null && _topPlayer.isOwner;
            bool onBottom = _bottomPlayer != null && _bottomPlayer.isOwner;
            if (!onTop && !onBottom)
                return;

            if (_leaveAction.WasPressedThisFrame())
            {
                LeaveSeatRpc();
                return;
            }

            Player.Player localPlayer = onTop ? _topPlayer : _bottomPlayer;
            bool menuOpen = localPlayer.TryGetComponent(out Player.PlayerInteract interaction) &&
                            interaction.IsInteractionOpen;
            (float steer, float throttle, float brake) input = (
                onTop && !menuOpen ? _driver.ReadSteerInput() : 0f,
                onBottom && !menuOpen ? _driver.ReadThrottleInput() : 0f,
                onBottom && !menuOpen ? _driver.ReadBrakeInput() : 0f);

            if (!_inputNeedsSending && input == _lastInput)
                return;

            _lastInput = input;
            _inputNeedsSending = false;
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

        public string UnavailableReason(int choiceIndex, Player.Player player)
        {
            if (!isSpawned)
                return "interaction indisponible";
            if (choiceIndex < 0 || choiceIndex > 1)
                return "interaction indisponible";
            PlayerID? owner = player != null ? player.owner : null;
            SeatAssignments seats = _seats.value;
            PlayerID? seat = choiceIndex == 0 ? seats.Top : seats.Bottom;
            PlayerID? otherSeat = choiceIndex == 0 ? seats.Bottom : seats.Top;
            if (owner.HasValue && seat == owner)
                return "place actuelle";
            return seat.HasValue && (!owner.HasValue || otherSeat != owner) ? "place occupee" : null;
        }

        public string ChoiceName(int choiceIndex, Player.Player player, string defaultName)
        {
            if (choiceIndex < 0 || choiceIndex > 1 || player == null || !player.owner.HasValue)
                return defaultName;
            SeatAssignments seats = _seats.value;
            PlayerID self = player.owner.Value;
            if (!seats.IsSwap(self, choiceIndex == 0))
                return defaultName;
            if (seats.SwapRequester == self)
                return $"{defaultName} (annuler la demande d'echange)";
            if (seats.SwapRequester.HasValue)
                return $"{defaultName} (accepter l'echange)";
            return $"{defaultName} (proposer d'echanger)";
        }

        private void OnAssignmentsChanged(SeatAssignments seats)
        {
            if (isServer)
            {
                _driver.SetRemoteSteer(0f);
                _driver.SetRemotePedals(0f, 0f);
                RefreshControlMode();
            }
            _inputNeedsSending = true;
            ReconcileSeats();
        }

        private void ReconcileSeats()
        {
            SeatAssignments seats = _seats.value;
            Player.Player newTop = ResolvePlayer(seats.Top);
            Player.Player newBottom = ResolvePlayer(seats.Bottom);
            if (newTop == _topPlayer && newBottom == _bottomPlayer)
                return;

            Player.Player oldTop = _topPlayer;
            Player.Player oldBottom = _bottomPlayer;
            _topPlayer = newTop;
            _bottomPlayer = newBottom;

            if (oldTop != null && oldTop != newTop && oldTop != newBottom)
                OnSeatChanged(oldTop, null, top);
            if (oldBottom != null && oldBottom != newTop && oldBottom != newBottom)
                OnSeatChanged(oldBottom, null, bottom);
            if (newTop != null && newTop != oldTop)
                OnSeatChanged(null, newTop, top);
            if (newBottom != null && newBottom != oldBottom)
                OnSeatChanged(null, newBottom, bottom);
            _inputNeedsSending = true;
        }

        private static Player.Player ResolvePlayer(PlayerID? id)
        {
            return id.HasValue && Player.Player.TryGetPlayer(id.Value, out var player) && player.isSpawned
                ? player : null;
        }

        private void RemoveDisconnectedPlayers()
        {
            SeatAssignments seats = _seats.value;
            bool changed = false;
            if (seats.Top.HasValue && _topPlayer == null)
            {
                seats.Top = null;
                changed = true;
            }
            if (seats.Bottom.HasValue && _bottomPlayer == null)
            {
                seats.Bottom = null;
                changed = true;
            }
            if (changed)
            {
                seats.SwapRequester = null;
                _seats.value = seats;
            }
        }

        private void OnSeatChanged(Player.Player oldPlayer, Player.Player newPlayer, Transform seat)
        {
            if (oldPlayer != null && oldPlayer.Seat == seat)
            {
                oldPlayer.GetOut(transform.position - transform.right * exitDistance + Vector3.up * 0.5f,
                    Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
                if (oldPlayer.isOwner)
                {
                    DriverVision.SetDriving(false, blackoutSeconds, recoverySeconds);
                    if (oldPlayer.TryGetComponent(out Player.CameraSync oldCamera))
                        oldCamera.UnlockView();
                }
            }

            if (newPlayer != null)
            {
                newPlayer.SitIn(seat, GetComponent<Interactable>(), seat == top ? "Direction" : "Pedales");
                if (seat == top)
                    newPlayer.SetLimbTargets(_cockpit.LeftGrip, _cockpit.RightGrip, null, null);
                else
                    newPlayer.SetLimbTargets(null, null, _cockpit.BrakeFoot, _cockpit.ThrottleFoot);
                if (newPlayer.isOwner)
                    DriverVision.SetDriving(seat == top, blackoutSeconds, recoverySeconds);
                if (newPlayer.isOwner && newPlayer.TryGetComponent(out Player.CameraSync newCamera))
                {
                    if (seat == bottom)
                        newCamera.LockFirstPersonStabilized(seat);
                    else
                        newCamera.LockThirdPerson();
                }
            }
        }

        private void TurnWheels()
        {
            float angle = _car.MaxSteerAngle * _steer.value;
            foreach (var wheel in _car.SteerableWheels)
                wheel.SteerAngle = angle;
        }

        [ServerRpc(requireOwnership: false)]
        private void RequestSeatRpc(bool onTop, RPCInfo info = default)
        {
            if (!Player.Player.TryGetPlayer(info.sender, out var player))
            {
                SeatRequestFailed(info.sender, "Joueur indisponible");
                return;
            }
            SeatAssignments seats = _seats.value;
            if (!seats.TryAssign(info.sender, onTop, out SeatAssignments updated))
            {
                SeatRequestFailed(info.sender, "Place occupee ou deja selectionnee");
                return;
            }
            if (seats.IsSwap(info.sender, onTop))
            {
                PlayerID other = (onTop ? seats.Top : seats.Bottom).Value;
                if (seats.SwapRequester != other)
                {
                    bool cancelling = seats.SwapRequester == info.sender;
                    seats.SwapRequester = cancelling ? null : info.sender;
                    _seats.value = seats;
                    if (cancelling)
                    {
                        SeatRequestFailed(info.sender, "Demande d'echange annulee");
                        return;
                    }
                    SeatRequestFailed(info.sender, "Demande d'echange envoyee");
                    SeatRequestFailed(other, "L'autre joueur propose d'echanger de place");
                    return;
                }
            }
            bool alreadySeatedHere = seats.Top == info.sender || seats.Bottom == info.sender;
            if (!alreadySeatedHere)
            {
                if (player.IsSeated)
                {
                    SeatRequestFailed(info.sender, "Vous etes deja assis");
                    return;
                }
                var interaction = player.GetComponent<Player.PlayerInteract>();
                if (interaction == null)
                {
                    Debug.LogError("Cannot validate a seat request without a player interaction component.", this);
                    SeatRequestFailed(info.sender, "Interaction indisponible");
                    return;
                }
                if (!TryGetInteractionDistance(player.transform.position + Vector3.up * 0.8f, out float distance))
                {
                    Debug.LogError("Cannot validate a seat request without an active car collider.", this);
                    SeatRequestFailed(info.sender, "Interaction indisponible");
                    return;
                }
                if (distance > interaction.InteractDistance)
                {
                    SeatRequestFailed(info.sender, "Trop loin");
                    return;
                }
            }

            _seats.value = updated;
        }

        private bool TryGetInteractionDistance(Vector3 origin, out float distance)
        {
            distance = float.PositiveInfinity;
            Interactable interactable = GetComponent<Interactable>();
            foreach (Collider body in GetComponentsInChildren<Collider>())
            {
                if (!body.enabled || body.isTrigger ||
                    (Physics.DefaultRaycastLayers & (1 << body.gameObject.layer)) == 0 ||
                    body.GetComponentInParent<Interactable>() != interactable)
                    continue;

                distance = Mathf.Min(distance, Vector3.Distance(origin, body.ClosestPoint(origin)));
            }
            return !float.IsPositiveInfinity(distance);
        }

        [TargetRpc]
        private void SeatRequestFailed(PlayerID target, string message)
        {
            InteractorUI ui = FindAnyObjectByType<InteractorUI>();
            if (ui != null)
                ui.ShowMessage(message);
            else
                Debug.LogWarning(message, this);
        }

        [ServerRpc(requireOwnership: false)]
        private void LeaveSeatRpc(RPCInfo info = default)
        {
            SeatAssignments seats = _seats.value;
            if (seats.Top == info.sender)
                seats.Top = null;
            else if (seats.Bottom == info.sender)
                seats.Bottom = null;
            else
                return;
            seats.SwapRequester = null;
            _seats.value = seats;
        }

        [ServerRpc(requireOwnership: false)]
        private void SendInputRpc(float steer, float throttle, float brake, RPCInfo info = default)
        {
            if (_seats.value.Top == info.sender)
                _driver.SetRemoteSteer(steer);
            if (_seats.value.Bottom == info.sender)
                _driver.SetRemotePedals(throttle, brake);
        }

        private void RefreshControlMode()
        {
            var mode = PlayerCarControl.ControlMode.None;
            if (_seats.value.Top.HasValue)
                mode |= PlayerCarControl.ControlMode.Steering;
            if (_seats.value.Bottom.HasValue)
                mode |= PlayerCarControl.ControlMode.Speed;
            _driver.SetControlMode(mode);
        }
    }
}
