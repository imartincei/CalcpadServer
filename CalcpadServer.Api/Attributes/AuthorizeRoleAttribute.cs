using CalcpadServer.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CalcpadServer.Api.Attributes;

public class AuthorizeRoleAttribute : Attribute, IAsyncActionFilter
{
    private readonly UserRole _minimumRole;

    public AuthorizeRoleAttribute(UserRole minimumRole)
    {
        _minimumRole = minimumRole;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Check if user context exists
        if (!context.HttpContext.Items.TryGetValue("UserContext", out var userContextObj) || 
            userContextObj is not UserContext userContext)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Authentication required" });
            return;
        }

        // Check if user has sufficient role
        if (userContext.Role < _minimumRole)
        {
            context.Result = new ForbidResult($"Minimum role required: {_minimumRole}");
            return;
        }

        await next();
    }
}