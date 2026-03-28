using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BattleServer;

/// <summary>
/// JWT access tokens; active session id (<c>jti</c>) is stored in <c>users.active_session_jti</c> only.
/// New login updates DB, invalidates prior JWTs, and disconnects battle + session WebSockets.
/// </summary>
public sealed class BattleAuthSession
{
    private readonly BattleUserDatabase _users;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly JwtSecurityTokenHandler _handler = new();

    public BattleAuthSession(IConfiguration cfg, BattleUserDatabase users)
    {
        _users = users;
        string? key = cfg["Auth:JwtSigningKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException("Auth:JwtSigningKey must be set and at least 32 characters.");
        _issuer = cfg["Auth:Issuer"] ?? "HopeBattle";
        _audience = cfg["Auth:Audience"] ?? "HopeClient";
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }

    /// <summary>Issues a new token and revokes the previous session (HTTP + WebSockets) for this user.</summary>
    public string IssueToken(long userId)
    {
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId));

        string jti = Guid.NewGuid().ToString("N");
        _users.SetActiveSessionJti(userId, jti);
        UserBattleSocketRegistry.RevokeAllForUser(userId);
        UserSessionSocketRegistry.RevokeAllForUser(userId);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti)
        };

        var creds = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _issuer,
            _audience,
            claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);

        return _handler.WriteToken(token);
    }

    public bool TryValidateToken(string? bearerToken, out long userId)
    {
        userId = 0;
        if (string.IsNullOrWhiteSpace(bearerToken))
            return false;

        try
        {
            var principal = _handler.ValidateToken(bearerToken, GetValidationParameters(), out _);
            string? sub = principal.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(sub, out userId) || userId <= 0)
                return false;

            string? jti = principal.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
            if (string.IsNullOrEmpty(jti))
                return false;

            if (!_users.TryGetActiveSessionJti(userId, out string? active) || active != jti)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private TokenValidationParameters GetValidationParameters() =>
        new()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
}
