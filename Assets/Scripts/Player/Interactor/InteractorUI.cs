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
        private GameObject[] _choiceObjects;
        
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
            if (_choices != null) return;
            
            _choices = choices;
            interactUI.SetActive(true);
            _choiceObjects = new GameObject[_choices.Length];
            for (int i = 0; i < _choices.Length; i++)
            {
                GameObject button = Instantiate(interactButtonPrefab, interactUITransform);
                _choiceObjects[i] = button;
                
            }
        }

        public void StopInteract()
        {
            _choices = null;
            interactUI.SetActive(false);
            for (int i = 0; i < _choiceObjects.Length; i++)
            {
                Destroy(_choiceObjects[i]);
            }
        }
    }
}