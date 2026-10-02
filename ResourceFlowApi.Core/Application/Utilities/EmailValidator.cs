using System.Text.RegularExpressions;

namespace ResourceFlowApi.Core.Application.Utilities;

/// <summary>
/// Centralised email format validation. The same regex was previously inlined in
/// <c>AuthController.IsValidEmail</c> and duplicated on the frontend (<c>useResourceHold.ts</c>);
/// the frontend mirrors this in <c>utils/validation.ts</c>. This is a deliberately loose shape
/// check, not RFC 5322 — strict validation happens via the confirmation email round-trip.
/// </summary>
public static class EmailValidator
{
    private static readonly Regex EmailPattern =
        new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    public static bool IsValid(string? email)
        => !string.IsNullOrWhiteSpace(email) && EmailPattern.IsMatch(email.Trim());
}
