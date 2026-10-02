namespace ResourceFlowApi.Core.Application.DTOs;

public class PlaceHoldRequest
{
    public int VenueId { get; set; }

    /// <summary>
    /// Specific resource to hold. When null (and <see cref="SectionId"/> is also null), the
    /// server auto-assigns the best available resource across all sections ("Any section" flow).
    /// </summary>
    public int? ResourceId { get; set; }

    /// <summary>
    /// Specific section of <see cref="ResourceId"/>. When null (and <see cref="ResourceId"/> is
    /// also null), the server auto-assigns. Must be provided whenever <see cref="ResourceId"/> is.
    /// </summary>
    public int? SectionId { get; set; }

    /// <summary>
    /// Combinable-resource group to hold. When set, the server resolves the group's members and
    /// places a single hold reserving all of them. Mutually exclusive with <see cref="ResourceId"/>.
    /// </summary>
    public int? ResourceGroupId { get; set; }

    /// <summary>
    /// Party size. Required for auto-assign and group holds (so the server can validate capacity).
    /// Optional for explicit-resource holds, whose capacity is checked at booking time, but it sets
    /// how long the hold blocks the resource: without it the hold uses the default duration.
    /// </summary>
    public int PartySize { get; set; }

    /// <summary>Full ISO 8601 datetime of the intended booking.</summary>
    public DateTime Date { get; set; }

    /// <summary>If the caller already holds this ID, the backend atomically replaces it.</summary>
    public string? CurrentHoldId { get; set; }
}

public class HoldResponse
{
    public string HoldId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Resolved resource for auto-assigned holds ("Any section"). Null for explicit-resource holds
    /// where the caller already knows the resource.
    /// </summary>
    public int? ResourceId { get; set; }

    /// <summary>
    /// Resolved section for auto-assigned holds ("Any section"). Null for explicit-resource holds.
    /// </summary>
    public int? SectionId { get; set; }

    /// <summary>
    /// Resolved combinable-resource group for group holds. Null for single-resource/auto holds.
    /// </summary>
    public int? ResourceGroupId { get; set; }

    /// <summary>Seconds until the hold expires, for countdown display.</summary>
    public int SecondsRemaining => Math.Max(0, (int)(ExpiresAt - DateTime.UtcNow).TotalSeconds);
}
