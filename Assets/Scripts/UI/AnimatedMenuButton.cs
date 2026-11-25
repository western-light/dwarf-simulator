using UnityEngine.UI;

namespace DwarfSimulator.UI
{
    public sealed class AnimatedMenuButton : Button
    {
        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);

            if (transition != Transition.Animation || !targetGraphic || !gameObject.activeInHierarchy)
                return;

            // The pack's controller animates scale. Tint the icon separately for disabled buttons.
            ColorBlock palette = colors;
            var tint = state == SelectionState.Disabled ? palette.disabledColor : palette.normalColor;
            targetGraphic.CrossFadeColor(tint, instant ? 0f : palette.fadeDuration, true, true);
        }
    }
}
