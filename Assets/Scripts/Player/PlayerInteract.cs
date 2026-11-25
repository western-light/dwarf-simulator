using DwarfSimulator.Player.Interactor;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player
{
    [RequireComponent(typeof(Player), typeof(PlayerMouvement))]
    public class PlayerInteract : MonoBehaviour
    {
        [SerializeField] private float interactDistance = 5f;
        [SerializeField, Min(0f)] private float holdDuration = 0.35f;
        [SerializeField, Range(0.01f, 0.5f)] private float screenTolerance = 0.22f;

        private InputAction _interact;
        private InputAction _leave;
        private InputAction _previous;
        private InputAction _next;
        private Player _player;
        private PlayerMouvement _playerMouvement;
        private InteractorUI _ui;
        private Interactable _currentInteractable;
        private Interactable _pressedTarget;
        private float _pressedAt;
        private bool _pressPending;
        private bool _interactionOpen;
        private bool _ownedLastFrame;
        private CursorLockMode _previousCursorLockMode;
        private bool _previousCursorVisible;
        private CinemachineInputAxisController[] _suspendedCameraInputs;
        public float InteractDistance => interactDistance;
        public bool IsInteractionOpen => _interactionOpen;

        private void Awake()
        {
            _player = GetComponent<Player>();
            _playerMouvement = GetComponent<PlayerMouvement>();
        }

        private void Start()
        {
            _interact = InputSystem.actions?.FindAction("Interact");
            _leave = InputSystem.actions?.FindAction("Jump");
            _previous = InputSystem.actions?.FindAction("Previous");
            _next = InputSystem.actions?.FindAction("Next");
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
                if (_ownedLastFrame)
                {
                    _ownedLastFrame = false;
                    _pressPending = false;
                    CloseInteraction();
                    if (_ui != null)
                        _ui.UpdateHud(null, null, null);
                }
                return;
            }
            _ownedLastFrame = true;
            if (_ui == null)
                _ui = FindAnyObjectByType<InteractorUI>();
            if (_ui == null)
            {
                Debug.LogError("Cannot interact without an InteractorUI in the scene.", this);
                enabled = false;
                return;
            }

            Camera camera = Camera.main;
            Collider targetCollider = null;
            string failure = "Aucun objet a portee";
            Interactable target = _player.IsSeated ? _player.SeatInteractable :
                FindTarget(camera, out targetCollider, out failure);
            if (_player.IsSeated)
                failure = "Interaction indisponible";
            if (target != null && !target.isActiveAndEnabled)
                target = null;

            string hint = _interactionOpen
                ? $"Choisir : souris ou {Binding(_previous)} / {Binding(_next)} | relacher {Binding(_interact)} pour confirmer"
                : BuildHint(target);
            _ui.UpdateHud(hint, camera,
                _interactionOpen || _player.IsSeated || target == null || !target.ShowTargetFrame ? null : targetCollider);

            if (!_interact.enabled)
            {
                _pressPending = false;
                CloseInteraction();
                return;
            }

            if (!_interactionOpen && _interact.WasPressedThisFrame())
            {
                if (target == null)
                    _ui.ShowMessage(failure);
                else
                {
                    _pressedTarget = target;
                    _pressedAt = Time.unscaledTime;
                    _pressPending = true;
                }
            }

            bool openedThisFrame = false;
            if (_pressPending)
            {
                if (_pressedTarget == null || target != _pressedTarget)
                {
                    _pressPending = false;
                    _ui.ShowMessage("Interaction interrompue");
                }
                else if (_interact.WasReleasedThisFrame())
                {
                    _pressPending = false;
                    InvokeDefault(target);
                }
                else if (_interact.IsPressed() && Time.unscaledTime - _pressedAt >= holdDuration)
                {
                    _pressPending = false;
                    OpenInteraction(target);
                    openedThisFrame = _interactionOpen;
                }
                else if (!_interact.IsPressed())
                    _pressPending = false;
            }

            if (_interactionOpen)
            {
                if (_currentInteractable == null || target != _currentInteractable)
                {
                    CloseInteraction();
                    _ui.ShowMessage("Interaction interrompue");
                }
                else if (_interact.WasReleasedThisFrame())
                    _ui.ConfirmInteract();
                else if (!_interact.IsPressed())
                    CloseInteraction();
                else if (!openedThisFrame)
                    _ui.UpdateSelection();
            }
        }

        private string BuildHint(Interactable target)
        {
            string interact = Binding(_interact);
            string navigation = $"{Binding(_previous)} / {Binding(_next)}";
            if (_player.IsSeated)
            {
                int seatedChoice = target != null ? target.DefaultChoice(_player) : -1;
                string seatedAction = seatedChoice >= 0 ? target.ChoiceName(seatedChoice, _player) : "aucune place disponible";
                return $"{_player.SeatDescription} | {Binding(_leave)} : sortir\n{interact} : {seatedAction} | maintenir : choix ({navigation})";
            }
            if (target == null)
                return "";
            int choice = target.DefaultChoice(_player);
            string action = choice >= 0 ? target.ChoiceName(choice, _player) : "aucune action disponible";
            return $"{target.DisplayName} - {action}\n{interact} : agir | maintenir : choix ({navigation})";
        }

        private static string Binding(InputAction action)
        {
            if (action == null)
                return "?";
            string keyboard = null;
            string gamepad = null;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                string path = action.bindings[i].effectivePath;
                if (path != null && path.StartsWith("<Keyboard>") && keyboard == null)
                    keyboard = action.GetBindingDisplayString(i);
                if (path != null && path.StartsWith("<Gamepad>") && gamepad == null)
                    gamepad = action.GetBindingDisplayString(i);
            }
            return keyboard != null && gamepad != null ? $"{keyboard}/{gamepad}" : keyboard ?? gamepad ?? "?";
        }

        private Interactable FindTarget(Camera camera, out Collider selectedCollider, out string failure)
        {
            selectedCollider = null;
            failure = "Aucun objet a portee";
            if (camera == null)
            {
                failure = "Camera indisponible";
                return null;
            }

            Interactable selected = null;
            float best = float.PositiveInfinity;
            Vector3 origin = transform.position + Vector3.up * 0.8f;
            foreach (Collider candidate in Physics.OverlapSphere(origin, interactDistance + 2f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Interactable interactable = candidate.GetComponentInParent<Interactable>()
                                             ?? candidate.GetComponentInChildren<Interactable>();
                if (interactable == null || !interactable.isActiveAndEnabled ||
                    candidate.transform.IsChildOf(transform))
                    continue;

                Bounds bounds = candidate.bounds;
                Vector2 min = Vector2.one * float.PositiveInfinity;
                Vector2 max = Vector2.one * float.NegativeInfinity;
                bool visible = false;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = camera.WorldToViewportPoint(corner);
                    if (p.z <= 0f) continue;
                    visible = true;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                if (!visible)
                    continue;
                float dx = Mathf.Max(min.x - 0.5f, 0f, 0.5f - max.x);
                float dy = Mathf.Max(min.y - 0.5f, 0f, 0.5f - max.y);
                float screenDistance = Mathf.Sqrt(dx * dx + dy * dy);
                if (screenDistance > screenTolerance)
                    continue;

                Vector3 point = candidate.ClosestPoint(origin);
                float distance = Vector3.Distance(origin, point);
                if (distance > interactDistance)
                {
                    failure = "Trop loin";
                    continue;
                }
                Vector3 direction = point - origin;
                bool blocked = false;
                if (direction.sqrMagnitude > 0.001f)
                {
                    foreach (RaycastHit hit in Physics.RaycastAll(origin, direction.normalized, distance,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (!hit.collider.transform.IsChildOf(transform) &&
                            !hit.collider.transform.IsChildOf(interactable.transform) &&
                            hit.collider.gameObject != interactable.gameObject)
                        {
                            blocked = true;
                            break;
                        }
                    }
                }
                if (blocked)
                {
                    failure = "Interaction bloquee";
                    continue;
                }
                float score = screenDistance + distance * 0.01f;
                if (score >= best) continue;
                best = score;
                selected = interactable;
                selectedCollider = candidate;
            }
            return selected;
        }

        private void InvokeDefault(Interactable target)
        {
            int index = target.DefaultChoice(_player);
            if (index < 0)
            {
                string reason = null;
                if (target.Choices != null)
                    for (int i = 0; i < target.Choices.Length; i++)
                    {
                        string unavailable = target.UnavailableReason(i, _player);
                        if (unavailable != null && unavailable != "place actuelle")
                        {
                            reason = unavailable;
                            break;
                        }
                    }
                _ui.ShowMessage(reason ?? "Aucune action disponible");
                return;
            }
            target.Choices[index].OnInteract?.Invoke(_player);
            _player.NotifyInteract();
        }

        private void OpenInteraction(Interactable target)
        {
            if (!_ui.Interact(target, _player, OnInteractionClosed))
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

        private void OnDisable()
        {
            _pressPending = false;
            CloseInteraction();
            if (_ownedLastFrame && _ui != null)
                _ui.UpdateHud(null, null, null);
            _ownedLastFrame = false;
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
                if (cameraInput != null)
                    cameraInput.enabled = true;
            _suspendedCameraInputs = null;
            Cursor.lockState = _previousCursorLockMode;
            Cursor.visible = _previousCursorVisible;
        }
    }
}
