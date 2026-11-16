using UnityEngine;
using UnityEngine.UI;

namespace DwarfSimulator.UI
{
    public class AutoFitGridLayoutGroup : GridLayoutGroup
    {
        public override void SetLayoutHorizontal()
        {
            Fit();
            base.SetLayoutHorizontal();
        }

        public override void SetLayoutVertical()
        {
            Fit();
            base.SetLayoutVertical();

            if (rectChildren.Count == 3)
            {
                float width = rectTransform.rect.width - padding.horizontal;
                SetChildAlongAxis(rectChildren[2], 0, padding.left, Mathf.Max(0, width));
            }
        }

        private void Fit()
        {
            int count = rectChildren.Count; // ignore les enfants inactifs
            if (count == 0) return;

            // 1 -> 1x1, 2 -> 2x1, 3 -> 2x2, 4 -> 2x2
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            int rows = Mathf.CeilToInt(count / (float)cols);

            Rect r = rectTransform.rect;
            float w = (r.width - padding.horizontal - spacing.x * (cols - 1)) / cols;
            float h = (r.height - padding.vertical - spacing.y * (rows - 1)) / rows;

            m_Constraint = Constraint.FixedColumnCount;
            m_ConstraintCount = cols;
            m_CellSize = new Vector2(Mathf.Max(0, w), Mathf.Max(0, h));
        }
    }
}