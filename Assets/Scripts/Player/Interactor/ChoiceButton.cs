using System;
using PurrNet.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DwarfSimulator.Player.Interactor
{
    public class ChoiceButton : Selectable
    {
        [SerializeField] private TextMeshProUGUI choiceText;
        [SerializeField] private Image choiceIcon;
        [SerializeField] private RectangleGraphic choiceBackground;
        private Action _onSelected;

        public void SetChoice(Choice choice, Action onSelected)
        {
            choiceText.SetText(choice.Name);
            choiceIcon.sprite = choice.Icon;
            choiceIcon.enabled = choice.Icon != null;
            choiceBackground.color = choice.Color;

            _onSelected = onSelected;
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            base.OnPointerEnter(eventData);
            Select();
        }

        public override void OnPointerDown(PointerEventData eventData) { }

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            _onSelected?.Invoke();
        }
    }
}