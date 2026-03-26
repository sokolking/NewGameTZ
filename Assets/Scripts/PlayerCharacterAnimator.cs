using System;
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
    [SerializeField] private UnityEngine.Object _idlePistol;
    [SerializeField] private UnityEngine.Object _walkPistol;
    [SerializeField] private UnityEngine.Object _runPistol;
    [SerializeField] private UnityEngine.Object _sitPistol;
    [SerializeField] private UnityEngine.Object _dead;

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
    private AnimationClip _cachedClipIdlePistol;
    private AnimationClip _cachedClipWalkPistol;
    private AnimationClip _cachedClipRunPistol;
    private AnimationClip _cachedClipSitPistol;
    private AnimationClip _cachedClipDead;
    private bool _clipsCached;

    // Кэш состояния для ResolveLocomotionClip — пересчитываем только при изменении.
    private AnimationClip _resolvedClipCache;
    private bool _resolvedClipDirty = true;
    private bool _lastArmed;
    private MovementPosture _lastPosture;
    private bool _lastMoving;

    private AnimationClip ClipIdle { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipIdle; } }
    private AnimationClip ClipWalk { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipWalk; } }
    private AnimationClip ClipRun { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipRun; } }
    private AnimationClip ClipSit { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipSit; } }
    private AnimationClip ClipIdlePistol { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipIdlePistol; } }
    private AnimationClip ClipWalkPistol { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipWalkPistol; } }
    private AnimationClip ClipRunPistol { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipRunPistol; } }
    private AnimationClip ClipSitPistol { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipSitPistol; } }
    private AnimationClip ClipDead { get { if (!_clipsCached) CacheClipReferences(); return _cachedClipDead; } }

    private void CacheClipReferences()
    {
        _cachedClipIdle = _idle as AnimationClip;
        _cachedClipWalk = _walk as AnimationClip;
        _cachedClipRun = _run as AnimationClip;
        _cachedClipSit = _sit as AnimationClip;
        _cachedClipIdlePistol = _idlePistol as AnimationClip;
        _cachedClipWalkPistol = _walkPistol as AnimationClip;
        _cachedClipRunPistol = _runPistol as AnimationClip;
        _cachedClipSitPistol = _sitPistol as AnimationClip;
        _cachedClipDead = _dead as AnimationClip;
        _clipsCached = true;
    }

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

    /// <summary>
    /// Вызывается из <see cref="Player"/> в начале каждого шага по гексу: один цикл walk/run с фазой 0 или 0.5 и скоростью под длительность шага.
    /// </summary>
    public void NotifyHexStepStarted(float stepDurationSeconds)
    {
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
        if (!IsWalkOrRunLocomotionClip(clip))
            return;

        _hexWalkPhaseFlip = !_hexWalkPhaseFlip;
        float frac = Mathf.Clamp(_locomotionCycleFractionPerHex, 0.05f, 1f);
        double startTime = _hexWalkPhaseFlip ? 0.0 : clip.length * 0.5;
        float cyclePortion = clip.length * frac;
        float speed = cyclePortion / stepDurationSeconds;
        PlayClipInternal(clip, speed, startTime);
    }

    private bool IsWalkOrRunLocomotionClip(AnimationClip clip)
    {
        if (clip == null)
            return false;
        return clip == ClipWalk || clip == ClipRun || clip == ClipWalkPistol || clip == ClipRunPistol;
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
        _idlePistol = CoerceClipReference(_idlePistol, new[] { "idle", "pistol" });
        _walkPistol = CoerceClipReference(_walkPistol, new[] { "walk", "pistol" });
        _runPistol = CoerceClipReference(_runPistol, new[] { "run", "pistol" });
        _sitPistol = CoerceClipReference(_sitPistol, new[] { "sit", "pistol" });
        _dead = CoerceClipReference(_dead, new[] { "dead", "death", "die" });

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

        if (_animator == null)
            _animator = GetComponent<Animator>();
        if (_animator == null)
            return;

        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (_clearAnimatorController)
            _animator.runtimeAnimatorController = null;

        EnsureAnimatorHasControllerForPlayables();

        ValidateClipSlot(nameof(_idle), _idle);
        ValidateClipSlot(nameof(_walk), _walk);
        ValidateClipSlot(nameof(_run), _run);
        ValidateClipSlot(nameof(_sit), _sit);
        ValidateClipSlot(nameof(_idlePistol), _idlePistol);
        ValidateClipSlot(nameof(_walkPistol), _walkPistol);
        ValidateClipSlot(nameof(_runPistol), _runPistol);
        ValidateClipSlot(nameof(_sitPistol), _sitPistol);
        ValidateClipSlot(nameof(_dead), _dead);
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
        }

        _lastWorldPos = _rotatePlayerRoot && _player != null
            ? _player.transform.position
            : _rotatePlayerRoot && _remoteBattleUnit != null
                ? _remoteBattleUnit.transform.position
                : transform.position;
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

        DestroyGraph();
    }

    private void HandleWeaponChanged() => _resolvedClipDirty = true;
    private void HandlePostureChanged(MovementPosture _) => _resolvedClipDirty = true;

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
                PlayDeathClipIfNeeded();
                return;
            }
        }
        else if (_remoteBattleUnit != null && _remoteBattleUnit.CurrentHp <= 0 && ClipDead != null)
        {
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
        if (_animator != null)
            ApplyResolvedLocomotionClip();

        Transform pivot = _rotatePlayerRoot
            ? (_player != null ? _player.transform : _remoteBattleUnit != null ? _remoteBattleUnit.transform : transform)
            : transform;
        Vector3 worldPos = pivot.position;

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
        MovementPosture posture = _player != null ? _player.CurrentMovementPosture : MovementPosture.Walk;
        bool moving = _player != null ? _player.IsMoving : _remoteBattleUnit != null && _remoteBattleUnit.IsMoving;

        // Кэш: если ничего не изменилось с прошлого кадра, возвращаем тот же клип.
        if (!_resolvedClipDirty && armed == _lastArmed && posture == _lastPosture && moving == _lastMoving
            && _resolvedClipCache != null)
            return _resolvedClipCache;

        _lastArmed = armed;
        _lastPosture = posture;
        _lastMoving = moving;
        _resolvedClipDirty = false;

        MovementPosture locomotion = MovementPostureUtility.GetPreviewMovementPosture(posture);
        bool crouch = posture == MovementPosture.Sit || posture == MovementPosture.Hide;
        bool run = locomotion == MovementPosture.Run;

        AnimationClip result = null;
        if (crouch)
        {
            if (armed && ClipSitPistol != null)
                result = ClipSitPistol;
            else if (ClipSit != null)
                result = ClipSit;
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
            result = (armed && ClipIdlePistol != null) ? ClipIdlePistol : ClipIdle;

        _resolvedClipCache = result;
        return result;
    }

    /// <summary>
    /// Не вызывать <see cref="PlayClip"/> на каждом кадре во время движения, если уже играет walk/run от
    /// <see cref="NotifyHexStepStarted"/>: <see cref="ResolveLocomotionClip"/> может отличаться (run vs walk,
    /// пистолет vs без), тогда <see cref="PlayClip"/> пересоздаёт граф с speed=1 и сбивает фазу/скорость — на планировании
    /// поза стабильна, после ответа сервера — нет, и анимация «глючит».
    /// </summary>
    private void ApplyResolvedLocomotionClip()
    {
        AnimationClip resolved = ResolveLocomotionClip();
        if (resolved == null)
            return;

        bool isMoving = _player != null ? _player.IsMoving : _remoteBattleUnit != null && _remoteBattleUnit.IsMoving;
        if (isMoving && IsWalkOrRunLocomotionClip(resolved))
        {
            if (_graph.IsValid() && _currentClip != null && IsWalkOrRunLocomotionClip(_currentClip))
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

        PlayClipInternal(clip, 1f, 0.0);
    }

    private void PlayClipInternal(AnimationClip clip, float speed, double startTimeSeconds)
    {
        DestroyGraph();

        _graph = PlayableGraph.Create("PlayerCharacter");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
        _clipPlayable.SetApplyFootIK(true);
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
}
