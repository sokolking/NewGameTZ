namespace BattleServer;

public static class BattleAuthHttp
{
    public static bool TryGetBearerToken(HttpRequest req, out string token)
    {
        token = "";
        if (!req.Headers.TryGetValue("Authorization", out var h))
            return false;
        var v = h.ToString();
        if (!v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        token = v["Bearer ".Length..].Trim();
        return !string.IsNullOrEmpty(token);
    }

    public static bool TryGetBearerUserId(HttpRequest req, BattleAuthSession auth, out long userId)
    {
        userId = 0;
        return TryGetBearerToken(req, out string? t) && auth.TryValidateToken(t, out userId);
    }
}
