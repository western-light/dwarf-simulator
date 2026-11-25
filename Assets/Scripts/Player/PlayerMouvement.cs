using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player
{
    public class PlayerMouvement:MonoBehaviour
    {
        private const float GroundCheckMargin = 0.05f;
        private const float JumpBufferDuration = 0.15f;
        private const float MinGroundNormalY = 0.15f;

        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float jumpForce = 5f;
        [SerializeField, Range(0f, 85f)] private float maxWalkableSlopeAngle = 35f;
        [SerializeField, Min(0f)] private float slideGravityMultiplier = 1f;
        [SerializeField, Min(0f)] private float slideControlAcceleration = 4f;
        [SerializeField, Min(0f)] private float slideFriction = 0.6f;
        [SerializeField, Min(0.1f)] private float slideRecoveryAcceleration = 6f;
        [SerializeField, Min(0.1f)] private float maxSlideSpeed = 12f;

        private InputAction _moveAction;
        private InputAction _runAction;
        private InputAction _jumpAction;
        private Player _player;
        private GnomeAvatar _avatar;
        private Rigidbody _rb;
        private Animator _anim;
        private Collider _col;
        private Transform _playerCamera;
        private bool _defaultUseGravity;
        
        private Vector3 _lastMoveDirection;
        private bool _interactionFrozen;
        private bool _jumpRequested;
        private float _jumpRequestedAt;
        private bool _slideMomentum;

        public float MaxWalkableSlopeAngle => maxWalkableSlopeAngle;

        void Awake()
        {
            _player = GetComponent<Player>();
            _avatar = GetComponent<GnomeAvatar>();
            _rb = GetComponent<Rigidbody>();
            _col = GetComponent<Collider>();
            _anim = GetComponent<Animator>();
            _defaultUseGravity = _rb.useGravity;
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void Start()
        {
            if (_avatar == null)
                _avatar = GetComponent<GnomeAvatar>();
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
                _slideMomentum = false;
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
                _rb.useGravity = _defaultUseGravity;
                if (_player.IsSeated && _avatar != null)
                    _avatar.SetRagdoll(false);
                if (_anim != null) _anim.SetBool("Walk", false);
                if (_anim != null) _anim.SetBool("Run", false);
                _lastMoveDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (!_player.IsSeated)
                {
                    Vector3 v = _rb.linearVelocity;
                    _rb.linearVelocity = new Vector3(0f, v.y, 0f);
                }
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
            
            bool grounded = CheckGround(out Vector3 groundNormal);
            float slopeAngle = grounded ? Vector3.Angle(groundNormal, Vector3.up) : 0f;
            bool steepSlope = grounded && slopeAngle > maxWalkableSlopeAngle;
            float ragdollThreshold = _avatar != null ? _avatar.RagdollSlopeAngle : maxWalkableSlopeAngle + 7f;
            bool ragdollSlope = grounded && slopeAngle >= ragdollThreshold;
            if (_avatar != null && ragdollSlope)
                _avatar.SetRagdoll(true);

            bool ragdolled = _avatar != null && _avatar.IsRagdolled;
            if (ragdolled)
            {
                _jumpRequested = false;
                _slideMomentum = true;
                _rb.useGravity = _defaultUseGravity;
                if (steepSlope)
                {
                    Vector3 gravityAlongSlope = Vector3.ProjectOnPlane(Physics.gravity, groundNormal);
                    float extraGravity = Mathf.Max(0f, slideGravityMultiplier - (_defaultUseGravity ? 1f : 0f));
                    _rb.AddForce(gravityAlongSlope * extraGravity, ForceMode.Acceleration);
                }
                return;
            }

            Vector3 velocity = _rb.linearVelocity;
            Vector3 wanted = moveDirection * speed;
            _rb.useGravity = steepSlope ? false : _defaultUseGravity;

            if (steepSlope)
            {
                _slideMomentum = true;
                velocity = CalculateSlideVelocity(velocity, wanted, groundNormal, Time.fixedDeltaTime);
            }
            else if (_slideMomentum)
            {
                Vector3 normal = grounded ? groundNormal : Vector3.up;
                Vector3 tangent = Vector3.ProjectOnPlane(velocity, normal);
                Vector3 target = Vector3.ProjectOnPlane(wanted, normal);
                tangent = Vector3.MoveTowards(tangent, target, slideRecoveryAcceleration * Time.fixedDeltaTime);
                float normalSpeed = Vector3.Dot(velocity, normal);
                velocity = tangent + normal * (grounded ? Mathf.Min(0f, normalSpeed) : normalSpeed);
                if (grounded && (tangent - target).sqrMagnitude < 0.0001f)
                    _slideMomentum = false;
            }
            else if (grounded && velocity.y <= 0.1f)
            {
                // Follow the slope so the body neither digs into it nor bounces off it.
                Vector3 onSlope = Vector3.ProjectOnPlane(wanted, groundNormal);
                velocity = onSlope.sqrMagnitude > 0f ? onSlope.normalized * wanted.magnitude : Vector3.zero;
                if (wanted.sqrMagnitude < 0.0001f)
                    velocity.y = Mathf.Min(0f, _rb.linearVelocity.y);
            }
            else
                velocity = new Vector3(wanted.x, velocity.y, wanted.z);

            if (_lastMoveDirection.sqrMagnitude > 0.01f)
                _rb.MoveRotation(Quaternion.LookRotation(_lastMoveDirection));

            bool jumpBuffered = _jumpRequested && Time.time - _jumpRequestedAt <= JumpBufferDuration;
            if (!steepSlope && jumpBuffered && grounded)
            {
                _jumpRequested = false;
                if (_anim != null) _anim.SetTrigger("Jump");
                velocity.y = jumpForce;
            }
            else if (_jumpRequested && (!jumpBuffered || steepSlope))
                _jumpRequested = false;

            _rb.linearVelocity = velocity;
        }

        private readonly RaycastHit[] _groundHits = new RaycastHit[64];

        private Vector3 CalculateSlideVelocity(Vector3 velocity, Vector3 wanted, Vector3 normal, float dt)
        {
            Vector3 gravityAlongSlope = Vector3.ProjectOnPlane(Physics.gravity, normal);
            if (gravityAlongSlope.sqrMagnitude < 0.000001f)
                return Vector3.ProjectOnPlane(velocity, normal);

            Vector3 downhill = gravityAlongSlope.normalized;
            Vector3 lateral = Vector3.Cross(normal, downhill).normalized;
            float downhillSpeed = Vector3.Dot(velocity, downhill);
            downhillSpeed *= Mathf.Exp(-slideFriction * dt);
            downhillSpeed += gravityAlongSlope.magnitude * slideGravityMultiplier * dt;
            float lateralSpeed = Vector3.Dot(velocity, lateral);
            float lateralTarget = Vector3.Dot(wanted, lateral);
            lateralSpeed = Mathf.MoveTowards(lateralSpeed, lateralTarget, slideControlAcceleration * dt);
            return Vector3.ClampMagnitude(downhill * downhillSpeed + lateral * lateralSpeed, maxSlideSpeed);
        }

        internal bool CheckGround(out Vector3 normal)
        {
            normal = Vector3.up;
            if (_col == null || !_col.enabled)
                return false;
            Bounds bounds = _col.bounds;
            float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f;
            Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + radius + 0.05f, bounds.center.z);
            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, _groundHits,
                0.05f + GroundCheckMargin + 0.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _groundHits[i];
                if (hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform) ||
                    hit.normal.y < MinGroundNormalY ||
                    hit.distance >= best)
                    continue;
                best = hit.distance;
                normal = hit.normal;
                found = true;
            }
            return found;
        }

        public void SetInteractionFrozen(bool value) => _interactionFrozen = value;
    }
}
