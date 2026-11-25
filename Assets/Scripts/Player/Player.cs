using PurrNet;
using UnityEngine;
using DwarfSimulator.Player.Interactor;

namespace DwarfSimulator.Player
{
    [RequireComponent(typeof(NetworkTransform), typeof(Rigidbody), typeof(Collider))]
    [RequireComponent(typeof(PlayerCustomisationLoader))]
    public class Player : PlayerIdentity<Player>
    {
        private Collider _collider;
        private Rigidbody _rigidbody;
        private NetworkTransform _networkTransform;
        private PlayerCustomisationLoader _customisationLoader;
        private readonly SyncVar<PlayerCustomisation> _customisation = new(ownerAuth: true);

        public Transform Seat { get; private set; }
        public bool IsSeated => Seat != null;
        public Interactable SeatInteractable { get; private set; }
        public string SeatDescription { get; private set; }

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _rigidbody = GetComponent<Rigidbody>();
            _networkTransform = GetComponent<NetworkTransform>();
            _customisationLoader = GetComponent<PlayerCustomisationLoader>();
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            if (isOwner)
                _customisation.value = PlayerCustomisationLoader.LoadLocal();

            _customisation.onChanged += ApplyCustomisation;
            ApplyCustomisation(_customisation.value);
        }

        protected override void OnDespawned()
        {
            _customisation.onChanged -= ApplyCustomisation;
            base.OnDespawned();
        }

        private void ApplyCustomisation(PlayerCustomisation data)
        {
            _customisationLoader.ApplyCustomisation(data);
        }

        public void SitIn(Transform seat, Interactable seatInteractable, string seatDescription)
        {
            Seat = seat;
            SeatInteractable = seatInteractable;
            SeatDescription = seatDescription;
            _networkTransform.enabled = false;
            _collider.enabled = false;
            _rigidbody.isKinematic = true;
            FollowSeat();
        }

        public void GetOut(Vector3 position, Quaternion rotation)
        {
            Seat = null;
            SeatInteractable = null;
            SeatDescription = null;
            _collider.enabled = true;
            _rigidbody.isKinematic = false;
            transform.SetPositionAndRotation(position, rotation);
            _networkTransform.enabled = true;
        }

        private void LateUpdate() => FollowSeat();

        private void FollowSeat()
        {
            if (Seat != null)
                transform.SetPositionAndRotation(Seat.position, Seat.rotation);
        }
    }
}
