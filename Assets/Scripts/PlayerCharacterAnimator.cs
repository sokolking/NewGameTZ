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
    [SerializeField] private Animator _animator;
    [Tooltip("Если true — сбросить назначенный контроллер в Awake (затем подставится заглушка из Resources или Playable Controller Override).")]
    [SerializeField] private bool _clearAnimatorController = true;
    [Tooltip("Необязательно: свой пустой Animator Controller вместо Resources/Animator/PlayablesStub.")]
    [SerializeField] private RuntimeAnimatorController _playableControllerOverride;
    [SerializeField] private float _uniformModelScale = 1f;
    [SerializeField] private bool _faceMovementDirection = true;
    [Tooltip("Крутить transform родителя Player (рекомендуется, если скрипт на дочернем мешe).")]
    [SerializeField] private bool _rotatePlayerRoot = true;
    [SerializeField] private float _rotationSpeed = 14f;
    [SerializeField] private float _moveFaceMinSqr = 0.0004f;

    [Header("Clips — Object: перетащите клип из раскрытого FBX (иконка зелёного слайдера)")]
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

    private AnimationClip ClipIdle => _idle as AnimationClip;
    private AnimationClip ClipWalk => _walk as AnimationClip;
    private AnimationClip ClipRun => _run as AnimationClip;
    private AnimationClip ClipSit => _sit as AnimationClip;
    private AnimationClip ClipIdlePistol => _idlePistol as AnimationClip;
    private AnimationClip ClipWalkPistol => _walkPistol as AnimationClip;
    private AnimationClip ClipRunPistol => _runPistol as AnimationClip;
    private AnimationClip ClipSitPistol => _sitPistol as AnimationClip;
    private AnimationClip ClipDead => _dead as AnimationClip;

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
                "[PlayerCharacterAnimator] Нет RuntimeAnimatorController: добавьте Resources/Animator/PlayablesStub.controller " +
                "или назначьте Playable Controller Override / отключите Clear Animator Controller и укажите пустой контроллер.",
                this);
    }

    private static void ValidateClipSlot(string name, UnityEngine.Object o)
    {
        if (o == null)
            return;
        if (o is AnimationClip)
            return;
        Debug.LogWarning(
            $"[PlayerCharacterAnimator] Поле «{name}» должно ссылаться на AnimationClip (подресурс FBX со значком слайдера). " +
            $"Сейчас: {o.GetType().Name} — приведение к AnimationClip даст null, анимация не переключится.",
            o);
    }

    private void OnEnable()
    {
        if (_player == null)
            _player = GetComponentInParent<Player>();
        if (_player != null)
            _player.OnHealthChanged += HandleHealthChanged;

        _lastWorldPos = _rotatePlayerRoot && _player != null ? _player.transform.position : transform.position;
        _deathPlayed = false;

        if (ClipIdle != null)
            PlayClip(ClipIdle);
    }

    private void OnDisable()
    {
        if (_player != null)
            _player.OnHealthChanged -= HandleHealthChanged;
        DestroyGraph();
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
        if (_animator == null || _player == null)
            return;

        if (_player.IsHidden)
            return;

        if (_player.IsDead && ClipDead != null)
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

            return;
        }

        AnimationClip clip = ResolveLocomotionClip();
        if (clip != null)
            PlayClip(clip);
    }

    private void LateUpdate()
    {
        if (!_faceMovementDirection || _player == null || _player.IsDead || _player.IsHidden)
            return;

        Transform pivot = _rotatePlayerRoot ? _player.transform : transform;
        Vector3 worldPos = pivot.position;
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
        bool armed = WeaponCatalog.IsPistolStyleWeapon(_player.WeaponCode);
        MovementPosture posture = _player.CurrentMovementPosture;
        MovementPosture locomotion = MovementPostureUtility.GetPreviewMovementPosture(posture);
        bool crouch = posture == MovementPosture.Sit || posture == MovementPosture.Hide;
        bool moving = _player.IsMoving;
        bool run = locomotion == MovementPosture.Run;

        if (crouch)
        {
            if (armed && ClipSitPistol != null)
                return ClipSitPistol;
            if (ClipSit != null)
                return ClipSit;
        }

        if (moving)
        {
            if (run)
            {
                if (armed && ClipRunPistol != null)
                    return ClipRunPistol;
                if (ClipRun != null)
                    return ClipRun;
            }
            else
            {
                if (armed && ClipWalkPistol != null)
                    return ClipWalkPistol;
                if (ClipWalk != null)
                    return ClipWalk;
            }
        }

        if (armed && ClipIdlePistol != null)
            return ClipIdlePistol;
        return ClipIdle;
    }

    private void PlayClip(AnimationClip clip)
    {
        if (clip == null || _animator == null)
            return;
        if (_currentClip == clip && _graph.IsValid())
            return;

        DestroyGraph();

        _graph = PlayableGraph.Create("PlayerCharacter");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        _clipPlayable = AnimationClipPlayable.Create(_graph, clip);
        _clipPlayable.SetApplyFootIK(true);
        _clipPlayable.SetSpeed(1f);

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
