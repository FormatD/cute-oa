using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Oa.Api.Services;

public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);
public sealed record TokenIdentity(string UserId, string TenantId, Guid? SessionId);

public sealed class JwtTokenService
{
    private readonly string issuer;
    private readonly string audience;
    private readonly byte[] signingKey;
    private readonly int accessTokenMinutes;

    public JwtTokenService(IConfiguration configuration)
    {
        issuer = configuration["Authentication:Issuer"] ?? "cute-oa";
        audience = configuration["Authentication:Audience"] ?? "cute-oa-web";
        var configuredKey = configuration["Authentication:SigningKey"] ?? throw new InvalidOperationException("缺少 Authentication:SigningKey 配置。");
        if (Encoding.UTF8.GetByteCount(configuredKey) < 32) throw new InvalidOperationException("Authentication:SigningKey 至少需要 32 字节。");
        signingKey = Encoding.UTF8.GetBytes(configuredKey);
        accessTokenMinutes = Math.Clamp(configuration.GetValue<int?>("Authentication:AccessTokenMinutes") ?? 15, 5, 1_440);
    }

    public IssuedToken Issue(Employee employee, Guid? sessionId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(accessTokenMinutes);
        var header = EncodeJson(new { alg = "HS256", typ = "JWT" });
        var claims = new Dictionary<string, object>
        {
            ["sub"] = employee.Id,
            ["name"] = employee.Name,
            ["role"] = employee.Role,
            ["tenant"] = "demo",
            ["iss"] = issuer,
            ["aud"] = audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };
        if (sessionId is not null) claims["sid"] = sessionId.Value.ToString("N");
        var payload = EncodeJson(claims);
        var unsigned = $"{header}.{payload}";
        return new IssuedToken($"{unsigned}.{Sign(unsigned)}", expiresAt);
    }

    public bool TryValidate(string token, out TokenIdentity? identity)
    {
        identity = null;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return false;
            using var header = JsonDocument.Parse(Decode(parts[0]));
            if (header.RootElement.GetProperty("alg").GetString() != "HS256") return false;
            var expected = Decode(Sign($"{parts[0]}.{parts[1]}"));
            var actual = Decode(parts[2]);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return false;

            using var payload = JsonDocument.Parse(Decode(parts[1]));
            var root = payload.RootElement;
            if (root.GetProperty("iss").GetString() != issuer || root.GetProperty("aud").GetString() != audience) return false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (root.GetProperty("nbf").GetInt64() > now + 30 || root.GetProperty("exp").GetInt64() <= now) return false;
            var userId = root.GetProperty("sub").GetString();
            var tenantId = root.GetProperty("tenant").GetString();
            if (string.IsNullOrWhiteSpace(userId) || tenantId != "demo") return false;
            Guid? sessionId = null;
            if (root.TryGetProperty("sid", out var sidClaim))
            {
                if (!Guid.TryParseExact(sidClaim.GetString(), "N", out var parsedSessionId)) return false;
                sessionId = parsedSessionId;
            }
            identity = new TokenIdentity(userId, tenantId, sessionId);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private string Sign(string value)
    {
        using var hmac = new HMACSHA256(signingKey);
        return Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string EncodeJson<T>(T value) => Encode(JsonSerializer.SerializeToUtf8Bytes(value));
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }
}
