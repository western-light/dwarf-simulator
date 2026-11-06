using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player
{
    public class PlayerMouvement:MonoBehaviour
    {
        private const float GroundCheckMargin = 0.05f;

        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float jumpForce = 5f;

        private InputAction moveAction;
        private InputAction jumpAction;
        private Rigidbody rb;
        private Collider col;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            moveAction = InputSystem.actions.FindAction("Move");
            jumpAction = InputSystem.actions.FindAction("Jump");
        }

        void FixedUpdate()
        {
            Vector2 moveInput = moveAction.ReadValue<Vector2>();
            Vector3 moveDirection = new Vector3(moveInput.x, 0, moveInput.y).normalized;
            rb.MovePosition(transform.position + moveDirection * Time.fixedDeltaTime * moveSpeed);
            
            if (jumpAction.IsPressed() && IsGrounded())
            {
                rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            }
        }

        private bool IsGrounded()
        {
            Bounds bounds = col.bounds;
            return Physics.Raycast(bounds.center, Vector3.down, bounds.extents.y + GroundCheckMargin,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
