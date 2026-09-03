using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class BearerAuthenticationMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> PublicPaths =
    [
        "/health",
        "/health/ready",
        "/api/v1/auth/login",
        "/api/v1/auth/mfa/setup/start",
        "/api/v1/auth/mfa/setup/confirm",
        "/api/v1/auth/mfa/verify",
        "/api/v1/auth/refresh",
        "/api/v1/auth/logout",
        "/api/v1/auth/demo-accounts"
    ];

    public async Task InvokeAsync(HttpContext context, JwtTokenService tokens, DemoData data)
    {
        if (HttpMethods.IsOptions(context.Request.Method) || PublicPaths.Contains(context.Request.Path.Value ?? string.Empty))
        {
            await next(context);
            return;
        }

        var authorization = context.Request.Headers.Authorization.FirstOrDefault();
        var token = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true ? authorization[7..].Trim() : null;
        if (string.IsNullOrWhiteSpace(token) || !tokens.TryValidate(token, out var identity))
        {
            await Unauthorized(context, "登录已失效，请重新登录。");
            return;
        }

        var employee = data.FindEmployee(identity!.UserId);
        if (employee is null || employee.Status != "ACTIVE")
        {
            await Unauthorized(context, "账号已停用或不存在。");
            return;
        }

        var db = context.RequestServices.GetService<OaDbContext>();
        if (db is not null)
        {
            if (identity.SessionId is null || !await db.AuthSessions.AsNoTracking().AnyAsync(item => item.Id == identity.SessionId && item.UserId == employee.Id && item.RevokedAt == null && item.ExpiresAt > DateTimeOffset.UtcNow))
            {
                await Unauthorized(context, "登录会话已撤销或过期，请重新登录。");
                return;
            }
            context.Items[DemoAuthService.SessionKey] = identity.SessionId.Value;
        }

        context.Items[DemoAuthService.ActorKey] = employee;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, employee.Id),
            new Claim(ClaimTypes.Name, employee.Name),
            new Claim(ClaimTypes.Role, employee.Role),
            new Claim("tenant", identity.TenantId)
        ], "Bearer"));
        await next(context);
    }

    private static async Task Unauthorized(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { code = "AUTH_001", message });
    }
}

public sealed class DemoAuthService
{
    public const string ActorKey = "AuthenticatedEmployee";
    public const string SessionKey = "AuthenticatedSession";

    public Employee Resolve(HttpRequest request) => request.HttpContext.Items.TryGetValue(ActorKey, out var actor) && actor is Employee employee
        ? employee
        : throw new UnauthorizedAccessException("当前请求未通过身份认证。");

    public Guid ResolveSessionId(HttpRequest request) => request.HttpContext.Items.TryGetValue(SessionKey, out var session) && session is Guid sessionId
        ? sessionId
        : throw new UnauthorizedAccessException("当前请求未绑定有效登录会话。");
}
