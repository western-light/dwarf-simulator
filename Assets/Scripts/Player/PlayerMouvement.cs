using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player
{
    public class PlayerMouvement:MonoBehaviour
    {
        private const float GroundCheckMargin = 0.05f;
        private const float JumpBufferDuration = 0.15f;

        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float jumpForce = 5f;

        private InputAction _moveAction;
        private InputAction _runAction;
        private InputAction _jumpAction;
        private Player _player;
        private Rigidbody _rb;
        private Animator _anim;
        private Collider _col;
        private Transform _playerCamera;
        
        private Vector3 _lastMoveDirection;
        private bool _interactionFrozen;
        private bool _jumpRequested;
        private float _jumpRequestedAt;

        void Awake()
        {
            _player = GetComponent<Player>();
            _rb = GetComponent<Rigidbody>();
            _col = GetComponent<Collider>();
            _anim = GetComponent<Animator>();
        }

        void Start()
        {
            if (_anim != null && (!_anim.enabled || _anim.runtimeAnimatorController == null))
                _anim = null;
            _playerCamera = Camera.main.transform;
            _moveAction = InputSystem.actions.FindAction("Move");
            _runAction = InputSystem.actions.FindAction("Sprint");
            _jumpAction = InputSystem.actions.FindAction("Jump");
            
        }

        void Update()
        {
            if (_player.IsSeated || _interactionFrozen)
            {
                _jumpRequested = false;
                return;
            }

            if (_jumpAction.WasPressedThisFrame())
            {
                _jumpRequested = true;
                _jumpRequestedAt = Time.time;
            }
        }

        void FixedUpdate()
        {
            if (_player.IsSeated || _interactionFrozen)
            {
                _jumpRequested = false;
                if (_anim != null) _anim.SetBool("Walk", false);
                if (_anim != null) _anim.SetBool("Run", false);
                _lastMoveDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
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
            
            if (_anim != null) _anim.SetBool("Walk", moveDirection.magnitude > 0);
            if (_anim != null) _anim.SetBool("Run", run);
            
            _rb.MovePosition(transform.position + moveDirection * Time.fixedDeltaTime * speed);
            if (_lastMoveDirection.sqrMagnitude > 0.01f)
                _rb.MoveRotation(Quaternion.LookRotation(_lastMoveDirection));
            
            bool jumpBuffered = _jumpRequested && Time.time - _jumpRequestedAt <= JumpBufferDuration;
            if ((_jumpAction.IsPressed() || jumpBuffered) && IsGrounded())
            {
                _jumpRequested = false;
                if (_anim != null) _anim.SetTrigger("Jump");
                _rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            }
            else if (_jumpRequested && !jumpBuffered)
                _jumpRequested = false;
        }

        private bool IsGrounded()
        {
            Bounds bounds = _col.bounds;
            return Physics.Raycast(bounds.center, Vector3.down, bounds.extents.y + GroundCheckMargin,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
        
        public void SetInteractionFrozen(bool value) => _interactionFrozen = value;
    }
}
