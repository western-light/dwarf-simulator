using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player
{
    public class PlayerMouvement:MonoBehaviour
    {
        private const float GroundCheckMargin = 0.05f;

        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float jumpForce = 5f;

        private InputAction _moveAction;
        private InputAction _runAction;
        private InputAction _jumpAction;
        private Rigidbody _rb;
        private Animator _anim;
        private Collider _col;
        private Transform _playerCamera;
        
        private Vector3 _lastMoveDirection;
        private Transform _parent;
        private bool _freeze;
        private bool _interactionFrozen;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            _col = GetComponent<Collider>();
            _anim = GetComponent<Animator>();
            _playerCamera = Camera.main.transform;
            _moveAction = InputSystem.actions.FindAction("Move");
            _runAction = InputSystem.actions.FindAction("Sprint");
            _jumpAction = InputSystem.actions.FindAction("Jump");
            
        }

        void FixedUpdate()
        {
            if (_parent != null)
            {
                transform.position = _parent.position;
                transform.rotation = _parent.rotation;
            }

            if (_freeze || _interactionFrozen)
            {
                _anim.SetBool("Walk", false);
                _anim.SetBool("Run", false);
                return;
            }
            
            Vector2 moveInput = _moveAction.ReadValue<Vector2>();
            Vector3 cameraForward = Vector3.ProjectOnPlane(_playerCamera.forward, Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(_playerCamera.right, Vector3.up).normalized;
            Vector3 moveDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;
            if (moveDirection.magnitude > 0.1f)
                _lastMoveDirection = moveDirection;
            bool run = _runAction.IsPressed();
            float speed = run ? runSpeed : moveSpeed;
            
            _anim.SetBool("Walk", moveDirection.magnitude > 0);
            _anim.SetBool("Run", run);
            
            _rb.MovePosition(transform.position + moveDirection * Time.fixedDeltaTime * speed);
            Quaternion targetRotation = Quaternion.LookRotation(_lastMoveDirection);
            _rb.MoveRotation(targetRotation);
            
            if (_jumpAction.IsPressed() && IsGrounded())
            {
                _anim.SetTrigger("Jump");
                _rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            }
        }

        private bool IsGrounded()
        {
            Bounds bounds = _col.bounds;
            return Physics.Raycast(bounds.center, Vector3.down, bounds.extents.y + GroundCheckMargin,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
        
        public void Freeze() => _freeze = true;
        public void SetInteractionFrozen(bool value) => _interactionFrozen = value;
        public void Freeze(Vector3 position, Quaternion rotation)
        {
            _freeze = true;
            _lastMoveDirection = new Vector3(0, 0, 0);
            transform.position = position;
            transform.rotation = rotation;
        }
        public void UnFreeze() => _freeze = false;

        public void SyncTransform(Transform parent)
        {
            _parent = parent;
        }
    }
}
