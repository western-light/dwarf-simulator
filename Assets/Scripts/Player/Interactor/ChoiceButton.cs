using PurrNet.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DwarfSimulator.Player.Interactor
{
    public class ChoiceButton : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI choiceText;
        [SerializeField] private Image choiceIcon;
        [SerializeField] private RectangleGraphic choiceBackground;
        [SerializeField] private Button choiceButton;

        public void SetChoice(Choice choice)
        {
            choiceText.SetText(choice.Name);
            choiceIcon.sprite = choice.Icon;
            choiceBackground.color = choice.Color;
            choiceButton.onClick = choice.OnInteract;
        }
    }
}