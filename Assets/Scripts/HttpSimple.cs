using System;
using System.Collections;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// HTTP без UnityWebRequest: в Unity 6 по умолчанию блокируется http:// («Insecure connection not allowed»).
/// System.Net.Http.HttpClient к локальному серверу обычно проходит без этой политики.
/// </summary>
public static class HttpSimple
{
    private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

    public static string Escape(string value) => Uri.EscapeDataString(value ?? "");

    public static IEnumerator PostJson(string url, string json, Action<string> onBody, Action<string> onError)
    {
        Task<HttpResponseMessage> task;
        try
        {
            task = Client.PostAsync(url, new StringContent(json ?? "", Encoding.UTF8, "application/json"));
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex.Message);
            yield break;
        }

        while (!task.IsCompleted)
            yield return null;

        HttpResponseMessage resp;
        try
        {
            resp = task.Result;
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex.Message);
            yield break;
        }

        Task<string> read = resp.Content.ReadAsStringAsync();
        while (!read.IsCompleted)
            yield return null;

        string body = read.Result;
        if (!resp.IsSuccessStatusCode)
        {
            onError?.Invoke(body);
            yield break;
        }

        onBody?.Invoke(body);
    }

    public static IEnumerator GetString(string url, Action<string> onBody, Action<string> onError)
    {
        Task<HttpResponseMessage> task;
        try
        {
            task = Client.GetAsync(url);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex.Message);
            yield break;
        }

        while (!task.IsCompleted)
            yield return null;

        HttpResponseMessage resp;
        try
        {
            resp = task.Result;
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex.Message);
            yield break;
        }

        Task<string> read = resp.Content.ReadAsStringAsync();
        while (!read.IsCompleted)
            yield return null;

        string body = read.Result;
        if (!resp.IsSuccessStatusCode)
        {
            onError?.Invoke(body);
            yield break;
        }

        onBody?.Invoke(body);
    }

    /// <summary>Возвращает код ответа и тело (в т.ч. при 404), чтобы опрос мог отличить «комната закрыта».</summary>
    public static IEnumerator GetStringWithStatus(string url, Action<int, string> onDone, Action<string> onTransportError)
    {
        Task<HttpResponseMessage> task;
        try
        {
            task = Client.GetAsync(url);
        }
        catch (Exception ex)
        {
            onTransportError?.Invoke(ex.Message);
            yield break;
        }

        while (!task.IsCompleted)
            yield return null;

        HttpResponseMessage resp;
        try
        {
            resp = task.Result;
        }
        catch (Exception ex)
        {
            onTransportError?.Invoke(ex.Message);
            yield break;
        }

        Task<string> read = resp.Content.ReadAsStringAsync();
        while (!read.IsCompleted)
            yield return null;

        string body = read.Result;
        onDone?.Invoke((int)resp.StatusCode, body);
    }
}
