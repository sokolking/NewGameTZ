using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sets <see cref="UnityEngine.UI.Text"/> from <see cref="Loc"/> on enable.
/// Keep the serialized <see cref="Text.text"/> as an English fallback in the scene/prefab.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Text))]
public class LocalizedText : MonoBehaviour
{
    [SerializeField] string _localizationKey;

    Text _text;

    void Awake() => _text = GetComponent<Text>();

    void OnEnable() => Apply();

    public void Apply()
    {
        if (_text == null)
            _text = GetComponent<Text>();
        if (_text == null || string.IsNullOrEmpty(_localizationKey))
            return;
        _text.text = Loc.T(_localizationKey);
    }
}
