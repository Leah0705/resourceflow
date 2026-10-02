using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Extensions;

/// <summary>
/// Checks every admin JWT against the account it names. A token stays valid for 30 days, so
/// without this check a deactivated user keeps working until the token expires, and a demoted
/// Owner keeps Owner access. API keys already resolve the account on every request
/// (<c>ApiKeyAuthenticationHandler</c>); this gives sessions the same guarantee.
/// <para>
/// The account is looked up by the id claim, falling back to the email claim for tokens minted
/// before multi-user support. When it is missing or inactive, authentication fails (401). When
/// it exists, the token's role claims are replaced by the account's current role, so a policy
/// decision always reflects the role an Owner last assigned.
/// </para>
/// </summary>
/// <seealso>AdminSessionValidationTests.ADeactivatedUsersToken_IsRejected</seealso>
/// <seealso>AdminSessionValidationTests.ADemotedOwnersToken_LosesOwnerAccess</seealso>
public static class AdminSessionValidation
{
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            context.Fail("The token carries no identity.");
            return;
        }

        IAdminCredentialRepository credentials =
            context.HttpContext.RequestServices.GetRequiredService<IAdminCredentialRepository>();

        AdminCredential? account = await CurrentUserResolver.ResolveAsync(
            ReadUserId(identity), ReadEmail(identity), credentials);

        if (account is null)
        {
            context.Fail("The account for this session no longer exists or has been deactivated.");
            return;
        }

        foreach (Claim roleClaim in identity.FindAll(c => c.Type == identity.RoleClaimType || c.Type == ClaimTypes.Role).ToList())
        {
            identity.TryRemoveClaim(roleClaim);
        }

        identity.AddClaim(new Claim(identity.RoleClaimType, account.Role));
    }

    private static int? ReadUserId(ClaimsIdentity identity)
    {
        // Inbound claim mapping may or may not rename "sub" to NameIdentifier; read both, as
        // CurrentUserService does.
        string? raw = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? identity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : null;
    }

    private static string? ReadEmail(ClaimsIdentity identity)
        => identity.FindFirst(ClaimTypes.Email)?.Value ?? identity.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
}
