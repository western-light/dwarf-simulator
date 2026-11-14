using PurrNet;
using UnityEngine;
using UnityEngine.UI;

namespace DwarfSimulator.Player.Interactor
{
    public class Interactable : NetworkIdentity
    {
        [SerializeField] private Choice[] choices;
        
        public void Interact()
        {
            FindAnyObjectByType<InteractorUI>().Interact(choices);
        }

        public void StopInteract()
        {
            FindAnyObjectByType<InteractorUI>().StopInteract();
        }
    }

    [System.Serializable]
    public class Choice
    {
        public string Name;
        public Sprite Icon;
        public Color Color;
        public Button.ButtonClickedEvent OnInteract;
        public bool Default;
    }
}