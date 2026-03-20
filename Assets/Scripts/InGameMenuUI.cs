using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Обработчик ESC в игровой сцене: показывает/скрывает меню паузы.
/// Первое нажатие ESC — открыть меню, второе — закрыть.
/// </summary>
public class InGameMenuUI : MonoBehaviour
{
    [Header("Панель меню паузы")]
    [SerializeField] private GameObject _menuPanel;

    [Tooltip("Серый фон (Image). Если не задан, ищется дочерний объект «Background». Его отправляем первым в иерархии, чтобы кнопки получали клики.")]
    [SerializeField] private Transform _backdropTransform;

    private bool _visible;

    /// <summary>Меню паузы открыто — блокирует ввод по карте (<see cref="GameplayMapInputBlock"/>).</summary>
    public static bool IsPauseMenuOpen { get; private set; }
    private Button _btnResume;
    private Button _btnMainMenu;

    private void Start()
    {
        CacheButtons();
        WireButtonEvents();

        if (_menuPanel != null)
            _menuPanel.SetActive(false);
        _visible = false;
        IsPauseMenuOpen = false;
        SendBackdropToBack();
    }

    private void OnDestroy()
    {
        IsPauseMenuOpen = false;
    }

    /// <summary>Фон должен быть первым в иерархии, иначе полноэкранный Image перехватывает raycast и кнопки не работают.</summary>
    private void SendBackdropToBack()
    {
        if (_menuPanel == null)
            return;

        Transform bg = _backdropTransform;
        if (bg == null)
        {
            Transform found = _menuPanel.transform.Find("Background");
            if (found == null) found = _menuPanel.transform.Find("Backdrop");
            if (found == null) found = _menuPanel.transform.Find("GrayBG");
            bg = found;
        }

        if (bg != null)
            bg.SetAsFirstSibling();
    }

    private void CacheButtons()
    {
        // Если панель не задана в инспекторе — попробуем найти её по имени под Canvas.
        if (_menuPanel == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Transform p = canvas.transform.Find(UiHierarchyNames.PauseMenuPanel);
                if (p != null) _menuPanel = p.gameObject;
            }
        }

        if (_menuPanel != null)
        {
            // Кнопки могут быть не прямыми детьми (например PauseMenuPanel → Canvas → Button_Resume).
            // Transform.Find ищет только среди непосредственных дочерних объектов.
            _btnResume = FindPauseMenuButton(UiHierarchyNames.PauseButtonResume);
            _btnMainMenu = FindPauseMenuButton(UiHierarchyNames.PauseButtonMainMenu);
        }
    }

    private Button FindPauseMenuButton(string objectName)
    {
        if (_menuPanel == null)
            return null;
        foreach (Button b in _menuPanel.GetComponentsInChildren<Button>(true))
        {
            if (b != null && b.name == objectName)
                return b;
        }
        return null;
    }

    private void WireButtonEvents()
    {
        if (_btnResume != null)
        {
            _btnResume.onClick.RemoveAllListeners();
            _btnResume.onClick.AddListener(OnResumeClicked);
        }

        if (_btnMainMenu != null)
        {
            _btnMainMenu.onClick.RemoveAllListeners();
            _btnMainMenu.onClick.AddListener(OnMainMenuClicked);
        }
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ToggleMenu();
        }
    }

    private void ToggleMenu()
    {
        if (_menuPanel == null) return;

        _visible = !_visible;
        _menuPanel.SetActive(_visible);
        IsPauseMenuOpen = _visible;
        if (_visible)
            SendBackdropToBack();

        // При открытом меню — пауза, при закрытии — продолжить.
        Time.timeScale = _visible ? 0f : 1f;
    }

    // Вспомогательные методы под кнопки внутри меню:
    public void OnResumeClicked()
    {
        _visible = false;
        IsPauseMenuOpen = false;
        if (_menuPanel != null)
            _menuPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    public void OnMainMenuClicked()
    {
        IsPauseMenuOpen = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }
}

