using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DwarfSimulator.Player
{
    public class PlayerCustomisationLoader : MonoBehaviour
    {
        private const string SaveKey = "PlayerCustomisation";
        private const string SaveVersionKey = SaveKey + "Version";
        private const int SaveVersion = 1;
        private const string GnomeResourcePath = "Player/gnome";
        private const string ColorPaletteResourcePath = "Player/GnomeColorPalette";

        [SerializeField] private GameObject _bodyForColor;

        [SerializeField] private List<GameObject> _beardValues = new();
        [SerializeField] private List<GameObject> _browsValues = new();
        [SerializeField] private List<GameObject> _eyesValues = new();
        [SerializeField] private List<GameObject> _hairValues = new();
        [SerializeField] private List<GameObject> _hatValues = new();
        [SerializeField] private List<GameObject> _noseValues = new();
        [SerializeField] private List<GameObject> _shoesValues = new();

        private bool _bindingsReady;
        private IReadOnlyList<Color> _colorValues = Array.Empty<Color>();
        private Renderer _colorRenderer;
        private MaterialPropertyBlock _colorProperties;

        public PlayerCustomisation CurrentCustomisation { get; private set; }

        public static PlayerCustomisation LoadLocal()
        {
            string json = PlayerPrefs.GetString(SaveKey, "");

            if (string.IsNullOrEmpty(json))
                return new PlayerCustomisation();

            var data = JsonUtility.FromJson<PlayerCustomisation>(json);
            if (PlayerPrefs.GetInt(SaveVersionKey) == 0)
            {
                // The old lists placed two beard bones and three hat bones before the meshes.
                data._beardIndex = data._beardIndex > 2 ? data._beardIndex - 2 : 0;
                data._hatIndex = data._hatIndex > 3 ? data._hatIndex - 3 : 0;
            }

            return data;
        }

        public static void SaveLocal(PlayerCustomisation data)
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
            PlayerPrefs.SetInt(SaveVersionKey, SaveVersion);
            PlayerPrefs.Save();
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

            var palette = Resources.Load<PlayerColorPalette>(ColorPaletteResourcePath);
            if (palette != null)
                _colorValues = palette.Colors;

            Transform modelRoot = FindGnomeRoot();
            if (modelRoot == null)
                modelRoot = CreateGnomeVisual();

            if (modelRoot == null)
            {
                Debug.LogError("Le modele gnome est introuvable pour la personnalisation.", this);
                _bindingsReady = true;
                return;
            }

            Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            RestoreLegacyAccessoryBones(renderers);

            BindParts(ref _beardValues, renderers, "Beard_", true);
            BindParts(ref _browsValues, renderers, "Brows_", false);
            BindParts(ref _eyesValues, renderers, "Eyes_", false);
            BindParts(ref _hairValues, renderers, "Hair_", true);
            BindParts(ref _hatValues, renderers, "Hat_", true);
            BindParts(ref _noseValues, renderers, "Nose_", false);
            BindParts(ref _shoesValues, renderers, "Shoes_", false);

            if (_bodyForColor == null || !_bodyForColor.transform.IsChildOf(modelRoot))
                _bodyForColor = FindColorTarget(renderers);
            if (_bodyForColor != null)
                _colorRenderer = _bodyForColor.GetComponent<Renderer>();

            _bindingsReady = true;
        }

        private Transform FindGnomeRoot()
        {
            Transform[] parts = GetComponentsInChildren<Renderer>(true)
                .Where(renderer => IsPartRenderer(renderer) && IsCustomPart(renderer.name))
                .Select(renderer => renderer.transform)
                .ToArray();
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

        private void RestoreLegacyAccessoryBones(Renderer[] renderers)
        {
            // Some older previews saved the listed rig bones with their GameObjects disabled.
            var inactiveParts = _beardValues.Concat(_hatValues)
                .Where(part => part != null && !part.activeSelf)
                .ToHashSet();
            if (inactiveParts.Count == 0)
                return;

            foreach (var renderer in renderers.OfType<SkinnedMeshRenderer>())
                foreach (Transform bone in renderer.bones)
                    if (bone != null && inactiveParts.Remove(bone.gameObject))
                        bone.gameObject.SetActive(true);
        }

        private static bool IsPartRenderer(Renderer renderer) => renderer is MeshRenderer or SkinnedMeshRenderer;

        private static void BindParts(ref List<GameObject> values, Renderer[] renderers, string prefix, bool allowNone)
        {
            var options = renderers
                .Where(renderer => IsPartRenderer(renderer) && renderer.name.StartsWith(prefix, StringComparison.Ordinal))
                .Select(renderer => renderer.gameObject)
                .ToHashSet();
            var configuredOptions = values?.Where(options.Contains).Distinct().ToList();
            values = configuredOptions is { Count: > 0 }
                ? configuredOptions
                : options.OrderBy(part => part.name, StringComparer.Ordinal).ToList();
            if (allowNone)
                values.Insert(0, null);
        }

        private static GameObject FindColorTarget(Renderer[] renderers)
        {
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
                CustomizationType.Color => _colorValues.Count,
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
