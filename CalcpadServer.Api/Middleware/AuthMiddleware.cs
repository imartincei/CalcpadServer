using CalcpadServer.Api.Services;

namespace CalcpadServer.Api.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserService userService)
    {
        var token = ExtractToken(context);
        
        if (!string.IsNullOrEmpty(token))
        {
            var userContext = await userService.GetUserContextFromTokenAsync(token);
            if (userContext != null)
            {
                context.Items["UserContext"] = userContext;
                _logger.LogDebug("User context set for {Username}", userContext.Username);
            }
        }

        await _next(context);
    }

    private string? ExtractToken(HttpContext context)
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        
        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            return authHeader.Substring("Bearer ".Length).Trim();
        }

        return null;
    }
}