using System;
using System.Collections.Generic;
using UnityEngine;

namespace DwarfSimulator.Player.Interactor
{
    public class InteractorUI : MonoBehaviour
    {
        [SerializeField] private GameObject interactButtonPrefab;
        [SerializeField] private GameObject interactUI;
        [SerializeField] private Transform interactUITransform;

        private List<GameObject> _choiceObjects;
        private Action _onClosed;
        private Choice _selectedChoice;
        private Player _interactor;

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
                interactButtonPrefab.GetComponent<ChoiceButton>() == null)
            {
                Debug.LogError("InteractorUI requires a panel, a button container and a ChoiceButton prefab.", this);
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
            interactUI.SetActive(true);
            selectedButton.Select();
            return true;
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