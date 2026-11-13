using DwarfSimulator.Player.Interactor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player
{
    public class PlayerInteract : MonoBehaviour
    {
        [SerializeField] private float interactDistance;

        private Camera _cam;
        private InputAction _interact;
        private Interactable _currentInteractable;
        private PlayerMouvement _playerMouvement;

        void Start()
        {
            _cam = Camera.main;
            _playerMouvement = GetComponent<PlayerMouvement>();
            _interact = InputSystem.actions.FindAction("Interact");
        }

        void Update()
        {
            if (_interact.IsPressed())
            {
                _playerMouvement.Freeze();
                Interact();
            }
            else if (_currentInteractable != null)
            {
                _currentInteractable.StopInteract();
                _playerMouvement.UnFreeze();
            }
        }

        void Interact()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            Ray camRay = _cam.ScreenPointToRay(mouse.position.ReadValue());
            Debug.DrawLine(camRay.origin, camRay.origin + camRay.direction * interactDistance);

            if (Physics.Raycast(camRay, out RaycastHit nearestHit, interactDistance))
            {
                _currentInteractable = nearestHit.transform.gameObject.GetComponent<Interactable>()
                                            ?? nearestHit.transform.gameObject.GetComponentInChildren<Interactable>();
                if (_currentInteractable != null)
                {
                    _currentInteractable.Interact();
                }
            }
        }

    }
}