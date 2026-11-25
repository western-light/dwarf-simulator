using System.Collections.Generic;
using UnityEngine;

namespace DwarfSimulator.Player
{
    /// <summary>
    /// Replaces the player's visual with the gnome model and animates its bones procedurally
    /// (idle, walk/run, jump/fall/landing, slopes, sitting, interaction reach).
    /// State is derived from the transform movement, so it works identically for remote players.
    /// </summary>
    [DefaultExecutionOrder(100)] // after FGroundRotator (order 0) so it reads the aligned rotation
    public class GnomeAvatar : MonoBehaviour
    {
        private const string ResourcePath = "Player/gnome";

        private static readonly string[] OptionalPartPrefixes =
            { "Beard_", "Brows_", "Eyes_", "Hair_", "Hat_", "Nose_", "Shoes_", "Teeth_", "Mustache" };

        [Header("Model")]
        [SerializeField] private GameObject modelPrefab;
        [SerializeField] private float modelScale = 1f;
        [SerializeField] private float modelYaw;
        [SerializeField] private bool seatedPreview;
        [SerializeField] private string[] visibleParts =
            { "Beard_Short", "Brows_Bushy", "Eyes_Round", "Hat_Pointy", "Nose_Round", "Shoes_Round" };

        [Header("Locomotion")]
        [SerializeField] private float walkSpeed = 2.5f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float strideLength = 1.0f;
        [SerializeField] private float legSwing = 28f;
        [SerializeField] private float runLegSwing = 48f;
        [SerializeField] private float armSwing = 25f;
        [SerializeField] private float hipBob = 0.04f;
        [SerializeField, Range(0f, 0.2f)] private float footLift = 0.07f;
        [SerializeField, Range(0f, 8f)] private float upperBodySway = 2.5f;
        [SerializeField, Range(0f, 30f)] private float turnCounterRotation = 12f;

        [Header("Ground")]
        [SerializeField] private float groundCheckDistance = 0.35f;
        [SerializeField, Range(1f, 30f)] private float slopeFittingSpeed = 8f;
        [SerializeField, Range(0f, 90f)] private float maxSlopeTilt = 25f;
        [SerializeField, Min(0.1f)] private float jumpReferenceSpeed = 5f;

        [Header("Ragdoll")]
        [SerializeField, Range(20f, 85f)] private float ragdollSlopeAngle = 45f;
        [SerializeField, Min(0f)] private float ragdollRecoveryDelay = 0.8f;
        [SerializeField, Min(0f)] private float ragdollRecoveryBlendDuration = 0.6f;

        private sealed class Bone
        {
            public Transform T;
            public Quaternion RestRotation;
            public Vector3 RestPosition;
            public int Depth;
        }

        private sealed class RagdollPart
        {
            public Bone Bone;
            public Rigidbody Body;
            public Collider Collider;
            public CharacterJoint Joint;
        }

        private struct Pose
        {
            public float LUp, RUp, LKnee, RKnee;
            public float LArm, RArm, LElbow, RElbow, ArmSpread;
            public float Lean, Twist, HeadPitch, HipsY;
            public float ChestRoll, NeckYaw, HeadRoll, ShoulderRoll, LToe, RToe, LFootLift, RFootLift;

            public static Pose Lerp(in Pose a, in Pose b, float t)
            {
                return new Pose
                {
                    LUp = Mathf.Lerp(a.LUp, b.LUp, t), RUp = Mathf.Lerp(a.RUp, b.RUp, t),
                    LKnee = Mathf.Lerp(a.LKnee, b.LKnee, t), RKnee = Mathf.Lerp(a.RKnee, b.RKnee, t),
                    LArm = Mathf.Lerp(a.LArm, b.LArm, t), RArm = Mathf.Lerp(a.RArm, b.RArm, t),
                    LElbow = Mathf.Lerp(a.LElbow, b.LElbow, t), RElbow = Mathf.Lerp(a.RElbow, b.RElbow, t),
                    ArmSpread = Mathf.Lerp(a.ArmSpread, b.ArmSpread, t),
                    Lean = Mathf.Lerp(a.Lean, b.Lean, t), Twist = Mathf.Lerp(a.Twist, b.Twist, t),
                    HeadPitch = Mathf.Lerp(a.HeadPitch, b.HeadPitch, t), HipsY = Mathf.Lerp(a.HipsY, b.HipsY, t),
                    ChestRoll = Mathf.Lerp(a.ChestRoll, b.ChestRoll, t),
                    NeckYaw = Mathf.Lerp(a.NeckYaw, b.NeckYaw, t),
                    HeadRoll = Mathf.Lerp(a.HeadRoll, b.HeadRoll, t),
                    ShoulderRoll = Mathf.Lerp(a.ShoulderRoll, b.ShoulderRoll, t),
                    LToe = Mathf.Lerp(a.LToe, b.LToe, t), RToe = Mathf.Lerp(a.RToe, b.RToe, t),
                    LFootLift = Mathf.Lerp(a.LFootLift, b.LFootLift, t),
                    RFootLift = Mathf.Lerp(a.RFootLift, b.RFootLift, t)
                };
            }
        }

        private Player _player;
        private PlayerMouvement _movement;
        private Rigidbody _rootBody;
        private Transform _model;
        private Quaternion _modelRestRotation = Quaternion.identity;
        private readonly List<Bone> _bones = new();
        private readonly List<RagdollPart> _ragdollParts = new();
        private readonly RaycastHit[] _avatarGroundHits = new RaycastHit[64];
        private readonly List<Collider> _rootColliders = new();
        private Bone _hips, _spine, _chest, _neck, _head;
        private Bone _lUpLeg, _rUpLeg, _lLowLeg, _rLowLeg, _lFoot, _rFoot;
        private Bone _lShoulder, _rShoulder, _lArm, _rArm, _lForeArm, _rForeArm, _lToe, _rToe;

        private float _speed;
        private float _verticalSpeed;
        private float _yawVelocity;
        private float _previousYaw;
        private float _slideLateral;
        private float _slidePhase;
        private float _phase;
        private float _moveBlend, _runBlend, _airBlend, _seatBlend, _slideBlend;
        private float _landing;
        private bool _wasGrounded = true;
        private bool _grounded = true;
        private bool _ragdolled;
        private Behaviour _groundRotator;
        private float _interactTime = -1f;
        private float _ragdollSafeTime;
        private float _airborneTime;
        private float _takeoff;
        private float _recoveryElapsed;
        private bool _recovering;
        private readonly List<Vector3> _recoveryLocalPositions = new();
        private readonly List<Quaternion> _recoveryLocalRotations = new();
        private readonly List<Vector3> _recoveryWorldPositions = new();
        private readonly List<Quaternion> _recoveryWorldRotations = new();
        private readonly List<Vector3> _targetWorldPositions = new();
        private readonly List<Quaternion> _targetWorldRotations = new();

        private const float InteractDuration = 0.7f;

        public bool HasModel => _model != null;
        public bool IsRagdolled => _ragdolled;
        public float RagdollSlopeAngle => ragdollSlopeAngle;

        private void Awake()
        {
            if (seatedPreview)
            {
                _model = FindEmbeddedModel();
                if (_model != null)
                {
                    _modelRestRotation = _model.localRotation;
                    CacheBones();
                    DisableAnimators();
                    Apply(SeatedPose());
                }
                return;
            }

            _player = GetComponent<Player>();
            _movement = GetComponent<PlayerMouvement>();
            _rootBody = GetComponent<Rigidbody>();
            Build();
            _samplePosition = transform.position;
            _sampleTime = Time.time;
            _previousYaw = transform.eulerAngles.y;
        }

        private void Build()
        {
            _model = FindEmbeddedModel();
            if (_model != null)
            {
                _model.localScale = Vector3.one * modelScale;
                _modelRestRotation = _model.localRotation;
                CacheBones();
                BuildRagdoll();
                DisableAnimators();
                EnsureGroundRotator();
                return;
            }

            GameObject prefab = modelPrefab != null ? modelPrefab : Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"Gnome model not found at Resources/{ResourcePath}", this);
                return;
            }

            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (r is SkinnedMeshRenderer || r is MeshRenderer)
                    r.enabled = false;
            Animator legacy = GetComponent<Animator>();
            if (legacy != null)
                legacy.enabled = false;

            _model = Instantiate(prefab, transform).transform;
            _model.name = "GnomeVisual";
            _model.localPosition = Vector3.zero;
            _modelRestRotation = Quaternion.Euler(0f, modelYaw, 0f) * _model.localRotation;
            _model.localRotation = _modelRestRotation;
            _model.localScale = Vector3.one * modelScale;

            ApplyParts();
            CacheBones();
            BuildRagdoll();
            DisableAnimators();
            EnsureGroundRotator();
        }

        // A gnome placed directly in the player prefab (child containing the "Hips" bone).
        private Transform FindEmbeddedModel()
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Hips")
                    continue;
                Transform root = t;
                while (root.parent != null && root.parent != transform)
                    root = root.parent;
                return root.parent == transform ? root : null;
            }
            return null;
        }

        private void DisableAnimators()
        {
            foreach (Animator a in GetComponentsInChildren<Animator>(true))
                a.enabled = false;
        }

        private void ApplyParts()
        {
            foreach (Renderer r in _model.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                bool optional = false;
                foreach (string prefix in OptionalPartPrefixes)
                    if (n.StartsWith(prefix, System.StringComparison.Ordinal))
                        optional = true;
                if (optional)
                    r.gameObject.SetActive(System.Array.IndexOf(visibleParts, n) >= 0);
            }
        }

        private void CacheBones()
        {
            var byName = new Dictionary<string, Transform>();
            foreach (Transform t in _model.GetComponentsInChildren<Transform>(true))
                byName.TryAdd(t.name, t);

            Bone Get(string boneName)
            {
                if (!byName.TryGetValue(boneName, out Transform t))
                {
                    Debug.LogWarning($"Gnome bone '{boneName}' not found", this);
                    return null;
                }
                int depth = 0;
                for (Transform p = t; p != _model; p = p.parent)
                    depth++;
                var bone = new Bone { T = t, RestRotation = t.localRotation, RestPosition = t.localPosition, Depth = depth };
                _bones.Add(bone);
                return bone;
            }

            _hips = Get("Hips");
            _spine = Get("Spine");
            _chest = Get("Chest");
            _neck = Get("Neck");
            _head = Get("Head");
            _lUpLeg = Get("UpperLeg.L");
            _rUpLeg = Get("UpperLeg.R");
            _lLowLeg = Get("LowerLeg.L");
            _rLowLeg = Get("LowerLeg.R");
            _lFoot = Get("Foot.L");
            _rFoot = Get("Foot.R");
            _lToe = Get("Toe.L");
            _rToe = Get("Toe.R");
            _lShoulder = Get("Shoulder.L");
            _rShoulder = Get("Shoulder.R");
            _lArm = Get("UpperArm.L");
            _rArm = Get("UpperArm.R");
            _lForeArm = Get("LowerArm.L");
            _rForeArm = Get("LowerArm.R");
            _bones.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        }

        public void PlayInteract() => _interactTime = 0f;

        private void LateUpdate()
        {
            if (_model == null || _hips == null)
                return;

            if (seatedPreview)
            {
                ResetBones();
                Pose seatedPose = SeatedPose();
                seatedPose.ChestRoll = 0.6f * Mathf.Sin(Time.time * 1.7f);
                seatedPose.HeadPitch = 0.8f * Mathf.Sin(Time.time * 0.9f);
                Apply(seatedPose);
                return;
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Track(dt);
            if (_ragdolled)
                return;
            UpdateBlends(dt);

            if (_recovering)
                SnapshotRecoveryWorldPose();
            ResetBones();
            UpdateGroundRotator();

            Pose pose = Pose.Lerp(LocomotionPose(), SlidingPose(), _slideBlend);
            pose = Pose.Lerp(pose, AirPose(), _airBlend);
            pose = Pose.Lerp(pose, SeatedPose(), _seatBlend);
            ApplyInteract(ref pose, dt);
            ApplyLanding(ref pose);
            Apply(pose);
            ApplyRecovery(dt);
        }

        private const float SampleWindow = 0.05f;
        private const float AirGrace = 0.08f;

        private Vector3 _samplePosition;
        private float _sampleTime;
        private Vector3 _rawVelocity;
        private float _airTime;

        private void Track(float dt)
        {
            bool seated = _player != null && _player.IsSeated;
            Vector3 position = transform.position;
            float yaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(_previousYaw, yaw) / dt;
            _previousYaw = yaw;
            _yawVelocity = Mathf.Lerp(_yawVelocity, yawRate, 1f - Mathf.Exp(-8f * dt));

            // Physics-driven movement is stepped, so velocity is measured over a window, not per frame.
            float elapsed = Time.time - _sampleTime;
            if (elapsed >= SampleWindow)
            {
                Vector3 delta = position - _samplePosition;
                _rawVelocity = delta.sqrMagnitude > 9f ? Vector3.zero : delta / elapsed;
                _samplePosition = position;
                _sampleTime = Time.time;
            }
            if (seated)
                _rawVelocity = Vector3.zero;

            float horizontal = new Vector2(_rawVelocity.x, _rawVelocity.z).magnitude;
            _speed = Mathf.Lerp(_speed, horizontal, 1f - Mathf.Exp(-8f * dt));
            _verticalSpeed = Mathf.Lerp(_verticalSpeed, _rawVelocity.y, 1f - Mathf.Exp(-12f * dt));
            Vector3 localVelocity = transform.InverseTransformDirection(
                new Vector3(_rawVelocity.x, 0f, _rawVelocity.z));
            float lateralSlide = Mathf.Clamp(localVelocity.x / Mathf.Max(runSpeed, 0.1f), -1f, 1f);
            _slideLateral = Mathf.Lerp(_slideLateral, lateralSlide, 1f - Mathf.Exp(-5f * dt));

            _wasGrounded = _grounded;
            Vector3 origin = position + Vector3.up * 0.1f;
            bool hit = false;
            float closest = float.PositiveInfinity;
            int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _avatarGroundHits,
                0.1f + groundCheckDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidate = _avatarGroundHits[i];
                if (candidate.collider.transform == transform || candidate.collider.transform.IsChildOf(transform) ||
                    candidate.distance >= closest)
                    continue;
                closest = candidate.distance;
                hit = true;
            }
            bool rising = _verticalSpeed > 2.5f;
            _airTime = hit && !rising ? 0f : _airTime + dt;
            _grounded = _airTime < AirGrace;

            if (_grounded && !_wasGrounded)
            {
                _landing = Mathf.Clamp01(-_prevFallSpeed / 9f);
                _airborneTime = 0f;
            }
            else if (!_grounded)
            {
                _airborneTime += dt;
                if (_wasGrounded)
                    _takeoff = 1f;
            }
            if (!_grounded)
                _prevFallSpeed = _verticalSpeed;

            Vector3 recoveryNormal = Vector3.up;
            bool onGround = _movement != null && _movement.CheckGround(out recoveryNormal);
            float walkableAngle = _movement != null ? _movement.MaxWalkableSlopeAngle : ragdollSlopeAngle;
            bool steep = !seated && onGround && Vector3.Angle(recoveryNormal, Vector3.up) >= ragdollSlopeAngle;
            if (seated && _ragdolled)
                SetRagdoll(false);
            else if (steep)
            {
                _ragdollSafeTime = 0f;
                SetRagdoll(true);
            }
            else if (_ragdolled && onGround &&
                     Vector3.Angle(recoveryNormal, Vector3.up) <= walkableAngle)
            {
                _ragdollSafeTime += dt;
                if (_ragdollSafeTime >= ragdollRecoveryDelay)
                    SetRagdoll(false);
            }
            else if (_ragdolled)
                _ragdollSafeTime = 0f;
        }

        private float _prevFallSpeed;

        private void UpdateBlends(float dt)
        {
            bool seated = _player != null && _player.IsSeated;
            float k = 1f - Mathf.Exp(-12f * dt);
            _moveBlend = Mathf.Lerp(_moveBlend, Mathf.InverseLerp(0.15f, walkSpeed, _speed), k);
            _runBlend = Mathf.Lerp(_runBlend, Mathf.InverseLerp(walkSpeed, runSpeed, _speed), k);
            _airBlend = Mathf.Lerp(_airBlend, _grounded || seated ? 0f : 1f, 1f - Mathf.Exp(-(_grounded ? 14f : 20f) * dt));
            _seatBlend = Mathf.Lerp(_seatBlend, seated ? 1f : 0f, 1f - Mathf.Exp(-10f * dt));
            bool sliding = !seated && _movement != null && _movement.CheckGround(out Vector3 groundNormal) &&
                           Vector3.Angle(groundNormal, Vector3.up) > _movement.MaxWalkableSlopeAngle;
            _slideBlend = Mathf.Lerp(_slideBlend, sliding ? 1f : 0f, 1f - Mathf.Exp(-7f * dt));
            if (sliding)
                _slidePhase = Mathf.Repeat(_slidePhase + dt * Mathf.Lerp(2f, 7f,
                    Mathf.Clamp01(_speed / Mathf.Max(0.1f, runSpeed))), Mathf.PI * 2f);
            _landing = Mathf.MoveTowards(_landing, 0f, dt * 2.5f);
            _takeoff = Mathf.MoveTowards(_takeoff, 0f, dt * 3f);

            float cycleRate = _speed / Mathf.Max(0.1f, strideLength * 2f);
            _phase = Mathf.Repeat(_phase + cycleRate * dt * Mathf.PI * 2f, Mathf.PI * 2f);

            if (_interactTime >= 0f)
            {
                _interactTime += dt;
                if (_interactTime > InteractDuration)
                    _interactTime = -1f;
            }
        }

        private void ResetBones()
        {
            foreach (Bone b in _bones)
            {
                b.T.localRotation = b.RestRotation;
                b.T.localPosition = b.RestPosition;
            }
        }

        private const string GroundRotatorType = "FIMSpace.GroundFitter.FGroundRotator, Assembly-CSharp";

        // FGroundRotator lives in Assembly-CSharp (no asmdef), so it is configured by reflection.
        private void EnsureGroundRotator()
        {
            foreach (Behaviour b in _model.GetComponents<Behaviour>())
                if (b != null && b.GetType().Name == "FGroundRotator")
                {
                    _groundRotator = b;
                    return;
                }

            System.Type type = System.Type.GetType(GroundRotatorType);
            if (type == null)
            {
                Debug.LogWarning("FGroundRotator not found, slope alignment disabled", this);
                return;
            }

            _model.gameObject.SetActive(false);
            _groundRotator = (Behaviour)_model.gameObject.AddComponent(type);
            Set("RootTransform", transform);
            Set("TransformToRotate", _model);
            Set("GroundLayerMask", (LayerMask)Physics.DefaultRaycastLayers);
            Set("FittingSpeed", slopeFittingSpeed);
            Set("MaxForwardRotation", maxSlopeTilt);
            Set("MaxHorizontalRotation", maxSlopeTilt);
            Set("RelativeLookUp", false);
            Set("GlueToGround", false);
            _model.gameObject.SetActive(true);
        }

        private void Set(string field, object value) =>
            _groundRotator.GetType().GetField(field)?.SetValue(_groundRotator, value);

        private void UpdateGroundRotator()
        {
            if (_groundRotator == null)
                return;
            bool seated = _seatBlend > 0.5f;
            _groundRotator.enabled = !seated && !_recovering;
            if (seated)
                _model.localRotation = _modelRestRotation;
        }

        private void SnapshotRecoveryWorldPose()
        {
            _recoveryWorldPositions.Clear();
            _recoveryWorldRotations.Clear();
            for (int i = 0; i < _bones.Count; i++)
            {
                Transform bone = _bones[i].T;
                Transform parent = bone.parent;
                _recoveryWorldPositions.Add(parent.TransformPoint(_recoveryLocalPositions[i]));
                _recoveryWorldRotations.Add(parent.rotation * _recoveryLocalRotations[i]);
                bone.SetPositionAndRotation(_recoveryWorldPositions[i], _recoveryWorldRotations[i]);
            }
        }

        private void ApplyRecovery(float dt)
        {
            if (!_recovering)
                return;

            bool seated = _player != null && _player.IsSeated;
            if (seated)
            {
                _recovering = false;
                return;
            }

            float t = ragdollRecoveryBlendDuration > 0f
                ? Mathf.Clamp01((_recoveryElapsed + dt) / ragdollRecoveryBlendDuration)
                : 1f;
            float blend = Mathf.SmoothStep(0f, 1f, t);
            _targetWorldPositions.Clear();
            _targetWorldRotations.Clear();
            foreach (Bone bone in _bones)
            {
                _targetWorldPositions.Add(bone.T.position);
                _targetWorldRotations.Add(bone.T.rotation);
            }
            for (int i = 0; i < _bones.Count; i++)
            {
                Bone bone = _bones[i];
                bone.T.SetPositionAndRotation(
                    Vector3.Lerp(_recoveryWorldPositions[i], _targetWorldPositions[i], blend),
                    Quaternion.Slerp(_recoveryWorldRotations[i], _targetWorldRotations[i], blend));
            }
            _recoveryLocalPositions.Clear();
            _recoveryLocalRotations.Clear();
            foreach (Bone bone in _bones)
            {
                _recoveryLocalPositions.Add(bone.T.localPosition);
                _recoveryLocalRotations.Add(bone.T.localRotation);
            }
            _recoveryElapsed += dt;
            if (t >= 1f)
                _recovering = false;
        }

        private void BuildRagdoll()
        {
            _rootColliders.Clear();
            foreach (Collider rootCollider in GetComponents<Collider>())
                _rootColliders.Add(rootCollider);

            AddRagdollPart(_hips, null, _spine, 0.13f, 4f);
            AddRagdollPart(_spine, _hips, _chest, 0.11f, 2f);
            AddRagdollPart(_chest, _spine, _head, 0.15f, 3f);
            AddRagdollPart(_head, _chest, null, 0.11f, 1f);
            AddRagdollPart(_lUpLeg, _hips, _lLowLeg, 0.09f, 1.5f);
            AddRagdollPart(_rUpLeg, _hips, _rLowLeg, 0.09f, 1.5f);
            AddRagdollPart(_lLowLeg, _lUpLeg, _lFoot, 0.07f, 1f);
            AddRagdollPart(_rLowLeg, _rUpLeg, _rFoot, 0.07f, 1f);
            AddRagdollPartToTransform(_lFoot, _lLowLeg, FindBoneTransform("Toe.L"), 0.07f, 0.5f);
            AddRagdollPartToTransform(_rFoot, _rLowLeg, FindBoneTransform("Toe.R"), 0.07f, 0.5f);
            AddRagdollPart(_lArm, _chest, _lForeArm, 0.065f, 0.8f);
            AddRagdollPart(_rArm, _chest, _rForeArm, 0.065f, 0.8f);
            AddRagdollPartToTransform(_lForeArm, _lArm, FindBoneTransform("Hand.L"), 0.065f, 0.5f);
            AddRagdollPartToTransform(_rForeArm, _rArm, FindBoneTransform("Hand.R"), 0.065f, 0.5f);

            RagdollPart hipsPart = _ragdollParts.Find(part => part.Bone == _hips);
            if (hipsPart != null && _rootBody != null)
            {
                CharacterJoint rootJoint = _hips.T.gameObject.AddComponent<CharacterJoint>();
                rootJoint.anchor = Vector3.zero;
                rootJoint.axis = Vector3.right;
                rootJoint.swingAxis = Vector3.up;
                rootJoint.lowTwistLimit = new SoftJointLimit { limit = -35f };
                rootJoint.highTwistLimit = new SoftJointLimit { limit = 35f };
                rootJoint.swing1Limit = new SoftJointLimit { limit = 45f };
                rootJoint.swing2Limit = new SoftJointLimit { limit = 35f };
                rootJoint.autoConfigureConnectedAnchor = false;
                rootJoint.enableProjection = true;
                hipsPart.Joint = rootJoint;
            }

            foreach (RagdollPart part in _ragdollParts)
            {
                part.Body.isKinematic = true;
                foreach (Collider rootCollider in _rootColliders)
                    Physics.IgnoreCollision(part.Collider, rootCollider, true);
                part.Collider.enabled = false;
            }
        }

        private Transform FindBoneTransform(string name)
        {
            foreach (Transform child in _model.GetComponentsInChildren<Transform>(true))
                if (child.name == name)
                    return child;
            Debug.LogWarning($"Gnome bone '{name}' not found; using a spherical ragdoll collider.", this);
            return null;
        }

        private void AddRagdollPart(Bone bone, Bone parent, Bone child, float radius, float mass) =>
            AddRagdollPartToTransform(bone, parent, child != null ? child.T : null, radius, mass);

        private void AddRagdollPartToTransform(Bone bone, Bone parent, Transform child, float radius, float mass)
        {
            if (bone == null)
                return;

            Rigidbody body = bone.T.GetComponent<Rigidbody>();
            if (body == null)
                body = bone.T.gameObject.AddComponent<Rigidbody>();
            body.mass = mass;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.solverIterations = Mathf.Max(body.solverIterations, 12);
            body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, 4);

            float worldRadius = radius * Mathf.Max(Mathf.Abs(_model.lossyScale.x),
                Mathf.Abs(_model.lossyScale.y), Mathf.Abs(_model.lossyScale.z));
            Collider collider;
            if (child != null)
            {
                Vector3 segment = bone.T.InverseTransformPoint(child.position);
                float length = segment.magnitude;
                GameObject shape = new GameObject("RagdollCollider");
                shape.layer = bone.T.gameObject.layer;
                shape.transform.SetParent(bone.T, false);
                shape.transform.localPosition = segment * 0.5f;
                shape.transform.localRotation = Quaternion.FromToRotation(Vector3.up, segment.normalized);
                CapsuleCollider capsule = shape.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                Vector3 scale = shape.transform.lossyScale;
                float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 0.0001f);
                float heightScale = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
                capsule.radius = worldRadius / radiusScale;
                float worldLength = Vector3.Distance(bone.T.position, child.position);
                capsule.height = (worldLength + 2f * worldRadius) / heightScale;
                capsule.height = Mathf.Max(capsule.height, 2f * capsule.radius);
                collider = capsule;
            }
            else
            {
                GameObject shape = new GameObject("RagdollCollider");
                shape.layer = bone.T.gameObject.layer;
                shape.transform.SetParent(bone.T, false);
                SphereCollider sphere = shape.AddComponent<SphereCollider>();
                Vector3 scale = shape.transform.lossyScale;
                float sphereScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y),
                    Mathf.Abs(scale.z), 0.0001f);
                sphere.radius = worldRadius / sphereScale;
                collider = sphere;
            }

            CharacterJoint joint = null;
            if (parent != null)
            {
                Rigidbody parentBody = parent.T.GetComponent<Rigidbody>();
                if (parentBody != null)
                {
                    joint = bone.T.gameObject.AddComponent<CharacterJoint>();
                    joint.connectedBody = parentBody;
                    joint.anchor = Vector3.zero;
                    joint.autoConfigureConnectedAnchor = false;
                    joint.connectedAnchor = parent.T.InverseTransformPoint(bone.T.position);
                    joint.enableProjection = true;
                    joint.axis = Vector3.right;
                    joint.swingAxis = Vector3.up;
                    joint.lowTwistLimit = new SoftJointLimit { limit = -35f };
                    joint.highTwistLimit = new SoftJointLimit { limit = 35f };
                    joint.swing1Limit = new SoftJointLimit { limit = 45f };
                    joint.swing2Limit = new SoftJointLimit { limit = 35f };
                }
            }

            _ragdollParts.Add(new RagdollPart
            {
                Bone = bone,
                Body = body,
                Collider = collider,
                Joint = joint
            });
        }

        public void SetRagdoll(bool active)
        {
            if (_ragdolled == active || _ragdollParts.Count == 0)
                return;

            _ragdolled = active;
            _ragdollSafeTime = 0f;
            if (active)
            {
                _recovering = false;
                Vector3 velocity = _rootBody != null ? _rootBody.linearVelocity : _rawVelocity;
                foreach (RagdollPart part in _ragdollParts)
                {
                    part.Collider.enabled = true;
                    if (part.Bone == _hips && part.Joint != null)
                    {
                        part.Joint.connectedBody = _rootBody;
                        part.Joint.connectedAnchor =
                            _rootBody.transform.InverseTransformPoint(_hips.T.position);
                    }
                    part.Body.isKinematic = part.Bone == _hips && _rootBody == null;
                    if (!part.Body.isKinematic)
                    {
                        part.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                        part.Body.linearVelocity = velocity;
                    }
                }
                for (int i = 0; i < _ragdollParts.Count; i++)
                {
                    Collider collider = _ragdollParts[i].Collider;
                    foreach (Collider rootCollider in _rootColliders)
                        if (rootCollider.enabled)
                            Physics.IgnoreCollision(collider, rootCollider, true);
                    for (int j = 0; j < i; j++)
                        Physics.IgnoreCollision(collider, _ragdollParts[j].Collider, true);
                }
                if (_groundRotator != null)
                    _groundRotator.enabled = false;
                return;
            }

            _recoveryElapsed = 0f;
            _recovering = ragdollRecoveryBlendDuration > 0f && (_player == null || !_player.IsSeated);

            _recoveryLocalPositions.Clear();
            _recoveryLocalRotations.Clear();
            foreach (Bone bone in _bones)
            {
                _recoveryLocalPositions.Add(bone.T.localPosition);
                _recoveryLocalRotations.Add(bone.T.localRotation);
            }

            foreach (RagdollPart part in _ragdollParts)
            {
                part.Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                part.Body.isKinematic = true;
                if (part.Bone == _hips && part.Joint != null)
                    part.Joint.connectedBody = null;
                part.Collider.enabled = false;
            }
            if (!_recovering)
            {
                ResetBones();
                _model.localRotation = _modelRestRotation;
            }
        }

        private Pose LocomotionPose()
        {
            float s = Mathf.Sin(_phase);
            float c = Mathf.Cos(_phase);
            float swing = Mathf.Lerp(legSwing, runLegSwing, _runBlend) * _moveBlend;
            float arms = armSwing * (1f + _runBlend * 0.8f) * _moveBlend;
            float knee = (12f + 50f * _runBlend) * _moveBlend;
            float t = Time.time;
            float idle = 1f - _moveBlend;
            float breath = Mathf.Sin(t * 1.7f) * idle;
            float idleSway = Mathf.Sin(t * 0.83f + 0.6f) * idle;
            float turn = Mathf.Clamp(_yawVelocity / 240f, -1f, 1f);

            return new Pose
            {
                LUp = swing * s,
                RUp = -swing * s,
                LKnee = knee * Mathf.Max(0f, c) + 4f * _moveBlend + 3f * idle,
                RKnee = knee * Mathf.Max(0f, -c) + 4f * _moveBlend + 3f * idle,
                LArm = -arms * s,
                RArm = arms * s,
                LElbow = (10f + 40f * _runBlend) * _moveBlend + 4f * idle,
                RElbow = (10f + 40f * _runBlend) * _moveBlend + 4f * idle,
                ArmSpread = 2f * breath + 1.5f * idleSway,
                Lean = (3f + 12f * _runBlend) * _moveBlend + 1.2f * breath,
                Twist = 6f * s * _moveBlend + turn * turnCounterRotation,
                HeadPitch = -1.5f * breath + 0.5f * idleSway
                    + 2.5f * Mathf.Sin(_phase * 2f - 0.7f) * _moveBlend,
                HipsY = hipBob * Mathf.Cos(_phase * 2f) * _moveBlend + 0.006f * breath,
                ChestRoll = upperBodySway * Mathf.Sin(_phase * 2f) * _moveBlend + 1.5f * idleSway,
                NeckYaw = -turn * turnCounterRotation * 0.45f + 1.5f * idleSway,
                HeadRoll = -2f * Mathf.Sin(_phase * 2f) * _moveBlend + idleSway,
                ShoulderRoll = 3f * Mathf.Sin(_phase * 2f + Mathf.PI * 0.5f) * _moveBlend
                    + 1.5f * breath,
                LToe = 12f * Mathf.Max(0f, s) * _moveBlend,
                RToe = 12f * Mathf.Max(0f, -s) * _moveBlend,
                LFootLift = footLift * Mathf.Max(0f, s) * _moveBlend,
                RFootLift = footLift * Mathf.Max(0f, -s) * _moveBlend
            };
        }

        private Pose SlidingPose()
        {
            float balance = Mathf.Sin(_slidePhase) * Mathf.Clamp01(_speed / Mathf.Max(0.1f, runSpeed));
            float lateral = _slideLateral;
            float speed = Mathf.Clamp01(_speed / Mathf.Max(0.1f, runSpeed));
            return new Pose
            {
                LUp = 28f + 12f * balance + 8f * lateral,
                RUp = 42f - 10f * balance - 8f * lateral,
                LKnee = 52f + 14f * Mathf.Max(0f, balance),
                RKnee = 34f + 14f * Mathf.Max(0f, -balance),
                LArm = 35f + 22f * lateral + 10f * balance,
                RArm = 35f + 22f * lateral - 10f * balance,
                LElbow = 48f + 12f * Mathf.Max(0f, balance),
                RElbow = 48f + 12f * Mathf.Max(0f, -balance),
                ArmSpread = 48f + 12f * speed,
                Lean = -16f - 8f * speed,
                Twist = 12f * lateral + 7f * balance,
                HeadPitch = 12f + 7f * speed,
                HipsY = -0.045f,
                ChestRoll = -10f * lateral + 6f * balance,
                NeckYaw = -6f * lateral - 3f * balance,
                HeadRoll = 8f * lateral - 5f * balance,
                ShoulderRoll = 12f + 8f * balance,
                LToe = 14f,
                RToe = 14f,
                LFootLift = 0.025f * Mathf.Max(0f, balance),
                RFootLift = 0.025f * Mathf.Max(0f, -balance)
            };
        }

        private Pose AirPose()
        {
            float velocityPhase = Mathf.Clamp(_verticalSpeed / Mathf.Max(0.1f, jumpReferenceSpeed), -1f, 1f);
            float rising = Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, velocityPhase));
            float falling = Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, -velocityPhase));
            float apex = 1f - Mathf.Abs(velocityPhase);
            float airtime = Mathf.Clamp01(_airborneTime / 0.28f);
            float launch = Mathf.SmoothStep(0f, 1f, _takeoff);
            float flutter = Mathf.Sin(_airborneTime * 8f) * airtime;
            float tuck = Mathf.Max(apex, falling * 0.65f);
            return new Pose
            {
                LUp = 34f + 18f * launch + 18f * tuck + 10f * falling,
                RUp = 28f + 14f * launch + 24f * tuck + 16f * falling,
                LKnee = 48f + 28f * launch + 28f * tuck + 12f * falling,
                RKnee = 38f + 32f * launch + 34f * tuck + 18f * falling,
                LArm = 68f + 35f * launch + 26f * rising - 20f * falling + 8f * flutter,
                RArm = 78f + 30f * launch + 18f * rising - 12f * falling - 8f * flutter,
                LElbow = 36f + 20f * tuck + 14f * falling,
                RElbow = 42f + 18f * tuck + 18f * falling,
                ArmSpread = 22f + 20f * launch + 18f * apex + 28f * falling,
                Lean = 8f + 12f * launch + 6f * rising - 10f * falling,
                Twist = 8f * flutter + 5f * launch,
                HeadPitch = 8f + 10f * rising - 16f * falling,
                HipsY = 0.025f * tuck,
                ChestRoll = 6f * flutter,
                NeckYaw = -7f * flutter,
                HeadRoll = -8f * flutter + 4f * falling,
                ShoulderRoll = 12f + 12f * falling,
                LToe = 18f + 10f * tuck,
                RToe = 12f + 14f * tuck,
                LFootLift = 0.035f * (launch + tuck + falling),
                RFootLift = 0.045f * (launch + tuck + falling)
            };
        }

        private static Pose SeatedPose()
        {
            return new Pose
            {
                LUp = 88f, RUp = 88f, LKnee = 88f, RKnee = 88f,
                LArm = 50f, RArm = 50f, LElbow = 55f, RElbow = 55f,
                ArmSpread = 0f, Lean = 0f, Twist = 0f, HeadPitch = 0f, HipsY = 0f,
                ChestRoll = 0f, NeckYaw = 0f, HeadRoll = 0f, ShoulderRoll = 0f, LToe = 0f, RToe = 0f,
                LFootLift = 0f, RFootLift = 0f
            };
        }

        private void ApplyInteract(ref Pose pose, float dt)
        {
            if (_interactTime < 0f || _seatBlend > 0.5f)
                return;

            float bump = Mathf.Sin(Mathf.Clamp01(_interactTime / InteractDuration) * Mathf.PI);
            bump = Mathf.SmoothStep(0f, 1f, bump);
            pose.RArm = Mathf.Lerp(pose.RArm, 85f, bump);
            pose.RElbow = Mathf.Lerp(pose.RElbow, 25f, bump);
            pose.Lean += 8f * bump;
            pose.HeadPitch += 4f * bump;
        }

        private void ApplyLanding(ref Pose pose)
        {
            float dip = _landing * _landing * (3f - 2f * _landing);
            pose.HipsY -= 0.2f * dip;
            pose.LKnee += 52f * dip;
            pose.RKnee += 52f * dip;
            pose.LUp += 24f * dip;
            pose.RUp += 24f * dip;
            pose.LArm -= 18f * dip;
            pose.RArm -= 18f * dip;
            pose.Lean += 14f * dip;
            pose.HeadPitch += 7f * dip;
            pose.HeadRoll += 5f * dip;
            pose.ShoulderRoll += 12f * dip;
            pose.LToe += 10f * dip;
            pose.RToe += 10f * dip;
        }

        private void Apply(in Pose p)
        {
            Quaternion frame = _model.rotation * Quaternion.Inverse(_modelRestRotation);
            Vector3 right = frame * Vector3.right;
            Vector3 up = frame * Vector3.up;
            Vector3 fwd = frame * Vector3.forward;

            // Axis-agnostic rotations expressed in the model frame.
            // Limbs hanging down move forward with a negative rotation around "right".
            Quaternion Forward(float deg) => Quaternion.AngleAxis(-deg, right);
            Quaternion Backward(float deg) => Quaternion.AngleAxis(deg, right);

            Rotate(_hips, Quaternion.AngleAxis(p.Twist * 0.5f, up));
            if (_hips != null)
                _hips.T.position += up * p.HipsY * modelScale;

            Rotate(_spine, Backward(p.Lean * 0.6f) * Quaternion.AngleAxis(-p.Twist, up));
            Rotate(_chest, Backward(p.Lean * 0.4f) * Quaternion.AngleAxis(-p.Twist * 0.5f, up)
                * Quaternion.AngleAxis(p.ChestRoll, fwd));
            Rotate(_neck, Quaternion.AngleAxis(p.NeckYaw, up));
            Rotate(_head, Backward(p.HeadPitch - p.Lean * 0.5f)
                * Quaternion.AngleAxis(p.NeckYaw * 0.5f, up)
                * Quaternion.AngleAxis(p.HeadRoll, fwd));

            Rotate(_lUpLeg, Forward(p.LUp));
            Rotate(_rUpLeg, Forward(p.RUp));
            Rotate(_lLowLeg, Backward(p.LKnee));
            Rotate(_rLowLeg, Backward(p.RKnee));
            // Keep feet roughly level with the ground while the legs swing.
            Rotate(_lFoot, Forward(p.LKnee * 0.4f - p.LUp * 0.3f));
            Rotate(_rFoot, Forward(p.RKnee * 0.4f - p.RUp * 0.3f));

            Rotate(_lShoulder, Quaternion.AngleAxis(p.ShoulderRoll, fwd));
            Rotate(_rShoulder, Quaternion.AngleAxis(-p.ShoulderRoll, fwd));
            Rotate(_lArm, Forward(p.LArm) * Quaternion.AngleAxis(p.ArmSpread, fwd));
            Rotate(_rArm, Forward(p.RArm) * Quaternion.AngleAxis(-p.ArmSpread, fwd));
            Rotate(_lForeArm, Forward(p.LElbow));
            Rotate(_rForeArm, Forward(p.RElbow));
            Rotate(_lToe, Forward(p.LToe));
            Rotate(_rToe, Forward(p.RToe));
            if (_lFoot != null && _lFoot.T.parent != null)
                _lFoot.T.localPosition += _lFoot.T.parent.InverseTransformVector(up * p.LFootLift * modelScale);
            if (_rFoot != null && _rFoot.T.parent != null)
                _rFoot.T.localPosition += _rFoot.T.parent.InverseTransformVector(up * p.RFootLift * modelScale);
        }

        // Applies a model-space world rotation on top of the bone's (already animated) parent chain.
        private static void Rotate(Bone bone, Quaternion worldDelta)
        {
            if (bone != null)
                bone.T.rotation = worldDelta * bone.T.rotation;
        }
    }
}
