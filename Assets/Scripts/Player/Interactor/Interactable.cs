using PurrNet;
using UnityEngine;
using UnityEngine.Events;

namespace DwarfSimulator.Player.Interactor
{
    public class Interactable : NetworkIdentity
    {
        [SerializeField] private Choice[] choices;

        public Choice[] Choices => choices;
    }

    [System.Serializable]
    public class Choice
    {
        public string Name;
        public Sprite Icon;
        public Color Color;
        public UnityEvent<Player> OnInteract;
        public bool Default;
    }
}