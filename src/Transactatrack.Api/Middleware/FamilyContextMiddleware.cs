using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Middleware;

public class FamilyContextMiddleware
{
    private static readonly string[] _unscopedPrefixes = ["/api/families", "/api/status", "/health"];

    private readonly RequestDelegate _next;

    public FamilyContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, FamilyContext familyContext)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isUnscoped = _unscopedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        if (context.Request.Headers.TryGetValue("X-Family-Id", out var headerValue))
        {
            if (!Guid.TryParse(headerValue, out var familyId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Invalid X-Family-Id",
                    detail = "The X-Family-Id header must be a valid GUID.",
                    status = 400
                });
                return;
            }
            familyContext.ActiveFamilyId = familyId;
        }
        else if (!isUnscoped)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Missing X-Family-Id",
                detail = "The X-Family-Id header is required for this endpoint.",
                status = 400
            });
            return;
        }

        await _next(context);
    }
}
