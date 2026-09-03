using Oa.Api.Domain;
using Oa.Api.Persistence;
using Oa.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddKeyPerFile(builder.Configuration["Configuration:KeyPerFileDirectory"] ?? "/run/secrets", optional: true, reloadOnChange: false);
ProductionConfigurationPolicy.Validate(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddScoped<DemoData>();
builder.Services.AddSingleton<DemoAuthService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<LoginAttemptLimiter>();
builder.Services.AddSingleton<MfaSecretProtector>();
builder.Services.AddScoped<MultiFactorAuthenticationService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<IdentityAdministrationService>();
builder.Services.AddScoped<DepartmentAdministrationService>();
builder.Services.AddScoped<PositionAdministrationService>();
builder.Services.AddScoped<AnnouncementService>();
builder.Services.AddScoped<PersonnelService>();
builder.Services.AddScoped<PersonnelCaseService>();
builder.Services.AddHostedService<PersonnelCaseAlertWorker>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<EmploymentContractService>();
builder.Services.AddHostedService<ContractAlertWorker>();
builder.Services.AddScoped<WorkCalendarService>();
builder.Services.AddScoped<IWorkCalendar>(serviceProvider => serviceProvider.GetRequiredService<WorkCalendarService>());
builder.Services.AddScoped<LeaveService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<TravelService>();
builder.Services.AddScoped<PurchaseService>();
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddScoped<NotificationService>();
var fileScanningMode = FileScanningPolicy.ValidateMode(builder.Configuration["FileScanning:Mode"], builder.Environment.IsDevelopment());
builder.Services.AddSingleton<IFileMalwareScanner>(serviceProvider =>
    fileScanningMode.Equals("ClamAv", StringComparison.OrdinalIgnoreCase)
        ? new ClamAvFileMalwareScanner(serviceProvider.GetRequiredService<IConfiguration>())
        : new DisabledFileMalwareScanner());
builder.Services.AddScoped<FileService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ProcessDefinitionService>();
builder.Services.AddScoped<IProcessRouter>(provider => provider.GetRequiredService<ProcessDefinitionService>());
builder.Services.AddScoped<DelegationService>();
builder.Services.AddScoped<FlowInstanceService>();
builder.Services.AddScoped<FlowCopyService>();
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 21 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<LeaveType>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<LeavePeriod>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<LeaveStatus>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<ExpenseStatus>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<TravelStatus>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<PurchaseStatus>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter<FlowTaskStatus>());
});
if (builder.Configuration.GetValue<bool>("Persistence:UsePostgreSql"))
    builder.Services.AddDbContext<OaDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("OaDatabase")));
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://127.0.0.1:5173", "http://localhost:5173"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.Configure<ForwardedHeadersOptions>(options => ReverseProxyPolicy.Configure(options, builder.Configuration));

var app = builder.Build();
app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(handler => handler.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { code = "SYSTEM_001", message = "系统暂时不可用，请稍后重试。" });
    }));
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseCors();
app.UseMiddleware<BearerAuthenticationMiddleware>();

if (app.Configuration.GetValue<bool>("Persistence:UsePostgreSql"))
{
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<OaDbContext>();
    database.Database.Migrate();
    if (app.Environment.IsDevelopment())
    {
        if (app.Configuration.GetValue<bool>("DemoFeatures:AllowDataGeneration"))
            IdentitySeeder.EnsureDemoSeeded(database);
    }
    else
    {
        IdentitySeeder.EnsureProductionReady(database, app.Configuration);
    }
}

Employee Actor(HttpRequest request, DemoAuthService auth) => auth.Resolve(request);
SessionClient Client(HttpRequest request) => new(request.Headers.UserAgent.FirstOrDefault(), request.HttpContext.Connection.RemoteIpAddress?.ToString());
const string RefreshCookie = "oa_refresh_token";
void SetRefreshCookie(HttpResponse response, AuthenticationGrant grant, bool secure)
{
    if (string.IsNullOrWhiteSpace(grant.RefreshToken) || grant.RefreshExpiresAt is null) return;
    response.Cookies.Append(RefreshCookie, grant.RefreshToken, new CookieOptions { HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Lax, Path = "/api/v1/auth", Expires = grant.RefreshExpiresAt });
}
void ClearRefreshCookie(HttpResponse response, bool secure) => response.Cookies.Delete(RefreshCookie, new CookieOptions { HttpOnly = true, Secure = secure, SameSite = SameSiteMode.Lax, Path = "/api/v1/auth" });

IResult Write<T>(HttpRequest request, Employee actor, IdempotencyService idempotency, Func<ServiceResult<T>> action, bool created = false, bool atomic = false, object? fingerprintPayload = null)
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
    if (lookup.Record is not null) return Results.Content(lookup.Record.ResponseJson, "application/json", statusCode: lookup.Record.StatusCode);

    IDbContextTransaction? transaction = null;
    try
    {
        if (atomic) transaction = idempotency.BeginTransaction();
        var result = action();
        if (!result.IsSuccess)
        {
            transaction?.Rollback();
            if (transaction is not null) idempotency.ClearTrackedChanges();
            var failureStatus = result.Code switch
            {
                "AUTH_002" => StatusCodes.Status403Forbidden,
                "DATA_001" => StatusCodes.Status404NotFound,
                "CONFLICT_001" or "CONCURRENCY_001" or "CONTRACT_002" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(new { code = result.Code, message = result.Error }, statusCode: failureStatus);
        }
        var statusCode = created ? StatusCodes.Status201Created : StatusCodes.Status200OK;
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        serializerOptions.Converters.Add(new JsonStringEnumConverter<LeaveType>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<LeavePeriod>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<LeaveStatus>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<ExpenseStatus>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<TravelStatus>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<PurchaseStatus>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<FlowTaskStatus>());
        var json = JsonSerializer.Serialize(result.Value, serializerOptions);
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
    finally { transaction?.Dispose(); }
}

async Task<IResult> WriteAsync<T>(HttpRequest request, Employee actor, IdempotencyService idempotency, Func<Task<ServiceResult<T>>> action, bool created = false, bool atomic = false, object? fingerprintPayload = null)
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
    if (lookup.Record is not null) return Results.Content(lookup.Record.ResponseJson, "application/json", statusCode: lookup.Record.StatusCode);

    IDbContextTransaction? transaction = null;
    try
    {
        if (atomic) transaction = idempotency.BeginTransaction();
        var result = await action();
        if (!result.IsSuccess)
        {
            transaction?.Rollback();
            if (transaction is not null) idempotency.ClearTrackedChanges();
            var failureStatus = result.Code switch
            {
                "AUTH_002" => StatusCodes.Status403Forbidden,
                "DATA_001" => StatusCodes.Status404NotFound,
                "CONFLICT_001" or "CONCURRENCY_001" or "CONTRACT_002" => StatusCodes.Status409Conflict,
                "FILE_007" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status400BadRequest
            };
            return Results.Json(new { code = result.Code, message = result.Error }, statusCode: failureStatus);
        }
        var statusCode = created ? StatusCodes.Status201Created : StatusCodes.Status200OK;
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        serializerOptions.Converters.Add(new JsonStringEnumConverter<LeaveType>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<LeavePeriod>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<LeaveStatus>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<ExpenseStatus>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<TravelStatus>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<PurchaseStatus>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<FlowTaskStatus>());
        var json = JsonSerializer.Serialize(result.Value, serializerOptions);
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
    finally { transaction?.Dispose(); }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "oa-api" }));
app.MapGet("/health/ready", async (OaDbContext db, FileService files, CancellationToken cancellationToken) =>
{
    try
    {
        var databaseReady = await db.Database.CanConnectAsync();
        var storageReady = files.IsStorageReady();
        var scanningReady = await files.IsScanningReadyAsync(cancellationToken);
        var scanningStatus = files.IsScanningEnabled ? (scanningReady ? "ok" : "unavailable") : "disabled";
        return databaseReady && storageReady && scanningReady
            ? Results.Ok(new { status = "ready", database = "ok", storage = "ok", fileScanning = scanningStatus, scanner = files.ScanningProvider })
            : Results.Json(new { status = "not_ready", database = databaseReady ? "ok" : "unavailable", storage = storageReady ? "ok" : "unavailable", fileScanning = scanningStatus, scanner = files.ScanningProvider }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch { return Results.Json(new { status = "not_ready", database = "unavailable", storage = "unknown" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
});
app.MapGet("/api/v1/auth/demo-accounts", (DemoData data, IConfiguration configuration) => Results.Ok(
    configuration.GetValue<bool>("DemoFeatures:AllowDataGeneration") ? data.ActiveEmployees.Select(data.ToProfile) : []));
app.MapPost("/api/v1/auth/login", (LoginRequest body, HttpRequest request, HttpResponse response, AuthenticationService service, LoginAttemptLimiter limiter) =>
{
    var attempt = limiter.TryAcquire(body.UserId, request.HttpContext.Connection.RemoteIpAddress?.ToString());
    if (!attempt.IsAllowed)
    {
        response.Headers.RetryAfter = attempt.RetryAfterSeconds.ToString();
        return Results.Json(new { code = "AUTH_003", message = "登录尝试过于频繁，请稍后再试。" }, statusCode: StatusCodes.Status429TooManyRequests);
    }
    var result = service.BeginLogin(body, Client(request));
    if (!result.IsSuccess) return Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status401Unauthorized);
    if (result.Value!.Grant is { } grant)
    {
        SetRefreshCookie(response, grant, request.IsHttps);
        return Results.Ok(new { status = "AUTHENTICATED", session = grant.Session });
    }
    var challenge = result.Value.Challenge!;
    return Results.Ok(new { status = challenge.State, challengeToken = challenge.ChallengeToken, challengeExpiresAt = challenge.ExpiresAt });
});
app.MapPost("/api/v1/auth/password/change-initial", (ChangeInitialPasswordRequest body, HttpRequest request, HttpResponse response, AuthenticationService service) =>
{
    var result = service.ChangeInitialPassword(body, Client(request));
    if (!result.IsSuccess)
    {
        var status = result.Code == "AUTH_006" ? StatusCodes.Status401Unauthorized : StatusCodes.Status400BadRequest;
        return Results.Json(new { code = result.Code, message = result.Error }, statusCode: status);
    }
    if (result.Value!.Grant is { } grant)
    {
        SetRefreshCookie(response, grant, request.IsHttps);
        return Results.Ok(new { status = "AUTHENTICATED", session = grant.Session });
    }
    var challenge = result.Value.Challenge!;
    return Results.Ok(new { status = challenge.State, challengeToken = challenge.ChallengeToken, challengeExpiresAt = challenge.ExpiresAt });
});
app.MapPost("/api/v1/auth/mfa/setup/start", (MfaSetupStartRequest body, AuthenticationService service) =>
{
    var result = service.StartMfaSetup(body);
    return result.IsSuccess
        ? Results.Ok(result.Value)
        : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status401Unauthorized);
});
app.MapPost("/api/v1/auth/mfa/setup/confirm", (MfaSetupConfirmRequest body, HttpRequest request, HttpResponse response, AuthenticationService service) =>
{
    var result = service.ConfirmMfaSetup(body, Client(request));
    if (!result.IsSuccess) return Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status401Unauthorized);
    SetRefreshCookie(response, result.Value!.Grant, request.IsHttps);
    return Results.Ok(new { status = "AUTHENTICATED", session = result.Value.Grant.Session, recoveryCodes = result.Value.RecoveryCodes });
});
app.MapPost("/api/v1/auth/mfa/verify", (MfaVerifyRequest body, HttpRequest request, HttpResponse response, AuthenticationService service) =>
{
    var result = service.VerifyMfa(body, Client(request));
    if (!result.IsSuccess) return Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status401Unauthorized);
    SetRefreshCookie(response, result.Value!, request.IsHttps);
    return Results.Ok(new { status = "AUTHENTICATED", session = result.Value!.Session });
});
app.MapPost("/api/v1/auth/refresh", (HttpRequest request, HttpResponse response, AuthenticationService service) =>
{
    var result = service.Refresh(request.Cookies[RefreshCookie], Client(request));
    if (!result.IsSuccess)
    {
        ClearRefreshCookie(response, request.IsHttps);
        return Results.Json(new { code = result.Code, message = "登录已失效，请重新登录。" }, statusCode: StatusCodes.Status401Unauthorized);
    }
    SetRefreshCookie(response, result.Value!, request.IsHttps);
    return Results.Ok(result.Value!.Session);
});
app.MapPost("/api/v1/auth/logout", (HttpRequest request, HttpResponse response, AuthenticationService service) =>
{
    service.Logout(request.Cookies[RefreshCookie]);
    ClearRefreshCookie(response, request.IsHttps);
    return Results.NoContent();
});
app.MapGet("/api/v1/auth/demo-users", (DemoData data, IConfiguration configuration) => Results.Ok(
    configuration.GetValue<bool>("DemoFeatures:AllowDataGeneration") ? data.ActiveEmployees.Select(data.ToProfile) : []));
app.MapGet("/api/v1/me", (HttpRequest request, DemoAuthService auth, DemoData data) => Results.Ok(data.ToProfile(Actor(request, auth))));
app.MapGet("/api/v1/auth/sessions", (int? page, int? pageSize, HttpRequest request, DemoAuthService auth, AuthenticationService service) =>
{
    var result = service.ListSessions(Actor(request, auth), auth.ResolveSessionId(request), page, pageSize);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { code = result.Code, message = result.Error });
});
app.MapGet("/api/v1/auth/mfa/status", (HttpRequest request, DemoAuthService auth, AuthenticationService service) =>
{
    var result = service.GetMfaStatus(Actor(request, auth));
    return Results.Ok(result.Value);
});
app.MapPost("/api/v1/auth/sessions/{id:guid}/revoke", (Guid id, HttpRequest request, DemoAuthService auth, AuthenticationService service, IdempotencyService idempotency) =>
{
    var actor = Actor(request, auth);
    return Write(request, actor, idempotency, () => service.RevokeSession(actor, auth.ResolveSessionId(request), id));
});
app.MapPost("/api/v1/auth/sessions/revoke-others", (HttpRequest request, DemoAuthService auth, AuthenticationService service, IdempotencyService idempotency) =>
{
    var actor = Actor(request, auth);
    return Write(request, actor, idempotency, () => service.RevokeOtherSessions(actor, auth.ResolveSessionId(request)));
});
app.MapGet("/api/v1/org/departments", (HttpRequest request, DemoAuthService auth, DemoData data) => { _ = Actor(request, auth); return Results.Ok(data.Departments); });
app.MapGet("/api/v1/org/employees", (HttpRequest request, DemoAuthService auth, DemoData data) => { _ = Actor(request, auth); return Results.Ok(data.DirectoryEmployees); });
app.MapGet("/api/v1/org/positions", (HttpRequest request, DemoAuthService auth, PositionAdministrationService service) => { _ = Actor(request, auth); return Results.Ok(service.ListOptions()); });
app.MapGet("/api/v1/admin/departments", (HttpRequest request, DemoAuthService auth, DepartmentAdministrationService service) =>
{
    var result = service.List(Actor(request, auth));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
});
app.MapPost("/api/v1/admin/departments", (CreateDepartmentRequest body, HttpRequest request, DemoAuthService auth, DepartmentAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Create(actor, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPut("/api/v1/admin/departments/{id}", (string id, UpdateDepartmentRequest body, HttpRequest request, DemoAuthService auth, DepartmentAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapDelete("/api/v1/admin/departments/{id}", (string id, int version, HttpRequest request, DemoAuthService auth, DepartmentAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Delete(actor, id, version), atomic: true, fingerprintPayload: new { id, version }); });
app.MapGet("/api/v1/admin/positions", (HttpRequest request, DemoAuthService auth, PositionAdministrationService service) =>
{
    var result = service.List(Actor(request, auth));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
});
app.MapPost("/api/v1/admin/positions", (CreatePositionRequest body, HttpRequest request, DemoAuthService auth, PositionAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Create(actor, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPut("/api/v1/admin/positions/{id}", (string id, UpdatePositionRequest body, HttpRequest request, DemoAuthService auth, PositionAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapDelete("/api/v1/admin/positions/{id}", (string id, int version, HttpRequest request, DemoAuthService auth, PositionAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Delete(actor, id, version), atomic: true, fingerprintPayload: new { id, version }); });
app.MapGet("/api/v1/admin/roles", (HttpRequest request, DemoAuthService auth, IdentityAdministrationService service) =>
{
    var result = service.ListRoles(Actor(request, auth));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
});
app.MapGet("/api/v1/admin/permissions", (HttpRequest request, DemoAuthService auth, IdentityAdministrationService service) =>
{
    var result = service.ListPermissions(Actor(request, auth));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
});
app.MapPost("/api/v1/admin/roles", (CreateRoleRequest body, HttpRequest request, DemoAuthService auth, IdentityAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.CreateRole(actor, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPut("/api/v1/admin/roles", (UpdateRoleRequest body, HttpRequest request, DemoAuthService auth, IdentityAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.UpdateRole(actor, body), atomic: true, fingerprintPayload: body); });
app.MapDelete("/api/v1/admin/roles", (string code, HttpRequest request, DemoAuthService auth, IdentityAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.DeleteRole(actor, code), atomic: true, fingerprintPayload: new { code }); });
app.MapGet("/api/v1/admin/users", (string? keyword, string? status, string? departmentId, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, IdentityAdministrationService service) =>
{
    var result = service.List(Actor(request, auth), keyword, status, departmentId, page, pageSize);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
});
app.MapPost("/api/v1/admin/users", (CreateManagedUserRequest body, HttpRequest request, DemoAuthService auth, IdentityAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Create(actor, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPut("/api/v1/admin/users/{id}", (string id, UpdateManagedUserRequest body, HttpRequest request, DemoAuthService auth, IdentityAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/admin/users/{id}/reset-password", (string id, ResetUserPasswordRequest body, HttpRequest request, DemoAuthService auth, IdentityAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.ResetPassword(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/admin/users/{id}/reset-mfa", (string id, HttpRequest request, DemoAuthService auth, IdentityAdministrationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.ResetMfa(actor, id), atomic: true, fingerprintPayload: new { id }); });
app.MapGet("/api/v1/hr/employees", (string? keyword, string? departmentId, string? personnelStatus, string? employmentType, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, PersonnelService service) =>
{
    var result = service.List(Actor(request, auth), keyword, departmentId, personnelStatus, employmentType, page, pageSize);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
});
app.MapGet("/api/v1/hr/employees/export", (string? keyword, string? departmentId, string? personnelStatus, string? employmentType, HttpRequest request, DemoAuthService auth, PersonnelService service) =>
{
    var result = service.Export(Actor(request, auth), keyword, departmentId, personnelStatus, employmentType);
    if (!result.IsSuccess)
        return result.Code == "AUTH_002"
            ? Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden)
            : Results.BadRequest(new { code = result.Code, message = result.Error });
    return Results.File(result.Value!.Content, "text/csv; charset=utf-8", result.Value.FileName);
});
app.MapGet("/api/v1/hr/employees/me", (HttpRequest request, DemoAuthService auth, PersonnelService service) =>
{
    var actor = Actor(request, auth);
    var result = service.Get(actor, actor.Id);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: result.Code == "AUTH_002" ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound);
});
app.MapGet("/api/v1/hr/employees/{id}", (string id, HttpRequest request, DemoAuthService auth, PersonnelService service) =>
{
    var result = service.Get(Actor(request, auth), id);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: result.Code == "AUTH_002" ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound);
});
app.MapPut("/api/v1/hr/employees/{id}", (string id, UpdatePersonnelProfileRequest body, HttpRequest request, DemoAuthService auth, PersonnelService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapGet("/api/v1/hr/personnel-cases", (string? keyword, string? userId, string? type, string? status, bool? assignedToMe, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, PersonnelCaseService service) =>
{
    var result = service.List(Actor(request, auth), keyword, userId, type, status, assignedToMe, page, pageSize);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(new { code = result.Code, message = result.Error });
});
app.MapGet("/api/v1/hr/personnel-cases/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, PersonnelCaseService service) =>
{
    var result = service.Get(Actor(request, auth), id);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: result.Code == "AUTH_002" ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound);
});
app.MapPost("/api/v1/hr/personnel-cases", (CreatePersonnelCaseRequest body, HttpRequest request, DemoAuthService auth, PersonnelCaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Create(actor, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPut("/api/v1/hr/personnel-cases/{id:guid}/tasks/{taskId:guid}", (Guid id, Guid taskId, UpdatePersonnelCaseTaskRequest body, HttpRequest request, DemoAuthService auth, PersonnelCaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.UpdateTask(actor, id, taskId, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/hr/personnel-cases/{id:guid}/complete", (Guid id, CompletePersonnelCaseRequest body, HttpRequest request, DemoAuthService auth, PersonnelCaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Complete(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/hr/personnel-cases/{id:guid}/cancel", (Guid id, CancelPersonnelCaseRequest body, HttpRequest request, DemoAuthService auth, PersonnelCaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Cancel(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapGet("/api/v1/hr/contracts", (string? keyword, string? departmentId, string? contractType, string? status, int? expiryDays, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, EmploymentContractService service) =>
    Results.Ok(service.List(Actor(request, auth), keyword, departmentId, contractType, status, expiryDays, page, pageSize).Value));
app.MapGet("/api/v1/hr/contracts/alerts/summary", (HttpRequest request, DemoAuthService auth, EmploymentContractService service) =>
    Results.Ok(service.AlertSummary(Actor(request, auth))));
app.MapGet("/api/v1/hr/contracts/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, EmploymentContractService service) =>
{
    var result = service.Get(Actor(request, auth), id);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: result.Code == "AUTH_002" ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound);
});
app.MapPost("/api/v1/hr/contracts", (SaveEmploymentContractRequest body, HttpRequest request, DemoAuthService auth, EmploymentContractService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Create(actor, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPut("/api/v1/hr/contracts/{id:guid}", (Guid id, SaveEmploymentContractRequest body, HttpRequest request, DemoAuthService auth, EmploymentContractService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/hr/contracts/{id:guid}/activate", (Guid id, ActivateEmploymentContractRequest body, HttpRequest request, DemoAuthService auth, EmploymentContractService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Activate(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/hr/contracts/{id:guid}/renew", (Guid id, SaveEmploymentContractRequest body, HttpRequest request, DemoAuthService auth, EmploymentContractService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Renew(actor, id, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/hr/contracts/{id:guid}/terminate", (Guid id, TerminateEmploymentContractRequest body, HttpRequest request, DemoAuthService auth, EmploymentContractService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Terminate(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/hr/contracts/{id:guid}/alerts/acknowledge", (Guid id, AcknowledgeContractAlertRequest body, HttpRequest request, DemoAuthService auth, EmploymentContractService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.AcknowledgeAlert(actor, id, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/hr/contracts/demo-data", (HttpRequest request, DemoAuthService auth, EmploymentContractService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.GenerateDemoData(actor), atomic: true, fingerprintPayload: new { operation = "contract-demo-data" }); });
app.MapGet("/api/v1/attendance/shifts", (HttpRequest request, DemoAuthService auth, AttendanceService service) => Results.Ok(service.ListShifts(Actor(request, auth))));
app.MapPut("/api/v1/attendance/shifts/{id:guid}", (Guid id, SaveAttendanceShiftRequest body, HttpRequest request, DemoAuthService auth, AttendanceService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.UpdateShift(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapGet("/api/v1/attendance/records", (string? keyword, string? userId, string? departmentId, string? status, DateOnly? month, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, AttendanceService service) =>
{
    var selectedMonth = month ?? BusinessTime.ChinaMonth();
    return Results.Ok(Paging.Create(service.List(Actor(request, auth), new AttendanceListQuery(keyword, userId, departmentId, status, selectedMonth)), page, pageSize));
});
app.MapGet("/api/v1/attendance/records/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, AttendanceService service) =>
{
    var result = service.Get(Actor(request, auth), id);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: result.Code == "AUTH_002" ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound);
});
app.MapPost("/api/v1/attendance/import", (ImportAttendanceRequest body, HttpRequest request, DemoAuthService auth, AttendanceService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Import(actor, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/attendance/demo-data/{month}", (DateOnly month, HttpRequest request, DemoAuthService auth, AttendanceService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.GenerateDemoData(actor, month), atomic: true, fingerprintPayload: new { month }); });
app.MapPost("/api/v1/attendance/records/{id:guid}/appeals", (Guid id, CreateAttendanceAppealRequest body, HttpRequest request, DemoAuthService auth, AttendanceService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.SubmitAppeal(actor, id, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/attendance/appeals/{id:guid}/review", (Guid id, ReviewAttendanceAppealRequest body, HttpRequest request, DemoAuthService auth, AttendanceService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.ReviewAppeal(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapGet("/api/v1/attendance/monthly-summary", (DateOnly? month, string? userId, HttpRequest request, DemoAuthService auth, AttendanceService service) =>
{
    var result = service.MonthlySummary(Actor(request, auth), month ?? BusinessTime.ChinaMonth(), userId);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status409Conflict);
});
app.MapGet("/api/v1/attendance/month-lock", (DateOnly? month, HttpRequest request, DemoAuthService auth, AttendanceService service) => Results.Ok(service.GetMonthLock(Actor(request, auth), month ?? BusinessTime.ChinaMonth())));
app.MapGet("/api/v1/attendance/month-locks/{month}/snapshot", (DateOnly month, HttpRequest request, DemoAuthService auth, AttendanceService service) =>
{
    var result = service.ExportMonthSnapshot(Actor(request, auth), month);
    if (!result.IsSuccess)
        return result.Code == "AUTH_002"
            ? Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden)
            : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status409Conflict);
    return Results.File(result.Value!.Content, "text/csv; charset=utf-8", result.Value.FileName);
});
app.MapPost("/api/v1/attendance/month-locks/{month}/lock", (DateOnly month, ChangeAttendanceMonthLockRequest body, HttpRequest request, DemoAuthService auth, AttendanceService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.LockMonth(actor, month, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/attendance/month-locks/{month}/unlock", (DateOnly month, ChangeAttendanceMonthLockRequest body, HttpRequest request, DemoAuthService auth, AttendanceService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.UnlockMonth(actor, month, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapGet("/api/v1/announcements", (int? page, int? pageSize, HttpRequest request, DemoAuthService auth, AnnouncementService service) =>
{
    var result = service.ListPublished(Actor(request, auth), page, pageSize);
    return Results.Ok(result.Value);
});
app.MapGet("/api/v1/announcements/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, AnnouncementService service) =>
{
    var result = service.Get(Actor(request, auth), id);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(new { code = result.Code, message = result.Error });
});
app.MapPost("/api/v1/announcements/{id:guid}/read", (Guid id, HttpRequest request, DemoAuthService auth, AnnouncementService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.MarkRead(actor, id)); });
app.MapGet("/api/v1/admin/announcements", (string? status, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, AnnouncementService service) =>
{
    var result = service.ListAdmin(Actor(request, auth), status, page, pageSize);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
});
app.MapPost("/api/v1/admin/announcements", (SaveAnnouncementRequest body, HttpRequest request, DemoAuthService auth, AnnouncementService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Create(actor, body), true); });
app.MapPut("/api/v1/admin/announcements/{id:guid}", (Guid id, SaveAnnouncementRequest body, HttpRequest request, DemoAuthService auth, AnnouncementService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, body)); });
app.MapPost("/api/v1/admin/announcements/{id:guid}/publish", (Guid id, int version, HttpRequest request, DemoAuthService auth, AnnouncementService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Publish(actor, id, version)); });
app.MapPost("/api/v1/admin/announcements/{id:guid}/withdraw", (Guid id, int version, HttpRequest request, DemoAuthService auth, AnnouncementService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Withdraw(actor, id, version)); });
app.MapGet("/api/v1/audit-logs", (string? keyword, string? actorId, string? resourceType, DateOnly? startDate, DateOnly? endDate, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, AuditService service) =>
{
    var result = service.List(Actor(request, auth), new AuditLogQuery(keyword, actorId, resourceType, startDate, endDate, page, pageSize));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
});
app.MapGet("/api/v1/process/definitions", (HttpRequest request, DemoAuthService auth, ProcessDefinitionService service) =>
{
    var result = service.List(Actor(request, auth));
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: StatusCodes.Status403Forbidden);
});
app.MapPost("/api/v1/process/definitions", (CreateProcessDefinitionRequest body, HttpRequest request, DemoAuthService auth, ProcessDefinitionService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Create(actor, body), true); });
app.MapPut("/api/v1/process/definitions/{id:guid}", (Guid id, UpdateProcessDefinitionRequest body, HttpRequest request, DemoAuthService auth, ProcessDefinitionService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, body)); });
app.MapPost("/api/v1/process/definitions/{id:guid}/clone", (Guid id, HttpRequest request, DemoAuthService auth, ProcessDefinitionService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Clone(actor, id), true); });
app.MapPost("/api/v1/process/definitions/{id:guid}/publish", (Guid id, HttpRequest request, DemoAuthService auth, ProcessDefinitionService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Publish(actor, id)); });
app.MapGet("/api/v1/flow/delegations/my", (HttpRequest request, DemoAuthService auth, DelegationService service) => Results.Ok(service.ListMine(Actor(request, auth))));
app.MapPost("/api/v1/flow/delegations", (CreateFlowDelegationRequest body, HttpRequest request, DemoAuthService auth, DelegationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Create(actor, body), true); });
app.MapPost("/api/v1/flow/delegations/{id:guid}/cancel", (Guid id, HttpRequest request, DemoAuthService auth, DelegationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Cancel(actor, id)); });
app.MapGet("/api/v1/flow/instances/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, FlowInstanceService flows, LeaveService leave, ExpenseService expense, TravelService travel, PurchaseService purchase) =>
{
    var actor = Actor(request, auth);
    var result = flows.Get(id);
    if (!result.IsSuccess) return Results.NotFound(new { code = result.Code, message = result.Error });
    var permitted = result.Value!.BusinessType switch
    {
        "Leave" => leave.Get(actor, result.Value.BusinessId).IsSuccess,
        "Expense" => expense.Get(actor, result.Value.BusinessId).IsSuccess,
        "Travel" => travel.Get(actor, result.Value.BusinessId).IsSuccess,
        "Purchase" => purchase.Get(actor, result.Value.BusinessId).IsSuccess,
        _ => false
    };
    return permitted ? Results.Ok(result.Value) : Results.Json(new { code = "AUTH_002", message = "无权查看该流程实例。" }, statusCode: StatusCodes.Status403Forbidden);
});
app.MapGet("/api/v1/flow/copies/my", (int? page, int? pageSize, HttpRequest request, DemoAuthService auth, FlowCopyService service) => Results.Ok(Paging.Create(service.ListMine(Actor(request, auth)), page, pageSize)));
app.MapPost("/api/v1/flow/copies/{id:guid}/read", (Guid id, HttpRequest request, DemoAuthService auth, FlowCopyService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.MarkRead(actor, id)); });
app.MapGet("/api/v1/notifications/my", (HttpRequest request, DemoAuthService auth, NotificationService service) => Results.Ok(service.List(Actor(request, auth))));
app.MapPost("/api/v1/notifications/{id:guid}/read", (Guid id, HttpRequest request, DemoAuthService auth, NotificationService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.MarkRead(actor, id)); });
app.MapPost("/api/v1/files", async (IFormFile? file, HttpRequest request, DemoAuthService auth, FileService service, IdempotencyService idempotency, CancellationToken cancellationToken) =>
{
    var actor = Actor(request, auth);
    if (file is null) return Results.BadRequest(new { code = "FILE_001", message = "请选择要上传的文件。" });
    return await WriteAsync(request, actor, idempotency, () => service.UploadAsync(actor, file, cancellationToken), true);
}).DisableAntiforgery();
app.MapGet("/api/v1/files/{id:guid}", (Guid id, string resourceType, Guid resourceId, HttpRequest request, DemoAuthService auth, LeaveService leave, ExpenseService expense, TravelService travel, PurchaseService purchase, AttendanceService attendance, EmploymentContractService contracts, FileService files) =>
{
    var actor = Actor(request, auth);
    var permitted = resourceType.ToLowerInvariant() switch
    {
        "leave" => leave.Get(actor, resourceId).Value?.Attachments.Contains(id.ToString()) == true,
        "expense" => expense.Get(actor, resourceId).Value is { } claim && (claim.Items.Any(item => item.Attachments?.Contains(id.ToString()) == true) || claim.Payment?.ProofFile == id.ToString()),
        "travel" => travel.Get(actor, resourceId).Value?.Attachments.Contains(id.ToString()) == true,
        "purchase" => purchase.Get(actor, resourceId).Value is { } requisition && (requisition.Attachments.Contains(id.ToString()) || requisition.Order?.Attachments.Contains(id.ToString()) == true || requisition.Receipt?.Attachments.Contains(id.ToString()) == true),
        "attendance" => attendance.Get(actor, resourceId).Value?.Appeals.Any(appeal => appeal.Attachments.Contains(id.ToString())) == true,
        "contract" => contracts.Get(actor, resourceId).Value?.Attachments.Contains(id.ToString()) == true,
        _ => false
    };
    if (!permitted) return Results.NotFound(new { code = "DATA_001", message = "文件不存在或无查看权限。" });
    var opened = files.Open(actor, id);
    return opened.IsSuccess ? Results.File(opened.Value!.Content, opened.Value.Descriptor.ContentType, opened.Value.Descriptor.Name, enableRangeProcessing: true) : Results.NotFound(new { code = opened.Code, message = opened.Error });
});
app.MapGet("/api/v1/demo/summary", (HttpRequest request, DemoAuthService auth, DemoData data, LeaveService leave, ExpenseService expense, TravelService travel, PurchaseService purchase, FlowCopyService copies, EmploymentContractService contracts) =>
{
    var actor = Actor(request, auth);
    var contractAlerts = contracts.AlertSummary(actor);
    return Results.Ok(new { tenant = data.Tenant, currentUser = data.ToProfile(actor), pendingTaskCount = leave.GetPendingTasks(actor).Count + expense.GetPendingTasks(actor).Count + travel.GetPendingTasks(actor).Count + purchase.GetPendingTasks(actor).Count, pendingReadCount = copies.ListMine(actor).Count(item => item.ReadAt is null), contractRiskCount = contractAlerts.AtRiskContracts, leaveBalance = leave.GetBalance(actor) });
});

app.MapGet("/api/v1/leave-requests", (string? keyword, int? status, string? applicantId, DateOnly? startDate, DateOnly? endDate, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, LeaveService service) => Results.Ok(Paging.Create(service.List(Actor(request, auth), new DocumentListQuery(keyword, status, applicantId, startDate, endDate)), page, pageSize)));
app.MapGet("/api/v1/leave-requests/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, LeaveService service) => { var result = service.Get(Actor(request, auth), id); return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(new { code = result.Code, message = result.Error }); });
app.MapGet("/api/v1/leave-balances/me", (LeaveType? type, int? year, HttpRequest request, DemoAuthService auth, LeaveService service) =>
{
    if (type is not null && type is not (LeaveType.Annual or LeaveType.CompTime) || year is < 2000 or > 2100)
        return Results.BadRequest(new { code = "LEAVE_004", message = "仅支持查询 2000–2100 年的年假或调休余额。" });
    return Results.Ok(service.GetBalance(Actor(request, auth), type ?? LeaveType.Annual, year));
});
app.MapGet("/api/v1/hr/leave-balances/{userId}", (string userId, LeaveType? type, int? year, HttpRequest request, DemoAuthService auth, LeaveService service) =>
{
    var result = service.GetBalance(Actor(request, auth), userId, type ?? LeaveType.Annual, year ?? BusinessTime.ChinaToday().Year);
    return result.IsSuccess ? Results.Ok(result.Value) : result.Code == "AUTH_002" ? Results.Json(new { code = result.Code, message = result.Error }, statusCode: 403) : Results.BadRequest(new { code = result.Code, message = result.Error });
});
app.MapPut("/api/v1/hr/leave-balances/{userId}", (string userId, AdjustLeaveBalanceRequest body, HttpRequest request, DemoAuthService auth, LeaveService service, IdempotencyService idempotency) =>
{
    var actor = Actor(request, auth);
    return Write(request, actor, idempotency, () => service.AdjustBalance(actor, userId, body));
});
app.MapGet("/api/v1/work-calendar/{year:int}", (int year, WorkCalendarService service) => Results.Ok(service.List(year)));
app.MapPut("/api/v1/work-calendar/{date}", (DateOnly date, UpdateWorkCalendarRequest body, HttpRequest request, DemoAuthService auth, WorkCalendarService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, date, body)); });
app.MapPost("/api/v1/leave-requests", (CreateLeaveRequest body, HttpRequest request, DemoAuthService auth, LeaveService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.CreateDraft(actor, body), true); });
app.MapPatch("/api/v1/leave-requests/{id:guid}", (Guid id, int version, CreateLeaveRequest body, HttpRequest request, DemoAuthService auth, LeaveService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, version, body)); });
app.MapDelete("/api/v1/leave-requests/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, LeaveService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Delete(actor, id)); });
app.MapPost("/api/v1/leave-requests/{id:guid}/submit", (Guid id, HttpRequest request, DemoAuthService auth, LeaveService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Submit(actor, id)); });
app.MapPost("/api/v1/leave-requests/{id:guid}/withdraw", (Guid id, HttpRequest request, DemoAuthService auth, LeaveService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Withdraw(actor, id)); });

app.MapGet("/api/v1/flow/tasks/my", (HttpRequest request, DemoAuthService auth, LeaveService service) => Results.Ok(service.GetPendingTasks(Actor(request, auth))));
app.MapGet("/api/v1/flow/tasks/done", (HttpRequest request, DemoAuthService auth, LeaveService service) => Results.Ok(service.GetProcessedTasks(Actor(request, auth))));
app.MapPost("/api/v1/flow/tasks/{id:guid}/approve", (Guid id, ApproveTaskRequest body, HttpRequest request, DemoAuthService auth, LeaveService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Approve(actor, id, body.Comment)); });
app.MapPost("/api/v1/flow/tasks/{id:guid}/reject", (Guid id, RejectTaskRequest body, HttpRequest request, DemoAuthService auth, LeaveService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Reject(actor, id, body.Comment)); });
app.MapPost("/api/v1/flow/tasks/{id:guid}/transfer", (Guid id, TransferTaskRequest body, HttpRequest request, DemoAuthService auth, LeaveService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Transfer(actor, id, body)); });

app.MapGet("/api/v1/expense-claims", (string? keyword, int? status, string? applicantId, DateOnly? startDate, DateOnly? endDate, decimal? minAmount, decimal? maxAmount, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, ExpenseService service) => Results.Ok(Paging.Create(service.List(Actor(request, auth), new DocumentListQuery(keyword, status, applicantId, startDate, endDate, minAmount, maxAmount)), page, pageSize)));
app.MapGet("/api/v1/expense-claims/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, ExpenseService service) => { var result = service.Get(Actor(request, auth), id); return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(new { code = result.Code, message = result.Error }); });
app.MapPost("/api/v1/expense-claims", (CreateExpenseClaim body, HttpRequest request, DemoAuthService auth, ExpenseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.CreateDraft(actor, body), true); });
app.MapPatch("/api/v1/expense-claims/{id:guid}", (Guid id, int version, CreateExpenseClaim body, HttpRequest request, DemoAuthService auth, ExpenseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, version, body)); });
app.MapDelete("/api/v1/expense-claims/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, ExpenseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Delete(actor, id)); });
app.MapPost("/api/v1/expense-claims/{id:guid}/submit", (Guid id, HttpRequest request, DemoAuthService auth, ExpenseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Submit(actor, id)); });
app.MapPost("/api/v1/expense-claims/{id:guid}/withdraw", (Guid id, HttpRequest request, DemoAuthService auth, ExpenseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Withdraw(actor, id)); });
app.MapPost("/api/v1/expense-tasks/{id:guid}/approve", (Guid id, ApproveTaskRequest body, HttpRequest request, DemoAuthService auth, ExpenseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Approve(actor, id, body.Comment)); });
app.MapGet("/api/v1/expense-tasks/my", (HttpRequest request, DemoAuthService auth, ExpenseService service) => Results.Ok(service.GetPendingTasks(Actor(request, auth))));
app.MapGet("/api/v1/expense-tasks/done", (HttpRequest request, DemoAuthService auth, ExpenseService service) => Results.Ok(service.GetProcessedTasks(Actor(request, auth))));
app.MapPost("/api/v1/expense-tasks/{id:guid}/reject", (Guid id, RejectTaskRequest body, HttpRequest request, DemoAuthService auth, ExpenseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Reject(actor, id, body.Comment)); });
app.MapPost("/api/v1/expense-tasks/{id:guid}/transfer", (Guid id, TransferTaskRequest body, HttpRequest request, DemoAuthService auth, ExpenseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Transfer(actor, id, body)); });
app.MapPost("/api/v1/expense-claims/{id:guid}/payment", (Guid id, RegisterPaymentRequest body, HttpRequest request, DemoAuthService auth, ExpenseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.RegisterPayment(actor, id, body)); });

app.MapGet("/api/v1/travel-requests", (string? keyword, int? status, string? applicantId, DateOnly? startDate, DateOnly? endDate, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, TravelService service) => Results.Ok(Paging.Create(service.List(Actor(request, auth), new DocumentListQuery(keyword, status, applicantId, startDate, endDate)), page, pageSize)));
app.MapGet("/api/v1/travel-requests/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, TravelService service) => { var result = service.Get(Actor(request, auth), id); return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(new { code = result.Code, message = result.Error }); });
app.MapPost("/api/v1/travel-requests", (SaveTravelRequest body, HttpRequest request, DemoAuthService auth, TravelService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.CreateDraft(actor, body), true); });
app.MapPatch("/api/v1/travel-requests/{id:guid}", (Guid id, int version, SaveTravelRequest body, HttpRequest request, DemoAuthService auth, TravelService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, version, body)); });
app.MapDelete("/api/v1/travel-requests/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, TravelService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Delete(actor, id)); });
app.MapPost("/api/v1/travel-requests/{id:guid}/submit", (Guid id, HttpRequest request, DemoAuthService auth, TravelService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Submit(actor, id)); });
app.MapPost("/api/v1/travel-requests/{id:guid}/withdraw", (Guid id, HttpRequest request, DemoAuthService auth, TravelService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Withdraw(actor, id)); });
app.MapGet("/api/v1/travel-tasks/my", (HttpRequest request, DemoAuthService auth, TravelService service) => Results.Ok(service.GetPendingTasks(Actor(request, auth))));
app.MapGet("/api/v1/travel-tasks/done", (HttpRequest request, DemoAuthService auth, TravelService service) => Results.Ok(service.GetProcessedTasks(Actor(request, auth))));
app.MapPost("/api/v1/travel-tasks/{id:guid}/approve", (Guid id, ApproveTaskRequest body, HttpRequest request, DemoAuthService auth, TravelService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Approve(actor, id, body.Comment)); });
app.MapPost("/api/v1/travel-tasks/{id:guid}/reject", (Guid id, RejectTaskRequest body, HttpRequest request, DemoAuthService auth, TravelService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Reject(actor, id, body.Comment)); });
app.MapPost("/api/v1/travel-tasks/{id:guid}/transfer", (Guid id, TransferTaskRequest body, HttpRequest request, DemoAuthService auth, TravelService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Transfer(actor, id, body)); });

app.MapGet("/api/v1/purchase-requests", (string? keyword, int? status, string? applicantId, DateOnly? startDate, DateOnly? endDate, int? page, int? pageSize, HttpRequest request, DemoAuthService auth, PurchaseService service) =>
    Results.Ok(service.List(Actor(request, auth), new DocumentListQuery(keyword, status, applicantId, startDate, endDate), page, pageSize)));
app.MapGet("/api/v1/purchase-requests/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, PurchaseService service) =>
{
    var result = service.Get(Actor(request, auth), id);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.Json(new { code = result.Code, message = result.Error }, statusCode: result.Code == "AUTH_002" ? StatusCodes.Status403Forbidden : StatusCodes.Status404NotFound);
});
app.MapPost("/api/v1/purchase-requests", (SavePurchaseRequest body, HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.CreateDraft(actor, body), created: true, atomic: true, fingerprintPayload: body); });
app.MapPatch("/api/v1/purchase-requests/{id:guid}", (Guid id, int version, SavePurchaseRequest body, HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Update(actor, id, version, body), atomic: true, fingerprintPayload: new { id, version, body }); });
app.MapDelete("/api/v1/purchase-requests/{id:guid}", (Guid id, HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Delete(actor, id), atomic: true, fingerprintPayload: new { id }); });
app.MapPost("/api/v1/purchase-requests/{id:guid}/submit", (Guid id, HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Submit(actor, id), atomic: true, fingerprintPayload: new { id }); });
app.MapPost("/api/v1/purchase-requests/{id:guid}/withdraw", (Guid id, HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Withdraw(actor, id), atomic: true, fingerprintPayload: new { id }); });
app.MapGet("/api/v1/purchase-tasks/my", (HttpRequest request, DemoAuthService auth, PurchaseService service) => Results.Ok(service.GetPendingTasks(Actor(request, auth))));
app.MapGet("/api/v1/purchase-tasks/done", (HttpRequest request, DemoAuthService auth, PurchaseService service) => Results.Ok(service.GetProcessedTasks(Actor(request, auth))));
app.MapPost("/api/v1/purchase-tasks/{id:guid}/approve", (Guid id, ApproveTaskRequest body, HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Approve(actor, id, body.Comment), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/purchase-tasks/{id:guid}/reject", (Guid id, RejectTaskRequest body, HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Reject(actor, id, body.Comment), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/purchase-tasks/{id:guid}/transfer", (Guid id, TransferTaskRequest body, HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Transfer(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/purchase-requests/{id:guid}/order", (Guid id, RegisterPurchaseOrderRequest body, HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.RegisterOrder(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/purchase-requests/{id:guid}/receipt", (Guid id, ReceivePurchaseRequest body, HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency) => { var actor = Actor(request, auth); return Write(request, actor, idempotency, () => service.Receive(actor, id, body), atomic: true, fingerprintPayload: body); });
app.MapPost("/api/v1/purchase-requests/demo-data", (HttpRequest request, DemoAuthService auth, PurchaseService service, IdempotencyService idempotency, IConfiguration configuration) =>
{
    if (!configuration.GetValue<bool>("DemoFeatures:AllowDataGeneration")) return Results.NotFound(new { code = "DATA_001", message = "演示数据生成功能未启用。" });
    var actor = Actor(request, auth);
    return Write(request, actor, idempotency, () => service.GenerateDemoData(actor), atomic: true, fingerprintPayload: new { operation = "purchase-demo-data" });
});

app.Run();

public partial class Program;
