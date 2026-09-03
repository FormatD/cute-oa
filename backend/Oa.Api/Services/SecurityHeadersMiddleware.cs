namespace Oa.Api.Services;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Content-Security-Policy"] = "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        if (context.Request.Path.StartsWithSegments("/api/v1/auth") || context.Request.Path == "/api/v1/me")
            headers.CacheControl = "no-store";
        await next(context);
    }
}
