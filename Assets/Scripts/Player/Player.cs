using PurrNet;
using UnityEngine;

namespace DwarfSimulator.Player
{
    public class Player : NetworkBehaviour
    {
        private Collider _collider;
        private Rigidbody _rigidbody;
        private bool _wasKinematic;
        private bool _seated;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _rigidbody = GetComponent<Rigidbody>();
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
