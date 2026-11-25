using PurrNet;
using UnityEngine;
using UnityEngine.Events;

namespace DwarfSimulator.Player.Interactor
{
    public class Interactable : NetworkIdentity
    {
        [SerializeField] private Choice[] choices;
        [SerializeField] private string displayName;
        [SerializeField] private bool showTargetFrame = true;

        public Choice[] Choices => choices;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;
        public bool ShowTargetFrame => showTargetFrame;

        public string UnavailableReason(int index, Player player)
        {
            var provider = GetComponent<IInteractionAvailability>();
            return provider != null ? provider.UnavailableReason(index, player) : null;
        }

        public string ChoiceName(int index, Player player)
        {
            var provider = GetComponent<IInteractionLabel>();
            return provider != null ? provider.ChoiceName(index, player, choices[index].Name) : choices[index].Name;
        }

        public int DefaultChoice(Player player)
        {
            if (choices == null)
                return -1;
            int first = -1;
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] == null || UnavailableReason(i, player) != null)
                    continue;
                if (first < 0)
                    first = i;
                if (choices[i].Default)
                    return i;
            }
            return first;
        }
    }

    public interface IInteractionAvailability
    {
        string UnavailableReason(int choiceIndex, Player player);
    }

    public interface IInteractionLabel
    {
        string ChoiceName(int choiceIndex, Player player, string defaultName);
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