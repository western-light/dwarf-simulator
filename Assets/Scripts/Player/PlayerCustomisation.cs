using System;
using UnityEngine;

namespace DwarfSimulator.Player
{
    [Serializable]
    public struct PlayerCustomisation
    {
        public int _colorIndex;
        public int _beardIndex;
        public int _browsIndex;
        public int _eyesIndex;
        public int _hairIndex;
        public int _hatIndex;
        public int _noseIndex;
        public int _shoesIndex;
    }

    public enum CustomizationType
    {
        Color,
        Beard,
        Brows,
        Eyes,
        Hair,
        Hat,
        Nose,
        Shoes
    }
}
