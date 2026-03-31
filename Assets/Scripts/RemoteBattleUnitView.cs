using System.Collections;
using UnityEngine;
using System;

/// <summary>
/// Визуал удалённого игрока на сетке: позиция и анимация по actualPath с сервера.
/// Для живого соперника (не моб) клонируется та же модель, что у локального игрока, с красным оттенком.
/// </summary>
public class RemoteBattleUnitView : MonoBehaviour
{
    [SerializeField] private HexGrid _grid;
    [SerializeField] private float _moveDurationPerHex = 0.2f;
    [SerializeField] private float _rangedFaceRotationSpeed = 14f;

    [Header("PvP visuals")]
    [SerializeField] private Color _enemyModelTint = new Color(0.95f, 0.28f, 0.22f, 1f);
    [SerializeField] private Color _allyModelTint = new Color(0.28f, 0.52f, 0.98f, 1f);

    [Header("Mob visuals")]
    [Tooltip("Resources path without extension, e.g. Mob/mob for Assets/Resources/Mob/mob.fbx")]
    [SerializeField] private string _mobModelResourcePath = "Mob/mob";
    [SerializeField] private Color _mobModelTint = new Color(0.85f, 0.35f, 0.25f, 1f);

    [Header("Overhead UI")]
    [Tooltip("Prefab with CharacterNameplateView; otherwise Resources/CharacterNameplate.")]
    [SerializeField] private GameObject _characterNameplatePrefab;
    [SerializeField] private Transform _nameplateFollowAnchor;

    private bool _isMoving;
    private Vector3? _horizontalFacingOverride;
    private int _maxHp = 10;
    private int _currentHp = 10;
    private Animator _cachedAnimator;
    private PlayerCharacterAnimator _characterAnimator;
    private string _displayName = "";
    private int _characterLevel = 1;
    private CharacterNameplateView _nameplateInstance;

    private int _localBattleTeamId = -1;
    private int _remoteBattleTeamId = -1;

    private const float MaxPenaltyFraction = 0.95f;
    private float _serverPenaltyFraction;
    private int _inspectStrength;
    private int _inspectAgility;
    private int _inspectIntuition;
    private int _inspectEndurance;
    private int _inspectAccuracy;
    private int _inspectIntellect;

    public string NetworkPlayerId { get; private set; }
    public bool IsMoving => _isMoving;
    public bool IsMob =>
        !string.IsNullOrEmpty(NetworkPlayerId)
        && (NetworkPlayerId.StartsWith("mob:", StringComparison.OrdinalIgnoreCase)
            || NetworkPlayerId.StartsWith("MOB_", StringComparison.OrdinalIgnoreCase));
    public int CurrentCol { get; private set; }
    public int CurrentRow { get; private set; }
    /// <summary>Battle grid (for snapping / grounding).</summary>
    public HexGrid Grid => _grid;
    public int CurrentHp => _currentHp;
    public int MaxHp => _maxHp;
    public string DisplayName => string.IsNullOrEmpty(_displayName) ? (NetworkPlayerId ?? "?") : _displayName;
    public int CharacterLevel => Mathf.Max(1, _characterLevel);

    /// <summary>Server movement fatigue (0…1) for inspect UI.</summary>
    public float ServerPenaltyFraction => _serverPenaltyFraction;

    /// <summary>Server PvP team (0/1); -1 if unknown or mob.</summary>
    public int BattleTeamId => _remoteBattleTeamId;

    /// <summary>True when server team ids match local player (excludes mobs).</summary>
    public bool IsAllyVersusLocal =>
        !IsMob && _localBattleTeamId >= 0 && _remoteBattleTeamId >= 0 && _localBattleTeamId == _remoteBattleTeamId;

    public event Action<int, int> OnHealthChanged;
    public event Action OnDisplayProfileChanged;

    /// <summary>Точка выстрела для VFX: кость Humanoid RightHand, иначе над корнем юнита.</summary>
    public bool TryGetRangedFireWorldPosition(out Vector3 worldPos)
    {
        if (_cachedAnimator == null) _cachedAnimator = GetComponentInChildren<Animator>();
        Animator anim = _cachedAnimator;
        if (anim != null && anim.isHuman && anim.isActiveAndEnabled)
        {
            Transform hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null)
            {
                worldPos = hand.position;
                return true;
            }
        }

        worldPos = transform.position + Vector3.up * 1.2f;
        return true;
    }

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

    public void ClearHorizontalFacingOverride()
    {
        _horizontalFacingOverride = null;
    }

    private void LateUpdate()
    {
        if (!_horizontalFacingOverride.HasValue)
            return;
        Vector3 d = _horizontalFacingOverride.Value;
        Quaternion target = Quaternion.LookRotation(d, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * _rangedFaceRotationSpeed);
    }

    public void Initialize(string playerId, HexGrid grid, int startCol, int startRow, float moveDurationPerHex = -1f,
        int localBattleTeamId = -1, int remoteBattleTeamId = -1)
    {
        NetworkPlayerId = playerId;
        _localBattleTeamId = localBattleTeamId;
        _remoteBattleTeamId = remoteBattleTeamId;
        _grid = grid;
        if (moveDurationPerHex > 0f) _moveDurationPerHex = moveDurationPerHex;
        if (_grid != null)
        {
            transform.position = _grid.GetCellWorldPosition(startCol, startRow);
            CurrentCol = startCol;
            CurrentRow = startRow;
        }
        EnsureVisual();
        // Spawn path calls SetDisplayProfile after Initialize; history replay / TurnResult recreation only calls Initialize,
        // so create the overhead nameplate here (DisplayName falls back to NetworkPlayerId until SetDisplayProfile).
        EnsureNameplate();
    }

    /// <summary>Updates ally/enemy tint when team info arrives (e.g. first turn result).</summary>
    public void SetBattleTeamIds(int localBattleTeamId, int remoteBattleTeamId)
    {
        _localBattleTeamId = localBattleTeamId;
        _remoteBattleTeamId = remoteBattleTeamId;
        ReapplyNonMobTintIfVisualExists();
    }

    public void SetDisplayProfile(string displayName, int level)
    {
        _displayName = displayName ?? "";
        _characterLevel = Mathf.Max(1, level);
        EnsureNameplate();
        OnDisplayProfileChanged?.Invoke();
    }

    private void EnsureNameplate()
    {
        if (_nameplateInstance != null)
            return;
        GameObject prefab = _characterNameplatePrefab;
        if (prefab == null)
            prefab = Resources.Load<GameObject>("CharacterNameplate");
        if (prefab == null)
            return;
        GameObject go = Instantiate(prefab, transform);
        _nameplateInstance = go.GetComponent<CharacterNameplateView>();
        if (_nameplateInstance == null)
            _nameplateInstance = go.AddComponent<CharacterNameplateView>();
        Transform follow = _nameplateFollowAnchor != null ? _nameplateFollowAnchor : transform;
        _nameplateInstance.Bind(this, follow);
    }

    private void EnsureVisual()
    {
        if (GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            return;

        if (IsMob)
        {
            EnsureMobHumanoidVisual();
            return;
        }

        Player localPlayer = UnityEngine.Object.FindFirstObjectByType<Player>();
        if (localPlayer == null || localPlayer.transform.childCount == 0)
        {
            EnsureCapsuleFallback();
            return;
        }

        GameObject template = localPlayer.transform.GetChild(0).gameObject;
        // Неактивный родитель: на клоне есть Player — иначе OnEnable у PlayerCharacterAnimator успевает
        // подписаться на клонированный Player до Destroy, и остаётся «висячая» ссылка на локального Player.
        GameObject holder = new GameObject("_RemoteCloneHolder");
        holder.hideFlags = HideFlags.HideAndDontSave;
        holder.SetActive(false);

        GameObject clone = Instantiate(template, holder.transform);
        clone.name = "RemoteVisual";
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;

        ApplyPlayerModelTint(clone);
        foreach (Player p in clone.GetComponentsInChildren<Player>(true))
        {
            if (p != null)
                Destroy(p);
        }

        Collider[] cols = clone.GetComponentsInChildren<Collider>(true);
        if (cols.Length > 0)
        {
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].isTrigger = true;
            }
        }
        else
            AddBoundsTriggerColliderForPicking(clone);

        clone.transform.SetParent(transform, false);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;

        Destroy(holder);

        _characterAnimator = clone.GetComponent<PlayerCharacterAnimator>();
        _cachedAnimator = clone.GetComponentInChildren<Animator>();

        clone.SetActive(true);
    }

    /// <summary>При модели без коллайдеров луч не попадает в силуэт/удержание ЛКМ — один общий trigger по bounds.</summary>
    private static void AddBoundsTriggerColliderForPicking(GameObject cloneRoot)
    {
        Renderer[] rends = cloneRoot.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0)
            return;

        Bounds combined = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
        {
            if (rends[i] != null)
                combined.Encapsulate(rends[i].bounds);
        }

        var box = cloneRoot.AddComponent<BoxCollider>();
        box.isTrigger = true;
        Transform t = cloneRoot.transform;
        box.center = t.InverseTransformPoint(combined.center);
        Vector3 ext = combined.size;
        Vector3 ls = t.lossyScale;
        float sx = Mathf.Abs(ls.x) > 1e-10f ? Mathf.Abs(ls.x) : 1f;
        float sy = Mathf.Abs(ls.y) > 1e-10f ? Mathf.Abs(ls.y) : 1f;
        float sz = Mathf.Abs(ls.z) > 1e-10f ? Mathf.Abs(ls.z) : 1f;
        box.size = new Vector3(ext.x / sx, ext.y / sy, ext.z / sz);
    }

    private void ReapplyNonMobTintIfVisualExists()
    {
        if (IsMob)
            return;
        Transform vis = transform.Find("RemoteVisual");
        if (vis == null)
            return;
        ApplyTintToRenderers(vis.gameObject, ChoosePlayerTint());
    }

    private Color ChoosePlayerTint() => IsAllyVersusLocal ? _allyModelTint : _enemyModelTint;

    private void ApplyPlayerModelTint(GameObject root) => ApplyTintToRenderers(root, ChoosePlayerTint());

    private static void ApplyTintToRenderers(GameObject root, Color tint)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty("_BaseColor"))
                    mpb.SetColor("_BaseColor", tint);
                else if (r.sharedMaterial.HasProperty("_Color"))
                    mpb.SetColor("_Color", tint);
                else
                    mpb.SetColor("_BaseColor", tint);
            }
            else
            {
                mpb.SetColor("_BaseColor", tint);
            }

            r.SetPropertyBlock(mpb);
        }
    }

    private void EnsureMobHumanoidVisual()
    {
        if (GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            return;

        string path = string.IsNullOrWhiteSpace(_mobModelResourcePath) ? "Mob/mob" : _mobModelResourcePath.Trim();
        GameObject mobPrefab = Resources.Load<GameObject>(path);
        if (mobPrefab == null)
        {
            Debug.LogWarning($"[RemoteBattleUnitView] Mob model not found at Resources/{path} — using capsule.");
            EnsureCapsuleFallback();
            return;
        }

        GameObject mobRoot = Instantiate(mobPrefab);
        mobRoot.name = "MobVisual";
        mobRoot.SetActive(false);
        mobRoot.transform.SetParent(transform, false);
        mobRoot.transform.localPosition = Vector3.zero;
        mobRoot.transform.localRotation = Quaternion.identity;
        mobRoot.transform.localScale = Vector3.one;

        Animator anim = mobRoot.GetComponentInChildren<Animator>(true);
        if (anim == null)
        {
            Debug.LogWarning("[RemoteBattleUnitView] Mob FBX has no Animator — using capsule.");
            Destroy(mobRoot);
            EnsureCapsuleFallback();
            return;
        }

        if (!anim.isHuman)
        {
            Debug.LogWarning(
                "[RemoteBattleUnitView] Mob FBX is not Humanoid (Rig → Animation Type → Humanoid on mob.fbx) — using capsule.");
            Destroy(mobRoot);
            EnsureCapsuleFallback();
            return;
        }

        Transform animHost = anim.transform;
        PlayerCharacterAnimator pca = animHost.GetComponent<PlayerCharacterAnimator>();
        if (pca == null)
            pca = animHost.gameObject.AddComponent<PlayerCharacterAnimator>();

        Player localPlayer = FindFirstObjectByType<Player>();
        PlayerCharacterAnimator template =
            localPlayer != null ? localPlayer.GetComponentInChildren<PlayerCharacterAnimator>(true) : null;
        if (template == null)
        {
            Debug.LogWarning("[RemoteBattleUnitView] No local PlayerCharacterAnimator — mob uses capsule.");
            Destroy(mobRoot);
            EnsureCapsuleFallback();
            return;
        }

        pca.CopyLocomotionConfigFrom(template);

        ApplyMobTint(mobRoot);
        Collider[] cols = mobRoot.GetComponentsInChildren<Collider>(true);
        if (cols.Length > 0)
        {
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].isTrigger = true;
            }
        }
        else
            AddBoundsTriggerColliderForPicking(mobRoot);

        mobRoot.SetActive(true);
        _characterAnimator = pca;
        _cachedAnimator = anim;
    }

    private void ApplyMobTint(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty("_BaseColor"))
                    mpb.SetColor("_BaseColor", _mobModelTint);
                else if (r.sharedMaterial.HasProperty("_Color"))
                    mpb.SetColor("_Color", _mobModelTint);
                else
                    mpb.SetColor("_BaseColor", _mobModelTint);
            }
            else
                mpb.SetColor("_BaseColor", _mobModelTint);

            r.SetPropertyBlock(mpb);
        }
    }

    private void EnsureCapsuleFallback()
    {
        if (GetComponentInChildren<MeshFilter>() != null) return;
        GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        cap.name = "Visual";
        cap.transform.SetParent(transform);
        cap.transform.localPosition = Vector3.zero;
        cap.transform.localScale = new Vector3(0.8f, 0.5f, 0.8f);
        var r = cap.GetComponent<Renderer>();
        if (r != null)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", new Color(0.85f, 0.35f, 0.25f, 1f));
            r.SetPropertyBlock(mpb);
        }

        Collider capCollider = cap.GetComponent<Collider>();
        if (capCollider != null)
            capCollider.isTrigger = true;
    }

    public void SetPenaltyFraction(float value)
    {
        _serverPenaltyFraction = Mathf.Clamp(value, 0f, MaxPenaltyFraction);
    }

    public void SetInspectCombatStats(int strength, int agility, int intuition, int endurance, int accuracy, int intellect)
    {
        _inspectStrength = Mathf.Max(0, strength);
        _inspectAgility = Mathf.Max(0, agility);
        _inspectIntuition = Mathf.Max(0, intuition);
        _inspectEndurance = Mathf.Max(0, endurance);
        _inspectAccuracy = Mathf.Max(0, accuracy);
        _inspectIntellect = Mathf.Max(0, intellect);
    }

    public void FillUnitCardPayload(UnitCardPayload dst)
    {
        if (dst == null) return;
        dst.DisplayName = DisplayName;
        dst.Level = CharacterLevel;
        dst.Strength = _inspectStrength;
        dst.Agility = _inspectAgility;
        dst.Intuition = _inspectIntuition;
        dst.Endurance = _inspectEndurance;
        dst.Accuracy = _inspectAccuracy;
        dst.Intellect = _inspectIntellect;
        dst.CurrentHp = CurrentHp;
        dst.MaxHp = Mathf.Max(1, MaxHp);
        dst.PenaltyFraction = _serverPenaltyFraction;
    }

    public void ApplyServerTurnResult(HexPosition finalPosition, HexPosition[] actualPath, int currentAp, float penaltyFraction, bool prepareForAnimation = true)
    {
        SetPenaltyFraction(penaltyFraction);

        if (_grid == null || actualPath == null || actualPath.Length == 0)
        {
            if (_grid != null && finalPosition != null)
            {
                transform.position = _grid.GetCellWorldPosition(finalPosition.col, finalPosition.row);
                CurrentCol = finalPosition.col;
                CurrentRow = finalPosition.row;
            }
            return;
        }
        var pos = prepareForAnimation ? actualPath[0] : actualPath[actualPath.Length - 1];
        transform.position = _grid.GetCellWorldPosition(pos.col, pos.row);
        CurrentCol = finalPosition != null ? finalPosition.col : pos.col;
        CurrentRow = finalPosition != null ? finalPosition.row : pos.row;
    }

    public void SetHealth(int currentHp, int maxHp)
    {
        _maxHp = Mathf.Max(1, maxHp);
        _currentHp = Mathf.Clamp(currentHp, 0, _maxHp);
        OnHealthChanged?.Invoke(_currentHp, _maxHp);
    }

    public IEnumerator PlayPathAnimation(HexPosition[] path, bool resetHexWalkPhase = true, bool clearMovementStateWhenDone = true)
    {
        if (_grid == null || path == null || path.Length < 2)
        {
            if (_grid != null && path != null && path.Length == 1)
                transform.position = _grid.GetCellWorldPosition(path[0].col, path[0].row);
            yield break;
        }

        _isMoving = true;
        transform.position = _grid.GetCellWorldPosition(path[0].col, path[0].row);

        if (_characterAnimator == null)
            _characterAnimator = GetComponentInChildren<PlayerCharacterAnimator>();
        ClearHorizontalFacingOverride();
        _characterAnimator?.ClearHorizontalFacingOverride();
        if (resetHexWalkPhase)
            _characterAnimator?.ResetHexWalkPhaseForNewPath();

        for (int i = 1; i < path.Length; i++)
        {
            var step = path[i];
            _characterAnimator?.NotifyHexStepStarted(_moveDurationPerHex);

            Vector3 target = _grid.GetCellWorldPosition(step.col, step.row);
            Vector3 stepStart = transform.position;
            float elapsed = 0f;
            while (elapsed < _moveDurationPerHex)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _moveDurationPerHex);
                transform.position = Vector3.Lerp(stepStart, target, t);
                yield return null;
            }
            transform.position = target;
        }

        if (clearMovementStateWhenDone)
            _isMoving = false;
    }

    public void ForceStopMovement()
    {
        _isMoving = false;
    }

    /// <summary>Align root to the logical hex cell (e.g. before death VFX) so pose is not played mid-lerp between cells.</summary>
    public void SnapToGridCell()
    {
        if (_grid == null)
            return;
        transform.position = _grid.GetCellWorldPosition(CurrentCol, CurrentRow);
    }
}
