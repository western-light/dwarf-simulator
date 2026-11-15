using System;
using UnityEngine;

namespace DwarfSimulator.Player
{
    [Serializable]
    public struct PlayerCustomisation
    {
        public int _colorIndex;
        public int _beardIndex;
        public int _hairIndex;
        public int _capeIndex;
    }

    public enum CustomizationType
    {
        Color,
        Beard,
        Hair,
        Cape
    }
}
