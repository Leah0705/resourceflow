namespace ResourceFlowApi.Core.Application.Utilities;

/// <summary>
/// Centralized bounds for party-size and resource-capacity values, so the DTO annotations,
/// service guards, and frontend dropdowns all agree on a single source of truth.
/// </summary>
public static class BookingLimits
{
    /// <summary>
    /// Smallest allowed value for a booking party size or a resource/group capacity.
    /// A booking must reserve at least one place; a resource must fit at least one guest.
    /// </summary>
    public const int MinPartySize = 1;

    /// <summary>
    /// Largest allowed party size (and per-resource/group capacity). Mirrors the guest-facing party-size
    /// picker's hard cap so a direct API POST can't bypass it. Parties larger than this should be
    /// handled by the venue directly (the guest UI shows a "contact us" notice past the
    /// location's effective capacity, which is always &lt;= this ceiling).
    /// </summary>
    public const int MaxPartySize = 50;
}
