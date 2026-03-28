using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EscScene / LanguagePage: выбор языка как у разрешения — подпись, текущее значение, Prev / Next, Apply.
/// До Apply меняется только предпросмотр; Apply вызывает <see cref="Loc.SetLanguage"/>.
/// </summary>
public sealed class EscLanguagePickerUI : MonoBehaviour
{
    static readonly GameLanguage[] Order = { GameLanguage.English, GameLanguage.Russian };

    [SerializeField] Text _languageText;
    [SerializeField] Button _btnPrev;
    [SerializeField] Button _btnNext;
    [SerializeField] Button _btnApply;

    GameLanguage _pending;

    void Awake()
    {
        CacheRefs();
        WireButtons();
    }

    void OnEnable() => SyncFromLoc();

    public void SyncFromLoc()
    {
        _pending = Loc.Current;
        RefreshDisplay();
    }

    void CacheRefs()
    {
        if (_languageText == null)
            _languageText = transform.Find("LanguageText")?.GetComponent<Text>();
        if (_btnPrev == null)
            _btnPrev = transform.Find("Button_LanguagePrev")?.GetComponent<Button>();
        if (_btnNext == null)
            _btnNext = transform.Find("Button_LanguageNext")?.GetComponent<Button>();
        if (_btnApply == null)
            _btnApply = transform.Find("Button_LanguageApply")?.GetComponent<Button>();
    }

    void WireButtons()
    {
        if (_btnPrev != null)
        {
            _btnPrev.onClick.RemoveListener(OnPrev);
            _btnPrev.onClick.AddListener(OnPrev);
        }

        if (_btnNext != null)
        {
            _btnNext.onClick.RemoveListener(OnNext);
            _btnNext.onClick.AddListener(OnNext);
        }

        if (_btnApply != null)
        {
            _btnApply.onClick.RemoveListener(OnApply);
            _btnApply.onClick.AddListener(OnApply);
        }
    }

    void OnPrev()
    {
        _pending = Step(_pending, -1);
        RefreshDisplay();
    }

    void OnNext()
    {
        _pending = Step(_pending, 1);
        RefreshDisplay();
    }

    static GameLanguage Step(GameLanguage current, int delta)
    {
        int i = IndexOf(current);
        if (i < 0)
            i = 0;
        i = (i + delta + Order.Length) % Order.Length;
        return Order[i];
    }

    static int IndexOf(GameLanguage lang)
    {
        for (int i = 0; i < Order.Length; i++)
        {
            if (Order[i] == lang)
                return i;
        }

        return -1;
    }

    void OnApply()
    {
        Loc.SetLanguage(_pending);
        RefreshDisplay();
    }

    void RefreshDisplay()
    {
        if (_languageText == null)
            return;
        string key = _pending == GameLanguage.English
            ? "esc.settings.lang_english"
            : "esc.settings.lang_russian";
        _languageText.text = Loc.T(key);
    }
}
