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
        private bool _freeze;
        private bool _interactionFrozen;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _col = GetComponent<Collider>();
            _anim = GetComponent<Animator>();
        }

        void Start()
        {
            _playerCamera = Camera.main.transform;
            _moveAction = InputSystem.actions.FindAction("Move");
            _runAction = InputSystem.actions.FindAction("Sprint");
            _jumpAction = InputSystem.actions.FindAction("Jump");
            
        }

        void FixedUpdate()
        {
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
            if (_lastMoveDirection.sqrMagnitude > 0.01f)
                _rb.MoveRotation(Quaternion.LookRotation(_lastMoveDirection));
            
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
        public void UnFreeze() => _freeze = false;

        public void Release(Vector3 position, Vector3 forward)
        {
            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            _lastMoveDirection = forward;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            _rb.position = position;
            _rb.rotation = transform.rotation;
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _freeze = false;
        }
    }
}
