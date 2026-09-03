using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class IdempotencyService(OaDbContext db)
{
    private const string TenantId = "demo";
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> LocalLocks = new();

    public IdempotencyRecord? Find(string actorId, string route, string key) =>
        db.IdempotencyKeys.SingleOrDefault(item => item.TenantId == TenantId && item.ActorId == actorId && item.Route == route && item.Key == key);

    public IdempotencyLookup Lookup(string actorId, string route, string key, string requestHash)
    {
        var record = Find(actorId, route, key);
        var hashConflict = record is not null && !string.IsNullOrWhiteSpace(record.RequestHash) && !CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(record.RequestHash),
            Encoding.ASCII.GetBytes(requestHash));
        return new IdempotencyLookup(record, hashConflict);
    }

    public void Store(string actorId, string route, string key, int statusCode, string responseJson, string? requestHash = null)
    {
        db.IdempotencyKeys.Add(new IdempotencyRecord { TenantId = TenantId, ActorId = actorId, Route = route, Key = key, RequestHash = requestHash, StatusCode = statusCode, ResponseJson = responseJson });
        db.SaveChanges();
    }

    public IDisposable Acquire(string actorId, string route, string key)
    {
        var lockKey = LockKey(actorId, route, key);
        if (!IsPostgreSql())
        {
            var semaphore = LocalLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            semaphore.Wait();
            return new IdempotencyLease(() => semaphore.Release());
        }

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) db.Database.OpenConnection();
        try
        {
            ExecuteAdvisoryCommand("SELECT pg_advisory_lock(@lock_key)", lockKey);
            return new IdempotencyLease(() =>
            {
                try { ExecuteAdvisoryCommand("SELECT pg_advisory_unlock(@lock_key)", lockKey); }
                finally { if (shouldClose) db.Database.CloseConnection(); }
            });
        }
        catch
        {
            if (shouldClose) db.Database.CloseConnection();
            throw;
        }
    }

    public IDbContextTransaction BeginTransaction() => db.Database.BeginTransaction();
    public void ClearTrackedChanges() => db.ChangeTracker.Clear();

    public static string Fingerprint(object? payload)
    {
        var json = payload is null ? string.Empty : JsonSerializer.Serialize(payload, payload.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private bool IsPostgreSql() => db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private void ExecuteAdvisoryCommand(string commandText, long lockKey)
    {
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = 30;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = lockKey;
        command.Parameters.Add(parameter);
        command.ExecuteScalar();
    }

    private static long LockKey(string actorId, string route, string key)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{TenantId}\n{actorId}\n{route}\n{key}"));
        return BitConverter.ToInt64(digest, 0);
    }

    private sealed class IdempotencyLease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

public sealed record IdempotencyLookup(IdempotencyRecord? Record, bool HashConflict);
