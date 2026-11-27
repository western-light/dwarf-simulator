using UnityEngine;

namespace DwarfSimulator.Car
{
    [DefaultExecutionOrder(50)]
    public class CarCockpit : MonoBehaviour
    {
        private sealed class Pedal
        {
            public Transform Pivot;
            public Transform FootTarget;
            public Quaternion BaseRotation;
            public float Value;
            public float Goal;
        }

        [Header("Steering wheel")]
        [SerializeField] private string wheelName = "steering_wheel";
        [SerializeField, Min(0f)] private float wheelTurnAngle = 140f;
        [SerializeField, Min(0.1f)] private float wheelSmoothing = 20f;
        [SerializeField, Range(0f, 90f)] private float gripAngle = 35f;
        [SerializeField, Range(0.3f, 1.2f)] private float gripRadiusScale = 0.9f;
        [SerializeField] private Vector3 wheelAxisOverride;

        [Header("Pedals")]
        [SerializeField] private Vector3 throttleOffset = new Vector3(0.08f, 0.12f, 0.28f);
        [SerializeField] private Vector3 brakeOffset = new Vector3(-0.08f, 0.12f, 0.28f);
        [SerializeField] private Vector3 throttleSize = new Vector3(0.07f, 0.015f, 0.13f);
        [SerializeField] private Vector3 brakeSize = new Vector3(0.11f, 0.015f, 0.13f);
        [SerializeField] private Vector3 footOffset = new Vector3(0f, 0.05f, 0.04f);
        [SerializeField] private float pedalRestAngle = -12f;
        [SerializeField] private float pedalPressedAngle = 12f;
        [SerializeField, Min(0.1f)] private float pedalSpeed = 8f;

        private Transform _wheel;
        private Vector3 _wheelRestPosition;
        private Quaternion _wheelRestRotation;
        private Vector3 _wheelPivot;
        private Vector3 _wheelAxis;
        private float _wheelAngle;
        private float _steer;
        private Pedal _throttle;
        private Pedal _brake;

        public Transform LeftGrip { get; private set; }
        public Transform RightGrip { get; private set; }
        public Transform ThrottleFoot => _throttle?.FootTarget;
        public Transform BrakeFoot => _brake?.FootTarget;

        public void Build(Transform pedalSeat)
        {
            BuildWheel();
            BuildPedals(pedalSeat);
        }

        public void SetInputs(float steer, float throttle, float brake)
        {
            _steer = Mathf.Clamp(steer, -1f, 1f);
            if (_throttle != null)
                _throttle.Goal = Mathf.Clamp01(throttle);
            if (_brake != null)
                _brake.Goal = Mathf.Clamp01(brake);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            UpdateWheel(dt);
            UpdatePedal(_throttle, dt);
            UpdatePedal(_brake, dt);
        }

        private void UpdateWheel(float dt)
        {
            if (_wheel == null)
                return;

            _wheelAngle = Mathf.Lerp(_wheelAngle, _steer * wheelTurnAngle, 1f - Mathf.Exp(-wheelSmoothing * dt));
            Quaternion turn = Quaternion.AngleAxis(_wheelAngle, _wheelAxis);
            _wheel.localRotation = turn * _wheelRestRotation;
            _wheel.localPosition = _wheelPivot + turn * (_wheelRestPosition - _wheelPivot);
        }

        private void UpdatePedal(Pedal pedal, float dt)
        {
            if (pedal == null)
                return;

            pedal.Value = Mathf.MoveTowards(pedal.Value, pedal.Goal, pedalSpeed * dt);
            float angle = Mathf.Lerp(pedalRestAngle, pedalPressedAngle, pedal.Value);
            pedal.Pivot.localRotation = pedal.BaseRotation * Quaternion.Euler(angle, 0f, 0f);
            pedal.FootTarget.SetPositionAndRotation(pedal.Pivot.TransformPoint(footOffset), pedal.Pivot.rotation);
        }

        private void BuildWheel()
        {
            foreach (Transform candidate in GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name != wheelName)
                    continue;
                _wheel = candidate;
                break;
            }
            if (_wheel == null || _wheel.parent == null)
            {
                Debug.LogWarning($"Steering wheel '{wheelName}' not found on the car.", this);
                _wheel = null;
                return;
            }

            MeshFilter filter = _wheel.GetComponentInChildren<MeshFilter>(true);
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
            {
                Debug.LogWarning("Steering wheel mesh is missing or not readable.", this);
                _wheel = null;
                return;
            }

            Vector3[] local = mesh.vertices;
            var points = new Vector3[local.Length];
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < local.Length; i++)
            {
                points[i] = filter.transform.TransformPoint(local[i]);
                sum += points[i];
            }
            Vector3 center = sum / points.Length;

            Vector3 axis = wheelAxisOverride.sqrMagnitude > 0f
                ? _wheel.TransformDirection(wheelAxisOverride).normalized
                : FindNormal(points, center);
            if (Vector3.Dot(axis, transform.forward) > 0f)
                axis = -axis;

            float rim = 0f;
            foreach (Vector3 point in points)
                rim = Mathf.Max(rim, Vector3.ProjectOnPlane(point - center, axis).magnitude);

            Vector3 right = Vector3.ProjectOnPlane(transform.right, axis).normalized;
            Vector3 up = Vector3.ProjectOnPlane(transform.up, axis).normalized;
            float radius = rim * gripRadiusScale;
            float angle = gripAngle * Mathf.Deg2Rad;
            Vector3 lift = up * Mathf.Sin(angle);
            Vector3 side = right * Mathf.Cos(angle);
            LeftGrip = CreateAnchor("GripLeft", center + (lift - side) * radius, _wheel);
            RightGrip = CreateAnchor("GripRight", center + (lift + side) * radius, _wheel);

            Transform parent = _wheel.parent;
            _wheelRestPosition = _wheel.localPosition;
            _wheelRestRotation = _wheel.localRotation;
            _wheelPivot = parent.InverseTransformPoint(center);
            _wheelAxis = parent.InverseTransformDirection(axis).normalized;
        }

        private static Transform CreateAnchor(string anchorName, Vector3 position, Transform parent)
        {
            Transform anchor = new GameObject(anchorName).transform;
            anchor.SetParent(parent, false);
            anchor.position = position;
            return anchor;
        }

        private static Vector3 FindNormal(Vector3[] points, Vector3 center)
        {
            var m = new double[3, 3];
            foreach (Vector3 point in points)
            {
                Vector3 d = point - center;
                double[] v = { d.x, d.y, d.z };
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                        m[r, c] += v[r] * v[c];
            }

            var vectors = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 32; sweep++)
            {
                int p = 0, q = 1;
                double largest = System.Math.Abs(m[0, 1]);
                if (System.Math.Abs(m[0, 2]) > largest)
                {
                    p = 0;
                    q = 2;
                    largest = System.Math.Abs(m[0, 2]);
                }
                if (System.Math.Abs(m[1, 2]) > largest)
                {
                    p = 1;
                    q = 2;
                    largest = System.Math.Abs(m[1, 2]);
                }
                if (largest < 1e-14)
                    break;

                double theta = (m[q, q] - m[p, p]) / (2.0 * m[p, q]);
                double t = (theta >= 0 ? 1.0 : -1.0) / (System.Math.Abs(theta) + System.Math.Sqrt(theta * theta + 1.0));
                double cos = 1.0 / System.Math.Sqrt(t * t + 1.0);
                double sin = t * cos;
                for (int k = 0; k < 3; k++)
                {
                    double kp = m[k, p], kq = m[k, q];
                    m[k, p] = cos * kp - sin * kq;
                    m[k, q] = sin * kp + cos * kq;
                }
                for (int k = 0; k < 3; k++)
                {
                    double pk = m[p, k], qk = m[q, k];
                    m[p, k] = cos * pk - sin * qk;
                    m[q, k] = sin * pk + cos * qk;
                }
                for (int k = 0; k < 3; k++)
                {
                    double kp = vectors[k, p], kq = vectors[k, q];
                    vectors[k, p] = cos * kp - sin * kq;
                    vectors[k, q] = sin * kp + cos * kq;
                }
            }

            int smallest = 0;
            for (int i = 1; i < 3; i++)
                if (m[i, i] < m[smallest, smallest])
                    smallest = i;
            return new Vector3((float)vectors[0, smallest], (float)vectors[1, smallest],
                (float)vectors[2, smallest]).normalized;
        }

        private void BuildPedals(Transform seat)
        {
            if (seat == null)
                return;

            _brake = CreatePedal("PedalBrake", seat, brakeOffset, brakeSize, new Color(0.35f, 0.08f, 0.08f));
            _throttle = CreatePedal("PedalThrottle", seat, throttleOffset, throttleSize, new Color(0.12f, 0.12f, 0.12f));
        }

        private Pedal CreatePedal(string pedalName, Transform seat, Vector3 offset, Vector3 size, Color color)
        {
            Transform pivot = new GameObject(pedalName).transform;
            pivot.SetParent(transform, false);
            pivot.SetPositionAndRotation(seat.TransformPoint(offset), seat.rotation);
            Quaternion baseRotation = pivot.localRotation;

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            Destroy(plate.GetComponent<Collider>());
            plate.transform.SetParent(pivot, false);
            plate.transform.localScale = size;
            plate.transform.localPosition = new Vector3(0f, size.y * 0.5f, size.z * 0.5f);
            plate.GetComponent<Renderer>().material.color = color;

            Transform footTarget = new GameObject(pedalName + "Foot").transform;
            footTarget.SetParent(transform, false);

            var pedal = new Pedal { Pivot = pivot, FootTarget = footTarget, BaseRotation = baseRotation };
            UpdatePedal(pedal, 0f);
            return pedal;
        }
    }
}
