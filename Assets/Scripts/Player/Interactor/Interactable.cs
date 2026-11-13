using System;
using PurrNet;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Color = System.Drawing.Color;

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
        public Image Icon;
        public Color Color;
        public UnityEvent OnInteract;
    }
}