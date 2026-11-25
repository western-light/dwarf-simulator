using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace DwarfSimulator.Player.Interactor
{
    public class InteractorUI : MonoBehaviour
    {
        private const float PointerRange = 150f;
        private const float PointerDeadZone = 50f;

        [SerializeField] private GameObject interactButtonPrefab;
        [SerializeField] private GameObject interactUI;
        [SerializeField] private Transform interactUITransform;

        private List<GameObject> _choiceObjects;
        private Action _onClosed;
        private Choice _selectedChoice;
        private Player _interactor;
        private Vector2 _virtualPointer;
        private Interactable _target;
        private int _selectedIndex;
        private InputAction _previous;
        private InputAction _next;
        private GameObject _hud;
        private TextMeshProUGUI _hint;
        private TextMeshProUGUI _message;
        private readonly RectTransform[] _frame = new RectTransform[4];
        private float _messageUntil;
        private EventSystem _eventSystem;
        private bool _previousNavigationEvents;

        private void Awake()
        {
            if (interactUI != null)
                interactUI.SetActive(false);
            else
                Debug.LogError("InteractorUI requires an interaction panel.", this);
            _previous = InputSystem.actions?.FindAction("Previous");
            _next = InputSystem.actions?.FindAction("Next");
            CreateHud();
        }
        
        public bool Interact(Interactable target, Player interactor, Action onClosed)
        {
            Choice[] choices = target.Choices;
            if (_choiceObjects != null)
                return false;

            if (choices == null || choices.Length == 0)
            {
                Debug.LogWarning("The interactable has no interaction choices.", interactor);
                return false;
            }

            if (interactButtonPrefab == null || interactUI == null || interactUITransform == null ||
                interactUITransform is not RectTransform || interactButtonPrefab.transform is not RectTransform ||
                interactButtonPrefab.GetComponent<ChoiceButton>() == null)
            {
                Debug.LogError("InteractorUI requires a panel, a RectTransform container and a UI ChoiceButton prefab.", this);
                return false;
            }

            _choiceObjects = new List<GameObject>();
            ChoiceButton selectedButton = null;
            Choice initialChoice = null;
            int defaultIndex = target.DefaultChoice(interactor);
            _target = target;
            for (int i = 0; i < choices.Length; i++)
            {
                Choice choice = choices[i];
                if (choice == null)
                {
                    Debug.LogWarning("An interaction choice is missing.", this);
                    continue;
                }

                GameObject button = Instantiate(interactButtonPrefab, interactUITransform);
                _choiceObjects.Add(button);
                ChoiceButton choiceButton = button.GetComponent<ChoiceButton>();
                int index = i;
                choiceButton.SetChoice(choice, () => { _selectedChoice = choice; _selectedIndex = index; },
                    target.ChoiceName(i, interactor),
                    target.UnavailableReason(i, interactor));
                if (selectedButton == null || i == defaultIndex)
                {
                    selectedButton = choiceButton;
                    initialChoice = choice;
                    _selectedIndex = i;
                }
            }

            if (selectedButton == null)
            {
                _choiceObjects = null;
                return false;
            }

            _onClosed = onClosed;
            _interactor = interactor;
            _selectedChoice = initialChoice;
            _virtualPointer = Vector2.zero;
            _eventSystem = EventSystem.current;
            if (_eventSystem != null)
            {
                _previousNavigationEvents = _eventSystem.sendNavigationEvents;
                _eventSystem.sendNavigationEvents = false;
            }
            interactUI.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)interactUITransform);
            selectedButton.Select();
            return true;
        }

        public void UpdateSelection()
        {
            if (_choiceObjects == null)
                return;

            for (int i = 0, buttonIndex = 0; i < _target.Choices.Length; i++)
            {
                Choice choice = _target.Choices[i];
                if (choice == null)
                    continue;
                _choiceObjects[buttonIndex++].GetComponent<ChoiceButton>()
                    .SetAvailability(_target.ChoiceName(i, _interactor), _target.UnavailableReason(i, _interactor));
            }

            int step = _next != null && _next.WasPressedThisFrame() ? 1 :
                _previous != null && _previous.WasPressedThisFrame() ? -1 : 0;
            if (step != 0)
            {
                int index = _selectedIndex;
                do
                    index = (index + step + _target.Choices.Length) % _target.Choices.Length;
                while (_target.Choices[index] == null);
                int buttonIndex = 0;
                for (int i = 0; i < index; i++)
                    if (_target.Choices[i] != null) buttonIndex++;
                _choiceObjects[buttonIndex].GetComponent<ChoiceButton>().Select();
                _virtualPointer = Vector2.zero;
                return;
            }

            if (Mouse.current == null)
                return;
            Vector2 delta = Mouse.current.delta.ReadValue();
            if (delta == Vector2.zero)
                return;

            _virtualPointer = Vector2.ClampMagnitude(_virtualPointer + delta, PointerRange);
            if (_virtualPointer.sqrMagnitude < PointerDeadZone * PointerDeadZone)
                return;

            RectTransform container = (RectTransform)interactUITransform;
            Rect rect = container.rect;
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            Vector2 pointer = _virtualPointer / PointerRange;
            ChoiceButton closest = null;
            float closestDistance = float.PositiveInfinity;
            foreach (GameObject button in _choiceObjects)
            {
                RectTransform buttonRect = (RectTransform)button.transform;
                Vector3 center = container.InverseTransformPoint(buttonRect.TransformPoint(buttonRect.rect.center));
                Vector2 position = new Vector2(
                    (center.x - rect.center.x) * 2f / rect.width,
                    (center.y - rect.center.y) * 2f / rect.height);
                float distance = (position - pointer).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = button.GetComponent<ChoiceButton>();
                }
            }

            if (closest != null)
                closest.Select();
        }

        public void ConfirmInteract()
        {
            if (_choiceObjects == null)
                return;

            Choice choice = _selectedChoice;
            Player interactor = _interactor;
            string reason = _target.UnavailableReason(_selectedIndex, interactor);
            StopInteract();
            if (reason != null)
                ShowMessage(reason);
            else
            {
                choice?.OnInteract?.Invoke(interactor);
                interactor.NotifyInteract();
            }
        }

        public void StopInteract()
        {
            if (_choiceObjects == null)
                return;

            List<GameObject> buttons = _choiceObjects;
            _choiceObjects = null;
            _selectedChoice = null;
            _interactor = null;
            _target = null;
            if (_eventSystem != null)
                _eventSystem.sendNavigationEvents = _previousNavigationEvents;
            _eventSystem = null;
            interactUI.SetActive(false);
            foreach (GameObject button in buttons)
                Destroy(button);

            Action onClosed = _onClosed;
            _onClosed = null;
            onClosed?.Invoke();
        }

        public void ShowMessage(string text)
        {
            _message.text = text;
            _messageUntil = Time.unscaledTime + 2.5f;
            _message.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (_message.gameObject.activeSelf && Time.unscaledTime >= _messageUntil)
                _message.gameObject.SetActive(false);
        }

        public void UpdateHud(string hint, Camera camera, Collider target)
        {
            _hint.text = hint ?? "";
            bool showFrame = camera != null && target != null;
            if (showFrame)
            {
                Bounds bounds = target.bounds;
                Vector2 min = new Vector2(Screen.width, Screen.height);
                Vector2 max = Vector2.zero;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = camera.WorldToScreenPoint(corner);
                    if (point.z <= 0f) continue;
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
                min = new Vector2(Mathf.Clamp(min.x, 0, Screen.width), Mathf.Clamp(min.y, 0, Screen.height));
                max = new Vector2(Mathf.Clamp(max.x, 0, Screen.width), Mathf.Clamp(max.y, 0, Screen.height));
                showFrame = max.x > min.x && max.y > min.y;
                if (showFrame)
                {
                    SetFrame(_frame[0], new Vector2(min.x, min.y), new Vector2(max.x - min.x, 2));
                    SetFrame(_frame[1], new Vector2(min.x, max.y), new Vector2(max.x - min.x, 2));
                    SetFrame(_frame[2], new Vector2(min.x, min.y), new Vector2(2, max.y - min.y));
                    SetFrame(_frame[3], new Vector2(max.x, min.y), new Vector2(2, max.y - min.y));
                }
            }
            foreach (RectTransform edge in _frame)
                edge.gameObject.SetActive(showFrame);
        }

        private static void SetFrame(RectTransform edge, Vector2 position, Vector2 size)
        {
            edge.anchoredPosition = position;
            edge.sizeDelta = size;
        }

        private void CreateHud()
        {
            _hud = new GameObject("Interaction HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = _hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            _hint = CreateText("Hint", new Vector2(0.5f, 0.28f), new Vector2(800, 100));
            _message = CreateText("Message", new Vector2(0.5f, 0.18f), new Vector2(800, 60));
            _message.gameObject.SetActive(false);
            for (int i = 0; i < _frame.Length; i++)
            {
                var edge = new GameObject("Target edge", typeof(RectTransform), typeof(Image));
                edge.transform.SetParent(_hud.transform, false);
                _frame[i] = edge.GetComponent<RectTransform>();
                _frame[i].anchorMin = _frame[i].anchorMax = Vector2.zero;
                _frame[i].pivot = Vector2.zero;
                edge.GetComponent<Image>().color = new Color(1f, 0.85f, 0.25f, 0.9f);
                edge.GetComponent<Image>().raycastTarget = false;
                edge.SetActive(false);
            }
        }

        private TextMeshProUGUI CreateText(string name, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(_hud.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size;
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = 22;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.color = Color.white;
            text.outlineWidth = 0.2f;
            return text;
        }

        private void OnDestroy()
        {
            if (_eventSystem != null)
                _eventSystem.sendNavigationEvents = _previousNavigationEvents;
            if (_hud != null)
                Destroy(_hud);
        }
    }
}