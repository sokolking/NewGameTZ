using System;
using UnityEngine;

/// <summary>
/// Loads the logged-in user profile via <c>/ws/session</c> and renders <see cref="UnitCardView"/>.
/// </summary>
public sealed class PlayerProfileCardController : MonoBehaviour
{
    [SerializeField] private UnitCardView _unitCardView;
    [Tooltip("If true, request profile when this object becomes enabled (MainMenu open / card shown).")]
    [SerializeField] private bool _loadOnStart = true;

    private void OnEnable()
    {
        SessionWebSocketConnection.OnUserProfileReceived += OnUserProfileReceived;
        if (_loadOnStart)
            TryRequestProfileFromSession();
    }

    private void OnDisable()
    {
        SessionWebSocketConnection.OnUserProfileReceived -= OnUserProfileReceived;
    }

    /// <summary>Re-request profile over <c>/ws/session</c> (also sent automatically right after session socket connects).</summary>
    public void Reload() => TryRequestProfileFromSession();

    private void TryRequestProfileFromSession()
    {
        if (_unitCardView == null)
            _unitCardView = GetComponent<UnitCardView>();
        if (_unitCardView == null)
        {
            Debug.LogWarning("[PlayerProfileCard] no UnitCardView on this object");
            return;
        }

        if (string.IsNullOrWhiteSpace(BattleSessionState.AccessToken))
        {
            Debug.LogWarning("[PlayerProfileCard] no access token — profile request skipped");
            return;
        }

        SessionWebSocketConnection.EnsureStarted();
        SessionWebSocketConnection.SendUserProfileRequest();
    }

    private void OnUserProfileReceived(UserProfileSocketDto dto)
    {
        if (_unitCardView == null)
            _unitCardView = GetComponent<UnitCardView>();
        if (_unitCardView == null || dto == null)
            return;
        var p = new UnitCardPayload
        {
            DisplayName = dto.username ?? "",
            Level = Math.Max(1, dto.level),
            Strength = dto.strength,
            Agility = dto.agility,
            Intuition = dto.intuition,
            Endurance = dto.endurance,
            Accuracy = dto.accuracy,
            Intellect = dto.intellect,
            CurrentHp = dto.currentHp,
            MaxHp = Math.Max(1, dto.maxHp),
            PenaltyFraction = 0f
        };
        _unitCardView.Render(p);
    }
}
