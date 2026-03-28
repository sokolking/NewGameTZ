using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Humanoid-анимации локального игрока через PlayableGraph + AnimationClipPlayable.
/// Важно: у <see cref="Animator"/> должен быть назначен пустой контроллер (см. Resources/Animator/PlayablesStub)
/// — иначе вывод Playables на Animator в Unity часто не даёт видимой анимации.
/// </summary>
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public sealed class PlayerCharacterAnimator : MonoBehaviour
{
    private const string DefaultStubResourcePath = "Animator/PlayablesStub";

    [SerializeField] private Player _player;
    [SerializeField] private RemoteBattleUnitView _remoteBattleUnit;
    [SerializeField] private Animator _animator;
    [Tooltip("If true, clear assigned controller in Awake (then stub from Resources or Playable Controller Override applies).")]
    [SerializeField] private bool _clearAnimatorController = true;
    [Tooltip("Optional: your own empty Animator Controller instead of Resources/Animator/PlayablesStub.")]
    [SerializeField] private RuntimeAnimatorController _playableControllerOverride;
    [SerializeField] private float _uniformModelScale = 1f;
    [SerializeField] private bool _faceMovementDirection = true;
    [Tooltip("Off: rotate this object (model) only. On: rotate Player root (Transform).")]
    [SerializeField] private bool _rotatePlayerRoot = false;
    [SerializeField] private float _rotationSpeed = 14f;
    [SerializeField] private float _moveFaceMinSqr = 0.0004f;
    [Header("Hex locomotion")]
    [Tooltip("Fraction of full walk/run cycle per adjacent hex step (0.5 ≈ half cycle — leg alternation at phase 0 vs 0.5).")]
    [SerializeField] [Range(0.1f, 1f)] private float _locomotionCycleFractionPerHex = 0.5f;

    [Header("Clips — Object: drag clip from expanded FBX (green slider icon)")]
    [SerializeField] private UnityEngine.Object _idle;
    [SerializeField] private UnityEngine.Object _walk;
    [SerializeField] private UnityEngine.Object _run;
    [SerializeField] private UnityEngine.Object _sit;
    [Tooltip("Walk cycle while crouched (Sit/Hide posture). Optional: falls back to loop sit when not moving.")]
    [SerializeField] private UnityEngine.Object _sitWalk;
    [SerializeField] private UnityEngine.Object _sitWalkPistol;
    [SerializeField] private UnityEngine.Object _idlePistol;
    [SerializeField] private UnityEngine.Object _walkPistol;
    [SerializeField] private UnityEngine.Object _runPistol;
    [SerializeField] private UnityEngine.Object _sitPistol;
    [SerializeField] private UnityEngine.Object _dead;
    [Header("Cold weapons (standing idle + melee swings; server category cold)")]
    [Tooltip("Standing still with fist/knife etc. Walk/run keep generic locomotion clips.")]
    [SerializeField] private UnityEngine.Object _idleCold;
    [SerializeField] private UnityEngine.Object _attackColdHead;
    [SerializeField] private UnityEngine.Object _attackColdBody;
    [SerializeField] private UnityEngine.Object _attackColdLegs;
    [Header("Items")]
    [Tooltip("One-shot self-use animation for medicine item actions.")]
    [SerializeField] private UnityEngine.Object _useItemMedicine;
    [Tooltip("Playback speed for medicine use animation (>1 = faster).")]
    [SerializeField] [Range(0.25f, 5f)] private float _medicineUsePlaybackSpeed = 1f;
    [Tooltip("Keep rig local Y while medicine clip plays to avoid sinking into floor from root/hips translation.")]
    [SerializeField] private bool _lockLocalRigYDuringMedicineUse = true;
    [Tooltip("If mesh bounds still go below baseline during medicine clip, lift model up to keep feet above ground.")]
    [SerializeField] private bool _preventMeshSinkingDuringMedicineUse = true;
    [Tooltip("Playback speed for cold melee attack clips (>1 = faster).")]
    [SerializeField] [Range(0.5f, 4f)] private float _coldMeleeAttackPlaybackSpeed = 2f;
    [Header("Posture transitions (Walk/Run ↔ Sit/Hide)")]
    [Tooltip("Once when switching from walk or run to sit or hide.")]
    [SerializeField] private UnityEngine.Object _standToSit;
    [Tooltip("Once when switching from sit or hide to walk or run.")]
    [SerializeField] private UnityEngine.Object _sitToStand;

    private PlayableGraph _graph;
    private AnimationClipPlayable _clipPlayable;
    private AnimationClip _currentClip;
    private bool _deathPlayed;
    private Vector3 _lastWorldPos;
    /// <summary>Если задано — <see cref="LateUpdate"/> крутит модель в эту сторону (выстрел), игнорируя лицо к движению.</summary>
    private Vector3? _horizontalFacingOverride;
    /// <summary>Чередование старта walk/run: true — время 0, false — середина цикла (другая нога впереди).</summary>
    private bool _hexWalkPhaseFlip;

    // Кэшированные касты — вместо `as AnimationClip` (9 virtual-call + type-check) каждый кадр в ResolveLocomotionClip.
    private AnimationClip _cachedClipIdle;
    private AnimationClip _cachedClipWalk;
    private AnimationClip _cachedClipRun;
    private AnimationClip _cachedClipSit;
    private AnimationClip _cachedClipSitWalk;
    private AnimationClip _cachedClipSitWalkPistol;
    private AnimationClip _cachedClipIdlePistol;
    private AnimationClip _cachedClipWalkPistol;
    private AnimationClip _cachedClipRunPistol;
    private AnimationClip _cachedClipSitPistol;
    private AnimationClip _cachedClipDead;
    private AnimationClip _cachedClipIdleCold;
    private AnimationClip _cachedClipAttackColdHead;
    private AnimationClip _cachedClipAttackColdBody;
    private AnimationClip _cachedClipAttackColdLegs;
    private AnimationClip _cachedClipUseItemMedicine;
    private AnimationClip _cachedClipStandToSit;
    private AnimationClip _cachedClipSitToStand;
    private bool _clipsCached;

    /// <summary>Blocks locomotion graph swaps while a one-shot melee clip plays.</summary>
    private bool _meleeAttackActive;
    /// <summary>Blocks locomotion graph swaps while a one-shot item-use clip plays.</summary>
    private bool _itemUseActive;

    private MovementPosture _previousPostureTracked;
    private Coroutine _postureTransitionCoroutine;
    private bool _postureTransitionActive;

    // Кэш состояния для ResolveLocomotionClip — пересчитываем только при изменении.
    private AnimationClip _resolvedClipCache;
    private bool _resolvedClipDirty = true;
    private bool _lastArmed;
    private bool _lastColdIdle;
    private MovementPosture _lastPosture;
    private bool _lastMoving;

    private AnimationClip ClipIdle { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipIdle; } }
    private AnimationClip ClipWalk { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipWalk; } }
    private AnimationClip ClipRun { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipRun; } }
    private AnimationClip ClipSit { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipSit; } }
    private AnimationClip ClipSitWalk { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipSitWalk; } }
    private AnimationClip ClipSitWalkPistol { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipSitWalkPistol; } }
    private AnimationClip ClipIdlePistol { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipIdlePistol; } }
    private AnimationClip ClipWalkPistol { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipWalkPistol; } }
    private AnimationClip ClipRunPistol { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipRunPistol; } }
    private AnimationClip ClipSitPistol { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipSitPistol; } }
    private AnimationClip ClipDead { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipDead; } }
    private AnimationClip ClipStandToSit { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipStandToSit; } }
    private AnimationClip ClipSitToStand { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipSitToStand; } }
    private AnimationClip ClipIdleCold { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipIdleCold; } }
    private AnimationClip ClipAttackColdHead { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipAttackColdHead; } }
    private AnimationClip ClipAttackColdBody { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipAttackColdBody; } }
    private AnimationClip ClipAttackColdLegs { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipAttackColdLegs; } }
    private AnimationClip ClipUseItemMedicine { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipUseItemMedicine; } }

    private void CacheClipReferences()
    {
        _cachedClipIdle = _idle as AnimationClip;
        _cachedClipWalk = _walk as AnimationClip;
        _cachedClipRun = _run as AnimationClip;
        _cachedClipSit = _sit as AnimationClip;
        _cachedClipSitWalk = _sitWalk as AnimationClip;
        _cachedClipSitWalkPistol = _sitWalkPistol as AnimationClip;
        _cachedClipIdlePistol = _idlePistol as AnimationClip;
        _cachedClipWalkPistol = _walkPistol as AnimationClip;
        _cachedClipRunPistol = _runPistol as AnimationClip;
        _cachedClipSitPistol = _sitPistol as AnimationClip;
        _cachedClipDead = _dead as AnimationClip;
        _cachedClipIdleCold = _idleCold as AnimationClip;
        _cachedClipAttackColdHead = _attackColdHead as AnimationClip;
        _cachedClipAttackColdBody = _attackColdBody as AnimationClip;
        _cachedClipAttackColdLegs = _attackColdLegs as AnimationClip;
        _cachedClipUseItemMedicine = _useItemMedicine as AnimationClip;
        _cachedClipStandToSit = _standToSit as AnimationClip;
        _cachedClipSitToStand = _sitToStand as AnimationClip;
        _clipsCached = true;
    }

    /// <summary>
    /// Copies clip references and tuning from the local player for humanoid retargeting on another mesh (e.g. mob).
    /// Call while this <see cref="GameObject"/> is <b>inactive</b> so <see cref="Awake"/> runs after fields are assigned.
    /// </summary>
    public void CopyLocomotionConfigFrom(PlayerCharacterAnimator source)
    {
        if (source == null)
            return;

        _clearAnimatorController = source._clearAnimatorController;
        _playableControllerOverride = source._playableControllerOverride;
        _uniformModelScale = source._uniformModelScale;
        _faceMovementDirection = source._faceMovementDirection;
        _rotatePlayerRoot = source._rotatePlayerRoot;
        _rotationSpeed = source._rotationSpeed;
        _moveFaceMinSqr = source._moveFaceMinSqr;
        _locomotionCycleFractionPerHex = source._locomotionCycleFractionPerHex;
        _coldMeleeAttackPlaybackSpeed = source._coldMeleeAttackPlaybackSpeed;
        _medicineUsePlaybackSpeed = source._medicineUsePlaybackSpeed;

        _idle = source._idle;
        _walk = source._walk;
        _run = source._run;
        _sit = source._sit;
        _sitWalk = source._sitWalk;
        _sitWalkPistol = source._sitWalkPistol;
        _idlePistol = source._idlePistol;
        _walkPistol = source._walkPistol;
        _runPistol = source._runPistol;
        _sitPistol = source._sitPistol;
        _dead = source._dead;
        _idleCold = source._idleCold;
        _attackColdHead = source._attackColdHead;
        _attackColdBody = source._attackColdBody;
        _attackColdLegs = source._attackColdLegs;
        _useItemMedicine = source._useItemMedicine;
        _standToSit = source._standToSit;
        _sitToStand = source._sitToStand;

        _player = null;
        _remoteBattleUnit = null;
        _animator = null;
        _clipsCached = false;
    }

    private static bool IsStandingPosture(MovementPosture p) =>
        p == MovementPosture.Walk || p == MovementPosture.Run;

    private static bool IsCrouchPosture(MovementPosture p) =>
        p == MovementPosture.Sit || p == MovementPosture.Hide;

    /// <summary>Тот же pivot, что и в <see cref="LateUpdate"/> (корень Player / RemoteBattleUnitView или этот объект).</summary>
    public Transform FacingPivot =>
        _rotatePlayerRoot && _player != null
            ? _player.transform
            : _rotatePlayerRoot && _remoteBattleUnit != null
                ? _remoteBattleUnit.transform
                : transform;

    /// <summary>Горизонтальное направление «вперёд» для выстрела; сбросить через <see cref="ClearHorizontalFacingOverride"/>.</summary>
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

    /// <summary>Сбросить чередование фазы walk/run перед новым путём (как в начале планирования с первой клетки пути).</summary>
    /// <remarks>После предпросмотра движения <see cref="_hexWalkPhaseFlip"/> уже переключён; без сброса повтор по <c>actualPath</c> с сервера начинает с «другой ноги», чем первый шаг планирования.</remarks>
    public void ResetHexWalkPhaseForNewPath()
    {
        _hexWalkPhaseFlip = false;
    }

    /// <summary>Совпасть с <see cref="Player.CurrentMovementPosture"/> перед журналом: трекер + idle без blend (после <c>ApplyReplayInitialLocomotionPosture</c> на игроке).</summary>
    public void SnapLocomotionPostureForRoundReplayStart()
    {
        if (_player == null)
            return;
        StopPostureTransitionCoroutine();
        _postureTransitionActive = false;
        _previousPostureTracked = _player.CurrentMovementPosture;
        _resolvedClipDirty = true;
        if (_player.IsMoving || _meleeAttackActive || _itemUseActive)
            return;
        AnimationClip clip = ResolveLocomotionClip();
        if (clip != null)
            PlayClip(clip);
    }

    /// <summary>Идёт one-shot клип Sit↔Stand — <see cref="NotifyHexStepStarted"/> его пропускает; корутина движения должна подождать.</summary>
    public bool IsPostureTransitionActive => _postureTransitionActive;

    /// <summary>
    /// Вызывается из <see cref="Player"/> в начале каждого шага по гексу: один цикл walk/run с фазой 0 или 0.5 и скоростью под длительность шага.
    /// </summary>
    public void NotifyHexStepStarted(float stepDurationSeconds)
    {
        if (_meleeAttackActive)
            return;
        if (_itemUseActive)
            return;
        if (_postureTransitionActive)
            return;
        if (_animator == null)
            return;
        if (_player == null && _remoteBattleUnit == null)
            return;
        if (stepDurationSeconds <= 1e-5f)
            return;
        bool moving = _player != null ? _player.IsMoving : _remoteBattleUnit.IsMoving;
        if (!moving)
            return;

        AnimationClip clip = ResolveLocomotionClip();
        if (clip == null || clip.length <= 1e-5f)
            return;
        if (!IsHexStepLocomotionClip(clip))
            return;

        _hexWalkPhaseFlip = !_hexWalkPhaseFlip;
        float frac = Mathf.Clamp(_locomotionCycleFractionPerHex, 0.05f, 1f);
        double startTime = _hexWalkPhaseFlip ? 0.0 : clip.length * 0.5;
        float cyclePortion = clip.length * frac;
        float speed = cyclePortion / stepDurationSeconds;
        PlayClipInternal(clip, speed, startTime);
    }

    /// <summary>Clips driven by <see cref="NotifyHexStepStarted"/> (phase/speed per hex); includes crouch walk.</summary>
    private bool IsHexStepLocomotionClip(AnimationClip clip)
    {
        if (clip == null)
            return false;
        return clip == ClipWalk || clip == ClipRun || clip == ClipWalkPistol || clip == ClipRunPistol
            || clip == ClipSitWalk || clip == ClipSitWalkPistol;
    }

    /// <summary>
    /// World position source for movement-facing delta.
    /// Player/Remote root moves, while the visual child (this transform) can stay locally fixed.
    /// </summary>
    private Vector3 GetMovementWorldPosition()
    {
        if (_player != null)
            return _player.transform.position;
        if (_remoteBattleUnit != null)
            return _remoteBattleUnit.transform.position;
        return transform.position;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplyCoerceAllClipReferences(recordUndo: true);
    }

    /// <summary>При Play в редакторе: исправить ссылки, если в сцене остались GameObject вместо клипов.</summary>
    private void EditorTryCoerceClipReferences()
    {
        ApplyCoerceAllClipReferences(recordUndo: false);
    }

    private void ApplyCoerceAllClipReferences(bool recordUndo)
    {
        if (recordUndo)
            Undo.RecordObject(this, "Coerce animation clips");

        _idle = CoerceClipReference(_idle, new[] { "idle" });
        _walk = CoerceClipReference(_walk, new[] { "walk" });
        _run = CoerceClipReference(_run, new[] { "run" });
        _sit = CoerceClipReference(_sit, new[] { "sit" });
        _sitWalk = CoerceClipReference(_sitWalk, new[] { "sit_walk", "sitwalk", "crouch", "walk" });
        _sitWalkPistol = CoerceClipReference(_sitWalkPistol, new[] { "sit_walk", "sitwalk", "walk", "pistol" });
        _idlePistol = CoerceClipReference(_idlePistol, new[] { "idle", "pistol" });
        _walkPistol = CoerceClipReference(_walkPistol, new[] { "walk", "pistol" });
        _runPistol = CoerceClipReference(_runPistol, new[] { "run", "pistol" });
        _sitPistol = CoerceClipReference(_sitPistol, new[] { "sit", "pistol" });
        _dead = CoerceClipReference(_dead, new[] { "dead", "death", "die" });
        // Order matters: both filenames contain "sit" and "stand" as substrings.
        _standToSit = CoerceClipReference(_standToSit, new[] { "stand_to_sit", "standtosit", "stand", "sit" });
        _sitToStand = CoerceClipReference(_sitToStand, new[] { "sit_to_stand", "sittostand", "sit", "stand" });
        _idleCold = CoerceClipReference(_idleCold, new[] { "idle", "cold" });
        _attackColdHead = CoerceClipReference(_attackColdHead, new[] { "head", "atack", "attack", "cold" });
        _attackColdBody = CoerceClipReference(_attackColdBody, new[] { "body", "atack", "attack", "cold" });
        _attackColdLegs = CoerceClipReference(_attackColdLegs, new[] { "leg", "legs", "atack", "attack", "cold" });
        _useItemMedicine = CoerceClipReference(_useItemMedicine, new[] { "medkit", "medicine", "use", "heal" });

        if (recordUndo)
            EditorUtility.SetDirty(this);
    }

    /// <summary>
    /// Перетаскивание корня FBX или Model (GameObject) вместо подресурса AnimationClip — подбираем клип из того же .fbx.
    /// </summary>
    private static UnityEngine.Object CoerceClipReference(UnityEngine.Object o, string[] nameHints)
    {
        if (o == null)
            return null;
        if (o is AnimationClip)
            return o;

        string path = AssetDatabase.GetAssetPath(o);
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            return o;

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        var clips = new List<AnimationClip>(8);
        foreach (UnityEngine.Object a in assets)
        {
            if (a is AnimationClip clip && !clip.name.StartsWith("__preview", StringComparison.Ordinal))
                clips.Add(clip);
        }

        if (clips.Count == 0)
            return o;
        if (clips.Count == 1)
            return clips[0];

        foreach (string hint in nameHints)
        {
            foreach (AnimationClip clip in clips)
            {
                if (clip.name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    return clip;
            }
        }

        string baseName = Path.GetFileNameWithoutExtension(path);
        foreach (AnimationClip clip in clips)
        {
            if (clip.name.IndexOf(baseName, StringComparison.OrdinalIgnoreCase) >= 0)
                return clip;
        }

        return clips[0];
    }
#endif

    private void Awake()
    {
#if UNITY_EDITOR
        EditorTryCoerceClipReferences();
#endif
        if (_uniformModelScale > 0f)
            transform.localScale = Vector3.one * _uniformModelScale;

        ResolveAnimatorReference();
        ConfigureAnimatorForPlayables();

        ValidateClipSlot(nameof(_idle), _idle);
        ValidateClipSlot(nameof(_walk), _walk);
        ValidateClipSlot(nameof(_run), _run);
        ValidateClipSlot(nameof(_sit), _sit);
        ValidateClipSlot(nameof(_sitWalk), _sitWalk);
        ValidateClipSlot(nameof(_sitWalkPistol), _sitWalkPistol);
        ValidateClipSlot(nameof(_idlePistol), _idlePistol);
        ValidateClipSlot(nameof(_walkPistol), _walkPistol);
        ValidateClipSlot(nameof(_runPistol), _runPistol);
        ValidateClipSlot(nameof(_sitPistol), _sitPistol);
        ValidateClipSlot(nameof(_dead), _dead);
        ValidateClipSlot(nameof(_standToSit), _standToSit);
        ValidateClipSlot(nameof(_sitToStand), _sitToStand);
        ValidateClipSlot(nameof(_idleCold), _idleCold);
        ValidateClipSlot(nameof(_attackColdHead), _attackColdHead);
        ValidateClipSlot(nameof(_attackColdBody), _attackColdBody);
        ValidateClipSlot(nameof(_attackColdLegs), _attackColdLegs);
        ValidateClipSlot(nameof(_useItemMedicine), _useItemMedicine);
    }

    /// <summary>Animator is usually on a child mesh; <see cref="GetComponent{T}"/> on the rig root is often null.</summary>
    private void ResolveAnimatorReference()
    {
        if (_animator != null)
            return;
        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>(true);
    }

    private void ConfigureAnimatorForPlayables()
    {
        if (_animator == null)
            return;

        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (_clearAnimatorController)
            _animator.runtimeAnimatorController = null;

        EnsureAnimatorHasControllerForPlayables();
    }

    /// <summary>PlayableGraph ожидает валидный AnimatorController на слое (часто достаточно пустого состояния).</summary>
    private void EnsureAnimatorHasControllerForPlayables()
    {
        if (_animator.runtimeAnimatorController != null)
            return;

        RuntimeAnimatorController ctrl = _playableControllerOverride;
        if (ctrl == null)
            ctrl = Resources.Load<RuntimeAnimatorController>(DefaultStubResourcePath);

        if (ctrl != null)
            _animator.runtimeAnimatorController = ctrl;
        else
            Debug.LogError(
                "[PlayerCharacterAnimator] No RuntimeAnimatorController: add Resources/Animator/PlayablesStub.controller " +
                "or assign Playable Controller Override / disable Clear Animator Controller and assign an empty controller.",
                this);
    }

    private static void ValidateClipSlot(string name, UnityEngine.Object o)
    {
        if (o == null)
            return;
        if (o is AnimationClip)
            return;
        Debug.LogWarning(
            $"[PlayerCharacterAnimator] Field \"{name}\" must reference an AnimationClip (FBX sub-asset with slider icon). " +
            $"Now: {o.GetType().Name} — cast to AnimationClip is null, animation will not switch.",
            o);
    }

    private void OnEnable()
    {
        ResolveAnimatorReference();
        ConfigureAnimatorForPlayables();

        // Клон модели с локального игрока сохраняет сериализованный _player на сценовый Player — без сброса
        // удалённый юнит читает IsMoving/оружие локального и «зеркалит» анимацию планирования.
        Player playerInParent = GetComponentInParent<Player>();
        RemoteBattleUnitView remoteInParent = GetComponentInParent<RemoteBattleUnitView>();

        if (playerInParent != null)
        {
            _player = playerInParent;
            _remoteBattleUnit = null;
        }
        else
        {
            _player = null;
            if (_remoteBattleUnit == null)
                _remoteBattleUnit = remoteInParent;
        }

        if (_player != null)
        {
            _player.OnHealthChanged += HandleHealthChanged;
            _player.OnEquippedWeaponChanged += HandleWeaponChanged;
            _player.OnMovementPostureChanged += HandlePostureChanged;
            _previousPostureTracked = _player.CurrentMovementPosture;
        }

        _lastWorldPos = GetMovementWorldPosition();
        _deathPlayed = false;
        _resolvedClipDirty = true;
        CacheClipReferences();

        if (ClipIdle != null)
            PlayClip(ClipIdle);
    }

    private void OnDisable()
    {
        if (_player != null)
        {
            _player.OnHealthChanged -= HandleHealthChanged;
            _player.OnEquippedWeaponChanged -= HandleWeaponChanged;
            _player.OnMovementPostureChanged -= HandlePostureChanged;
        }

        StopPostureTransitionCoroutine();
        DestroyGraph();
    }

    private void HandleWeaponChanged() => _resolvedClipDirty = true;

    private void HandlePostureChanged(MovementPosture newPosture)
    {
        if (_player == null)
            return;

        MovementPosture oldPosture = _previousPostureTracked;
        bool started = TryBeginStandCrouchTransition(oldPosture, newPosture);
        _previousPostureTracked = newPosture;
        if (!started)
            _resolvedClipDirty = true;
    }

    private bool TryBeginStandCrouchTransition(MovementPosture oldPosture, MovementPosture newPosture)
    {
        if (_postureTransitionActive)
            StopPostureTransitionCoroutine();

        if (IsStandingPosture(oldPosture) && IsCrouchPosture(newPosture))
        {
            AnimationClip clip = ClipStandToSit;
            if (clip == null || clip.length <= 1e-5f)
                return false;
            _postureTransitionCoroutine = StartCoroutine(PostureTransitionRoutine(clip));
            return true;
        }

        if (IsCrouchPosture(oldPosture) && IsStandingPosture(newPosture))
        {
            AnimationClip clip = ClipSitToStand;
            if (clip == null || clip.length <= 1e-5f)
                return false;
            _postureTransitionCoroutine = StartCoroutine(PostureTransitionRoutine(clip));
            return true;
        }

        return false;
    }

    private IEnumerator PostureTransitionRoutine(AnimationClip transition)
    {
        _postureTransitionActive = true;
        // Foot IK helps keep feet on the ground during retargeted root-in-pose transitions (same as locomotion clips).
        PlayClipInternal(transition, 1f, 0.0, applyFootIk: true);

        // Do not use playable time for completion: looped clips never satisfy GetTime() < length.
        float duration = Mathf.Clamp((float)transition.length, 0.05f, 60f);
        yield return new WaitForSeconds(duration);

        _postureTransitionActive = false;
        _postureTransitionCoroutine = null;
        _resolvedClipDirty = true;
    }

    private void StopPostureTransitionCoroutine()
    {
        if (_postureTransitionCoroutine != null)
        {
            StopCoroutine(_postureTransitionCoroutine);
            _postureTransitionCoroutine = null;
        }

        _postureTransitionActive = false;
    }

    private void HandleHealthChanged(int hp, int maxHp)
    {
        if (hp > 0)
        {
            _deathPlayed = false;
            AnimationClip clip = ResolveLocomotionClip();
            if (clip != null)
                PlayClip(clip);
        }
    }

    private void Update()
    {
        if (_animator == null)
            return;
        if (_player == null && _remoteBattleUnit == null)
            return;

        if (_player != null)
        {
            if (_player.IsHidden)
                return;

            if (_player.IsDead && ClipDead != null)
            {
                StopPostureTransitionCoroutine();
                PlayDeathClipIfNeeded();
                return;
            }
        }
        else if (_remoteBattleUnit != null && _remoteBattleUnit.CurrentHp <= 0 && ClipDead != null)
        {
            StopPostureTransitionCoroutine();
            PlayDeathClipIfNeeded();
            return;
        }
    }

    private void PlayDeathClipIfNeeded()
    {
        if (!_deathPlayed)
        {
            PlayClip(ClipDead);
            _deathPlayed = true;
        }

        if (_graph.IsValid() && _clipPlayable.IsValid() && ClipDead.length > 0.05f)
        {
            double t = _clipPlayable.GetTime();
            if (t >= ClipDead.length - 0.03f)
                _clipPlayable.SetSpeed(0f);
        }
    }

    private void LateUpdate()
    {
        if (_player != null)
        {
            if (_player.IsDead || _player.IsHidden)
                return;
        }
        else if (_remoteBattleUnit != null)
        {
            if (_remoteBattleUnit.CurrentHp <= 0)
                return;
        }
        else
        {
            return;
        }

        // Локомоцию применяем здесь, а не в Update: корутины движения (PlayPathAnimation и т.д.)
        // выполняются после Update, но до LateUpdate — иначе один кадр IsMoving ещё false,
        // PlayClip(idle) сносит walk/run граф до NotifyHexStepStarted (серверная анимация «глючит»).
        if (_animator == null)
        {
            ResolveAnimatorReference();
            ConfigureAnimatorForPlayables();
        }

        if (_animator != null)
            ApplyResolvedLocomotionClip();

        Transform pivot = _rotatePlayerRoot
            ? (_player != null ? _player.transform : _remoteBattleUnit != null ? _remoteBattleUnit.transform : transform)
            : transform;
        Vector3 worldPos = GetMovementWorldPosition();

        if (_horizontalFacingOverride.HasValue)
        {
            Vector3 d = _horizontalFacingOverride.Value;
            Quaternion target = Quaternion.LookRotation(d, Vector3.up);
            pivot.rotation = Quaternion.Slerp(pivot.rotation, target, Time.deltaTime * _rotationSpeed);
            _lastWorldPos = worldPos;
            return;
        }

        if (!_faceMovementDirection)
            return;

        Vector3 delta = worldPos - _lastWorldPos;
        delta.y = 0f;
        if (delta.sqrMagnitude > _moveFaceMinSqr)
        {
            Quaternion target = Quaternion.LookRotation(delta.normalized, Vector3.up);
            pivot.rotation = Quaternion.Slerp(pivot.rotation, target, Time.deltaTime * _rotationSpeed);
        }

        _lastWorldPos = worldPos;
    }

    private AnimationClip ResolveLocomotionClip()
    {
        bool armed = _player != null && WeaponCatalog.IsPistolStyleWeapon(_player.WeaponCode);
        bool coldIdle = _player != null && WeaponCatalog.IsColdWeapon(_player.WeaponCode) && ClipIdleCold != null;
        MovementPosture posture = _player != null ? _player.CurrentMovementPosture : MovementPosture.Walk;
        bool moving = _player != null ? _player.IsMoving : _remoteBattleUnit != null && _remoteBattleUnit.IsMoving;

        // Кэш: если ничего не изменилось с прошлого кадра, возвращаем тот же клип.
        if (!_resolvedClipDirty && armed == _lastArmed && coldIdle == _lastColdIdle && posture == _lastPosture && moving == _lastMoving
            && _resolvedClipCache != null)
            return _resolvedClipCache;

        _lastArmed = armed;
        _lastColdIdle = coldIdle;
        _lastPosture = posture;
        _lastMoving = moving;
        _resolvedClipDirty = false;

        MovementPosture locomotion = MovementPostureUtility.GetPreviewMovementPosture(posture);
        bool crouch = posture == MovementPosture.Sit || posture == MovementPosture.Hide;
        bool run = locomotion == MovementPosture.Run;

        AnimationClip result = null;
        if (crouch)
        {
            if (moving)
            {
                if (armed && ClipSitWalkPistol != null)
                    result = ClipSitWalkPistol;
                else if (ClipSitWalk != null)
                    result = ClipSitWalk;
            }

            if (result == null)
            {
                if (armed && ClipSitPistol != null)
                    result = ClipSitPistol;
                else if (ClipSit != null)
                    result = ClipSit;
            }
        }

        if (result == null && moving)
        {
            if (run)
            {
                if (armed && ClipRunPistol != null)
                    result = ClipRunPistol;
                else if (ClipRun != null)
                    result = ClipRun;
            }
            else
            {
                if (armed && ClipWalkPistol != null)
                    result = ClipWalkPistol;
                else if (ClipWalk != null)
                    result = ClipWalk;
            }
        }

        if (result == null)
        {
            if (!crouch && !moving && coldIdle)
                result = ClipIdleCold;
            if (result == null)
                result = (armed && ClipIdlePistol != null) ? ClipIdlePistol : ClipIdle;
        }

        _resolvedClipCache = result;
        return result;
    }

    /// <summary>
    /// Не вызывать <see cref="PlayClip"/> на каждом кадре во время движения, если уже играет walk/run/sit-walk от
    /// <see cref="NotifyHexStepStarted"/>: <see cref="ResolveLocomotionClip"/> может отличаться (run vs walk,
    /// пистолет vs без), тогда <see cref="PlayClip"/> пересоздаёт граф с speed=1 и сбивает фазу/скорость — на планировании
    /// поза стабильна, после ответа сервера — нет, и анимация «глючит».
    /// </summary>
    private void ApplyResolvedLocomotionClip()
    {
        if (_meleeAttackActive)
            return;
        if (_itemUseActive)
            return;
        if (_postureTransitionActive)
            return;

        AnimationClip resolved = ResolveLocomotionClip();
        if (resolved == null)
            return;

        bool isMoving = _player != null ? _player.IsMoving : _remoteBattleUnit != null && _remoteBattleUnit.IsMoving;
        if (isMoving && IsHexStepLocomotionClip(resolved))
        {
            if (_graph.IsValid() && _currentClip != null && IsHexStepLocomotionClip(_currentClip))
                return;
        }

        PlayClip(resolved);
    }

    private void PlayClip(AnimationClip clip)
    {
        if (clip == null || _animator == null)
            return;
        if (_currentClip == clip && _graph.IsValid())
            return;

        PlayClipInternal(clip, 1f, 0.0, applyFootIk: true);
    }

    private void PlayClipInternal(AnimationClip clip, float speed, double startTimeSeconds, bool applyFootIk = true)
    {
        DestroyGraph();

        _graph = PlayableGraph.Create("PlayerCharacter");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
        _clipPlayable.SetApplyFootIK(applyFootIk);
        _clipPlayable.SetSpeed(speed);
        _clipPlayable.SetTime(startTimeSeconds);

        var output = AnimationPlayableOutput.Create(_graph, "Animation", _animator);
        output.SetSourcePlayable(_clipPlayable);
        _graph.Play();
        _currentClip = clip;
    }

    private void DestroyGraph()
    {
        if (_graph.IsValid())
            _graph.Destroy();
        _graph = default;
        _currentClip = null;
    }

    private AnimationClip ResolveColdAttackClip(int bodyPartId)
    {
        switch (bodyPartId)
        {
            case BodyPartIds.Head:
                return ClipAttackColdHead;
            case BodyPartIds.Legs:
                return ClipAttackColdLegs;
            case BodyPartIds.Torso:
            case BodyPartIds.LeftArm:
            case BodyPartIds.RightArm:
            case BodyPartIds.None:
            default:
                return ClipAttackColdBody;
        }
    }

    /// <summary>One-shot melee swing for cold weapons; caller should face the target first. Run via <c>StartCoroutine</c> from a <see cref="MonoBehaviour"/>.</summary>
    public IEnumerator RunColdMeleeAttackRoutine(int bodyPartId)
    {
        AnimationClip clip = ResolveColdAttackClip(bodyPartId);
        if (clip == null || clip.length <= 1e-5f || _animator == null)
            yield break;

        _meleeAttackActive = true;
        float atkSpeed = Mathf.Clamp(_coldMeleeAttackPlaybackSpeed, 0.25f, 5f);
        PlayClipInternal(clip, atkSpeed, 0.0, applyFootIk: true);
        float len = Mathf.Clamp((float)clip.length / atkSpeed, 0.02f, 30f);
        float t = 0f;
        while (t < len)
        {
            t += Time.deltaTime;
            yield return null;
        }

        _meleeAttackActive = false;
        _resolvedClipDirty = true;
    }

    /// <summary>One-shot self-use animation for medicine actions.</summary>
    public IEnumerator RunUseItemMedicineRoutine()
    {
        AnimationClip clip = ClipUseItemMedicine;
        if (clip == null || clip.length <= 1e-5f || _animator == null)
            yield break;

        _itemUseActive = true;
        float useSpeed = Mathf.Clamp(_medicineUsePlaybackSpeed, 0.25f, 5f);
        PlayClipInternal(clip, useSpeed, 0.0, applyFootIk: true);
        Transform rig = _animator.transform;
        float lockY = rig != null ? rig.localPosition.y : 0f;
        Transform modelRoot = transform;
        float startModelWorldY = modelRoot != null ? modelRoot.position.y : 0f;
        float baselineMinY = GetVisualMinWorldY(out bool hasBaseline);
        float len = Mathf.Clamp((float)clip.length / useSpeed, 0.02f, 30f);
        float t = 0f;
        while (t < len)
        {
            if (_lockLocalRigYDuringMedicineUse && rig != null)
            {
                Vector3 lp = rig.localPosition;
                if (!Mathf.Approximately(lp.y, lockY))
                {
                    lp.y = lockY;
                    rig.localPosition = lp;
                }
            }
            if (_preventMeshSinkingDuringMedicineUse && modelRoot != null && hasBaseline)
            {
                float currentMinY = GetVisualMinWorldY(out bool hasCurrent);
                if (hasCurrent)
                {
                    float lift = baselineMinY - currentMinY;
                    if (lift > 1e-4f)
                    {
                        Vector3 p = modelRoot.position;
                        p.y += lift;
                        modelRoot.position = p;
                    }
                }
            }
            t += Time.deltaTime;
            yield return null;
        }

        if (_preventMeshSinkingDuringMedicineUse && modelRoot != null)
        {
            Vector3 endPos = modelRoot.position;
            endPos.y = startModelWorldY;
            modelRoot.position = endPos;
        }
        _itemUseActive = false;
        _resolvedClipDirty = true;
    }

    private float GetVisualMinWorldY(out bool ok)
    {
        ok = false;
        float minY = 0f;
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled)
                continue;
            Bounds b = r.bounds;
            if (!ok)
            {
                minY = b.min.y;
                ok = true;
            }
            else if (b.min.y < minY)
                minY = b.min.y;
        }
        return minY;
    }
}
