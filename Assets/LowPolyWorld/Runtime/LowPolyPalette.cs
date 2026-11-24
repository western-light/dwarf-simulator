using System;
using UnityEngine;

namespace DwarfSimulator.LowPolyWorld
{
    [Serializable]
    public sealed class LowPolyPalette
    {
        [Header("Terrain")]
        public Color grassShadow = new Color(0.31f, 0.48f, 0.20f, 1f);
        public Color grassLight = new Color(0.58f, 0.76f, 0.36f, 1f);
        public Color rockShadow = new Color(0.29f, 0.34f, 0.34f, 1f);
        public Color rockLight = new Color(0.58f, 0.62f, 0.58f, 1f);
        public Color snowShadow = new Color(0.70f, 0.80f, 0.83f, 1f);
        public Color snowLight = new Color(0.94f, 0.97f, 0.96f, 1f);

        [Header("Routes")]
        public Color asphalt = new Color(0.24f, 0.26f, 0.27f, 1f);
        public Color dirt = new Color(0.43f, 0.30f, 0.19f, 1f);
        public Color gravel = new Color(0.42f, 0.43f, 0.40f, 1f);
        public Color cityShoulder = new Color(0.48f, 0.49f, 0.47f, 1f);
        public Color cityMarking = new Color(0.90f, 0.76f, 0.32f, 1f);
        public Color path = new Color(0.48f, 0.34f, 0.20f, 1f);
        public Color wornAsphalt = new Color(0.32f, 0.32f, 0.30f, 1f);
        public Color roadCrack = new Color(0.12f, 0.12f, 0.11f, 1f);
        public Color packedSnow = new Color(0.73f, 0.83f, 0.88f, 1f);
        public Color snowTrack = new Color(0.53f, 0.66f, 0.73f, 1f);
        public Color earthFill = new Color(0.31f, 0.23f, 0.15f, 1f);
    }
}
