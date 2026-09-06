using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Oa.Api.Domain;

namespace Oa.Api.Services;

public static class BusinessConfigurationEndpoints
{
    public static IServiceCollection AddBusinessConfigurationServices(this IServiceCollection services)
    {
        services.AddScoped<BusinessConfigurationService>();
        return services;
    }

    public static IEndpointRouteBuilder MapBusinessConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/business-configurations");

        group.MapGet("/", (
            string? domain,
            string? code,
            string? keyword,
            string? status,
            DateTimeOffset? effectiveAsOf,
            int? page,
            int? pageSize,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service) =>
        {
            var actor = auth.Resolve(request);
            var query = new BusinessConfigurationFilterQuery(domain, code, keyword, status, effectiveAsOf, page ?? 1, pageSize ?? 20);
            var result = service.List(actor, query);
            return ToResult(result);
        });

        group.MapGet("/effective", (
            string domain,
            string code,
            DateTimeOffset? asOf,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service) =>
        {
            _ = auth.Resolve(request);
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(code))
                return Results.BadRequest(new { code = "CONFIG_001", message = "domain 和 code 参数必填。" });

            var result = service.GetEffective(domain.Trim(), code.Trim(), asOf);
            return result is not null
                ? Results.Ok(result)
                : Results.NotFound(new { code = "DATA_001", message = "未找到生效中的配置。" });
        });

        group.MapGet("/effective-options", (
            DateTimeOffset? asOf,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service) =>
        {
            _ = auth.Resolve(request);
            var result = service.GetEffectiveBundle(asOf);
            return ToResult(result);
        });

        group.MapGet("/{id:guid}", (
            Guid id,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service) =>
        {
            var actor = auth.Resolve(request);
            var result = service.Get(actor, id);
            return ToResult(result);
        });

        group.MapGet("/{id:guid}/versions", (
            Guid id,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service) =>
        {
            var actor = auth.Resolve(request);
            var result = service.ListVersions(actor, id);
            return ToResult(result);
        });

        group.MapGet("/{id:guid}/history", (
            Guid id,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service) =>
        {
            var actor = auth.Resolve(request);
            var result = service.ListVersions(actor, id);
            return ToResult(result);
        });

        group.MapGet("/dictionaries/{code}", (
            string code,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service) =>
        {
            _ = auth.Resolve(request);
            if (string.IsNullOrWhiteSpace(code))
                return Results.BadRequest(new { code = "CONFIG_001", message = "code 参数必填。" });

            var config = service.GetEffective(ConfigurationDomains.Dictionary, code.Trim());
            DictionaryConfig? dict = null;
            string name = code;
            if (config is null)
                return Results.NotFound(new { code = "CONFIG_MISSING", message = $"未找到字典【{code}】的生效配置。" });

            name = config.Name;
            try
            {
                dict = JsonSerializer.Deserialize<DictionaryConfig>(config.ContentJson, BusinessConfigurationDefaults.JsonOptions);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { code = "CONFIG_INVALID", message = $"字典配置反序列化失败：{ex.Message}" });
            }

            if (dict is null)
                return Results.BadRequest(new { code = "CONFIG_INVALID", message = $"字典【{code}】配置内容为空。" });

            var enabledItems = dict.Items.Where(item => item.IsEnabled).OrderBy(item => item.SortOrder).ToList();
            return Results.Ok(new
            {
                domain = ConfigurationDomains.Dictionary,
                code = code.Trim(),
                name,
                items = enabledItems
            });
        });

        group.MapPost("/activate-scheduled", (
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service,
            IdempotencyService idempotency,
            DemoData data) =>
        {
            var actor = auth.Resolve(request);
            if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
            {
                return Results.Json(new { code = "AUTH_002", message = "无权访问业务参数配置中心。" }, statusCode: StatusCodes.Status403Forbidden);
            }
            return Write(request, actor, idempotency, () =>
            {
                var count = service.ActivateScheduledConfigurations();
                return ServiceResult<object>.Success(new { activatedCount = count });
            }, atomic: false, fingerprintPayload: new { action = "activate-scheduled" });
        });

        group.MapPost("/", (
            CreateBusinessConfigurationRequest body,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service,
            IdempotencyService idempotency) =>
        {
            var actor = auth.Resolve(request);
            return Write(request, actor, idempotency, () => service.CreateDraft(actor, body), created: true, atomic: true, fingerprintPayload: body);
        });

        group.MapPut("/{id:guid}", (
            Guid id,
            UpdateBusinessConfigurationRequest body,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service,
            IdempotencyService idempotency) =>
        {
            var actor = auth.Resolve(request);
            return Write(request, actor, idempotency, () => service.UpdateDraft(actor, id, body), atomic: true, fingerprintPayload: new { id, body });
        });

        group.MapPost("/{id:guid}/versions", (
            Guid id,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service,
            IdempotencyService idempotency) =>
        {
            var actor = auth.Resolve(request);
            return Write(request, actor, idempotency, () => service.CreateNewVersion(actor, id), created: true, atomic: true, fingerprintPayload: new { id });
        });

        group.MapPost("/{id:guid}/branch", (
            Guid id,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service,
            IdempotencyService idempotency) =>
        {
            var actor = auth.Resolve(request);
            return Write(request, actor, idempotency, () => service.CreateNewVersion(actor, id), created: true, atomic: true, fingerprintPayload: new { id });
        });

        group.MapPost("/{id:guid}/publish", (
            Guid id,
            PublishBusinessConfigurationRequest? body,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service,
            IdempotencyService idempotency) =>
        {
            var actor = auth.Resolve(request);
            return Write(request, actor, idempotency, () => service.Publish(actor, id, body), atomic: true, fingerprintPayload: new { id, body });
        });

        group.MapPost("/{id:guid}/retire", (
            Guid id,
            RetireBusinessConfigurationRequest? body,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service,
            IdempotencyService idempotency) =>
        {
            var actor = auth.Resolve(request);
            return Write(request, actor, idempotency, () => service.Retire(actor, id, body), atomic: true, fingerprintPayload: new { id, body });
        });

        group.MapDelete("/{id:guid}", (
            Guid id,
            HttpRequest request,
            DemoAuthService auth,
            BusinessConfigurationService service,
            IdempotencyService idempotency) =>
        {
            var actor = auth.Resolve(request);
            return Write(request, actor, idempotency, () => service.Delete(actor, id), atomic: true, fingerprintPayload: new { id });
        });

        return app;
    }

    private static IResult ToResult<T>(ServiceResult<T> result, int successStatusCode = StatusCodes.Status200OK)
    {
        if (!result.IsSuccess)
        {
            var statusCode = result.Code switch
            {
                "AUTH_002" => StatusCodes.Status403Forbidden,
                "DATA_001" => StatusCodes.Status404NotFound,
                "CONFLICT_001" or "CONCURRENCY_001" or "CONFIG_002" or "CONFIG_003" or "CONFIG_004" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(new { code = result.Code, message = result.Error }, statusCode: statusCode);
        }
        return Results.Json(result.Value, statusCode: successStatusCode);
    }

    private static IResult Write<T>(
        HttpRequest request,
        Employee actor,
        IdempotencyService idempotency,
        Func<ServiceResult<T>> action,
        bool created = false,
        bool atomic = false,
        object? fingerprintPayload = null)
    {
        var key = request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            return Results.BadRequest(new { code = "IDEMPOTENCY_001", message = "写操作必须提供 1–128 位 Idempotency-Key。" });

        var route = $"{request.Method}:{request.Path}";
        var requestHash = IdempotencyService.Fingerprint(fingerprintPayload);
        using var lease = idempotency.Acquire(actor.Id, route, key);
        var lookup = idempotency.Lookup(actor.Id, route, key, requestHash);
        if (lookup.HashConflict)
            return Results.Json(new { code = "IDEMPOTENCY_002", message = "同一 Idempotency-Key 不能用于不同的请求内容。" }, statusCode: StatusCodes.Status409Conflict);
        if (lookup.Record is not null)
            return Results.Content(lookup.Record.ResponseJson, "application/json", statusCode: lookup.Record.StatusCode);

        IDbContextTransaction? transaction = null;
        try
        {
            if (atomic) transaction = idempotency.BeginTransaction();
            var result = action();
            if (!result.IsSuccess)
            {
                transaction?.Rollback();
                if (transaction is not null) idempotency.ClearTrackedChanges();
                try
                {
                    idempotency.LogAudit(actor.Id, "CONFIG_OPERATION_FAILED", "BusinessConfiguration", route, $"配置操作失败【{route}】：[{result.Code}] {result.Error}");
                }
                catch { }
                var failureStatus = result.Code switch
                {
                    "AUTH_002" => StatusCodes.Status403Forbidden,
                    "DATA_001" or "CONFIG_MISSING" => StatusCodes.Status404NotFound,
                    "CONFLICT_001" or "CONCURRENCY_001" or "CONFIG_002" or "CONFIG_003" or "CONFIG_004" => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest
                };
                return Results.Json(new { code = result.Code, message = result.Error }, statusCode: failureStatus);
            }

            var statusCode = created ? StatusCodes.Status201Created : StatusCodes.Status200OK;
            var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            idempotency.Store(actor.Id, route, key, statusCode, json, requestHash);
            transaction?.Commit();
            return Results.Content(json, "application/json", statusCode: statusCode);
        }
        catch
        {
            transaction?.Rollback();
            if (transaction is not null) idempotency.ClearTrackedChanges();
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }
}
