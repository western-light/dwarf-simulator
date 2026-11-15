using UnityEngine;
using System;
using System.Collections.Generic;

namespace DwarfSimulator.Player
{
    public class PlayerCustomisationLoader : MonoBehaviour 
    {
        [SerializeField] private List<Color> _colorValues;
        [SerializeField] private GameObject _bodyForColor;
        [SerializeField] private List<GameObject> _beardValues;
        [SerializeField] private List<GameObject> _hairValues;
        [SerializeField] private List<GameObject> _capeValues;

        private const string SaveKey = "PlayerCustomisation";

        public PlayerCustomisation CurrentCustomisation { get; private set; }

        public static PlayerCustomisation LoadLocal()
        {
            string json = PlayerPrefs.GetString(SaveKey, "");

            if (string.IsNullOrEmpty(json))
                return new PlayerCustomisation();

            return JsonUtility.FromJson<PlayerCustomisation>(json);
        }

        public void LoadLocalCustomisation()
        {
            ApplyCustomisation(LoadLocal());
        }

        public void ApplyCustomisation(PlayerCustomisation data)
        {
            CurrentCustomisation = data;

            UpdateColor(_colorValues, data._colorIndex, _bodyForColor);
            UpdatePart(_beardValues, data._beardIndex);
            UpdatePart(_hairValues, data._hairIndex);
            UpdatePart(_capeValues, data._capeIndex);
        }

        private void UpdateColor(List<Color> colors, int index, GameObject obj)
        {
            SkinnedMeshRenderer renderer = obj.GetComponent<SkinnedMeshRenderer>();

            if (renderer == null)
            {
                Debug.LogError("SkinnedMeshRenderer not found on " + obj.name);
                return;
            }

            Material mat = new Material(renderer.sharedMaterial);
            mat.color = colors[index];

            renderer.material = mat;
        }

        private void UpdatePart(List<GameObject> parts, int index)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null)
                {
                    parts[i].SetActive(i == index);
                }
            }
        }

        public int GetCount(CustomizationType type)
        {
            return type switch
            {
                CustomizationType.Color => _colorValues.Count,
                CustomizationType.Beard => _beardValues.Count,
                CustomizationType.Hair => _hairValues.Count,
                CustomizationType.Cape => _capeValues.Count,
                _ => 0
            };
        }
    }
}
