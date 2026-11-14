using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DwarfSimulator.Player.Interactor
{
    public class InteractorUI : MonoBehaviour
    {
        private const float PointerRange = 150f;
        private const float PointerDeadZone = 10f;

        [SerializeField] private GameObject interactButtonPrefab;
        [SerializeField] private GameObject interactUI;
        [SerializeField] private Transform interactUITransform;

        private List<GameObject> _choiceObjects;
        private Action _onClosed;
        private Choice _selectedChoice;
        private Player _interactor;
        private Vector2 _virtualPointer;

        private void Awake()
        {
            if (interactUI != null)
                interactUI.SetActive(false);
            else
                Debug.LogError("InteractorUI requires an interaction panel.", this);
        }
        
        public bool Interact(Choice[] choices, Player interactor, Action onClosed)
        {
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
            bool hasDefault = false;
            foreach (Choice choice in choices)
            {
                if (choice == null)
                {
                    Debug.LogWarning("An interaction choice is missing.", this);
                    continue;
                }

                GameObject button = Instantiate(interactButtonPrefab, interactUITransform);
                _choiceObjects.Add(button);
                ChoiceButton choiceButton = button.GetComponent<ChoiceButton>();
                choiceButton.SetChoice(choice, () => _selectedChoice = choice);
                if (selectedButton == null || (choice.Default && !hasDefault))
                {
                    selectedButton = choiceButton;
                    initialChoice = choice;
                }
                hasDefault |= choice.Default;
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
            interactUI.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)interactUITransform);
            selectedButton.Select();
            return true;
        }

        public void UpdateSelection()
        {
            if (_choiceObjects == null || Mouse.current == null)
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
            StopInteract();
            choice?.OnInteract?.Invoke(interactor);
        }

        public void StopInteract()
        {
            if (_choiceObjects == null)
                return;

            List<GameObject> buttons = _choiceObjects;
            _choiceObjects = null;
            _selectedChoice = null;
            _interactor = null;
            interactUI.SetActive(false);
            foreach (GameObject button in buttons)
                Destroy(button);

            Action onClosed = _onClosed;
            _onClosed = null;
            onClosed?.Invoke();
        }
    }
}