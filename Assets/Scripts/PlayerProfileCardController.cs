using System;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Загружает профиль игрока с сервера и обновляет <see cref="PlayerProfileCardView"/>.
/// </summary>
public sealed class PlayerProfileCardController : MonoBehaviour
{
    [SerializeField] private PlayerProfileCardView _view;
    [SerializeField] private bool _loadOnStart = true;

    private void Start()
    {
        if (_loadOnStart)
            Reload();
    }

    public void Reload()
    {
        if (_view == null)
            _view = GetComponent<PlayerProfileCardView>();
        if (_view == null)
            return;
        StartCoroutine(CoLoadProfile());
    }

    private IEnumerator CoLoadProfile()
    {
        string username = BattleSessionState.LastUsername;
        if (string.IsNullOrWhiteSpace(username))
            yield break;

        string baseUrl = BattleServerRuntime.CurrentBaseUrl.TrimEnd('/');
        string url = baseUrl + "/api/db/user/profile/" + HttpSimple.Escape(username);

        string body = null;
        string err = null;
        yield return HttpSimple.GetString(url, b => body = b, e => err = e);
        if (!string.IsNullOrEmpty(err) || string.IsNullOrEmpty(body))
            yield break;

        UserProgressProfile profile = null;
        try
        {
            profile = JsonConvert.DeserializeObject<UserProgressProfile>(body);
        }
        catch (Exception)
        {
            profile = JsonUtility.FromJson<UserProgressProfile>(body);
        }

        if (profile == null)
            yield break;

        _view.SetData(profile.username, profile.level, profile.strength, profile.endurance, profile.accuracy);
    }

    [Serializable]
    private class UserProgressProfile
    {
        public string username;
        public int level;
        public int strength;
        public int endurance;
        public int accuracy;
    }
}
