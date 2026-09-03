using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Oa.Api.Services;

public sealed record LoginProtectionSettings(
    bool Enabled,
    int WindowSeconds,
    int MaxAttemptsPerAccountAndIp,
    int MaxAttemptsPerAccount,
    int MaxAttemptsPerIp,
    int MaxTrackedKeys)
{
    public static LoginProtectionSettings From(IConfiguration configuration) => new(
        configuration.GetValue("LoginProtection:Enabled", true),
        configuration.GetValue("LoginProtection:WindowSeconds", 300),
        configuration.GetValue("LoginProtection:MaxAttemptsPerAccountAndIp", 5),
        configuration.GetValue("LoginProtection:MaxAttemptsPerAccount", 15),
        configuration.GetValue("LoginProtection:MaxAttemptsPerIp", 60),
        configuration.GetValue("LoginProtection:MaxTrackedKeys", 10_000));

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (WindowSeconds is < 60 or > 3600) errors.Add("WindowSeconds 必须为 60–3600");
        if (MaxAttemptsPerAccountAndIp is < 1 or > 20) errors.Add("MaxAttemptsPerAccountAndIp 必须为 1–20");
        if (MaxAttemptsPerAccount < MaxAttemptsPerAccountAndIp || MaxAttemptsPerAccount > 100) errors.Add("MaxAttemptsPerAccount 必须不小于组合限额且不大于 100");
        if (MaxAttemptsPerIp < MaxAttemptsPerAccount || MaxAttemptsPerIp > 1000) errors.Add("MaxAttemptsPerIp 必须不小于账号限额且不大于 1000");
        if (MaxTrackedKeys is < 1000 or > 100_000) errors.Add("MaxTrackedKeys 必须为 1000–100000");
        return errors;
    }
}

public sealed record LoginAttemptDecision(bool IsAllowed, int RetryAfterSeconds)
{
    public static LoginAttemptDecision Allowed { get; } = new(true, 0);
}

public sealed class LoginAttemptLimiter
{
    private sealed record Counter(DateTimeOffset WindowStartedAt, int Count);

    private readonly LoginProtectionSettings settings;
    private readonly TimeProvider timeProvider;
    private readonly Dictionary<string, Counter> counters = new(StringComparer.Ordinal);
    private readonly object sync = new();
    private int operations;

    public LoginAttemptLimiter(IConfiguration configuration) : this(LoginProtectionSettings.From(configuration), TimeProvider.System) { }

    public LoginAttemptLimiter(LoginProtectionSettings settings, TimeProvider timeProvider)
    {
        var errors = settings.Validate();
        if (errors.Count > 0) throw new InvalidOperationException($"登录限流配置无效：{string.Join("；", errors)}。");
        this.settings = settings;
        this.timeProvider = timeProvider;
    }

    public LoginAttemptDecision TryAcquire(string? userId, string? ipAddress)
    {
        if (!settings.Enabled) return LoginAttemptDecision.Allowed;

        var now = timeProvider.GetUtcNow();
        var accountKey = AccountKey(userId);
        var ipKey = IpKey(ipAddress);
        var dimensions = new[]
        {
            ($"pair:{accountKey}:{ipKey}", settings.MaxAttemptsPerAccountAndIp),
            ($"account:{accountKey}", settings.MaxAttemptsPerAccount),
            ($"ip:{ipKey}", settings.MaxAttemptsPerIp)
        };

        lock (sync)
        {
            if (++operations % 128 == 0) RemoveExpired(now);

            var retryAfter = 0;
            foreach (var (key, limit) in dimensions)
            {
                if (!TryGetActive(key, now, out var counter) || counter.Count < limit) continue;
                retryAfter = Math.Max(retryAfter, SecondsUntilExpiry(counter, now));
            }
            if (retryAfter > 0) return new LoginAttemptDecision(false, retryAfter);

            var missing = dimensions.Count(dimension => !TryGetActive(dimension.Item1, now, out _));
            if (counters.Count + missing > settings.MaxTrackedKeys)
            {
                RemoveExpired(now);
                missing = dimensions.Count(dimension => !counters.ContainsKey(dimension.Item1));
                if (counters.Count + missing > settings.MaxTrackedKeys)
                    return new LoginAttemptDecision(false, Math.Min(settings.WindowSeconds, 60));
            }

            foreach (var (key, _) in dimensions)
            {
                counters[key] = TryGetActive(key, now, out var current)
                    ? current with { Count = current.Count + 1 }
                    : new Counter(now, 1);
            }
            return LoginAttemptDecision.Allowed;
        }
    }

    private bool TryGetActive(string key, DateTimeOffset now, out Counter counter)
    {
        if (counters.TryGetValue(key, out counter!) && now - counter.WindowStartedAt < TimeSpan.FromSeconds(settings.WindowSeconds)) return true;
        counters.Remove(key);
        counter = null!;
        return false;
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var key in counters.Where(item => now - item.Value.WindowStartedAt >= TimeSpan.FromSeconds(settings.WindowSeconds)).Select(item => item.Key).ToArray())
            counters.Remove(key);
    }

    private int SecondsUntilExpiry(Counter counter, DateTimeOffset now) =>
        Math.Max(1, (int)Math.Ceiling((counter.WindowStartedAt.AddSeconds(settings.WindowSeconds) - now).TotalSeconds));

    private static string AccountKey(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length > 256) normalized = normalized[..256];
        normalized = normalized.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string IpKey(string? value)
    {
        if (!IPAddress.TryParse(value, out var address)) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.ToString();
    }
}
