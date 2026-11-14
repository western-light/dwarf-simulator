using DwarfSimulator.Player.Interactor;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player
{
    [RequireComponent(typeof(Player), typeof(PlayerMouvement))]
    public class PlayerInteract : MonoBehaviour
    {
        [SerializeField] private float interactDistance;

        private InputAction _interact;
        private Player _player;
        private PlayerMouvement _playerMouvement;
        private InteractorUI _ui;
        private Interactable _currentInteractable;
        private bool _interactionOpen;
        private CursorLockMode _previousCursorLockMode;
        private bool _previousCursorVisible;
        private CinemachineInputAxisController[] _suspendedCameraInputs;

        private void Awake()
        {
            _player = GetComponent<Player>();
            _playerMouvement = GetComponent<PlayerMouvement>();
        }

        private void Start()
        {
            _interact = InputSystem.actions?.FindAction("Interact");
            if (_interact == null)
            {
                Debug.LogError("The Interact input action could not be found.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (!_player.isOwner)
            {
                CloseInteraction();
                return;
            }

            bool wasOpen = _interactionOpen;
            if (!wasOpen && _interact.WasPressedThisFrame())
                TryInteract();

            if (_interactionOpen)
            {
                if (_ui == null || _currentInteractable == null || !_currentInteractable.isActiveAndEnabled ||
                    !_interact.enabled)
                    CloseInteraction();
                else
                {
                    if (wasOpen)
                        _ui.UpdateSelection();

                    if (_interact.WasReleasedThisFrame())
                        _ui.ConfirmInteract();
                    else if (!_interact.IsPressed())
                        CloseInteraction();
                }
            }
        }

        private void OnDisable()
        {
            CloseInteraction();
        }

        private void TryInteract()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("Cannot interact without a main camera.", this);
                return;
            }

            Vector2 screenPosition = Cursor.lockState == CursorLockMode.Locked || Mouse.current == null
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : Mouse.current.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance))
                return;

            Interactable target = hit.collider.GetComponentInParent<Interactable>()
                                  ?? hit.collider.GetComponentInChildren<Interactable>();
            if (target == null || !target.isActiveAndEnabled)
                return;

            if (_ui == null)
                _ui = FindAnyObjectByType<InteractorUI>();
            if (_ui == null)
            {
                Debug.LogError("Cannot interact without an InteractorUI in the scene.", this);
                return;
            }

            if (!_ui.Interact(target.Choices, _player, OnInteractionClosed))
                return;

            _currentInteractable = target;
            _interactionOpen = true;
            _previousCursorLockMode = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            _playerMouvement.SetInteractionFrozen(true);
            _suspendedCameraInputs = FindObjectsByType<CinemachineInputAxisController>();
            for (int i = 0; i < _suspendedCameraInputs.Length; i++)
            {
                if (_suspendedCameraInputs[i].enabled)
                    _suspendedCameraInputs[i].enabled = false;
                else
                    _suspendedCameraInputs[i] = null;
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void CloseInteraction()
        {
            if (!_interactionOpen)
                return;

            if (_ui != null)
                _ui.StopInteract();
            else
                OnInteractionClosed();
        }

        private void OnInteractionClosed()
        {
            if (!_interactionOpen)
                return;

            _interactionOpen = false;
            _currentInteractable = null;
            _playerMouvement.SetInteractionFrozen(false);
            foreach (CinemachineInputAxisController cameraInput in _suspendedCameraInputs)
            {
                if (cameraInput != null)
                    cameraInput.enabled = true;
            }
            _suspendedCameraInputs = null;
            Cursor.lockState = _previousCursorLockMode;
            Cursor.visible = _previousCursorVisible;
        }
    }
}
