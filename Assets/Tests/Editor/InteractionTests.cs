using System.Reflection;
using DwarfSimulator.Car;
using DwarfSimulator.Player;
using DwarfSimulator.Player.Interactor;
using NUnit.Framework;
using PurrNet;
using UnityEditor;
using UnityEngine;

namespace DwarfSimulator.Tests
{
    public class InteractionAvailabilityStub : MonoBehaviour, IInteractionAvailability
    {
        public bool FirstUnavailable;
        public bool SecondUnavailable;

        public string UnavailableReason(int choiceIndex, DwarfSimulator.Player.Player player) =>
            (choiceIndex == 0 ? FirstUnavailable : SecondUnavailable) ? "unavailable" : null;
    }

    public class InteractionTests
    {
        private GameObject _playerObject;
        private GameObject _cameraObject;
        private GameObject _targetObject;
        private GameObject _obstacle;
        private PlayerInteract _interactor;
        private Camera _camera;
        private Interactable _target;

        [SetUp]
        public void SetUp()
        {
            _playerObject = new GameObject("player");
            _playerObject.SetActive(false);
            _interactor = _playerObject.AddComponent<PlayerInteract>();
            _cameraObject = new GameObject("camera");
            _camera = _cameraObject.AddComponent<Camera>();
            _camera.transform.position = new Vector3(0, 0.8f, 0);
            _targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _targetObject.transform.position = new Vector3(0, 0.8f, 4);
            _target = _targetObject.AddComponent<Interactable>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_obstacle);
            Object.DestroyImmediate(_targetObject);
            Object.DestroyImmediate(_cameraObject);
            Object.DestroyImmediate(_playerObject);
        }

        [Test]
        public void DefaultChoicePrefersAvailableDefaultAndFallsBack()
        {
            var serialized = new SerializedObject(_target);
            var choices = serialized.FindProperty("choices");
            choices.arraySize = 2;
            choices.GetArrayElementAtIndex(1).FindPropertyRelative("Default").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var availability = _targetObject.AddComponent<InteractionAvailabilityStub>();

            Assert.AreEqual(1, _target.DefaultChoice(null));
            availability.SecondUnavailable = true;
            Assert.AreEqual(0, _target.DefaultChoice(null));
            availability.FirstUnavailable = true;
            Assert.AreEqual(-1, _target.DefaultChoice(null));
        }

        [Test]
        public void CarHidesTargetFrameWhileOtherInteractablesShowItByDefault()
        {
            Assert.IsTrue(_target.ShowTargetFrame);
            var car = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Car/CICADA_PARENT.prefab");
            Assert.IsNotNull(car);
            Assert.IsFalse(car.GetComponent<Interactable>().ShowTargetFrame);
        }

        [Test]
        public void TargetingAllowsNearMissButRejectsDistanceAndObstacles()
        {
            _targetObject.transform.position = new Vector3(1.3f, 0.8f, 4);
            Physics.SyncTransforms();
            Assert.AreEqual(_target, FindTarget(out _));

            var serialized = new SerializedObject(_interactor);
            serialized.FindProperty("interactDistance").floatValue = 2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsNull(FindTarget(out string failure));
            Assert.AreEqual("Trop loin", failure);

            serialized.FindProperty("interactDistance").floatValue = 5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _targetObject.transform.position = new Vector3(0, 0.8f, 4);
            _obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _obstacle.transform.position = new Vector3(0, 0.8f, 2);
            Physics.SyncTransforms();
            Assert.IsNull(FindTarget(out failure));
            Assert.AreEqual("Interaction bloquee", failure);
        }

        private Interactable FindTarget(out string failure)
        {
            MethodInfo method = typeof(PlayerInteract).GetMethod("FindTarget",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object[] arguments = { _camera, null, null };
            var result = (Interactable)method.Invoke(_interactor, arguments);
            failure = (string)arguments[2];
            return result;
        }
    }

    public class CarInteractionDistanceTests
    {
        private GameObject _car;
        private CarPlace _place;
        private MethodInfo _distanceMethod;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Car/CICADA_PARENT.prefab");
            Assert.IsNotNull(prefab);
            _car = Object.Instantiate(prefab);
            _place = _car.GetComponent<CarPlace>();
            _distanceMethod = typeof(CarPlace).GetMethod("TryGetInteractionDistance",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(_distanceMethod);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_car);
        }

        [Test]
        public void ServerDistanceSupportsRootAndChildColliders()
        {
            Collider rootCollider = _car.GetComponent<Collider>();
            Assert.IsNotNull(rootCollider);
            Physics.SyncTransforms();
            Vector3 origin = rootCollider.bounds.center + Vector3.right * (rootCollider.bounds.extents.x + 1f);
            Assert.IsTrue(TryDistance(origin, out float distance));
            Assert.AreEqual(Vector3.Distance(origin, rootCollider.ClosestPoint(origin)), distance, 0.001f);

            Object.DestroyImmediate(rootCollider);
            var child = new GameObject("Scene car collider");
            child.transform.SetParent(_car.transform, false);
            var childCollider = child.AddComponent<BoxCollider>();
            childCollider.size = new Vector3(2f, 2f, 2f);
            Physics.SyncTransforms();
            origin = childCollider.bounds.center + Vector3.right * (childCollider.bounds.extents.x + 1f);
            Assert.IsTrue(TryDistance(origin, out distance));
            Assert.AreEqual(Vector3.Distance(origin, childCollider.ClosestPoint(origin)), distance, 0.001f);
            Assert.LessOrEqual(distance, 5f);

            origin = childCollider.bounds.center + Vector3.right * (childCollider.bounds.extents.x + 6f);
            Assert.IsTrue(TryDistance(origin, out distance));
            Assert.Greater(distance, 5f);

            childCollider.enabled = false;
            Assert.IsFalse(TryDistance(origin, out _));
        }

        private bool TryDistance(Vector3 origin, out float distance)
        {
            object[] arguments = { origin, 0f };
            bool result = (bool)_distanceMethod.Invoke(_place, arguments);
            distance = (float)arguments[1];
            return result;
        }
    }

    public class CarSeatAssignmentTests
    {
        private readonly PlayerID _first = new PlayerID(1, false);
        private readonly PlayerID _second = new PlayerID(2, false);
        private readonly PlayerID _outsider = new PlayerID(3, false);

        [Test]
        public void OccupantsExchangeBothSeatsAndRepeatedRequestDoesNotUndoSwap()
        {
            var seats = new CarPlace.SeatAssignments { Top = _first, Bottom = _second };
            Assert.IsTrue(seats.TryAssign(_first, false, out var swapped));
            Assert.AreEqual(_second, swapped.Top);
            Assert.AreEqual(_first, swapped.Bottom);
            Assert.IsFalse(swapped.TryAssign(_first, false, out _));
            Assert.IsFalse(swapped.TryAssign(_second, true, out _));
            Assert.IsTrue(swapped.TryAssign(_first, true, out var restored));
            Assert.AreEqual(_first, restored.Top);
            Assert.AreEqual(_second, restored.Bottom);
        }

        [Test]
        public void EmptySeatChangeAndOutsideEntryPreserveOtherOccupant()
        {
            var seats = new CarPlace.SeatAssignments { Top = _first };
            Assert.IsTrue(seats.TryAssign(_first, false, out var moved));
            Assert.IsNull(moved.Top);
            Assert.AreEqual(_first, moved.Bottom);
            Assert.IsTrue(moved.TryAssign(_second, true, out var full));
            Assert.AreEqual(_second, full.Top);
            Assert.AreEqual(_first, full.Bottom);
            Assert.IsFalse(full.TryAssign(_outsider, true, out _));
            Assert.IsFalse(full.TryAssign(_outsider, false, out _));
        }
    }

    public class RagdollRecoveryTests
    {
        private GameObject _playerObject;
        private GameObject _ground;
        private GnomeAvatar _avatar;
        private MethodInfo _track;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/GnomePlayer.prefab");
            Assert.IsNotNull(prefab);
            _playerObject = Object.Instantiate(prefab);
            _playerObject.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            _avatar = _playerObject.GetComponent<GnomeAvatar>();
            var movement = _playerObject.GetComponent<PlayerMouvement>();
            Assert.IsNotNull(_avatar);
            Assert.IsNotNull(movement);
            Invoke(movement, "Awake");
            if (!_avatar.HasModel)
                Invoke(_avatar, "Awake");
            Assert.IsTrue(_avatar.HasModel);

            var settings = new SerializedObject(_avatar);
            settings.FindProperty("ragdollRecoveryDelay").floatValue = 0.2f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            _track = typeof(GnomeAvatar).GetMethod("Track", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(_track);

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(10f, 1f, 10f);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_playerObject);
            Object.DestroyImmediate(_ground);
        }

        [Test]
        public void RagdollKeepsGroundColliderAndRecoversOnWalkableGround()
        {
            var collider = _playerObject.GetComponent<Collider>();
            var body = _playerObject.GetComponent<Rigidbody>();
            _avatar.SetRagdoll(true);
            Assert.IsTrue(_avatar.IsRagdolled);
            Assert.IsTrue(collider.enabled);
            Assert.IsTrue(body.freezeRotation);

            _track.Invoke(_avatar, new object[] { 0.1f });
            Assert.IsTrue(_avatar.IsRagdolled);
            _track.Invoke(_avatar, new object[] { 0.11f });
            Assert.IsFalse(_avatar.IsRagdolled);
            Assert.IsTrue(collider.enabled);
            Assert.IsTrue(body.freezeRotation);
        }

        [Test]
        public void RagdollDoesNotRecoverInMidair()
        {
            _playerObject.transform.position = Vector3.up * 5f;
            Physics.SyncTransforms();
            _avatar.SetRagdoll(true);
            _track.Invoke(_avatar, new object[] { 1f });
            Assert.IsTrue(_avatar.IsRagdolled);
        }

        [Test]
        public void RagdollDoesNotRecoverOnAnUnwalkableSlope()
        {
            _ground.transform.rotation = Quaternion.Euler(0f, 0f, 40f);
            _ground.transform.position = -_ground.transform.up * 0.5f;
            Physics.SyncTransforms();

            var movement = _playerObject.GetComponent<PlayerMouvement>();
            MethodInfo checkGround = typeof(PlayerMouvement).GetMethod("CheckGround",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object[] arguments = { Vector3.up };
            Assert.IsTrue((bool)checkGround.Invoke(movement, arguments));
            Assert.Greater(Vector3.Angle((Vector3)arguments[0], Vector3.up), movement.MaxWalkableSlopeAngle);
            Assert.Less(Vector3.Angle((Vector3)arguments[0], Vector3.up), _avatar.RagdollSlopeAngle);

            _avatar.SetRagdoll(true);
            _track.Invoke(_avatar, new object[] { 1f });
            Assert.IsTrue(_avatar.IsRagdolled);
        }

        [Test]
        public void RecoveryBlendsFromTheRagdollPoseToTheAnimatedPose()
        {
            Transform chest = FindBone("Chest");
            Quaternion animatedRotation = chest.localRotation;
            _avatar.SetRagdoll(true);
            Quaternion fallenRotation = animatedRotation * Quaternion.Euler(60f, 0f, 0f);
            chest.localRotation = fallenRotation;

            _avatar.SetRagdoll(false);
            Assert.Less(Quaternion.Angle(fallenRotation, chest.localRotation), 0.01f);

            Invoke(_avatar, "SnapshotRecoveryWorldPose");
            Invoke(_avatar, "ResetBones");
            InvokeWithDelta(_avatar, "ApplyRecovery", 0.3f);
            Assert.AreEqual(30f, Quaternion.Angle(animatedRotation, chest.localRotation), 0.1f);

            Invoke(_avatar, "SnapshotRecoveryWorldPose");
            Invoke(_avatar, "ResetBones");
            InvokeWithDelta(_avatar, "ApplyRecovery", 0.3f);
            Assert.Less(Quaternion.Angle(animatedRotation, chest.localRotation), 0.01f);
        }

        [Test]
        public void RecoveryRestoresChildWorldPoseWhenParentMoves()
        {
            Transform spine = FindBone("Spine");
            Transform chest = FindBone("Chest");
            _avatar.SetRagdoll(true);
            spine.localRotation *= Quaternion.Euler(30f, 20f, 0f);
            chest.localRotation *= Quaternion.Euler(20f, 0f, 10f);
            Vector3 position = chest.position;
            Quaternion rotation = chest.rotation;
            _avatar.SetRagdoll(false);

            Invoke(_avatar, "SnapshotRecoveryWorldPose");
            Invoke(_avatar, "ResetBones");
            InvokeWithDelta(_avatar, "ApplyRecovery", 0f);
            Assert.Less(Vector3.Distance(position, chest.position), 0.0001f);
            Assert.Less(Quaternion.Angle(rotation, chest.rotation), 0.01f);
        }

        [Test]
        public void SlidePreservesInertiaAndGravityReversesUphillMotion()
        {
            var movement = _playerObject.GetComponent<PlayerMouvement>();
            MethodInfo slide = typeof(PlayerMouvement).GetMethod("CalculateSlideVelocity",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(slide);
            Vector3 normal = Quaternion.Euler(0f, 0f, 40f) * Vector3.up;
            Vector3 downhill = Vector3.ProjectOnPlane(Physics.gravity, normal).normalized;
            Vector3 lateral = Vector3.Cross(normal, downhill).normalized;
            Vector3 result = (Vector3)slide.Invoke(movement,
                new object[] { -downhill * 5f, -downhill * 5f + lateral * 5f, normal, 0.02f });
            float expectedDownhill = Vector3.ProjectOnPlane(Physics.gravity, normal).magnitude * 0.02f;
            Assert.Less(Vector3.Dot(result, downhill), 0f);
            Assert.Greater(Vector3.Dot(result, downhill), -5f);
            Assert.Greater(Vector3.Dot(result, lateral), 0f);
            Assert.AreEqual(0f, Vector3.Dot(result, normal), 0.0001f);

            Vector3 uphill = -downhill * 5f;
            for (int i = 0; i < 100; i++)
                uphill = (Vector3)slide.Invoke(movement, new object[] { uphill, -downhill * 5f, normal, 0.02f });
            Assert.Greater(Vector3.Dot(uphill, downhill), 0f);

            Vector3 downhillInput = (Vector3)slide.Invoke(movement,
                new object[] { Vector3.zero, downhill * 5f, normal, 0.02f });
            Assert.AreEqual(expectedDownhill, Vector3.Dot(downhillInput, downhill), 0.0001f);
            Vector3 uphillInput = (Vector3)slide.Invoke(movement,
                new object[] { Vector3.zero, -downhill * 5f, normal, 0.02f });
            Assert.AreEqual(expectedDownhill, Vector3.Dot(uphillInput, downhill), 0.0001f);
        }

        [Test]
        public void RagdollCollidersHaveWorldSizedCapsulesAndContinuousDetection()
        {
            _avatar.SetRagdoll(true);
            Transform lowerArm = FindBone("LowerArm.L");
            Transform hand = FindBone("Hand.L");
            CapsuleCollider capsule = lowerArm.GetComponentInChildren<CapsuleCollider>();
            Assert.IsNotNull(capsule);
            Assert.IsTrue(capsule.enabled);
            Vector3 scale = capsule.transform.lossyScale;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            Assert.AreEqual(0.065f, radius, 0.005f);
            Assert.GreaterOrEqual(capsule.bounds.size.magnitude, Vector3.Distance(lowerArm.position, hand.position));
            Assert.AreEqual(CollisionDetectionMode.ContinuousDynamic,
                lowerArm.GetComponent<Rigidbody>().collisionDetectionMode);
        }

        private Transform FindBone(string name)
        {
            foreach (Transform bone in _playerObject.GetComponentsInChildren<Transform>(true))
                if (bone.name == name)
                    return bone;
            Assert.Fail($"Bone {name} not found.");
            return null;
        }

        private static void InvokeWithDelta(object target, string methodName, float dt)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(target, new object[] { dt });
        }

        private static void Invoke(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(target, null);
        }
    }
}
