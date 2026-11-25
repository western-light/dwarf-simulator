using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using DwarfSimulator.Player;
using UnityEngine.Serialization;

namespace DwarfSimulator.Lobby
{
    public class MenuCustomise : MonoBehaviour
    {
        [SerializeField] private Button _saveButton;
        [Space]
        [SerializeField] private GameObject _colorLine;
        [SerializeField] private GameObject _bodyForColor;
        [Space]
        [SerializeField] private GameObject _beardLine;
        [Space]
        [FormerlySerializedAs("_capeLine")]
        [SerializeField] private GameObject _browsLine;
        [SerializeField] private GameObject _eyesLine;
        [SerializeField] private GameObject _hairLine;
        [SerializeField] private GameObject _hatLine;
        [SerializeField] private GameObject _noseLine;
        [SerializeField] private GameObject _shoesLine;

        [Space]
        [Header("Index")]
        [SerializeField] private PlayerCustomisation _persoIndex = new PlayerCustomisation();

        private PlayerCustomisationLoader _playerCustoLoader;

        private void Start()
        {
            _playerCustoLoader = gameObject.GetComponent<PlayerCustomisationLoader>();
            if (_playerCustoLoader == null)
            {
                Debug.LogError("PlayerCustomisationLoader introuvable sur le menu de personnalisation.", this);
                enabled = false;
                return;
            }

            _persoIndex = PlayerCustomisationLoader.LoadLocal();

            // The old menu only had four rows. Missing rows are generated from the
            // same UI template so existing scenes are migrated without manual wiring.
            _browsLine = EnsureLine(_browsLine, _beardLine, "Brows");
            _eyesLine = EnsureLine(_eyesLine, _beardLine, "Eyes");
            _hatLine = EnsureLine(_hatLine, _beardLine, "Hat");
            _noseLine = EnsureLine(_noseLine, _beardLine, "Nose");
            _shoesLine = EnsureLine(_shoesLine, _beardLine, "Shoes");
            OrderLines(_colorLine, _beardLine, _browsLine, _eyesLine,
                _hairLine, _hatLine, _noseLine, _shoesLine);

            InitLine(_colorLine, "Color", () => _persoIndex._colorIndex, v => _persoIndex._colorIndex = v, CustomizationType.Color);
            InitLine(_beardLine, "Beard", () => _persoIndex._beardIndex, v => _persoIndex._beardIndex = v, CustomizationType.Beard);
            InitLine(_browsLine, "Brows", () => _persoIndex._browsIndex, v => _persoIndex._browsIndex = v, CustomizationType.Brows);
            InitLine(_eyesLine, "Eyes", () => _persoIndex._eyesIndex, v => _persoIndex._eyesIndex = v, CustomizationType.Eyes);
            InitLine(_hairLine, "Hair", () => _persoIndex._hairIndex, v => _persoIndex._hairIndex = v, CustomizationType.Hair);
            InitLine(_hatLine, "Hat", () => _persoIndex._hatIndex, v => _persoIndex._hatIndex = v, CustomizationType.Hat);
            InitLine(_noseLine, "Nose", () => _persoIndex._noseIndex, v => _persoIndex._noseIndex = v, CustomizationType.Nose);
            InitLine(_shoesLine, "Shoes", () => _persoIndex._shoesIndex, v => _persoIndex._shoesIndex = v, CustomizationType.Shoes);

            _saveButton.onClick.AddListener(SavePersonalisation);

            UpdateModel();
        }

        private GameObject EnsureLine(GameObject line, GameObject template, string displayName)
        {
            if (line == null && template != null)
                line = Instantiate(template, template.transform.parent);
            if (line == null)
                return null;

            line.name = displayName;
            TMP_Text nameText = FindNameText(line.transform);
            if (nameText != null)
                nameText.text = $"{displayName} : ";
            return line;
        }

        private static TMP_Text FindNameText(Transform line)
        {
            return line.Find("label/label")?.GetComponent<TMP_Text>()
                   ?? line.Find("label/name")?.GetComponent<TMP_Text>();
        }

        private static void OrderLines(params GameObject[] lines)
        {
            int firstIndex = int.MaxValue;
            foreach (GameObject line in lines)
                if (line != null)
                    firstIndex = Mathf.Min(firstIndex, line.transform.GetSiblingIndex());

            if (firstIndex == int.MaxValue)
                return;

            for (int i = 0; i < lines.Length; i++)
                if (lines[i] != null)
                    lines[i].transform.SetSiblingIndex(firstIndex + i);
        }

        private void InitLine(GameObject line, string displayName, Func<int> getIndex,
            Action<int> setIndex, CustomizationType type)
        {
            if (line == null)
            {
                Debug.LogWarning($"Ligne de personnalisation '{displayName}' introuvable.", this);
                return;
            }

            Button leftBtn = line.transform.Find("left")?.GetComponent<Button>();
            TMP_Text lineIndexText = line.transform.Find("label/value")?.GetComponent<TMP_Text>();
            Button rightBtn = line.transform.Find("right")?.GetComponent<Button>();
            TMP_Text nameText = FindNameText(line.transform);
            int maxValue = _playerCustoLoader.GetCount(type);

            if (leftBtn == null || rightBtn == null || lineIndexText == null)
            {
                Debug.LogError($"La ligne '{displayName}' ne respecte pas la hierarchie left/label/value/right.", line);
                return;
            }

            if (nameText != null)
                nameText.text = $"{displayName} : ";

            int initialIndex = maxValue > 0 ? Mathf.Clamp(getIndex(), 0, maxValue - 1) : 0;
            setIndex(initialIndex);

            void UpdateButtons()
            {
                leftBtn.interactable = maxValue > 0 && getIndex() > 0;
                rightBtn.interactable = maxValue > 0 && getIndex() < maxValue - 1;
            }

            void UpdateIndex(int delta)
            {
                int newIndex = getIndex() + delta;

                if (newIndex >= 0 && newIndex < maxValue)
                {
                    setIndex(newIndex);
                    lineIndexText.text = newIndex.ToString();

                    UpdateModel();
                }

                UpdateButtons();
            }

            leftBtn.onClick.AddListener(() => UpdateIndex(-1));
            rightBtn.onClick.AddListener(() => UpdateIndex(1));

            lineIndexText.text = getIndex().ToString();

            UpdateButtons();
        }

        private void UpdateModel()
        {
            _playerCustoLoader.ApplyCustomisation(_persoIndex);
        }

        private void SavePersonalisation() => PlayerCustomisationLoader.SaveLocal(_persoIndex);
    }
}
