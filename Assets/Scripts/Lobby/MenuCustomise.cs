using UnityEngine;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;

namespace DwarfSimulator.Lobby
{
    public class MenuCustomise : MonoBehaviour
    {
        [SerializeField] private GameObject _PlayerPrefab;
        [Space]
        [SerializeField] private GameObject _ColorLine;
        [SerializeField] private List<Color> _ColorValues;
        [Space]
        [SerializeField] private GameObject _BarberLine;
        [SerializeField] private List<GameObject> _BaberValues;
        [Space]
        [SerializeField] private GameObject _HairLine;
        [SerializeField] private List<GameObject> _HairValues;
        [Space]
        [SerializeField] private GameObject _CapeLine;
        [SerializeField] private List<GameObject> _CapeValues;

        void Start()
        {
             
        }
    }
}
