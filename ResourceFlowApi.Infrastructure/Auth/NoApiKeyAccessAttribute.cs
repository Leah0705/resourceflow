using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Utilities;

namespace ResourceFlowApi.Infrastructure.Auth;

/// <summary>
/// Rejects a request outright when it authenticated via an admin API key, regardless of scope —
/// for the surface that must stay JWT/browser-session only. <c>ApiKeysController</c> carries this
/// (a key must not be usable to mint or revoke keys) and it is also the mechanism for the v1
/// exclusions of the API key design: email settings, notifications/push, and the auth self-service
/// endpoints.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoApiKeyAccessAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.User.IsApiKeyAuthenticated())
        {
            return;
        }

        context.Result = new ObjectResult(new MessageResponse
        {
            Message = "This endpoint cannot be accessed with an API key.",
            Code = ErrorCodes.ApiKeyNotAllowed,
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }
}
