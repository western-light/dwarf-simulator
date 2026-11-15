using UnityEngine;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

namespace DwarfSimulator.Lobby
{
    public class MenuCustomise : MonoBehaviour
    {
        [SerializeField] private GameObject _playerPrefab;
        [Space]
        [SerializeField] private GameObject _colorLine;
        [SerializeField] private List<Color> _colorValues;
        [Space]
        [SerializeField] private GameObject _beardLine;
        [SerializeField] private List<GameObject> _beardValues;
        [Space]
        [SerializeField] private GameObject _hairLine;
        [SerializeField] private List<GameObject> _hairValues;
        [Space]
        [SerializeField] private GameObject _capeLine;
        [SerializeField] private List<GameObject> _capeValues;

        [Space]
        [Header("Index")]
        [SerializeField] private int _colorIndex = 0;
        [SerializeField] private int _beardIndex = 0;
        [SerializeField] private int _hairIndex = 0;
        [SerializeField] private int _capeIndex = 0;

        void Start()
        {
            InitLine(_colorLine, () => _colorIndex, v => _colorIndex = v, _colorValues.Count);
            InitLine(_beardLine, () => _beardIndex, v => _beardIndex = v, _beardValues.Count);
            InitLine(_hairLine, () => _hairIndex, v => _hairIndex = v, _hairValues.Count);
            InitLine(_capeLine, () => _capeIndex, v => _capeIndex = v, _capeValues.Count);

            UpdateModel();
        }

        void InitLine(GameObject line, Func<int> getIndex, Action<int> setIndex, int maxValue)
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

        void UpdateModel()
        {
            UpdatePart(_beardValues, _beardIndex);
            UpdatePart(_hairValues, _hairIndex);
            UpdatePart(_capeValues, _capeIndex);
        }

        void UpdatePart(List<GameObject> parts, int index)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null)
                {
                    parts[i].SetActive(i == index);
                }
            }
        }
    }
}
