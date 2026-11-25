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

        [Header("Ground")]
        [SerializeField] private float groundCheckDistance = 0.35f;
        [SerializeField, Range(1f, 30f)] private float slopeFittingSpeed = 8f;
        [SerializeField, Range(0f, 90f)] private float maxSlopeTilt = 25f;
        [SerializeField] private float jumpReferenceSpeed = 5f;

        private sealed class Bone
        {
            public Transform T;
            public Quaternion RestRotation;
            public Vector3 RestPosition;
            public int Depth;
        }

        private struct Pose
        {
            public float LUp, RUp, LKnee, RKnee;
            public float LArm, RArm, LElbow, RElbow, ArmSpread;
            public float Lean, Twist, HeadPitch, HipsY;

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
                    HeadPitch = Mathf.Lerp(a.HeadPitch, b.HeadPitch, t), HipsY = Mathf.Lerp(a.HipsY, b.HipsY, t)
                };
            }
        }

        private Player _player;
        private Transform _model;
        private Quaternion _modelRestRotation = Quaternion.identity;
        private readonly List<Bone> _bones = new();
        private Bone _hips, _spine, _chest, _head;
        private Bone _lUpLeg, _rUpLeg, _lLowLeg, _rLowLeg, _lFoot, _rFoot;
        private Bone _lArm, _rArm, _lForeArm, _rForeArm;

        private float _speed;
        private float _verticalSpeed;
        private float _phase;
        private float _moveBlend, _runBlend, _airBlend, _seatBlend;
        private float _landing;
        private bool _wasGrounded = true;
        private bool _grounded = true;
        private Behaviour _groundRotator;
        private float _interactTime = -1f;

        private const float InteractDuration = 0.7f;

        public bool HasModel => _model != null;

        private void Awake()
        {
            _player = GetComponent<Player>();
            Build();
            _samplePosition = transform.position;
            _sampleTime = Time.time;
        }

        private void Build()
        {
            _model = FindEmbeddedModel();
            if (_model != null)
            {
                _model.localScale = Vector3.one * modelScale;
                _modelRestRotation = _model.localRotation;
                CacheBones();
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
            _head = Get("Head");
            _lUpLeg = Get("UpperLeg.L");
            _rUpLeg = Get("UpperLeg.R");
            _lLowLeg = Get("LowerLeg.L");
            _rLowLeg = Get("LowerLeg.R");
            _lFoot = Get("Foot.L");
            _rFoot = Get("Foot.R");
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

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Track(dt);
            UpdateBlends(dt);

            ResetBones();
            UpdateGroundRotator();

            Pose pose = Pose.Lerp(LocomotionPose(), AirPose(), _airBlend);
            pose = Pose.Lerp(pose, SeatedPose(), _seatBlend);
            ApplyInteract(ref pose, dt);
            ApplyLanding(ref pose);
            Apply(pose);
        }

        private const float SampleWindow = 0.1f;
        private const float AirGrace = 0.08f;

        private Vector3 _samplePosition;
        private float _sampleTime;
        private Vector3 _rawVelocity;
        private float _airTime;

        private void Track(float dt)
        {
            bool seated = _player != null && _player.IsSeated;
            Vector3 position = transform.position;

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

            _wasGrounded = _grounded;
            Vector3 origin = position + Vector3.up * 0.1f;
            bool hit = Physics.Raycast(origin, Vector3.down, out RaycastHit info, 0.1f + groundCheckDistance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool rising = _verticalSpeed > 2.5f;
            _airTime = hit && !rising ? 0f : _airTime + dt;
            _grounded = _airTime < AirGrace;

            if (_grounded && !_wasGrounded)
                _landing = Mathf.Clamp01(-_prevFallSpeed / 9f);
            if (!_grounded)
                _prevFallSpeed = _verticalSpeed;
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
            _landing = Mathf.MoveTowards(_landing, 0f, dt * 2.5f);

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
            _groundRotator.enabled = !seated;
            if (seated)
                _model.localRotation = _modelRestRotation;
        }

        private Pose LocomotionPose()
        {
            float s = Mathf.Sin(_phase);
            float c = Mathf.Cos(_phase);
            float swing = Mathf.Lerp(legSwing, runLegSwing, _runBlend) * _moveBlend;
            float arms = armSwing * (1f + _runBlend * 0.8f) * _moveBlend;
            float knee = (12f + 50f * _runBlend) * _moveBlend;
            float t = Time.time;
            float breath = Mathf.Sin(t * 1.7f) * (1f - _moveBlend);

            return new Pose
            {
                LUp = swing * s,
                RUp = -swing * s,
                LKnee = knee * Mathf.Max(0f, c) + 4f * _moveBlend,
                RKnee = knee * Mathf.Max(0f, -c) + 4f * _moveBlend,
                LArm = -arms * s,
                RArm = arms * s,
                LElbow = (10f + 40f * _runBlend) * _moveBlend,
                RElbow = (10f + 40f * _runBlend) * _moveBlend,
                ArmSpread = 2f * breath,
                Lean = (3f + 12f * _runBlend) * _moveBlend + 1.2f * breath,
                Twist = 6f * s * _moveBlend,
                HeadPitch = -1.5f * breath,
                HipsY = hipBob * Mathf.Cos(_phase * 2f) * _moveBlend + 0.006f * breath
            };
        }

        private Pose AirPose()
        {
            float rising = Mathf.Clamp01(_verticalSpeed / jumpReferenceSpeed);
            float falling = Mathf.Clamp01(-_verticalSpeed / jumpReferenceSpeed);
            return new Pose
            {
                LUp = Mathf.Lerp(20f, 45f, rising),
                RUp = Mathf.Lerp(-5f, 15f, rising),
                LKnee = Mathf.Lerp(30f, 70f, rising),
                RKnee = Mathf.Lerp(10f, 40f, rising),
                LArm = Mathf.Lerp(30f, 100f, rising) + 40f * falling,
                RArm = Mathf.Lerp(30f, 100f, rising) + 40f * falling,
                LElbow = 25f,
                RElbow = 25f,
                ArmSpread = 15f + 35f * falling,
                Lean = Mathf.Lerp(4f, 8f, falling) + 6f * rising,
                Twist = 0f,
                HeadPitch = -4f * falling,
                HipsY = 0f
            };
        }

        private static Pose SeatedPose()
        {
            return new Pose
            {
                LUp = 88f, RUp = 88f, LKnee = 88f, RKnee = 88f,
                LArm = 50f, RArm = 50f, LElbow = 55f, RElbow = 55f,
                ArmSpread = 0f, Lean = 0f, Twist = 0f, HeadPitch = 0f, HipsY = 0f
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
            pose.HipsY -= 0.14f * dip;
            pose.LKnee += 40f * dip;
            pose.RKnee += 40f * dip;
            pose.LUp += 30f * dip;
            pose.RUp += 30f * dip;
            pose.Lean += 10f * dip;
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
            Rotate(_chest, Backward(p.Lean * 0.4f) * Quaternion.AngleAxis(-p.Twist * 0.5f, up));
            Rotate(_head, Backward(p.HeadPitch - p.Lean * 0.5f));

            Rotate(_lUpLeg, Forward(p.LUp));
            Rotate(_rUpLeg, Forward(p.RUp));
            Rotate(_lLowLeg, Backward(p.LKnee));
            Rotate(_rLowLeg, Backward(p.RKnee));
            // Keep feet roughly level with the ground while the legs swing.
            Rotate(_lFoot, Forward(p.LKnee * 0.4f - p.LUp * 0.3f));
            Rotate(_rFoot, Forward(p.RKnee * 0.4f - p.RUp * 0.3f));

            Rotate(_lArm, Forward(p.LArm) * Quaternion.AngleAxis(p.ArmSpread, fwd));
            Rotate(_rArm, Forward(p.RArm) * Quaternion.AngleAxis(-p.ArmSpread, fwd));
            Rotate(_lForeArm, Forward(p.LElbow));
            Rotate(_rForeArm, Forward(p.RElbow));
        }

        // Applies a model-space world rotation on top of the bone's (already animated) parent chain.
        private static void Rotate(Bone bone, Quaternion worldDelta)
        {
            if (bone != null)
                bone.T.rotation = worldDelta * bone.T.rotation;
        }
    }
}
