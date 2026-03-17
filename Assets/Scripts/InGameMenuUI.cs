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

    private bool _visible;
    private Button _btnResume;
    private Button _btnMainMenu;

    private void Start()
    {
        CacheButtons();
        WireButtonEvents();

        if (_menuPanel != null)
            _menuPanel.SetActive(false);
        _visible = false;
    }

    private void CacheButtons()
    {
        // Если панель не задана в инспекторе — попробуем найти её по имени под Canvas.
        if (_menuPanel == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Transform p = canvas.transform.Find("PauseMenuPanel");
                if (p != null) _menuPanel = p.gameObject;
            }
        }

        if (_menuPanel != null)
        {
            _btnResume = _menuPanel.transform.Find("Button_Resume")?.GetComponent<Button>();
            _btnMainMenu = _menuPanel.transform.Find("Button_MainMenu")?.GetComponent<Button>();
        }
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

        // При открытом меню — пауза, при закрытии — продолжить.
        Time.timeScale = _visible ? 0f : 1f;
    }

    // Вспомогательные методы под кнопки внутри меню:
    public void OnResumeClicked()
    {
        _visible = false;
        if (_menuPanel != null)
            _menuPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    public void OnMainMenuClicked()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }
}

