using PurrNet;
using UnityEngine;

namespace DwarfSimulator.Player
{
    [RequireComponent(typeof(NetworkTransform), typeof(Rigidbody), typeof(Collider))]
    public class Player : PlayerIdentity<Player>
    {
        private Collider _collider;
        private Rigidbody _rigidbody;
        private NetworkTransform _networkTransform;

        public Transform Seat { get; private set; }
        public bool IsSeated => Seat != null;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _rigidbody = GetComponent<Rigidbody>();
            _networkTransform = GetComponent<NetworkTransform>();
        }

        public void SitIn(Transform seat)
        {
            Seat = seat;
            _networkTransform.enabled = false;
            _collider.enabled = false;
            _rigidbody.isKinematic = true;
            FollowSeat();
        }

        public void GetOut(Vector3 position, Quaternion rotation)
        {
            Seat = null;
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
