using System.Security.Claims;

namespace AgentWorkflowBuilder.Api;

/// <summary>
/// Extracts the authenticated user's identity from Entra ID claims.
/// </summary>
internal static class UserContext
{
    private const string ObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>
    /// Returns the user's Entra Object ID (oid claim).
    /// </summary>
    internal static string GetUserId(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? oid = principal.FindFirstValue(ObjectIdClaim)
                      ?? principal.FindFirstValue("oid");

        if (string.IsNullOrWhiteSpace(oid))
        {
            throw new UnauthorizedAccessException("Missing oid claim in token.");
        }

        return oid;
    }
}
