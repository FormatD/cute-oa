using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public static class MfaChallengeStates
{
    public const string Required = "MFA_REQUIRED";
    public const string SetupRequired = "MFA_SETUP_REQUIRED";
}

public sealed record MfaChallengeView(string ChallengeToken, string State, DateTimeOffset ExpiresAt);
public sealed record MfaSetupView(string ChallengeToken, string Secret, string ProvisioningUri, DateTimeOffset ExpiresAt);
public sealed record MfaCompletion(string UserId, bool UsedRecoveryCode = false);
public sealed record MfaSetupCompletion(string UserId, IReadOnlyList<string> RecoveryCodes);
public sealed record MfaVerifyRequest(string ChallengeToken, string Code);
public sealed record MfaSetupStartRequest(string ChallengeToken);
public sealed record MfaSetupConfirmRequest(string ChallengeToken, string Code);
public sealed record MfaStatusView(bool Enabled, bool Required, DateTimeOffset? EnabledAt, int RecoveryCodesRemaining);

public sealed class MultiFactorSettings
{
    private static readonly string[] DefaultRequiredPermissions =
    [
        OaPermissions.UserManage,
        OaPermissions.PersonnelExport,
        OaPermissions.PersonnelManage,
        OaPermissions.AttendanceManage,
        OaPermissions.ContractManage,
        OaPermissions.PurchaseManage
    ];

    public bool Enabled { get; init; }
    public int ChallengeMinutes { get; init; }
    public string Issuer { get; init; } = IdentityDefaults.TenantName;
    public IReadOnlySet<string> RequiredPermissions { get; init; } = new HashSet<string>();

    public static MultiFactorSettings From(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Authentication:MultiFactor:RequiredPermissions").Get<string[]>();
        return new MultiFactorSettings
        {
            Enabled = configuration.GetValue<bool>("Authentication:MultiFactor:Enabled"),
            ChallengeMinutes = Math.Clamp(configuration.GetValue<int?>("Authentication:MultiFactor:ChallengeMinutes") ?? 5, 2, 15),
            Issuer = configuration["Authentication:MultiFactor:Issuer"]?.Trim() is { Length: > 0 } issuer ? issuer : IdentityDefaults.TenantName,
            RequiredPermissions = (configured is { Length: > 0 } ? configured : DefaultRequiredPermissions)
                .Where(OaPermissions.All.Contains)
                .ToHashSet(StringComparer.Ordinal)
        };
    }
}

public sealed class MfaSecretProtector
{
    private readonly byte[]? key;

    public MfaSecretProtector(IConfiguration configuration)
    {
        var configured = configuration["Authentication:MultiFactor:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(configured)) return;
        try
        {
            var decoded = Convert.FromBase64String(configured);
            if (decoded.Length == 32) key = decoded;
        }
        catch (FormatException)
        {
            // ProductionConfigurationPolicy reports a precise startup error.
        }
    }

    public string Protect(string plaintext)
    {
        if (key is null) throw new InvalidOperationException("缺少有效的 MFA 加密密钥。");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var source = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[source.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, source, cipher, tag);
        return $"v1.{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}.{Convert.ToBase64String(cipher)}";
    }

    public string Unprotect(string protectedValue)
    {
        if (key is null) throw new InvalidOperationException("缺少有效的 MFA 加密密钥。");
        var parts = protectedValue.Split('.');
        if (parts.Length != 4 || parts[0] != "v1") throw new CryptographicException("MFA 密钥密文格式无效。");
        var nonce = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var cipher = Convert.FromBase64String(parts[3]);
        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}

public sealed class MultiFactorAuthenticationService
{
    private const int MaxChallengeAttempts = 5;
    private const int RecoveryCodeCount = 10;
    private readonly OaDbContext db;
    private readonly DemoData data;
    private readonly MultiFactorSettings settings;
    private readonly MfaSecretProtector protector;

    public MultiFactorAuthenticationService(OaDbContext db, DemoData data, IConfiguration configuration, MfaSecretProtector protector)
    {
        this.db = db;
        this.data = data;
        this.protector = protector;
        settings = MultiFactorSettings.From(configuration);
    }

    public bool ShouldChallenge(Employee employee, UserAccountRecord account) =>
        settings.Enabled && (account.MfaEnabled || settings.RequiredPermissions.Any(permission => data.HasPermission(employee, permission)));

    public bool IsRequired(Employee employee) => settings.Enabled && settings.RequiredPermissions.Any(permission => data.HasPermission(employee, permission));

    public ServiceResult<MfaChallengeView> CreateChallenge(Employee employee, UserAccountRecord account, SessionClient? client)
    {
        if (!settings.Enabled) return ServiceResult<MfaChallengeView>.Failure("多因素认证未启用。", "STATE_001");
        var now = DateTimeOffset.UtcNow;
        db.MfaChallenges.Where(item => item.UserId == employee.Id && (item.ConsumedAt != null || item.ExpiresAt <= now.AddDays(-1))).ExecuteDelete();
        var challenge = new MfaChallengeRecord
        {
            UserId = employee.Id,
            Purpose = account.MfaEnabled ? "VERIFY" : "SETUP",
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(settings.ChallengeMinutes),
            UserAgent = Normalize(client?.UserAgent, 256),
            IpAddress = Normalize(client?.IpAddress, 64)
        };
        var token = GenerateToken(challenge.Id);
        challenge.TokenHash = Hash(token);
        db.MfaChallenges.Add(challenge);
        db.SaveChanges();
        var state = challenge.Purpose == "VERIFY" ? MfaChallengeStates.Required : MfaChallengeStates.SetupRequired;
        return ServiceResult<MfaChallengeView>.Success(new MfaChallengeView(token, state, challenge.ExpiresAt));
    }

    public ServiceResult<MfaSetupView> StartSetup(string? rawToken)
    {
        var challenge = FindChallenge(rawToken, "SETUP");
        if (challenge is null) return ChallengeFailure<MfaSetupView>();
        var secret = challenge.PendingSecretCiphertext is null
            ? TotpGenerator.GenerateSecret()
            : protector.Unprotect(challenge.PendingSecretCiphertext);
        if (challenge.PendingSecretCiphertext is null)
        {
            var protectedSecret = protector.Protect(secret);
            var updated = db.MfaChallenges.Where(item => item.Id == challenge.Id && item.ConsumedAt == null && item.ExpiresAt > DateTimeOffset.UtcNow && item.PendingSecretCiphertext == null)
                .ExecuteUpdate(setters => setters.SetProperty(item => item.PendingSecretCiphertext, protectedSecret));
            if (updated == 0)
            {
                challenge = FindChallenge(rawToken, "SETUP");
                if (challenge?.PendingSecretCiphertext is null) return ChallengeFailure<MfaSetupView>();
                secret = protector.Unprotect(challenge.PendingSecretCiphertext);
            }
        }
        var label = $"{settings.Issuer}:{challenge.UserId}";
        var uri = $"otpauth://totp/{Uri.EscapeDataString(label)}?secret={secret}&issuer={Uri.EscapeDataString(settings.Issuer)}&algorithm=SHA1&digits=6&period=30";
        return ServiceResult<MfaSetupView>.Success(new MfaSetupView(rawToken!, secret, uri, challenge.ExpiresAt));
    }

    public ServiceResult<MfaSetupCompletion> ConfirmSetup(string? rawToken, string? code)
    {
        var challenge = FindChallenge(rawToken, "SETUP");
        if (challenge?.PendingSecretCiphertext is null) return ChallengeFailure<MfaSetupCompletion>();
        var secret = protector.Unprotect(challenge.PendingSecretCiphertext);
        if (!TotpGenerator.TryValidate(secret, code, DateTimeOffset.UtcNow, out var timeStep))
        {
            RegisterFailure(challenge);
            return CodeFailure<MfaSetupCompletion>();
        }

        var now = DateTimeOffset.UtcNow;
        var recoveryCodes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => GenerateRecoveryCode()).ToList();
        var hashesJson = JsonSerializer.Serialize(recoveryCodes.Select(HashRecoveryCode));
        using var transaction = db.Database.BeginTransaction();
        var accountUpdated = db.UserAccounts.Where(item => item.UserId == challenge.UserId && !item.MfaEnabled)
            .ExecuteUpdate(setters => setters
                .SetProperty(item => item.MfaEnabled, true)
                .SetProperty(item => item.MfaSecretCiphertext, protector.Protect(secret))
                .SetProperty(item => item.RecoveryCodeHashesJson, hashesJson)
                .SetProperty(item => item.LastTotpTimeStep, timeStep)
                .SetProperty(item => item.MfaEnabledAt, now)
                .SetProperty(item => item.MfaUpdatedAt, now));
        var challengeConsumed = ConsumeChallenge(challenge.Id, now);
        if (accountUpdated != 1 || challengeConsumed != 1)
        {
            transaction.Rollback();
            return ChallengeFailure<MfaSetupCompletion>();
        }
        AddAudit(challenge.UserId, "MFA_ENABLED", "启用 TOTP 多因素认证并生成一次性恢复码", now);
        db.SaveChanges();
        transaction.Commit();
        return ServiceResult<MfaSetupCompletion>.Success(new MfaSetupCompletion(challenge.UserId, recoveryCodes));
    }

    public ServiceResult<MfaCompletion> Verify(string? rawToken, string? code)
    {
        var challenge = FindChallenge(rawToken, "VERIFY");
        if (challenge is null) return ChallengeFailure<MfaCompletion>();
        var account = db.UserAccounts.AsNoTracking().SingleOrDefault(item => item.UserId == challenge.UserId && item.MfaEnabled);
        if (account?.MfaSecretCiphertext is null) return ChallengeFailure<MfaCompletion>();
        var now = DateTimeOffset.UtcNow;
        var normalizedCode = NormalizeRecoveryCode(code);
        var recoveryHashes = DeserializeHashes(account.RecoveryCodeHashesJson);
        var recoveryHash = HashRecoveryCode(normalizedCode);
        var recoveryIndex = recoveryHashes.FindIndex(hash => FixedTimeEquals(hash, recoveryHash));
        var usedRecoveryCode = recoveryIndex >= 0;
        long timeStep = 0;
        if (!usedRecoveryCode && !TotpGenerator.TryValidate(protector.Unprotect(account.MfaSecretCiphertext), code, now, out timeStep))
        {
            RegisterFailure(challenge);
            return CodeFailure<MfaCompletion>();
        }

        using var transaction = db.Database.BeginTransaction();
        int accountUpdated;
        if (usedRecoveryCode)
        {
            recoveryHashes.RemoveAt(recoveryIndex);
            var nextJson = JsonSerializer.Serialize(recoveryHashes);
            accountUpdated = db.UserAccounts.Where(item => item.UserId == account.UserId && item.MfaEnabled && item.RecoveryCodeHashesJson == account.RecoveryCodeHashesJson)
                .ExecuteUpdate(setters => setters.SetProperty(item => item.RecoveryCodeHashesJson, nextJson).SetProperty(item => item.MfaUpdatedAt, now));
        }
        else
        {
            accountUpdated = db.UserAccounts.Where(item => item.UserId == account.UserId && item.MfaEnabled && (item.LastTotpTimeStep == null || item.LastTotpTimeStep < timeStep))
                .ExecuteUpdate(setters => setters.SetProperty(item => item.LastTotpTimeStep, timeStep).SetProperty(item => item.MfaUpdatedAt, now));
        }
        var challengeConsumed = ConsumeChallenge(challenge.Id, now);
        if (accountUpdated != 1 || challengeConsumed != 1)
        {
            transaction.Rollback();
            return CodeFailure<MfaCompletion>();
        }
        if (usedRecoveryCode)
        {
            AddAudit(challenge.UserId, "MFA_RECOVERY_CODE_USED", "使用一次性恢复码完成多因素认证", now);
            db.SaveChanges();
        }
        transaction.Commit();
        return ServiceResult<MfaCompletion>.Success(new MfaCompletion(challenge.UserId, usedRecoveryCode));
    }

    public MfaStatusView GetStatus(Employee actor)
    {
        var account = db.UserAccounts.AsNoTracking().Single(item => item.UserId == actor.Id);
        return new MfaStatusView(account.MfaEnabled, IsRequired(actor), account.MfaEnabledAt, DeserializeHashes(account.RecoveryCodeHashesJson).Count);
    }

    private MfaChallengeRecord? FindChallenge(string? rawToken, string purpose)
    {
        if (!TryReadTokenId(rawToken, out var id)) return null;
        var challenge = db.MfaChallenges.AsNoTracking().SingleOrDefault(item => item.Id == id && item.Purpose == purpose && item.ConsumedAt == null && item.ExpiresAt > DateTimeOffset.UtcNow && item.FailedAttempts < MaxChallengeAttempts);
        if (challenge is null || !FixedTimeEquals(challenge.TokenHash, Hash(rawToken!))) return null;
        return challenge;
    }

    private void RegisterFailure(MfaChallengeRecord challenge)
    {
        var nextAttempts = challenge.FailedAttempts + 1;
        var now = DateTimeOffset.UtcNow;
        db.MfaChallenges.Where(item => item.Id == challenge.Id && item.ConsumedAt == null && item.FailedAttempts == challenge.FailedAttempts)
            .ExecuteUpdate(setters => setters.SetProperty(item => item.FailedAttempts, nextAttempts).SetProperty(item => item.ConsumedAt, nextAttempts >= MaxChallengeAttempts ? now : null));
        if (nextAttempts >= MaxChallengeAttempts)
        {
            AddAudit(challenge.UserId, "MFA_CHALLENGE_LOCKED", "多因素认证连续失败，当前挑战已失效", now);
            db.SaveChanges();
        }
    }

    private int ConsumeChallenge(Guid challengeId, DateTimeOffset now) => db.MfaChallenges
        .Where(item => item.Id == challengeId && item.ConsumedAt == null && item.ExpiresAt > now && item.FailedAttempts < MaxChallengeAttempts)
        .ExecuteUpdate(setters => setters.SetProperty(item => item.ConsumedAt, now));

    private void AddAudit(string userId, string action, string summary, DateTimeOffset now)
    {
        db.AuditLogs.Add(new AuditRecord { TenantId = IdentityDefaults.TenantId, ActorId = userId, Action = action, ResourceType = "UserAccount", ResourceId = userId, Summary = summary, OccurredAt = now });
    }

    private static List<string> DeserializeHashes(string? json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json ?? "[]") ?? []; }
        catch (JsonException) { return []; }
    }

    private static string GenerateToken(Guid id) => $"{id:N}.{Base64Url(RandomNumberGenerator.GetBytes(48))}";
    private static string GenerateRecoveryCode()
    {
        var value = Base32.Encode(RandomNumberGenerator.GetBytes(10));
        return string.Join('-', Enumerable.Range(0, 4).Select(index => value.Substring(index * 4, 4)));
    }
    private static string NormalizeRecoveryCode(string? value) => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string HashRecoveryCode(string? value) => Hash(NormalizeRecoveryCode(value));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool FixedTimeEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
    private static bool TryReadTokenId(string? rawToken, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(rawToken)) return false;
        var separator = rawToken.IndexOf('.');
        return separator == 32 && Guid.TryParseExact(rawToken[..separator], "N", out id);
    }
    private static string? Normalize(string? value, int maxLength) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];
    private static ServiceResult<T> ChallengeFailure<T>() => ServiceResult<T>.Failure("多因素认证挑战无效或已过期，请重新登录。", "AUTH_004");
    private static ServiceResult<T> CodeFailure<T>() => ServiceResult<T>.Failure("动态验证码或恢复码无效。", "AUTH_005");
}

public static class TotpGenerator
{
    public static string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    public static string GenerateCode(string secret, DateTimeOffset timestamp) => GenerateCode(secret, timestamp.ToUnixTimeSeconds() / 30);

    public static bool TryValidate(string secret, string? code, DateTimeOffset timestamp, out long matchedTimeStep)
    {
        matchedTimeStep = 0;
        if (code is null || code.Length != 6 || code.Any(character => character is < '0' or > '9')) return false;
        var current = timestamp.ToUnixTimeSeconds() / 30;
        foreach (var timeStep in new[] { current, current - 1, current + 1 })
        {
            var candidate = GenerateCode(secret, timeStep);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(code))) continue;
            matchedTimeStep = timeStep;
            return true;
        }
        return false;
    }

    private static string GenerateCode(string secret, long timeStep)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, timeStep);
        using var hmac = new HMACSHA1(Base32.Decode(secret));
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0f;
        var value = ((hash[offset] & 0x7f) << 24) | ((hash[offset + 1] & 0xff) << 16) | ((hash[offset + 2] & 0xff) << 8) | (hash[offset + 3] & 0xff);
        return (value % 1_000_000).ToString("D6");
    }
}

internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }
        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    public static byte[] Decode(string value)
    {
        var output = new List<byte>(value.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var character in value.Trim().TrimEnd('=').ToUpperInvariant())
        {
            var index = Alphabet.IndexOf(character);
            if (index < 0) throw new FormatException("Base32 内容无效。");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            output.Add((byte)((buffer >> bits) & 0xff));
        }
        return output.ToArray();
    }
}
