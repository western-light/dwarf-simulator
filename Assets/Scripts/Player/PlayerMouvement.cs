using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace DwarfSimulator.Player
{
    public class PlayerMouvement:MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float jumpHeight = 20f;
        
        private InputAction moveAction;
        private InputAction jumpAction;
        private Rigidbody rb;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            moveAction = InputSystem.actions.FindAction("Move");
            jumpAction = InputSystem.actions.FindAction("Jump");
        }

        void FixedUpdate()
        {
            Vector2 moveInput = moveAction.ReadValue<Vector2>();
            Vector3 moveDirection = new Vector3(moveInput.x, 0, moveInput.y).normalized;
            rb.MovePosition(transform.position + moveDirection * Time.fixedDeltaTime * moveSpeed);
            
            if (jumpAction.IsPressed())
            {
                rb.AddForce(transform.up * jumpHeight);
            }
        }
    }
}