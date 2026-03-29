using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Игрок на гекс-сетке: текущая ячейка, очки действия и счётчик ходов.
/// </summary>
public class Player : MonoBehaviour
{
    /// <summary>Offline / missing-payload defaults; online caps come from <see cref="SetMaxAp"/> and <see cref="SetHealth"/> via battle spawn.</summary>
    public const int DefaultCombatMaxAp = 15;
    public const int DefaultCombatMaxHp = 20;

    private const int ChangePostureCost = 2;
    private const float RunCostMultiplier = 0.5f;
    private const float SitCostMultiplier = 1.5f;
    private const float RunPenaltyThresholdFraction = 0.85f;
    private const float MaxPenaltyFraction = 0.95f;

    [Header("Grid")]
    [SerializeField] private HexGrid _grid;

    [Header("Movement")]
    [SerializeField] private float _moveDurationPerHex = 0.2f;
    private PlayerCharacterAnimator _characterAnimator;
    private Animator _animator;
    private HexGridCamera _hexGridCamera;
    private Renderer[] _cachedRenderers;
    private Collider[] _cachedColliders;

    [Header("Ranged combat (VFX)")]
    [Tooltip("Optional: fire origin instead of Humanoid RightHand bone.")]
    [SerializeField] private Transform _rangedMuzzleOverride;
    [Tooltip("If no Animator/Humanoid, line start at this height above hex center.")]
    [SerializeField] private float _rangedFireFallbackHeightAboveHex = 1.05f;

    [Header("Action points")]
    [Tooltip("Used until server spawn applies max AP (see DefaultCombatMaxAp).")]
    [SerializeField] private int _maxAp = DefaultCombatMaxAp;
    [Header("Health")]
    [Tooltip("Used until server spawn applies max HP (see DefaultCombatMaxHp).")]
    [SerializeField] private int _maxHp = DefaultCombatMaxHp;

    [Header("Overhead UI")]
    [Tooltip("Optional CharacterNameplateView prefab; else Resources/CharacterNameplate.")]
    [SerializeField] private GameObject _characterNameplatePrefab;
    [Tooltip("Anchor above head; else Player root.")]
    [SerializeField] private Transform _nameplateFollowAnchor;

    [Header("Turn timer (sec)")]
    [SerializeField] private float _turnDurationSeconds = 100f;

    private int _currentCol;
    private int _currentRow;
    private bool _isMoving;
    private int _currentAp;
    private int _turnCount;
    private int _currentHp;
    private string _displayName = "";
    private int _characterLevel = 1;
    private CharacterNameplateView _nameplateInstance;
    private int _runMovementApSpentThisTurn;
    private MovementPosture _currentPosture = MovementPosture.Walk;

    // Накопленный штраф в долях от MaxAp (0–1): 0 = нет штрафа, 0.1 = -10% от MaxAp.
    // Сколько ОД уже потрачено в текущем ходу (используется для штрафа и подсчёта шагов).
    private int _apSpentThisTurn;
    // Сколько "шагов" уже совершено в этом ходу (независимо от направления).
    private int _stepsTakenThisTurn;
    // Эффективный максимум ОД в начале текущего хода (до трат в этом ходу).
    /// <summary>Подотрезок пути по ОД — без <see cref="List{T}.GetRange"/> (аллокация подсписка).</summary>
    private readonly List<(int col, int row)> _moveSubPathScratch = new(32);
    /// <summary>Автопродолжение к цели после раунда — без аллокации.</summary>
    private readonly List<(int col, int row)> _movementFlagPathBuffer = new(64);
    /// <summary>Per-edge AP for current <see cref="MoveAlongPath"/> subpath (includes escape-entry lump).</summary>
    private int[] _moveEdgeCostsScratch = Array.Empty<int>();
    private bool _movementFlagActive;
    private int _movementFlagCol;
    private int _movementFlagRow;
    // Полный путь за текущий ход (все переходы по гексам, в порядке кликов).
    private List<(int col, int row)> _turnPath;
    private List<BattleQueuedAction> _turnActions;
    // Остаток времени текущего хода (сек).
    private float _turnTimeLeft;
    // Абсолютный момент окончания хода по UTC timestamp от сервера.
    private long _turnDeadlineUtcMs;
    // Флаг, что таймер хода уже истёк (для внешней логики автозавершения).
    private bool _turnTimeExpired;
    private bool _turnTimerPaused;
    private bool _isHidden;
    private int _movementInterruptVersion;

    /// <summary>Код оружия (сервер / локальный каталог).</summary>
    private string _weaponCode = WeaponCatalog.DefaultWeaponCode;
    private int _weaponDamage = 1;
    private int _weaponDamageMin = 1;
    /// <summary>Макс. дистанция атаки в шагах гекса (как на сервере <see cref="UnitStateDto.WeaponRange"/>).</summary>
    private int _weaponRangeHexes = 1;
    /// <summary>Стоимость атаки (ОД) из БД / сервера, не из каталога.</summary>
    private int _weaponAttackApCost = 1;
    private int _magazineCapacity;
    private int _currentMagazineRounds;
    private int _reserveAmmoRounds;
    /// <summary>From last <see cref="PlayerTurnResult.isEscaping"/> — server runs empty turns until flee completes or the unit leaves the escape ring.</summary>
    private bool _serverIsEscaping;

    public int CurrentCol => _currentCol;
    public int CurrentRow => _currentRow;

    /// <summary>Первая клетка пути в этом ходу (старт первого MoveStep) или текущая позиция — для симуляции гекса атаки до отправки хода.</summary>
    public void GetTurnSimulationStartHex(out int col, out int row)
    {
        if (_turnPath != null && _turnPath.Count > 0)
        {
            col = _turnPath[0].col;
            row = _turnPath[0].row;
        }
        else
        {
            col = _currentCol;
            row = _currentRow;
        }
    }
    public bool IsMoving => _isMoving;

    /// <summary>Гарантированно выключить флаг движения после журнала (например, обрыв таймлайна).</summary>
    public void ClearMovementPlaybackState() => _isMoving = false;

    /// <summary>
    /// Ranged aim sets <see cref="PlayerCharacterAnimator"/> horizontal facing override; until cleared,
    /// <c>LateUpdate</c> keeps facing the aim direction instead of movement.
    /// </summary>
    private void ClearRangedFacingOverrideForLocomotion()
    {
        if (_characterAnimator == null)
            _characterAnimator = GetComponentInChildren<PlayerCharacterAnimator>();
        _characterAnimator?.ClearHorizontalFacingOverride();
    }

    public int MaxAp => _maxAp;
    public int MaxHp => _maxHp;
    public int CurrentHp => _currentHp;
    /// <summary>Имя для планки (сервер / BattleStarted).</summary>
    public string DisplayName => string.IsNullOrEmpty(_displayName) ? "Player" : _displayName;
    /// <summary>Уровень персонажа для планки.</summary>
    public int CharacterLevel => Mathf.Max(1, _characterLevel);
    public bool IsDead => _currentHp <= 0;
    public MovementPosture CurrentMovementPosture => _currentPosture;
    public MovementPosture PreviewMovementPosture => MovementPostureUtility.GetPreviewMovementPosture(_currentPosture);
    /// <summary>Текущие ОД (0 while <see cref="IsServerEscaping"/>).</summary>
    public int CurrentAp => _serverIsEscaping ? 0 : _currentAp;

    public bool IsServerEscaping => _serverIsEscaping;

    public void SetServerEscapeState(bool escaping)
    {
        _serverIsEscaping = escaping;
        if (escaping)
            _currentAp = 0;
    }
    public int TurnCount => _turnCount;
    /// <summary>Сколько ОД уже потрачено в текущем ходу.</summary>
    public int ApSpentThisTurn => _apSpentThisTurn;
    /// <summary>Оставшееся время текущего хода (сек).</summary>
    public float TurnTimeLeft => _turnTimeLeft;
    /// <summary>Длительность хода (сек).</summary>
    public float TurnDurationSeconds => _turnDurationSeconds;
    public bool IsTurnTimerPaused => _turnTimerPaused;
    public bool IsHidden => _isHidden;
    /// <summary>Истёк ли таймер хода.</summary>
    public bool TurnTimeExpired => _turnTimeExpired;
    /// <summary>Сколько "шагов" уже совершено в этом ходу.</summary>
    public int StepsTakenThisTurn => _stepsTakenThisTurn;
    public HexGrid Grid => _grid;
    public float MoveDurationPerHex => _moveDurationPerHex;

    public string WeaponCode => _weaponCode;
    public int WeaponDamage => _weaponDamage;
    /// <summary>Минимальный урон за попадание (сервер); для UI диапазона.</summary>
    public int WeaponDamageMin => _weaponDamageMin;
    /// <summary>Мировая точка выстрела для линии пули: <see cref="_rangedMuzzleOverride"/>, иначе кость RightHand, иначе центр гекса + высота.</summary>
    public bool TryGetRangedFireWorldPosition(out Vector3 worldPos)
    {
        if (_rangedMuzzleOverride != null)
        {
            worldPos = _rangedMuzzleOverride.position;
            return true;
        }

        Animator anim = _animator;
        if (anim != null && anim.isHuman && anim.isActiveAndEnabled)
        {
            Transform hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null)
            {
                worldPos = hand.position;
                return true;
            }
        }

        if (_grid != null)
        {
            worldPos = _grid.GetCellWorldPosition(_currentCol, _currentRow)
                       + Vector3.up * Mathf.Max(0.05f, _rangedFireFallbackHeightAboveHex);
            return true;
        }

        worldPos = transform.position + Vector3.up * Mathf.Max(0.05f, _rangedFireFallbackHeightAboveHex);
        return true;
    }

    /// <summary>Радиус атаки в гексах (hex distance), как у текущего оружия.</summary>
    public int WeaponRangeHexes => Mathf.Max(0, _weaponRangeHexes);
    /// <summary>Стоимость одной атаки текущим оружием (ОД), с сервера / weapons.attack_ap_cost.</summary>
    public int WeaponAttackApCost => Mathf.Max(1, _weaponAttackApCost);
    public int MagazineCapacity => Mathf.Max(0, _magazineCapacity);
    public int CurrentMagazineRounds => Mathf.Clamp(_currentMagazineRounds, 0, Mathf.Max(0, _magazineCapacity));
    public int ReserveAmmoRounds => Mathf.Max(0, _reserveAmmoRounds);

    /// <summary>Смена отображаемого оружия (после смены из инвентаря / результата раунда).</summary>
    public event System.Action OnEquippedWeaponChanged;
    /// <summary>Текущее и макс. HP после синхронизации с сервером (для анимации смерти / респавна).</summary>
    public event System.Action<int, int> OnHealthChanged;
    /// <summary>Ник или уровень сменились (планка над головой).</summary>
    public event System.Action OnDisplayProfileChanged;

    /// <summary>Синхронизация с сервером или локальная смена (кулак / камень и т.д.).</summary>
    /// <param name="attackApCost">Стоимость атаки из БД / сервера; по умолчанию 1.</param>
    /// <param name="weaponDamageMin">Если &lt; 0 — считается равным <paramref name="damage"/>.</param>
    public void SetEquippedWeapon(string code, int damage, int rangeHexes, int attackApCost = 1, int weaponDamageMin = -1)
    {
        _weaponCode = WeaponCatalog.NormalizeWeaponCode(code);
        _weaponDamage = Mathf.Max(0, damage);
        int rawMin = weaponDamageMin >= 0 ? weaponDamageMin : _weaponDamage;
        _weaponDamageMin = Mathf.Clamp(rawMin, 0, _weaponDamage);
        _weaponRangeHexes = Mathf.Max(0, rangeHexes);
        _weaponAttackApCost = Mathf.Max(1, attackApCost);
        OnEquippedWeaponChanged?.Invoke();
    }

    public void SetMagazineState(int capacity, int currentRounds, bool notify = true)
    {
        int newCapacity = Mathf.Max(0, capacity);
        int newRounds = newCapacity <= 0 ? 0 : Mathf.Clamp(currentRounds, 0, newCapacity);
        if (_magazineCapacity == newCapacity && _currentMagazineRounds == newRounds)
            return;

        _magazineCapacity = newCapacity;
        _currentMagazineRounds = newRounds;
        if (notify)
            OnEquippedWeaponChanged?.Invoke();
    }

    public void SetReserveAmmoRounds(int rounds, bool notify = true)
    {
        int v = Mathf.Max(0, rounds);
        if (_reserveAmmoRounds == v)
            return;
        _reserveAmmoRounds = v;
        if (notify)
            OnEquippedWeaponChanged?.Invoke();
    }

    public delegate void PlayerMovedHandler(HexCell cell);
    /// <summary>Событие: игрок завершил перемещение в новую ячейку (после MoveAlongPath, телепорта или анимации).</summary>
    public event PlayerMovedHandler OnMovedToCell;
    public event Action<MovementPosture> OnMovementPostureChanged;

    private void Awake()
    {
        _apSpentThisTurn = 0;
        _stepsTakenThisTurn = 0;
        _currentAp = _maxAp;
        _currentHp = _maxHp;
        _currentPosture = MovementPosture.Walk;
        _runMovementApSpentThisTurn = 0;
        _turnTimeLeft = _turnDurationSeconds;
        _turnDeadlineUtcMs = BuildRoundDeadlineUtcMs(_turnDurationSeconds);
        _turnTimeExpired = false;
        _turnPath = new List<(int col, int row)>();
        _turnActions = new List<BattleQueuedAction>();
        _characterAnimator = GetComponentInChildren<PlayerCharacterAnimator>();
        _animator = GetComponentInChildren<Animator>();
        _hexGridCamera = FindFirstObjectByType<HexGridCamera>();
    }

    private void Start()
    {
        // В онлайн-бою позицию задаёт сервер (ApplyBattleStarted); иначе Player.Start перезапишет спавн в (0,0).
        if (GameSession.Active != null && GameSession.Active.IsInBattleWithServer())
        {
            EnsureNameplate();
            return;
        }
        if (_grid != null && _grid.GetCell(0, 0) != null)
        {
            _currentCol = 0;
            _currentRow = 0;
            transform.position = _grid.GetCellWorldPosition(0, 0);
        }
        EnsureNameplate();
    }

    /// <summary>Ник и уровень для планки (BattleStarted / локальная настройка).</summary>
    public void SetDisplayProfile(string displayName, int level)
    {
        _displayName = displayName ?? "";
        _characterLevel = Mathf.Max(1, level);
        EnsureNameplate();
        OnDisplayProfileChanged?.Invoke();
    }

    /// <summary>Server-authoritative max AP (battle spawn / sync).</summary>
    public void SetMaxAp(int maxAp)
    {
        _maxAp = Mathf.Max(1, maxAp);
        _currentAp = Mathf.Clamp(_currentAp, 0, _maxAp);
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

    private void Update()
    {
        // Таймер хода: считаем только время, авто-завершение делает UI (чтобы можно было показать анимацию).
        if (!_turnTimerPaused && _turnTimeLeft > 0f)
        {
            _turnTimeLeft = GetRemainingTimeFromDeadline();
            if (_turnTimeLeft <= 0f)
            {
                _turnTimeLeft = 0f;
                _turnTimeExpired = true;
            }
        }
    }

    /// <summary>Запускает движение по пути (список (col, row)), ограничивая длину по ОД. animate=false – телепорт.
    /// Стоимость перехода на L клеток при уже сделанных K шагах = GetStepCost(K+L) − GetStepCost(K).</summary>
    public void MoveAlongPath(List<(int col, int row)> path, bool animate, System.Action onMovementFullyComplete = null)
    {
        if (path == null || path.Count < 2 || _isMoving || _grid == null) return;
        if (IsDead || _isHidden) return;
        if (!MovementPostureUtility.CanMove(_currentPosture)) return;

        int stepsToMove = path.Count - 1;
        if (stepsToMove <= 0) return;

        int stepsAlready = _stepsTakenThisTurn;
        int allowedSteps = GetAllowedStepsAlongHexPath(PreviewMovementPosture, path, stepsToMove);
        if (allowedSteps <= 0)
            return;

        if (_turnPath == null)
            _turnPath = new List<(int col, int row)>();
        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        MovementPosture posture = PreviewMovementPosture;
        int subLen = allowedSteps + 1;
        _moveSubPathScratch.Clear();
        for (int i = 0; i < subLen; i++)
            _moveSubPathScratch.Add(path[i]);
        List<(int col, int row)> subPath = _moveSubPathScratch;

        if (!TryComputeSequentialMoveEdgeApCosts(posture, stepsAlready, subPath, allowedSteps, _currentAp, out int moveCost, out int[] edgeCosts))
            return;

        _apSpentThisTurn += moveCost;
        _stepsTakenThisTurn = stepsAlready + allowedSteps;
        _currentAp -= moveCost;
        if (posture == MovementPosture.Run)
            _runMovementApSpentThisTurn += moveCost;

        // Накопить полный путь за ход:
        // при первом перемещении добавляем стартовую клетку, далее — только новые шаги.
        if (_turnPath.Count == 0)
            _turnPath.Add((_currentCol, _currentRow));
        for (int i = 1; i < subPath.Count; i++)
        {
            _turnPath.Add(subPath[i]);
            int stepCost = edgeCosts[i - 1];
            _turnActions.Add(new BattleQueuedAction
            {
                actionType = "MoveStep",
                targetPosition = new HexPosition(subPath[i].col, subPath[i].row),
                posture = MovementPostureUtility.ToId(posture),
                cost = stepCost
            });
        }

        if (!animate)
        {
            // Без анимации — сразу телепортируем в последний гекс подотрезка.
            var last = subPath[subPath.Count - 1];
            _currentCol = last.col;
            _currentRow = last.row;
            transform.position = _grid.GetCellWorldPosition(last.col, last.row);

            HexCell cell = _grid.GetCell(_currentCol, _currentRow);
            OnMovedToCell?.Invoke(cell);
            TryClearMovementFlagIfReachedDestination();
            ClearRangedFacingOverrideForLocomotion();
            onMovementFullyComplete?.Invoke();
        }
        else
            StartCoroutine(MoveAlongPathCoroutine(subPath, onMovementFullyComplete));
    }

    private IEnumerator MoveAlongPathCoroutine(List<(int col, int row)> path, System.Action onMovementFullyComplete)
    {
        int interruptVersion = _movementInterruptVersion;
        _isMoving = true;
        ClearRangedFacingOverrideForLocomotion();

        foreach (var step in path)
        {
            if (interruptVersion != _movementInterruptVersion || IsDead || _isHidden)
            {
                _isMoving = false;
                yield break;
            }

            if (step.col == _currentCol && step.row == _currentRow)
                continue;

            if (_characterAnimator == null)
                _characterAnimator = GetComponentInChildren<PlayerCharacterAnimator>();
            _characterAnimator?.NotifyHexStepStarted(_moveDurationPerHex);

            Vector3 target = _grid.GetCellWorldPosition(step.col, step.row);
            Vector3 stepStart = transform.position;
            float elapsed = 0f;

            while (elapsed < _moveDurationPerHex)
            {
                if (interruptVersion != _movementInterruptVersion || IsDead || _isHidden)
                {
                    _isMoving = false;
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _moveDurationPerHex);
                transform.position = Vector3.Lerp(stepStart, target, t);
                yield return null;
            }

            _currentCol = step.col;
            _currentRow = step.row;
            transform.position = target;
        }

        if (_grid != null)
        {
            HexCell cell = _grid.GetCell(_currentCol, _currentRow);
            OnMovedToCell?.Invoke(cell);
        }

        _isMoving = false;
        TryClearMovementFlagIfReachedDestination();
        onMovementFullyComplete?.Invoke();
    }

    /// <summary>Сколько шагов по пути можно пройти за текущие ОД (учёт позы и шагов в ходу).</summary>
    public int GetAllowedMoveStepsForPath(List<(int col, int row)> path)
    {
        if (path == null || path.Count < 2)
            return 0;
        return GetAllowedStepsAlongHexPath(PreviewMovementPosture, path, path.Count - 1);
    }

    /// <summary>Цель «добежать» на следующих ходах; подсветка на гексе.</summary>
    public void SetMovementFlag(int col, int row)
    {
        if (_grid == null)
            return;
        ClearMovementFlagVisualOnly();
        _movementFlagActive = true;
        _movementFlagCol = col;
        _movementFlagRow = row;
        HexCell c = _grid.GetCell(col, row);
        if (c != null)
            c.SetMovementFlag(true);
    }

    public void ClearMovementFlag()
    {
        ClearMovementFlagVisualOnly();
        _movementFlagActive = false;
    }

    private void ClearMovementFlagVisualOnly()
    {
        if (!_movementFlagActive || _grid == null)
            return;
        HexCell c = _grid.GetCell(_movementFlagCol, _movementFlagRow);
        if (c != null)
            c.SetMovementFlag(false);
    }

    private void TryClearMovementFlagIfReachedDestination()
    {
        if (!_movementFlagActive)
            return;
        if (_currentCol == _movementFlagCol && _currentRow == _movementFlagRow)
            ClearMovementFlag();
    }

    /// <summary>После раунда — автоматически идти к флагу, если он ещё актуален.</summary>
    public void TryAutoMoveTowardFlag()
    {
        if (!_movementFlagActive || _isMoving || IsDead || _isHidden || _grid == null)
            return;
        // Не используем BlockPlayerInput: удалённый юнит может ещё анимироваться, локальный уже может идти к флагу.
        if (GameSession.Active != null)
        {
            if (GameSession.Active.IsWaitingForServerRoundResolve) return;
            if (GameSession.Active.LocalPlayerIsEscaping) return;
            if (GameSession.Active.IsTurnHistoryReplayPlaying) return;
            if (GameSession.Active.IsViewingHistoricalTurn) return;
        }
        if (!MovementPostureUtility.CanMove(_currentPosture))
            return;
        if (_currentPosture == MovementPosture.Hide)
            return;
        if (!EnsureMovablePostureForMovement())
            return;

        if (_currentCol == _movementFlagCol && _currentRow == _movementFlagRow)
        {
            ClearMovementFlag();
            return;
        }

        if (!HexPathfinding.TryBuildPath(_grid, _currentCol, _currentRow, _movementFlagCol, _movementFlagRow, _movementFlagPathBuffer))
            return;
        if (_movementFlagPathBuffer.Count < 2)
            return;

        MoveAlongPath(_movementFlagPathBuffer, MovementPlanningVisualSettings.ShowMovementAnimation);
    }

    /// <summary>
    /// «Шаг назад», когда отменять нечего: снять флаг «добежать», если в этом ходу ещё не тратил ОД (как при полном запасе до первого действия).
    /// </summary>
    public bool TryClearMovementFlagOnStepBackAtFullAp()
    {
        if (!_movementFlagActive)
            return false;
        if (_turnActions != null && _turnActions.Count > 0)
            return false;
        if (_apSpentThisTurn > 0)
            return false;
        ClearMovementFlag();
        return true;
    }

    /// <summary>
    /// Закончить ход.
    /// Штрафный ход — финиш на предпоследнем или последнем шаге при макс. ОД.
    /// Если ход не штрафной — восстановление усталости на 5%. Новый ход начинается с EffectiveMaxAp ОД.
    /// </summary>
    public void EndTurn()
    {
        _apSpentThisTurn = 0;
        _stepsTakenThisTurn = 0;
        _runMovementApSpentThisTurn = 0;
        _currentAp = _maxAp;
        SetMovementPostureInternal(MovementPosture.Walk, notify: true);

        _turnPath.Clear();
        _turnActions.Clear();
        _turnCount++;
        _turnDeadlineUtcMs = BuildRoundDeadlineUtcMs(_turnDurationSeconds);
        _turnTimeLeft = GetRemainingTimeFromDeadline();
        _turnTimeExpired = false;
    }

    /// <summary>Стоимость n-го шага в рамках одного перемещения.</summary>
    public int GetStepCost(int stepIndex)
    {
        if (stepIndex <= 0)
            return 0;

        float n = stepIndex;
        float value = (5f * n * n - 8f * n + 21f) / 3f;
        return Mathf.Max(1, Mathf.RoundToInt(value));
    }

    public int GetMoveStepCost(MovementPosture posture, int stepIndex)
    {
        int baseStepCost = GetStepCost(stepIndex) - GetStepCost(stepIndex - 1);
        return posture switch
        {
            MovementPosture.Run => Mathf.Max(1, Mathf.CeilToInt(baseStepCost * RunCostMultiplier)),
            MovementPosture.Sit or MovementPosture.Hide => Mathf.Max(1, Mathf.FloorToInt(baseStepCost * SitCostMultiplier)),
            _ => Mathf.Max(1, baseStepCost)
        };
    }

    /// <summary>Стоимость перехода на steps шагов, если уже сделано fromStepIndex шагов в этом ходу.</summary>
    public int GetMoveCost(int fromStepIndex, int steps)
    {
        return GetMoveCost(PreviewMovementPosture, fromStepIndex, steps);
    }

    public int GetMoveCost(MovementPosture posture, int fromStepIndex, int steps)
    {
        if (steps <= 0)
            return 0;

        int total = 0;
        for (int i = 1; i <= steps; i++)
            total += GetMoveStepCost(posture, fromStepIndex + i);
        return total;
    }

    /// <summary>Копия пути за текущий ход (для отправки на сервер). Вызывать до EndTurn().</summary>
    public List<(int col, int row)> GetTurnPathCopy()
    {
        if (_turnPath == null) return new List<(int col, int row)>(0);
        return new List<(int col, int row)>(_turnPath);
    }

    public BattleQueuedAction[] GetTurnActionsCopy()
    {
        if (_turnActions == null || _turnActions.Count == 0)
            return Array.Empty<BattleQueuedAction>();

        var copy = new BattleQueuedAction[_turnActions.Count];
        for (int i = 0; i < _turnActions.Count; i++)
        {
            var src = _turnActions[i];
            copy[i] = new BattleQueuedAction
            {
                actionType = src.actionType,
                targetPosition = src.targetPosition != null ? new HexPosition(src.targetPosition.col, src.targetPosition.row) : null,
                targetUnitId = src.targetUnitId,
                bodyPart = src.bodyPart,
                posture = src.posture,
                previousPosture = src.previousPosture,
                weaponCode = src.weaponCode,
                previousWeaponCode = src.previousWeaponCode,
                previousWeaponAttackApCost = src.previousWeaponAttackApCost,
                previousWeaponDamage = src.previousWeaponDamage,
                previousWeaponRange = src.previousWeaponRange,
                weaponAttackApCost = src.weaponAttackApCost,
                previousMagazineRounds = src.previousMagazineRounds,
                cost = src.cost
            };
        }
        return copy;
    }

    public bool QueuePostureChange(MovementPosture posture)
    {
        if (_currentPosture == posture)
            return true;
        if (_currentAp < ChangePostureCost)
            return false;

        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        _turnActions.Add(new BattleQueuedAction
        {
            actionType = "ChangePosture",
            posture = MovementPostureUtility.ToId(posture),
            previousPosture = MovementPostureUtility.ToId(_currentPosture),
            cost = ChangePostureCost
        });
        _apSpentThisTurn += ChangePostureCost;
        _currentAp -= ChangePostureCost;
        SetMovementPostureInternal(posture, notify: true);
        return true;
    }

    public bool QueueWaitAction(int cost)
    {
        int safeCost = Mathf.Max(1, cost);
        if (_currentAp < safeCost)
            return false;

        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        _turnActions.Add(new BattleQueuedAction
        {
            actionType = "Wait",
            cost = safeCost
        });
        _apSpentThisTurn += safeCost;
        _currentAp -= safeCost;
        return true;
    }

    public bool EnsureMovablePostureForMovement()
    {
        if (_currentPosture != MovementPosture.Hide)
            return true;
        return QueuePostureChange(MovementPosture.Sit);
    }

    /// <param name="bodyPartId">Server <see cref="BodyPartIds"/> / body_parts.id; 0 = unspecified.</param>
    public bool QueueAttackAction(string targetUnitId, int bodyPartId, int cost = 1)
    {
        bool usesMagazine = _weaponRangeHexes > 1 && _magazineCapacity > 0;
        if (usesMagazine && _currentMagazineRounds <= 0)
            return false;
        int safeCost = Mathf.Max(1, cost);
        if (_currentAp < safeCost)
            return false;

        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        _turnActions.Add(new BattleQueuedAction
        {
            actionType = "Attack",
            targetUnitId = targetUnitId,
            bodyPart = bodyPartId,
            posture = MovementPostureUtility.ToId(_currentPosture),
            previousMagazineRounds = _currentMagazineRounds,
            cost = safeCost
        });
        _apSpentThisTurn += safeCost;
        _currentAp -= safeCost;
        if (usesMagazine)
            _currentMagazineRounds = Mathf.Max(0, _currentMagazineRounds - 1);
        OnEquippedWeaponChanged?.Invoke();
        return true;
    }

    /// <summary>Выстрел по гексу прицела (Ctrl+клик): без targetUnitId, с <see cref="BattleQueuedAction.targetPosition"/> — урон по стене на ЛС, см. сервер.</summary>
    public bool QueueHexAttackAction(int col, int row, int cost = 1)
    {
        bool usesMagazine = _weaponRangeHexes > 1 && _magazineCapacity > 0;
        if (usesMagazine && _currentMagazineRounds <= 0)
            return false;
        int safeCost = Mathf.Max(1, cost);
        if (_currentAp < safeCost)
            return false;

        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        _turnActions.Add(new BattleQueuedAction
        {
            actionType = "Attack",
            targetUnitId = "",
            targetPosition = new HexPosition(col, row),
            bodyPart = BodyPartIds.None,
            posture = MovementPostureUtility.ToId(_currentPosture),
            previousMagazineRounds = _currentMagazineRounds,
            cost = safeCost
        });
        _apSpentThisTurn += safeCost;
        _currentAp -= safeCost;
        if (usesMagazine)
            _currentMagazineRounds = Mathf.Max(0, _currentMagazineRounds - 1);
        OnEquippedWeaponChanged?.Invoke();
        return true;
    }

    /// <summary>Queue manual reload action (R key). Server resets per-turn magazine usage.</summary>
    public bool QueueReloadAction(int cost = 1)
    {
        if (_magazineCapacity <= 0 || _currentMagazineRounds >= _magazineCapacity)
            return false;
        int need = Mathf.Max(0, _magazineCapacity - _currentMagazineRounds);
        if (_reserveAmmoRounds <= 0 || need <= 0)
            return false;
        int safeCost = Mathf.Max(1, cost);
        if (_currentAp < safeCost)
            return false;

        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        _turnActions.Add(new BattleQueuedAction
        {
            actionType = "Reload",
            posture = MovementPostureUtility.ToId(_currentPosture),
            previousMagazineRounds = _currentMagazineRounds,
            cost = safeCost
        });
        _apSpentThisTurn += safeCost;
        _currentAp -= safeCost;
        int loaded = Mathf.Min(need, _reserveAmmoRounds);
        _currentMagazineRounds += loaded;
        _reserveAmmoRounds = Mathf.Max(0, _reserveAmmoRounds - loaded);
        OnEquippedWeaponChanged?.Invoke();
        return true;
    }

    public bool QueueUseItemAction(int cost = 1)
    {
        int safeCost = Mathf.Max(1, cost);
        if (_currentAp < safeCost)
            return false;

        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        _turnActions.Add(new BattleQueuedAction
        {
            actionType = "UseItem",
            posture = MovementPostureUtility.ToId(_currentPosture),
            cost = safeCost
        });
        _apSpentThisTurn += safeCost;
        _currentAp -= safeCost;
        return true;
    }

    /// <summary>Смена оружия в очереди хода (<see cref="WeaponCatalog.EquipWeaponSwapApCost"/> ОД); сервер применяет EquipWeapon при закрытии раунда.</summary>
    /// <param name="costOverride">Если задано — переопределяет стоимость смены (по умолчанию 2 ОД).</param>
    /// <param name="weaponAttackApCost">Стоимость атаки новым оружием из БД (weapons.attack_ap_cost).</param>
    /// <param name="weaponDamageFromDb">Урон из БД/инвентаря; если &lt; 0 — подставляется 1 (офлайн без данных).</param>
    /// <param name="weaponRangeFromDb">Дальность из БД/инвентаря; если &lt; 0 — подставляется 1.</param>
    public bool QueueEquipWeaponAction(string weaponCode, int? costOverride = null, int weaponAttackApCost = 1, int weaponDamageFromDb = -1, int weaponRangeFromDb = -1)
    {
        string norm = WeaponCatalog.NormalizeWeaponCode(weaponCode);
        int dmg = weaponDamageFromDb >= 0 ? weaponDamageFromDb : 1;
        int range = weaponRangeFromDb >= 0 ? weaponRangeFromDb : 1;
        int safeCost = Mathf.Max(1, costOverride ?? WeaponCatalog.EquipWeaponSwapApCost);
        if (_currentAp < safeCost)
            return false;
        string prevCode = _weaponCode;
        int prevAtk = _weaponAttackApCost;
        int newAtk = Mathf.Max(1, weaponAttackApCost);

        if (_turnActions == null)
            _turnActions = new List<BattleQueuedAction>();

        _turnActions.Add(new BattleQueuedAction
        {
            actionType = "EquipWeapon",
            weaponCode = norm,
            previousWeaponCode = prevCode,
            previousWeaponAttackApCost = prevAtk,
            previousWeaponDamage = _weaponDamage,
            previousWeaponRange = _weaponRangeHexes,
            weaponAttackApCost = newAtk,
            posture = MovementPostureUtility.ToId(_currentPosture),
            cost = safeCost
        });
        _apSpentThisTurn += safeCost;
        _currentAp -= safeCost;
        SetEquippedWeapon(norm, dmg, range, newAtk);
        return true;
    }

    /// <summary>Отменить последнее действие в очереди текущего хода (движение, атака, смена позы, ожидание).</summary>
    public bool TryUndoLastQueuedAction(out string failureReason)
    {
        failureReason = null;
        if (IsDead || _isHidden)
        {
            failureReason = Loc.T("player.undo_unavailable");
            return false;
        }

        if (_isMoving)
        {
            failureReason = Loc.T("player.undo_wait_movement");
            return false;
        }

        if (_turnActions == null || _turnActions.Count == 0)
        {
            failureReason = Loc.T("player.undo_no_actions");
            return false;
        }

        BattleQueuedAction last = _turnActions[_turnActions.Count - 1];
        _turnActions.RemoveAt(_turnActions.Count - 1);

        string type = last.actionType ?? string.Empty;
        if (string.Equals(type, "MoveStep", StringComparison.OrdinalIgnoreCase))
        {
            int cost = Mathf.Max(0, last.cost);
            _currentAp += cost;
            _apSpentThisTurn -= cost;
            if (_stepsTakenThisTurn > 0)
                _stepsTakenThisTurn--;

            MovementPosture stepPosture = MovementPostureUtility.FromId(last.posture);
            if (stepPosture == MovementPosture.Run && cost > 0)
                _runMovementApSpentThisTurn = Mathf.Max(0, _runMovementApSpentThisTurn - cost);

            if (_turnPath != null && _turnPath.Count > 1)
                _turnPath.RemoveAt(_turnPath.Count - 1);

            if (_turnPath != null && _turnPath.Count > 0)
            {
                var back = _turnPath[_turnPath.Count - 1];
                _currentCol = back.col;
                _currentRow = back.row;
                if (_grid != null)
                {
                    transform.position = _grid.GetCellWorldPosition(_currentCol, _currentRow);
                    HexCell cell = _grid.GetCell(_currentCol, _currentRow);
                    OnMovedToCell?.Invoke(cell);
                }
            }

            return true;
        }

        if (string.Equals(type, "ChangePosture", StringComparison.OrdinalIgnoreCase))
        {
            int cost = Mathf.Max(0, last.cost);
            _currentAp += cost;
            _apSpentThisTurn -= cost;
            MovementPosture restore = MovementPostureUtility.FromId(last.previousPosture);
            SetMovementPostureInternal(restore, notify: true);
            return true;
        }

        if (string.Equals(type, "EquipWeapon", StringComparison.OrdinalIgnoreCase))
        {
            int cost = Mathf.Max(0, last.cost);
            _currentAp += cost;
            _apSpentThisTurn -= cost;
            string prev = string.IsNullOrEmpty(last.previousWeaponCode) ? WeaponCatalog.DefaultWeaponCode : last.previousWeaponCode;
            string c = WeaponCatalog.NormalizeWeaponCode(prev);
            int prevAtk = last.previousWeaponAttackApCost > 0 ? last.previousWeaponAttackApCost : 1;
            SetEquippedWeapon(c, last.previousWeaponDamage, last.previousWeaponRange, prevAtk);
            return true;
        }

        if (string.Equals(type, "Wait", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "Reload", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "Attack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "UseItem", StringComparison.OrdinalIgnoreCase))
        {
            int cost = Mathf.Max(0, last.cost);
            _currentAp += cost;
            _apSpentThisTurn -= cost;
            if (_magazineCapacity > 0)
            {
                _currentMagazineRounds = Mathf.Clamp(last.previousMagazineRounds, 0, _magazineCapacity);
                OnEquippedWeaponChanged?.Invoke();
            }
            return true;
        }

        _turnActions.Add(last);
        failureReason = Loc.T("player.undo_unknown_action");
        return false;
    }

    public bool IsPenaltyHexAtDistance(int distanceFromCurrent)
    {
        if (distanceFromCurrent <= 0)
            return false;

        MovementPosture posture = PreviewMovementPosture;
        if (posture != MovementPosture.Run)
            return false;

        int totalRunCost = _runMovementApSpentThisTurn + GetMoveCost(MovementPosture.Run, _stepsTakenThisTurn, distanceFromCurrent);
        int threshold = Mathf.Max(1, Mathf.CeilToInt(_maxAp * RunPenaltyThresholdFraction));
        return totalRunCost >= threshold;
    }

    /// <summary>Максимум шагов, которые можно пройти при текущих ОД (уже сделано stepsAlready, лимит stepsLimit).</summary>
    private int GetAllowedSteps(MovementPosture posture, int stepsAlready, int stepsLimit, int currentAp)
    {
        int allowed = 0;
        for (int steps = 1; steps <= stepsLimit; steps++)
        {
            if (GetMoveCost(posture, stepsAlready, steps) > currentAp)
                break;
            allowed = steps;
        }
        return allowed;
    }

    /// <summary>Ordinary edge cost (no escape lump). Full path uses sequential simulation in <see cref="GetAllowedStepsAlongHexPath"/>.</summary>
    public int GetMoveStepApCostForEdge(MovementPosture posture, int stepIndexWithinTurn, (int col, int row) from, (int col, int row) to) =>
        GetMoveStepCost(posture, stepIndexWithinTurn);

    private static bool IsPlanningEscapeEntry((int col, int row) from, (int col, int row) to)
    {
        GameSession s = GameSession.Active;
        return s != null
            && !s.IsEscapeBorderHex(from.col, from.row)
            && s.IsEscapeBorderHex(to.col, to.row);
    }

    private bool TryComputeSequentialMoveEdgeApCosts(
        MovementPosture posture,
        int stepsAlreadyAtSubPathStart,
        List<(int col, int row)> subPath,
        int edgeCount,
        int startingAp,
        out int totalCost,
        out int[] stepCosts)
    {
        totalCost = 0;
        stepCosts = null;
        if (subPath == null || edgeCount <= 0 || subPath.Count < edgeCount + 1)
            return false;
        if (_moveEdgeCostsScratch.Length < edgeCount)
            _moveEdgeCostsScratch = new int[Math.Max(edgeCount, 16)];
        int ap = startingAp;
        for (int i = 0; i < edgeCount; i++)
        {
            var from = subPath[i];
            var to = subPath[i + 1];
            int c;
            if (IsPlanningEscapeEntry(from, to))
            {
                if (ap < 1)
                    return false;
                c = ap;
            }
            else
                c = GetMoveStepApCostForEdge(posture, stepsAlreadyAtSubPathStart + i, from, to);
            if (c > ap)
                return false;
            _moveEdgeCostsScratch[i] = c;
            ap -= c;
            totalCost += c;
        }
        stepCosts = _moveEdgeCostsScratch;
        return true;
    }

    private int GetAllowedStepsAlongHexPath(MovementPosture posture, List<(int col, int row)> path, int maxEdges)
    {
        if (path == null || path.Count < 2 || maxEdges <= 0)
            return 0;
        int cap = Mathf.Min(maxEdges, path.Count - 1);
        int stepsAlready = _stepsTakenThisTurn;
        int allowed = 0;
        int ap = _currentAp;
        for (int edges = 1; edges <= cap; edges++)
        {
            var from = path[edges - 1];
            var to = path[edges];
            int c;
            if (IsPlanningEscapeEntry(from, to))
            {
                if (ap < 1)
                    break;
                c = ap;
            }
            else
                c = GetMoveStepApCostForEdge(posture, stepsAlready + edges - 1, from, to);
            if (c > ap)
                break;
            ap -= c;
            allowed = edges;
        }
        return allowed;
    }

    private int SumApCostForHexPathEdges(MovementPosture posture, int stepsAlready, List<(int col, int row)> path, int edgeCount)
    {
        if (path == null || path.Count < 2 || edgeCount <= 0)
            return 0;
        int cap = Mathf.Min(edgeCount, path.Count - 1);
        int ap = _currentAp;
        int total = 0;
        for (int i = 1; i <= cap; i++)
        {
            var from = path[i - 1];
            var to = path[i];
            int c;
            if (IsPlanningEscapeEntry(from, to))
                c = ap >= 1 ? ap : 0;
            else
                c = GetMoveStepApCostForEdge(posture, stepsAlready + i - 1, from, to);
            total += c;
            ap -= c;
        }
        return total;
    }

    /// <summary>Total AP to walk the full path from current planning state (for hover labels).</summary>
    public int SumApCostForHexPath(List<(int col, int row)> path)
    {
        if (path == null || path.Count < 2)
            return 0;
        return SumApCostForHexPathEdges(PreviewMovementPosture, _stepsTakenThisTurn, path, path.Count - 1);
    }

    /// <summary>Установить штраф с сервера (доля 0–0.9).</summary>
    public void SetPenaltyFraction(float value)
    {
        value = Mathf.Clamp(value, 0f, MaxPenaltyFraction);
    }

    public void SetMovementPostureFromServer(string postureId)
    {
        SetMovementPostureInternal(MovementPostureUtility.FromId(postureId), notify: true);
    }

    /// <summary>Перед проигрыванием журнала раунда: поза на <b>начало</b> раунда на сервере (не конец планирования на клиенте).</summary>
    public void ApplyReplayInitialLocomotionPosture(string postureId)
    {
        _currentPosture = MovementPostureUtility.FromId(postureId);
        if (_characterAnimator == null)
            _characterAnimator = GetComponentInChildren<PlayerCharacterAnimator>();
        _characterAnimator?.SnapLocomotionPostureForRoundReplayStart();
        OnMovementPostureChanged?.Invoke(_currentPosture);
    }

    /// <summary>Пауза таймера хода (ожидание сервера после «Завершить ход»).</summary>
    public void SetTurnTimerPaused(bool paused)
    {
        if (_turnTimerPaused == paused) return;

        if (paused)
            _turnTimeLeft = GetRemainingTimeFromDeadline();
        else if (!_turnTimeExpired)
            _turnDeadlineUtcMs = BuildRoundDeadlineUtcMs(_turnTimeLeft);

        _turnTimerPaused = paused;
        if (paused) _turnTimeExpired = false;
    }

    /// <summary>Синхронизировать номер раунда и дедлайн таймера с сервером.</summary>
    public void SetRoundState(int roundIndex, long roundDeadlineUtcMs)
    {
        _turnCount = roundIndex;
        _turnDeadlineUtcMs = roundDeadlineUtcMs > 0 ? roundDeadlineUtcMs : BuildRoundDeadlineUtcMs(_turnDurationSeconds);
        _turnTimeLeft = GetRemainingTimeFromDeadline();
        _turnTimeExpired = _turnTimeLeft <= 0f;
    }

    public static long BuildRoundDeadlineUtcMs(float durationSeconds)
    {
        long durationMs = (long)Mathf.Round(Mathf.Max(0f, durationSeconds) * 1000f);
        return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + durationMs;
    }

    private float GetRemainingTimeFromDeadline()
    {
        if (_turnDeadlineUtcMs <= 0)
            return Mathf.Max(0f, _turnTimeLeft);

        long remainingMs = _turnDeadlineUtcMs - System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return Mathf.Max(0f, remainingMs / 1000f);
    }

    /// <summary>Применить результат хода с сервера: ОД, штраф, финальная позиция; визуал ставится в начало пути для последующей анимации.</summary>
    /// <param name="applyLocomotionPosture">Если false — не выставлять позу походки (ожидается проигрывание журнала executedActions по шагам).</param>
    public void ApplyServerTurnResult(HexPosition finalPosition, HexPosition[] actualPath, int currentAp, float penaltyFraction, string currentPosture, bool prepareForAnimation = true, bool applyLocomotionPosture = true)
    {
        _currentAp = currentAp;
        _currentCol = finalPosition != null ? finalPosition.col : _currentCol;
        _currentRow = finalPosition != null ? finalPosition.row : _currentRow;
        _apSpentThisTurn = 0;
        _stepsTakenThisTurn = 0;
        _runMovementApSpentThisTurn = 0;
        if (applyLocomotionPosture)
            SetMovementPostureInternal(MovementPostureUtility.FromId(currentPosture), notify: true);
        if (_turnPath != null) _turnPath.Clear();
        if (_turnActions != null) _turnActions.Clear();

        if (_grid != null && actualPath != null && actualPath.Length > 0)
        {
            var pos = prepareForAnimation ? actualPath[0] : actualPath[actualPath.Length - 1];
            transform.position = _grid.GetCellWorldPosition(pos.col, pos.row);
        }
        else if (_grid != null)
            transform.position = _grid.GetCellWorldPosition(_currentCol, _currentRow);
    }

    /// <summary>Проиграть анимацию движения по пути с сервера (actualPath). Запускать после ApplyServerTurnResult. Не меняет состояние.</summary>
    /// <param name="driveCamera">Если true — при старте включается слежение 3-го лица (только по явному вызову; <see cref="GameSession"/> для серверных анимаций передаёт false).</param>
    /// <param name="resetHexWalkPhase">Если true — сбросить чередование фазы походки перед первым шагом (полный путь с сервера). False — следующий сегмент в журнале executedActions.</param>
    /// <param name="clearMovementStateWhenDone">Если false — не сбрасывать <see cref="IsMoving"/> по завершении (следующий MoveStep того же юнита в журнале). Иначе аниматор на кадр уходит в idle и ломает плавность как при планировании.</param>
    public IEnumerator PlayPathAnimation(HexPosition[] path, bool driveCamera = true, bool resetHexWalkPhase = true, bool clearMovementStateWhenDone = true)
    {
        if (_grid == null || path == null || path.Length < 2)
        {
            if (_grid != null && path != null && path.Length == 1)
                transform.position = _grid.GetCellWorldPosition(path[0].col, path[0].row);
            yield break;
        }

        HexGridCamera hexCam = null;
        Vector3? firstStepDir = null;
        if (driveCamera)
        {
            hexCam = _hexGridCamera;
            Vector3 a = _grid.GetCellWorldPosition(path[0].col, path[0].row);
            Vector3 b = _grid.GetCellWorldPosition(path[1].col, path[1].row);
            Vector3 d = b - a;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f)
                firstStepDir = d.normalized;
        }

        int interruptVersion = _movementInterruptVersion;
        _isMoving = true;
        ClearRangedFacingOverrideForLocomotion();
        transform.position = _grid.GetCellWorldPosition(path[0].col, path[0].row);
        if (resetHexWalkPhase)
        {
            _characterAnimator?.ResetHexWalkPhaseForNewPath();
        }
        bool enteredThirdPerson = false;
        if (driveCamera && hexCam != null)
        {
            yield return hexCam.EnterThirdPersonFollowRoutine(transform, firstStepDir);
            enteredThirdPerson = true;
        }

        bool interrupted = false;
        try
        {
            for (int i = 1; i < path.Length; i++)
            {
                if (interruptVersion != _movementInterruptVersion || IsDead || _isHidden)
                {
                    interrupted = true;
                    break;
                }

                var step = path[i];
                if (_characterAnimator == null)
                    _characterAnimator = GetComponentInChildren<PlayerCharacterAnimator>();
                bool waitedPostureTransition = false;
                while (_characterAnimator != null && _characterAnimator.IsPostureTransitionActive)
                {
                    waitedPostureTransition = true;
                    yield return null;
                }

                if (waitedPostureTransition)
                    yield return null; // кадр после Sit→Stand: LateUpdate подставит walk/run до NotifyHexStepStarted

                _characterAnimator?.NotifyHexStepStarted(_moveDurationPerHex);

                Vector3 target = _grid.GetCellWorldPosition(step.col, step.row);
                Vector3 stepStart = transform.position;
                float elapsed = 0f;

                while (elapsed < _moveDurationPerHex)
                {
                    if (interruptVersion != _movementInterruptVersion || IsDead || _isHidden)
                    {
                        interrupted = true;
                        break;
                    }

                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / _moveDurationPerHex);
                    transform.position = Vector3.Lerp(stepStart, target, t);
                    yield return null;
                }

                if (interrupted)
                    break;

                transform.position = target;
            }
        }
        finally
        {
            if (clearMovementStateWhenDone || interrupted)
                _isMoving = false;
        }

        if (enteredThirdPerson && hexCam != null)
        {
            if (interrupted)
            {
                hexCam.EndThirdPersonFollowImmediate();
                GamePhaseViewController.StopModeButtonPulseIfAny();
            }
            else
                GamePhaseViewController.NotifyViewAnimationEndedKeepThirdPerson();
        }
    }

    /// <summary>Проиграть анимацию всего пути за ход (без изменения ОД и штрафов), используется при завершении хода с анимацией.</summary>
    /// <remarks>Камера и режим не переключаются автоматически — только по кнопке режима в UI.</remarks>
    public IEnumerator PlayLastMoveAnimation()
    {
        if (_grid == null || _turnPath == null || _turnPath.Count < 2) yield break;

        _isMoving = true;
        ClearRangedFacingOverrideForLocomotion();

        // Стартуем с исходной клетки хода.
        Vector3 startPos = _grid.GetCellWorldPosition(_turnPath[0].col, _turnPath[0].row);
        transform.position = startPos;
        _characterAnimator?.ResetHexWalkPhaseForNewPath();

        try
        {
            for (int i = 1; i < _turnPath.Count; i++)
            {
                var step = _turnPath[i];
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

            _isMoving = false;
        }
        finally
        {
            _isMoving = false;
        }
    }

    /// <summary>
    /// Остановить анимацию движения. Очередь хода и <see cref="GetTurnActionsCopy"/> не меняются — на сервер уходит весь запланированный путь.
    /// Логическая позиция остаётся на последней достигнутой клетке до ответа сервера.
    /// </summary>
    /// <param name="exitThirdPersonCamera">Если false — не выходим из 3-го лица (например, «конец хода» в режиме просмотра).</param>
    public void ForceStopMovement(bool exitThirdPersonCamera = true)
    {
        _movementInterruptVersion++;
        StopAllCoroutines();
        if (exitThirdPersonCamera)
        {
            _hexGridCamera?.EndThirdPersonFollowImmediate();
            GamePhaseViewController.StopModeButtonPulseIfAny();
        }
        if (_grid != null)
            transform.position = _grid.GetCellWorldPosition(_currentCol, _currentRow);
        _isMoving = false;
    }

    public void SetHidden(bool hidden)
    {
        if (_isHidden == hidden)
            return;

        _isHidden = hidden;

        _cachedRenderers ??= GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in _cachedRenderers)
            renderer.enabled = !hidden;

        _cachedColliders ??= GetComponentsInChildren<Collider>(true);
        foreach (var collider in _cachedColliders)
            collider.enabled = !hidden;
    }

    public void SetHealth(int currentHp, int maxHp)
    {
        _maxHp = Mathf.Max(1, maxHp);
        _currentHp = Mathf.Clamp(currentHp, 0, _maxHp);
        OnHealthChanged?.Invoke(_currentHp, _maxHp);
    }

    private void SetMovementPostureInternal(MovementPosture posture, bool notify)
    {
        if (_currentPosture == posture)
            return;

        _currentPosture = posture;
        if (notify)
            OnMovementPostureChanged?.Invoke(_currentPosture);
    }
}
