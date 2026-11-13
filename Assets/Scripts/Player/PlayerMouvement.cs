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

        private InputAction moveAction;
        private InputAction runAction;
        private InputAction jumpAction;
        private Rigidbody rb;
        private Animator anim;
        private Collider col;
        private Transform playerCamera;
        
        private Vector3 lastMoveDirection;
        private bool freeze;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            anim = GetComponent<Animator>();
            playerCamera = Camera.main.transform;
            moveAction = InputSystem.actions.FindAction("Move");
            runAction = InputSystem.actions.FindAction("Sprint");
            jumpAction = InputSystem.actions.FindAction("Jump");
            
        }

        void FixedUpdate()
        {
            if (freeze) return;
            
            Vector2 moveInput = moveAction.ReadValue<Vector2>();
            Vector3 cameraForward = Vector3.ProjectOnPlane(playerCamera.forward, Vector3.up).normalized;
            Vector3 cameraRight = Vector3.ProjectOnPlane(playerCamera.right, Vector3.up).normalized;
            Vector3 moveDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;
            if (moveDirection.magnitude > 0.1f)
                lastMoveDirection = moveDirection;
            bool run = runAction.IsPressed();
            float speed = run ? runSpeed : moveSpeed;
            
            anim.SetBool("Walk", moveDirection.magnitude > 0);
            anim.SetBool("Run", run);
            
            rb.MovePosition(transform.position + moveDirection * Time.fixedDeltaTime * speed);
            Quaternion targetRotation = Quaternion.LookRotation(lastMoveDirection);
            rb.MoveRotation(targetRotation);
            
            if (jumpAction.IsPressed() && IsGrounded())
            {
                anim.SetTrigger("Jump");
                rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            }
        }

        private bool IsGrounded()
        {
            Bounds bounds = col.bounds;
            return Physics.Raycast(bounds.center, Vector3.down, bounds.extents.y + GroundCheckMargin,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
        
        public void Freeze() => freeze = true;
        public void UnFreeze() => freeze = false;
    }
}
