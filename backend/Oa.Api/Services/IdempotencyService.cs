using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class IdempotencyService(OaDbContext db)
{
    private const string TenantId = "demo";

    public IdempotencyRecord? Find(string actorId, string route, string key) =>
        db.IdempotencyKeys.SingleOrDefault(item => item.TenantId == TenantId && item.ActorId == actorId && item.Route == route && item.Key == key);

    public void Store(string actorId, string route, string key, int statusCode, string responseJson)
    {
        db.IdempotencyKeys.Add(new IdempotencyRecord { TenantId = TenantId, ActorId = actorId, Route = route, Key = key, StatusCode = statusCode, ResponseJson = responseJson });
        db.SaveChanges();
    }
}
