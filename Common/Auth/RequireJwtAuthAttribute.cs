using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace LoanPlatform.Common.Auth;

/// <summary>
/// Requires a valid "Authorization: Bearer &lt;token&gt;" header, verified
/// via IJwtService. Same pattern as backend-dotnet's
/// RequireJwtAuthAttribute.cs. Applied per-controller across all four
/// services — every business endpoint requires auth; only /api/health,
/// /api/docs.json, and /api/auth/login remain open.
/// </summary>
public class RequireJwtAuthAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var jwt = httpContext.RequestServices.GetRequiredService<IJwtService>();

        var header = httpContext.Request.Headers.Authorization.ToString();
        var parts = header.Split(' ', 2);

        if (parts.Length != 2 || parts[0] != "Bearer" || string.IsNullOrEmpty(parts[1]))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Missing or malformed Authorization header." });
            return;
        }

        var payload = jwt.VerifyToken(parts[1]);
        if (payload is null)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid or expired token." });
            return;
        }

        httpContext.Items["JwtUser"] = payload;
        await next();
    }
}
