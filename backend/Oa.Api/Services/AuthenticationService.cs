using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed record LoginRequest(string UserId, string Password);
public sealed record ChangeInitialPasswordRequest(string ChallengeToken, string NewPassword);
public sealed record LoginSession(string AccessToken, DateTimeOffset ExpiresAt, UserProfile User);
public sealed record SessionClient(string? UserAgent, string? IpAddress);
public sealed record AuthenticationGrant(LoginSession Session, string? RefreshToken, DateTimeOffset? RefreshExpiresAt, Guid? SessionId);
public sealed record LoginCompletion(AuthenticationGrant? Grant, MfaChallengeView? Challenge);
public sealed record MfaEnrollmentGrant(AuthenticationGrant Grant, IReadOnlyList<string> RecoveryCodes);

public static class AuthenticationChallengeStates
{
    public const string PasswordChangeRequired = "PASSWORD_CHANGE_REQUIRED";
}

public sealed class AuthenticationService
{
    private const string LegacyPasswordSalt = "cute-oa-demo-auth-v1";
    private const string LegacyPasswordHash = "7SvEK7p9V+hgRZYcRDAruXSRVV7DxBymj5Sp5lXxI3I=";
    private readonly DemoData data;
    private readonly JwtTokenService tokens;
    private readonly OaDbContext? db;
    private readonly MultiFactorAuthenticationService? mfa;
    private readonly int refreshTokenDays;
    private readonly int passwordChangeChallengeMinutes;

    public AuthenticationService(DemoData data, JwtTokenService tokens) : this(data, tokens, null, null, null) { }

    public AuthenticationService(DemoData data, JwtTokenService tokens, OaDbContext? db, IConfiguration? configuration = null, MultiFactorAuthenticationService? mfa = null)
    {
        this.data = data;
        this.tokens = tokens;
        this.db = db;
        this.mfa = mfa;
        refreshTokenDays = Math.Clamp(configuration?.GetValue<int?>("Authentication:RefreshTokenDays") ?? 30, 1, 90);
        passwordChangeChallengeMinutes = Math.Clamp(configuration?.GetValue<int?>("Authentication:PasswordChangeChallengeMinutes") ?? 10, 5, 30);
    }

    public ServiceResult<AuthenticationGrant> Login(LoginRequest request, SessionClient? client = null)
    {
        var result = BeginLogin(request, client);
        if (!result.IsSuccess) return ServiceResult<AuthenticationGrant>.Failure(result.Error!, result.Code!);
        return result.Value!.Grant is { } grant
            ? ServiceResult<AuthenticationGrant>.Success(grant)
            : ServiceResult<AuthenticationGrant>.Failure("需要完成多因素认证。", "AUTH_004");
    }

    public ServiceResult<LoginCompletion> BeginLogin(LoginRequest request, SessionClient? client = null)
    {
        var userId = request.UserId?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        var employee = data.FindEmployee(userId);
        if (employee is null || employee.Status != "ACTIVE" || password.Length == 0)
        {
            PerformDummyPasswordCheck(password);
            return LoginFailure();
        }

        if (db is null)
        {
            if (!LegacyPasswordMatches(password)) return LoginFailure();
            var legacyToken = tokens.Issue(employee);
            return ServiceResult<LoginCompletion>.Success(new LoginCompletion(CreateGrant(employee, legacyToken, null, null, null), null));
        }

        var account = db.UserAccounts.SingleOrDefault(item => item.UserId == employee.Id);
        var now = DateTimeOffset.UtcNow;
        if (account is null || account.LockedUntil > now)
        {
            PerformDummyPasswordCheck(password);
            return LoginFailure();
        }
        if (!PasswordHasher.Verify(password, account.PasswordSalt, account.PasswordHash, account.PasswordIterations))
        {
            RecordFailedLogin(account, now);
            return LoginFailure();
        }

        var reset = db.UserAccounts
            .Where(item => item.UserId == account.UserId && item.PasswordHash == account.PasswordHash && item.PasswordChangedAt == account.PasswordChangedAt && (item.LockedUntil == null || item.LockedUntil <= now))
            .ExecuteUpdate(setters => setters.SetProperty(item => item.FailedLoginCount, 0).SetProperty(item => item.LockedUntil, (DateTimeOffset?)null));
        if (reset != 1) return LoginFailure();
        db.Entry(account).Reload();
        if (account.MustChangePassword)
            return ServiceResult<LoginCompletion>.Success(new LoginCompletion(null, CreatePasswordChangeChallenge(employee.Id, client, now)));
        if (mfa?.ShouldChallenge(employee, account) == true)
        {
            var challenge = mfa.CreateChallenge(employee, account, client);
            return challenge.IsSuccess
                ? ServiceResult<LoginCompletion>.Success(new LoginCompletion(null, challenge.Value))
                : ServiceResult<LoginCompletion>.Failure(challenge.Error!, challenge.Code!);
        }
        var created = CreateSession(employee.Id, client, now);
        db.AuthSessions.Add(created.Record);
        db.SaveChanges();
        var issued = tokens.Issue(employee, created.Record.Id);
        return ServiceResult<LoginCompletion>.Success(new LoginCompletion(CreateGrant(employee, issued, created.RawToken, created.Record.ExpiresAt, created.Record.Id), null));
    }

    public ServiceResult<LoginCompletion> ChangeInitialPassword(ChangeInitialPasswordRequest request, SessionClient? client = null)
    {
        if (db is null) return ServiceResult<LoginCompletion>.Failure("当前环境不支持首次登录改密。", "STATE_001");
        var newPassword = request.NewPassword ?? string.Empty;
        if (!PasswordHasher.MeetsProductionPolicy(newPassword))
            return ServiceResult<LoginCompletion>.Failure("新密码须为 12–128 位，并包含大小写字母、数字和特殊字符。", "VALIDATION_001");
        if (!TryReadSessionId(request.ChallengeToken, out var challengeId)) return PasswordChallengeFailure();

        var now = DateTimeOffset.UtcNow;
        var tokenHash = Hash(request.ChallengeToken);
        var challenge = db.MfaChallenges.AsNoTracking().SingleOrDefault(item =>
            item.Id == challengeId && item.TokenHash == tokenHash && item.Purpose == "PASSWORD" && item.ConsumedAt == null && item.ExpiresAt > now);
        if (challenge is null) return PasswordChallengeFailure();
        var employee = data.FindEmployee(challenge.UserId);
        var account = db.UserAccounts.AsNoTracking().SingleOrDefault(item => item.UserId == challenge.UserId && item.MustChangePassword);
        if (employee is null || employee.Status != "ACTIVE" || account is null) return PasswordChallengeFailure();
        if (PasswordHasher.Verify(newPassword, account.PasswordSalt, account.PasswordHash, account.PasswordIterations))
            return ServiceResult<LoginCompletion>.Failure("新密码不能与临时密码相同。", "VALIDATION_001");

        var password = PasswordHasher.Hash(newPassword);
        using var transaction = db.Database.IsRelational() ? db.Database.BeginTransaction() : null;
        try
        {
            var accountUpdated = db.UserAccounts
                .Where(item => item.UserId == account.UserId && item.MustChangePassword && item.PasswordHash == account.PasswordHash && item.PasswordChangedAt == account.PasswordChangedAt)
                .ExecuteUpdate(setters => setters
                    .SetProperty(item => item.PasswordSalt, password.Salt)
                    .SetProperty(item => item.PasswordHash, password.Hash)
                    .SetProperty(item => item.PasswordIterations, password.Iterations)
                    .SetProperty(item => item.PasswordChangedAt, now)
                    .SetProperty(item => item.MustChangePassword, false)
                    .SetProperty(item => item.FailedLoginCount, 0)
                    .SetProperty(item => item.LockedUntil, (DateTimeOffset?)null));
            var challengeConsumed = db.MfaChallenges
                .Where(item => item.Id == challenge.Id && item.ConsumedAt == null && item.ExpiresAt > now)
                .ExecuteUpdate(setters => setters.SetProperty(item => item.ConsumedAt, now));
            if (accountUpdated != 1 || challengeConsumed != 1)
            {
                transaction?.Rollback();
                return PasswordChallengeFailure();
            }
            db.MfaChallenges.Where(item => item.UserId == account.UserId && item.Purpose == "PASSWORD" && item.ConsumedAt == null)
                .ExecuteUpdate(setters => setters.SetProperty(item => item.ConsumedAt, now));
            RevokeAllSessions(db, account.UserId, "INITIAL_PASSWORD_CHANGED");
            db.AuditLogs.Add(new AuditRecord
            {
                TenantId = IdentityDefaults.TenantId,
                ActorId = account.UserId,
                Action = "INITIAL_PASSWORD_CHANGED",
                ResourceType = "User",
                ResourceId = account.UserId,
                Summary = "首次登录完成临时密码修改"
            });
            db.SaveChanges();
            transaction?.Commit();
        }
        catch
        {
            transaction?.Rollback();
            throw;
        }

        db.ChangeTracker.Entries<UserAccountRecord>().FirstOrDefault(entry => entry.Entity.UserId == account.UserId)?.Reload();
        var updatedAccount = db.UserAccounts.AsNoTracking().Single(item => item.UserId == account.UserId);
        if (mfa?.ShouldChallenge(employee, updatedAccount) == true)
        {
            var mfaChallenge = mfa.CreateChallenge(employee, updatedAccount, client);
            return mfaChallenge.IsSuccess
                ? ServiceResult<LoginCompletion>.Success(new LoginCompletion(null, mfaChallenge.Value))
                : ServiceResult<LoginCompletion>.Failure(mfaChallenge.Error!, mfaChallenge.Code!);
        }
        var created = CreateSession(employee.Id, client, now);
        db.AuthSessions.Add(created.Record);
        db.SaveChanges();
        var issued = tokens.Issue(employee, created.Record.Id);
        return ServiceResult<LoginCompletion>.Success(new LoginCompletion(CreateGrant(employee, issued, created.RawToken, created.Record.ExpiresAt, created.Record.Id), null));
    }

    public ServiceResult<MfaSetupView> StartMfaSetup(MfaSetupStartRequest request) => mfa is null
        ? ServiceResult<MfaSetupView>.Failure("当前环境未启用多因素认证。", "STATE_001")
        : mfa.StartSetup(request.ChallengeToken);

    public ServiceResult<AuthenticationGrant> VerifyMfa(MfaVerifyRequest request, SessionClient? client = null)
    {
        if (mfa is null) return ServiceResult<AuthenticationGrant>.Failure("当前环境未启用多因素认证。", "STATE_001");
        var verified = mfa.Verify(request.ChallengeToken, request.Code);
        return verified.IsSuccess ? CompleteMfaSession(verified.Value!.UserId, client) : ServiceResult<AuthenticationGrant>.Failure(verified.Error!, verified.Code!);
    }

    public ServiceResult<MfaEnrollmentGrant> ConfirmMfaSetup(MfaSetupConfirmRequest request, SessionClient? client = null)
    {
        if (mfa is null) return ServiceResult<MfaEnrollmentGrant>.Failure("当前环境未启用多因素认证。", "STATE_001");
        var enrolled = mfa.ConfirmSetup(request.ChallengeToken, request.Code);
        if (!enrolled.IsSuccess) return ServiceResult<MfaEnrollmentGrant>.Failure(enrolled.Error!, enrolled.Code!);
        var grant = CompleteMfaSession(enrolled.Value!.UserId, client);
        return grant.IsSuccess
            ? ServiceResult<MfaEnrollmentGrant>.Success(new MfaEnrollmentGrant(grant.Value!, enrolled.Value.RecoveryCodes))
            : ServiceResult<MfaEnrollmentGrant>.Failure(grant.Error!, grant.Code!);
    }

    public ServiceResult<MfaStatusView> GetMfaStatus(Employee actor) => mfa is null
        ? ServiceResult<MfaStatusView>.Success(new MfaStatusView(false, false, null, 0))
        : ServiceResult<MfaStatusView>.Success(mfa.GetStatus(actor));

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
        var requiresPasswordChange = db.UserAccounts.AsNoTracking().Any(item => item.UserId == session.UserId && item.MustChangePassword);
        if (employee is null || employee.Status != "ACTIVE" || requiresPasswordChange)
        {
            RevokeSessionRecord(session.Id, requiresPasswordChange ? "PASSWORD_CHANGE_REQUIRED" : "USER_INACTIVE", now);
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
        return ServiceResult<AuthenticationGrant>.Success(CreateGrant(employee, issued, rotated, session.ExpiresAt, session.Id));
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

    private void RecordFailedLogin(UserAccountRecord account, DateTimeOffset now)
    {
        var lockedUntil = now.AddMinutes(15);
        db!.UserAccounts
            .Where(item => item.UserId == account.UserId && item.PasswordHash == account.PasswordHash && item.PasswordChangedAt == account.PasswordChangedAt && (item.LockedUntil == null || item.LockedUntil <= now))
            .ExecuteUpdate(setters => setters
                .SetProperty(item => item.FailedLoginCount, item => item.FailedLoginCount + 1 >= 5 ? 0 : item.FailedLoginCount + 1)
                .SetProperty(item => item.LockedUntil, item => item.FailedLoginCount + 1 >= 5 ? lockedUntil : (DateTimeOffset?)null));
        db.Entry(account).Reload();
    }

    private (AuthSessionRecord Record, string RawToken) CreateSession(string userId, SessionClient? client, DateTimeOffset now)
    {
        var record = new AuthSessionRecord { UserId = userId, CreatedAt = now, LastUsedAt = now, ExpiresAt = now.AddDays(refreshTokenDays), UserAgent = Normalize(client?.UserAgent, 256), IpAddress = Normalize(client?.IpAddress, 64) };
        var raw = GenerateRawToken(record.Id);
        record.RefreshTokenHash = Hash(raw);
        return (record, raw);
    }

    private MfaChallengeView CreatePasswordChangeChallenge(string userId, SessionClient? client, DateTimeOffset now)
    {
        db!.MfaChallenges.Where(item => item.UserId == userId && item.Purpose == "PASSWORD" && item.ConsumedAt == null)
            .ExecuteUpdate(setters => setters.SetProperty(item => item.ConsumedAt, now));
        db.MfaChallenges.Where(item => item.Purpose == "PASSWORD" && (item.ConsumedAt != null || item.ExpiresAt <= now.AddDays(-1))).ExecuteDelete();
        var challenge = new MfaChallengeRecord
        {
            UserId = userId,
            Purpose = "PASSWORD",
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(passwordChangeChallengeMinutes),
            UserAgent = Normalize(client?.UserAgent, 256),
            IpAddress = Normalize(client?.IpAddress, 64)
        };
        var token = GenerateRawToken(challenge.Id);
        challenge.TokenHash = Hash(token);
        db.MfaChallenges.Add(challenge);
        db.SaveChanges();
        return new MfaChallengeView(token, AuthenticationChallengeStates.PasswordChangeRequired, challenge.ExpiresAt);
    }

    private ServiceResult<AuthenticationGrant> CompleteMfaSession(string userId, SessionClient? client)
    {
        if (db is null) return Failure();
        var employee = data.FindEmployee(userId);
        if (employee is null || employee.Status != "ACTIVE" || db.UserAccounts.AsNoTracking().Any(item => item.UserId == userId && item.MustChangePassword)) return Failure();
        var now = DateTimeOffset.UtcNow;
        var created = CreateSession(employee.Id, client, now);
        db.AuthSessions.Add(created.Record);
        db.SaveChanges();
        var issued = tokens.Issue(employee, created.Record.Id);
        return ServiceResult<AuthenticationGrant>.Success(CreateGrant(employee, issued, created.RawToken, created.Record.ExpiresAt, created.Record.Id));
    }

    private AuthenticationGrant CreateGrant(Employee employee, IssuedToken issued, string? refreshToken, DateTimeOffset? refreshExpiresAt, Guid? sessionId) =>
        new(new LoginSession(issued.Token, issued.ExpiresAt, data.ToProfile(employee)), refreshToken, refreshExpiresAt, sessionId);

    private static ServiceResult<AuthenticationGrant> Failure() => ServiceResult<AuthenticationGrant>.Failure("账号、密码或登录会话无效。", "AUTH_001");
    private static ServiceResult<LoginCompletion> LoginFailure() => ServiceResult<LoginCompletion>.Failure("账号、密码或登录会话无效。", "AUTH_001");
    private static ServiceResult<LoginCompletion> PasswordChallengeFailure() => ServiceResult<LoginCompletion>.Failure("首次登录改密凭证无效或已过期，请重新登录。", "AUTH_006");
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

    private static void PerformDummyPasswordCheck(string password) => _ = LegacyPasswordMatches(password);

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
