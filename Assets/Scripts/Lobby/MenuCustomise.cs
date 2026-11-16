using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using DwarfSimulator.Player; 

namespace DwarfSimulator.Lobby
{
    public class MenuCustomise : MonoBehaviour
    {
        private const string PERSO_PREF = "PlayerCustomisation";

        [SerializeField] private Button _saveButton;
        [Space]
        [SerializeField] private GameObject _colorLine;
        [SerializeField] private GameObject _bodyForColor;
        [Space]
        [SerializeField] private GameObject _beardLine;
        [Space]
        [SerializeField] private GameObject _hairLine;
        [Space]
        [SerializeField] private GameObject _capeLine;

        [Space]
        [Header("Index")]
        [SerializeField] private PlayerCustomisation _persoIndex = new PlayerCustomisation();

        private PlayerCustomisationLoader _playerCustoLoader;

        void Start()
        {
            _playerCustoLoader = gameObject.GetComponent<PlayerCustomisationLoader>();
            LoadPersonalisation(); 

            InitLine(_colorLine, () => _persoIndex._colorIndex, v => _persoIndex._colorIndex = v, _playerCustoLoader.GetCount(CustomizationType.Color));
            InitLine(_beardLine, () => _persoIndex._beardIndex, v => _persoIndex._beardIndex = v, _playerCustoLoader.GetCount(CustomizationType.Beard));
            InitLine(_hairLine, () => _persoIndex._hairIndex, v => _persoIndex._hairIndex = v, _playerCustoLoader.GetCount(CustomizationType.Hair));
            InitLine(_capeLine, () => _persoIndex._capeIndex, v => _persoIndex._capeIndex = v, _playerCustoLoader.GetCount(CustomizationType.Cape));

            _saveButton.onClick.AddListener(() => SavePersonalisation());

            UpdateModel();
        }

        private void InitLine(GameObject line, Func<int> getIndex, Action<int> setIndex, int maxValue)
        {
            Button leftBtn = line.transform.Find("left")?.GetComponent<Button>();
            TMP_Text lineIndexText = line.transform.Find("label/value")?.GetComponent<TMP_Text>();
            Button rightBtn = line.transform.Find("right")?.GetComponent<Button>();

            void UpdateButtons()
            {
                leftBtn.interactable = getIndex() > 0;
                rightBtn.interactable = getIndex() < maxValue - 1;
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

        private void SavePersonalisation()
        {
            string json = JsonUtility.ToJson(_persoIndex);

            PlayerPrefs.SetString(PERSO_PREF, json);
            PlayerPrefs.Save();
        }

        private void LoadPersonalisation()
        {
            if (PlayerPrefs.HasKey(PERSO_PREF))
            {
                string json = PlayerPrefs.GetString(PERSO_PREF);
                _persoIndex = JsonUtility.FromJson<PlayerCustomisation>(json);
            }
            else
            {
                _persoIndex = new PlayerCustomisation();
            }
        }
    }
}