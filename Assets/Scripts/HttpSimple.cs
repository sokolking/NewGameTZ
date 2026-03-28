using System;
using System.Collections;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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

    public static IEnumerator PostJsonWithAuth(string url, string json, string bearerToken, Action<string> onBody, Action<string> onError)
    {
        Task<HttpResponseMessage> task;
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json ?? "", Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(bearerToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            task = Client.SendAsync(req);
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

    public static IEnumerator GetStringWithAuth(string url, string bearerToken, Action<string> onBody, Action<string> onError)
    {
        Task<HttpResponseMessage> task;
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(bearerToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            task = Client.SendAsync(req);
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

    public static IEnumerator GetStringWithStatusAndAuth(string url, string bearerToken, Action<int, string> onDone, Action<string> onTransportError)
    {
        Task<HttpResponseMessage> task;
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(bearerToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            task = Client.SendAsync(req);
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

    /// <summary>Прогресс 0–1 из фонового потока; читайте в Update/из главного потока для UI.</summary>
    public sealed class DownloadProgressHolder
    {
        public volatile float Value;
    }

    /// <summary>Скачивание в файл (HttpClient). Прогресс обновляет holder из потока.</summary>
    public static IEnumerator DownloadFile(
        string url,
        string destinationPath,
        DownloadProgressHolder progressHolder,
        Action onComplete,
        Action<string> onError)
    {
        Task task = null;
        Exception caught = null;
        task = Task.Run(async () =>
        {
            try
            {
                if (progressHolder != null)
                    progressHolder.Value = 0f;
                using var resp = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                await using var stream = await resp.Content.ReadAsStreamAsync();
                await using var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[65536];
                long read = 0;
                int n;
                while ((n = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, n);
                    read += n;
                    if (progressHolder != null && total.HasValue && total.Value > 0)
                        progressHolder.Value = read / (float)total.Value;
                }
                if (progressHolder != null)
                    progressHolder.Value = 1f;
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });

        while (!task.IsCompleted)
            yield return null;

        if (caught != null)
        {
            onError?.Invoke(caught.Message);
            yield break;
        }

        onComplete?.Invoke();
    }
}
