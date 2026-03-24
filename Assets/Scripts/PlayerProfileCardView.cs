using TMPro;
using UnityEngine;

/// <summary>
/// UI карточка профиля игрока: 3D модель, ник, уровень и характеристики.
/// </summary>
public sealed class PlayerProfileCardView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _nicknameText;
    [SerializeField] private TextMeshProUGUI _levelText;
    [SerializeField] private TextMeshProUGUI _strengthText;
    [SerializeField] private TextMeshProUGUI _enduranceText;
    [SerializeField] private TextMeshProUGUI _accuracyText;
    [SerializeField] private Transform _modelAnchor;
    [SerializeField] private float _modelRotateSpeed = 30f;

    public Transform ModelAnchor => _modelAnchor;

    public void SetData(string nickname, int level, int strength, int endurance, int accuracy)
    {
        if (_nicknameText != null) _nicknameText.text = string.IsNullOrWhiteSpace(nickname) ? "Player" : nickname;
        if (_levelText != null) _levelText.text = $"Level {Mathf.Max(1, level)}";
        if (_strengthText != null) _strengthText.text = $"Сила: {Mathf.Max(0, strength)}";
        if (_enduranceText != null) _enduranceText.text = $"Выносливость: {Mathf.Max(0, endurance)}";
        if (_accuracyText != null) _accuracyText.text = $"Меткость: {Mathf.Max(0, accuracy)}";
    }

    private void Update()
    {
        if (_modelAnchor != null)
            _modelAnchor.Rotate(0f, _modelRotateSpeed * Time.unscaledDeltaTime, 0f, Space.World);
    }
}
