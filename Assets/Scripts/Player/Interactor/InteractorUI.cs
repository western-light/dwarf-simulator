using UnityEngine;
using UnityEngine.InputSystem;

namespace DwarfSimulator.Player.Interactor
{
    public class InteractorUI : MonoBehaviour
    {
        [SerializeField] private GameObject interactButtonPrefab;
        [SerializeField] private GameObject interactUI;
        [SerializeField] private Transform interactUITransform;
        
        private Camera _cam;
        private Choice[] _choices;
        
        private void Awake()
        {
            _cam = Camera.main;
            StopInteract();
        }

        private void Update()
        {
            if (_choices == null) return;
            
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            
            Vector2 mousePosition = mouse.position.ReadValue();
            
        }

        public void Interact(Choice[] choices)
        {
            _choices = choices;
            interactUI.SetActive(true);
            foreach (Choice choice in _choices)
            {
                Instantiate(interactButtonPrefab, interactUITransform);
            }
        }

        public void StopInteract()
        {
            _choices = null;
            interactUI.SetActive(false);
        }
    }
}