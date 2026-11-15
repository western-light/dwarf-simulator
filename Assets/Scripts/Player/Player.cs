using PurrNet;
using UnityEngine;

namespace DwarfSimulator.Player
{
    public class Player : NetworkBehaviour
    {
        private Collider _collider;
        private Rigidbody _rigidbody;
        private NetworkTransform _networkTransform;
        private bool _wasKinematic;
        private bool _seated;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _rigidbody = GetComponent<Rigidbody>();
            _networkTransform = GetComponent<NetworkTransform>();
        }

        public void SitIn(Transform seat)
        {
            if (transform.parent == seat)
                return;

            SetLocalParent(seat, false);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            if (_networkTransform != null && !_networkTransform.isController)
                _networkTransform.ClearInterpolation(Vector3.zero, Quaternion.identity, null);
        }

        public void LeaveSeat()
        {
            if (transform.parent == null)
                return;

            SetLocalParent(null, true);

            if (_networkTransform != null && !_networkTransform.isController)
                _networkTransform.ClearInterpolation(transform.position, transform.rotation, null);
        }

        private void SetLocalParent(Transform parent, bool worldPositionStays)
        {
            if (_networkTransform != null)
                _networkTransform.StartIgnoringParentChanges();

            transform.SetParent(parent, worldPositionStays);

            if (_networkTransform != null)
                _networkTransform.StopIgnoringParentChanges();
        }

        private void OnTransformParentChanged()
        {
            bool seated = transform.parent != null;
            if (seated == _seated)
                return;
            _seated = seated;

            if (_collider != null)
                _collider.enabled = !seated;

            if (_rigidbody == null)
                return;

            if (seated)
            {
                _wasKinematic = _rigidbody.isKinematic;
                _rigidbody.isKinematic = true;
            }
            else
            {
                _rigidbody.isKinematic = _wasKinematic;
            }
        }
    }
}
