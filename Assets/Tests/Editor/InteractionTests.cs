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
}
