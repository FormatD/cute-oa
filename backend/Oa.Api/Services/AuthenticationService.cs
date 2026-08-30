using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed record LoginRequest(string UserId, string Password);
public sealed record LoginSession(string AccessToken, DateTimeOffset ExpiresAt, UserProfile User);
public sealed record SessionClient(string? UserAgent, string? IpAddress);
public sealed record AuthenticationGrant(LoginSession Session, string? RefreshToken, DateTimeOffset? RefreshExpiresAt, Guid? SessionId);

public sealed class AuthenticationService
{
    private const string LegacyPasswordSalt = "cute-oa-demo-auth-v1";
    private const string LegacyPasswordHash = "7SvEK7p9V+hgRZYcRDAruXSRVV7DxBymj5Sp5lXxI3I=";
    private readonly DemoData data;
    private readonly JwtTokenService tokens;
    private readonly OaDbContext? db;
    private readonly int refreshTokenDays;

    public AuthenticationService(DemoData data, JwtTokenService tokens) : this(data, tokens, null, null) { }

    public AuthenticationService(DemoData data, JwtTokenService tokens, OaDbContext? db, IConfiguration? configuration = null)
    {
        this.data = data;
        this.tokens = tokens;
        this.db = db;
        refreshTokenDays = Math.Clamp(configuration?.GetValue<int?>("Authentication:RefreshTokenDays") ?? 30, 1, 90);
    }

    public ServiceResult<AuthenticationGrant> Login(LoginRequest request, SessionClient? client = null)
    {
        var userId = request.UserId?.Trim() ?? string.Empty;
        var employee = data.FindEmployee(userId);
        if (employee is null || employee.Status != "ACTIVE" || string.IsNullOrEmpty(request.Password)) return Failure();

        if (db is null)
        {
            if (!LegacyPasswordMatches(request.Password)) return Failure();
            var legacyToken = tokens.Issue(employee);
            return Success(employee, legacyToken, null, null, null);
        }

        var account = db.UserAccounts.SingleOrDefault(item => item.UserId == employee.Id);
        var now = DateTimeOffset.UtcNow;
        if (account is null || account.LockedUntil > now) return Failure();
        if (!PasswordHasher.Verify(request.Password, account.PasswordSalt, account.PasswordHash, account.PasswordIterations))
        {
            account.FailedLoginCount++;
            if (account.FailedLoginCount >= 5)
            {
                account.LockedUntil = now.AddMinutes(15);
                account.FailedLoginCount = 0;
            }
            db.SaveChanges();
            return Failure();
        }

        account.FailedLoginCount = 0;
        account.LockedUntil = null;
        var created = CreateSession(employee.Id, client, now);
        db.AuthSessions.Add(created.Record);
        db.SaveChanges();
        var issued = tokens.Issue(employee, created.Record.Id);
        return Success(employee, issued, created.RawToken, created.Record.ExpiresAt, created.Record.Id);
    }

    public ServiceResult<AuthenticationGrant> Refresh(string? rawToken, SessionClient? client = null)
    {
        if (db is null || !TryReadSessionId(rawToken, out var sessionId)) return Failure();
        var now = DateTimeOffset.UtcNow;
        var hash = Hash(rawToken!);
        var session = db.AuthSessions.AsNoTracking().SingleOrDefault(item => item.Id == sessionId);
        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now || session.RefreshTokenHash != hash)
        {
            if (session is { RevokedAt: null }) RevokeSessionRecord(session.Id, "TOKEN_REUSE", now);
            return Failure();
        }

        var employee = data.FindEmployee(session.UserId);
        if (employee is null || employee.Status != "ACTIVE")
        {
            RevokeSessionRecord(session.Id, "USER_INACTIVE", now);
            return Failure();
        }

        var rotated = GenerateRawToken(session.Id);
        var nextUserAgent = Normalize(client?.UserAgent, 256) ?? session.UserAgent;
        var nextIpAddress = Normalize(client?.IpAddress, 64) ?? session.IpAddress;
        var updated = db.AuthSessions.Where(item => item.Id == session.Id && item.RefreshTokenHash == hash && item.RevokedAt == null && item.ExpiresAt > now)
            .ExecuteUpdate(setters => setters
                .SetProperty(item => item.RefreshTokenHash, Hash(rotated))
                .SetProperty(item => item.LastUsedAt, now)
                .SetProperty(item => item.UserAgent, nextUserAgent)
                .SetProperty(item => item.IpAddress, nextIpAddress));
        if (updated != 1)
        {
            RevokeSessionRecord(session.Id, "TOKEN_REUSE", now);
            return Failure();
        }
        var issued = tokens.Issue(employee, session.Id);
        return Success(employee, issued, rotated, session.ExpiresAt, session.Id);
    }

    public void Logout(string? rawToken)
    {
        if (db is null || !TryReadSessionId(rawToken, out var sessionId)) return;
        var session = db.AuthSessions.SingleOrDefault(item => item.Id == sessionId && item.RevokedAt == null);
        if (session is null) return;
        session.RevokedAt = DateTimeOffset.UtcNow;
        session.RevokedReason = "LOGOUT";
        db.SaveChanges();
    }

    public ServiceResult<PagedResponse<SessionView>> ListSessions(Employee actor, Guid? currentSessionId, int? page, int? pageSize)
    {
        if (db is null) return ServiceResult<PagedResponse<SessionView>>.Failure("当前环境不支持会话管理。", "STATE_001");
        var query = db.AuthSessions.AsNoTracking().Where(item => item.UserId == actor.Id);
        var size = Math.Clamp(pageSize ?? 10, 1, 50);
        var total = query.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)size));
        var currentPage = Math.Clamp(page ?? 1, 1, totalPages);
        var now = DateTimeOffset.UtcNow;
        var items = query.OrderByDescending(item => item.LastUsedAt).Skip((currentPage - 1) * size).Take(size).ToList()
            .Select(item => ToView(item, currentSessionId, now)).ToList();
        return ServiceResult<PagedResponse<SessionView>>.Success(new PagedResponse<SessionView>(items, total, currentPage, size, totalPages));
    }

    public ServiceResult<bool> RevokeSession(Employee actor, Guid currentSessionId, Guid sessionId)
    {
        if (db is null) return ServiceResult<bool>.Failure("当前环境不支持会话管理。", "STATE_001");
        if (sessionId == currentSessionId) return ServiceResult<bool>.Failure("不能在此处撤销当前会话，请使用退出登录。", "STATE_001");
        var session = db.AuthSessions.SingleOrDefault(item => item.Id == sessionId && item.UserId == actor.Id);
        if (session is null) return ServiceResult<bool>.Failure("登录设备不存在。", "DATA_001");
        if (session.RevokedAt is null)
        {
            session.RevokedAt = DateTimeOffset.UtcNow;
            session.RevokedReason = "USER_REVOKED";
            db.SaveChanges();
        }
        return ServiceResult<bool>.Success(true);
    }

    public ServiceResult<int> RevokeOtherSessions(Employee actor, Guid currentSessionId)
    {
        if (db is null) return ServiceResult<int>.Failure("当前环境不支持会话管理。", "STATE_001");
        var now = DateTimeOffset.UtcNow;
        var count = db.AuthSessions.Where(item => item.UserId == actor.Id && item.Id != currentSessionId && item.RevokedAt == null)
            .ExecuteUpdate(setters => setters.SetProperty(item => item.RevokedAt, now).SetProperty(item => item.RevokedReason, "USER_REVOKED_OTHERS"));
        return ServiceResult<int>.Success(count);
    }

    public static int RevokeAllSessions(OaDbContext db, string userId, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        return db.AuthSessions.Where(item => item.UserId == userId && item.RevokedAt == null)
            .ExecuteUpdate(setters => setters.SetProperty(item => item.RevokedAt, now).SetProperty(item => item.RevokedReason, reason));
    }

    private void RevokeSessionRecord(Guid sessionId, string reason, DateTimeOffset now)
    {
        db!.AuthSessions.Where(item => item.Id == sessionId && item.RevokedAt == null)
            .ExecuteUpdate(setters => setters.SetProperty(item => item.RevokedAt, now).SetProperty(item => item.RevokedReason, reason));
    }

    private (AuthSessionRecord Record, string RawToken) CreateSession(string userId, SessionClient? client, DateTimeOffset now)
    {
        var record = new AuthSessionRecord { UserId = userId, CreatedAt = now, LastUsedAt = now, ExpiresAt = now.AddDays(refreshTokenDays), UserAgent = Normalize(client?.UserAgent, 256), IpAddress = Normalize(client?.IpAddress, 64) };
        var raw = GenerateRawToken(record.Id);
        record.RefreshTokenHash = Hash(raw);
        return (record, raw);
    }

    private ServiceResult<AuthenticationGrant> Success(Employee employee, IssuedToken issued, string? refreshToken, DateTimeOffset? refreshExpiresAt, Guid? sessionId) =>
        ServiceResult<AuthenticationGrant>.Success(new AuthenticationGrant(new LoginSession(issued.Token, issued.ExpiresAt, data.ToProfile(employee)), refreshToken, refreshExpiresAt, sessionId));

    private static ServiceResult<AuthenticationGrant> Failure() => ServiceResult<AuthenticationGrant>.Failure("账号、密码或登录会话无效。", "AUTH_001");
    private static string GenerateRawToken(Guid sessionId) => $"{sessionId:N}.{Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool TryReadSessionId(string? rawToken, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(rawToken)) return false;
        var separator = rawToken.IndexOf('.');
        return separator == 32 && Guid.TryParseExact(rawToken[..separator], "N", out sessionId);
    }

    private static string? Normalize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private static bool LegacyPasswordMatches(string password)
    {
        var candidate = Rfc2898DeriveBytes.Pbkdf2(password, Encoding.UTF8.GetBytes(LegacyPasswordSalt), 120_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(candidate, Convert.FromBase64String(LegacyPasswordHash));
    }

    private static SessionView ToView(AuthSessionRecord item, Guid? currentSessionId, DateTimeOffset now)
    {
        var status = item.RevokedAt is not null ? "REVOKED" : item.ExpiresAt <= now ? "EXPIRED" : "ACTIVE";
        return new SessionView(item.Id, DescribeDevice(item.UserAgent), item.IpAddress, item.CreatedAt, item.LastUsedAt, item.ExpiresAt, status, item.Id == currentSessionId);
    }

    private static string DescribeDevice(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "未知设备";
        var browser = userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" : userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome" : userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox" : userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" : "浏览器";
        var system = userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone" : userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" : userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS" : userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" : userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" : "未知系统";
        return $"{browser} · {system}";
    }
}
