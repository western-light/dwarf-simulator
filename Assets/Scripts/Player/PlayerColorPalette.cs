using System.Collections.Generic;
using UnityEngine;

namespace DwarfSimulator.Player
{
    public sealed class PlayerColorPalette : ScriptableObject
    {
        [SerializeField, ColorUsage(false)] private List<Color> _colors = new();

        public IReadOnlyList<Color> Colors => _colors;
    }
}
