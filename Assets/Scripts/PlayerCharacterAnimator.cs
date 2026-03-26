using UnityEngine;

/// <summary>
/// Placeholder while movement/combat animation is rebuilt. Handles ranged facing override only (no clips / Playables).
/// </summary>
public sealed class PlayerCharacterAnimator : MonoBehaviour
{
    [SerializeField] private Player _player;
    [SerializeField] private RemoteBattleUnitView _remoteBattleUnit;
    [Tooltip("Off: rotate this object. On: rotate Player or Remote root.")]
    [SerializeField] private bool _rotatePlayerRoot;
    [SerializeField] private float _rotationSpeed = 14f;

    private Vector3? _horizontalFacingOverride;

    public Transform FacingPivot =>
        _rotatePlayerRoot && _player != null
            ? _player.transform
            : _rotatePlayerRoot && _remoteBattleUnit != null
                ? _remoteBattleUnit.transform
                : transform;

    public void SetHorizontalFacingOverride(Vector3 worldHorizontalDir)
    {
        worldHorizontalDir.y = 0f;
        if (worldHorizontalDir.sqrMagnitude < 1e-8f)
        {
            _horizontalFacingOverride = null;
            return;
        }

        _horizontalFacingOverride = worldHorizontalDir.normalized;
    }

    public void ClearHorizontalFacingOverride() => _horizontalFacingOverride = null;

    public void ResetHexWalkPhaseForNewPath() { }

    public void NotifyHexStepStarted(float stepDurationSeconds) { }

    private void LateUpdate()
    {
        if (_player == null && _remoteBattleUnit == null)
            return;

        if (_player != null && (_player.IsDead || _player.IsHidden))
            return;
        if (_remoteBattleUnit != null && _remoteBattleUnit.CurrentHp <= 0)
            return;

        if (!_horizontalFacingOverride.HasValue)
            return;

        Transform pivot = FacingPivot;
        Vector3 d = _horizontalFacingOverride.Value;
        Quaternion target = Quaternion.LookRotation(d, Vector3.up);
        pivot.rotation = Quaternion.Slerp(pivot.rotation, target, Time.deltaTime * _rotationSpeed);
    }
}
