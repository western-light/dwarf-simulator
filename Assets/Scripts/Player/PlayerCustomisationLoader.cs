using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DwarfSimulator.Player
{
    public class PlayerCustomisationLoader : MonoBehaviour
    {
        private const string SaveKey = "PlayerCustomisation";
        private const string GnomeResourcePath = "Player/gnome";

        private static readonly Color[] DefaultColors =
        {
            new(1f, 0.882f, 0f, 1f),
            new(1f, 0f, 0f, 1f),
            new(0f, 1f, 0.015f, 1f),
            new(0.969f, 0f, 1f, 1f)
        };

        [SerializeField] private List<Color> _colorValues = new();
        [SerializeField] private GameObject _bodyForColor;

        [SerializeField] private List<GameObject> _beardValues = new();
        [SerializeField] private List<GameObject> _browsValues = new();
        [SerializeField] private List<GameObject> _eyesValues = new();
        [SerializeField] private List<GameObject> _hairValues = new();
        [SerializeField] private List<GameObject> _hatValues = new();
        [SerializeField] private List<GameObject> _noseValues = new();
        [SerializeField] private List<GameObject> _shoesValues = new();

        private bool _bindingsReady;
        private Renderer _colorRenderer;
        private MaterialPropertyBlock _colorProperties;

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
            EnsureBindings();
            CurrentCustomisation = data;

            UpdateColor(data._colorIndex);
            UpdatePart(_beardValues, data._beardIndex);
            UpdatePart(_browsValues, data._browsIndex);
            UpdatePart(_eyesValues, data._eyesIndex);
            UpdatePart(_hairValues, data._hairIndex);
            UpdatePart(_hatValues, data._hatIndex);
            UpdatePart(_noseValues, data._noseIndex);
            UpdatePart(_shoesValues, data._shoesIndex);
        }

        private void EnsureBindings()
        {
            if (_bindingsReady)
                return;

            Transform modelRoot = FindGnomeRoot();
            if (modelRoot == null)
                modelRoot = CreateGnomeVisual();

            if (modelRoot == null)
            {
                Debug.LogError("Le modele gnome est introuvable pour la personnalisation.", this);
                _bindingsReady = true;
                return;
            }

            BindParts(ref _beardValues, modelRoot, "Beard_", true);
            BindParts(ref _browsValues, modelRoot, "Brows_", false);
            BindParts(ref _eyesValues, modelRoot, "Eyes_", false);
            BindParts(ref _hairValues, modelRoot, "Hair_", true);
            BindParts(ref _hatValues, modelRoot, "Hat_", true);
            BindParts(ref _noseValues, modelRoot, "Nose_", false);
            BindParts(ref _shoesValues, modelRoot, "Shoes_", false);

            if (_colorValues == null)
                _colorValues = new List<Color>();
            if (_colorValues.Count == 0)
                _colorValues.AddRange(DefaultColors);

            if (_bodyForColor == null || !_bodyForColor.transform.IsChildOf(modelRoot))
                _bodyForColor = FindColorTarget(modelRoot);
            if (_bodyForColor != null)
                _colorRenderer = _bodyForColor.GetComponent<Renderer>();

            _bindingsReady = true;
        }

        private Transform FindGnomeRoot()
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            Transform[] parts = children.Where(t => IsCustomPart(t.name)).ToArray();
            if (parts.Length == 0)
                return null;

            Transform commonRoot = parts[0];
            while (commonRoot != null && parts.Any(part => !part.IsChildOf(commonRoot)))
                commonRoot = commonRoot.parent;
            return commonRoot != null && commonRoot.IsChildOf(transform) ? commonRoot : transform;
        }

        private Transform CreateGnomeVisual()
        {
            GameObject prefab = Resources.Load<GameObject>(GnomeResourcePath);
            if (prefab == null)
                return null;

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            foreach (Animator animator in GetComponentsInChildren<Animator>(true))
                animator.enabled = false;

            GameObject visual = Instantiate(prefab, transform);
            visual.name = "GnomeCustomisationVisual";
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.transform.localScale = Vector3.one;
            return visual.transform;
        }

        private static void BindParts(ref List<GameObject> values, Transform modelRoot, string prefix, bool allowNone)
        {
            bool alreadyBound = values != null && values.Any(value => value != null &&
                value.name.StartsWith(prefix, System.StringComparison.Ordinal) && value.transform.IsChildOf(modelRoot));
            if (alreadyBound)
                return;

            values = modelRoot.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith(prefix, System.StringComparison.Ordinal))
                .OrderBy(t => t.name, System.StringComparer.Ordinal)
                .Select(t => t.gameObject)
                .ToList();
            if (allowNone)
                values.Insert(0, null);
        }

        private static GameObject FindColorTarget(Transform modelRoot)
        {
            Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            Renderer target = renderers.FirstOrDefault(r => r.name == "Gnome")
                              ?? renderers.FirstOrDefault(r => r.name == "Body")
                              ?? renderers.FirstOrDefault(r => !IsCustomPart(r.name));
            return target != null ? target.gameObject : null;
        }

        private static bool IsCustomPart(string objectName)
        {
            return objectName.StartsWith("Beard_") || objectName.StartsWith("Brows_") ||
                   objectName.StartsWith("Eyes_") || objectName.StartsWith("Hair_") ||
                   objectName.StartsWith("Hat_") || objectName.StartsWith("Nose_") ||
                   objectName.StartsWith("Shoes_");
        }

        private void UpdateColor(int index)
        {
            if (_colorRenderer == null || _colorValues.Count == 0)
                return;

            index = Mathf.Clamp(index, 0, _colorValues.Count - 1);
            Color color = _colorValues[index];
            color.a = 1f;

            _colorProperties ??= new MaterialPropertyBlock();
            _colorRenderer.GetPropertyBlock(_colorProperties);
            _colorProperties.SetColor("_BaseColor", color);
            _colorProperties.SetColor("_Color", color);
            _colorRenderer.SetPropertyBlock(_colorProperties);
        }

        private static void UpdatePart(List<GameObject> parts, int index)
        {
            if (parts == null || parts.Count == 0)
                return;

            index = Mathf.Clamp(index, 0, parts.Count - 1);
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null)
                    parts[i].SetActive(i == index);
        }

        public int GetCount(CustomizationType type)
        {
            EnsureBindings();
            return type switch
            {
                CustomizationType.Color => _colorValues?.Count ?? 0,
                CustomizationType.Beard => _beardValues?.Count ?? 0,
                CustomizationType.Brows => _browsValues?.Count ?? 0,
                CustomizationType.Eyes => _eyesValues?.Count ?? 0,
                CustomizationType.Hair => _hairValues?.Count ?? 0,
                CustomizationType.Hat => _hatValues?.Count ?? 0,
                CustomizationType.Nose => _noseValues?.Count ?? 0,
                CustomizationType.Shoes => _shoesValues?.Count ?? 0,
                _ => 0
            };
        }
    }
}
