using System.ComponentModel.DataAnnotations;
using ResourceFlowApi.Core.Application.Utilities;

namespace ResourceFlowApi.Core.Application.DTOs;

public class CancelBookingByRefRequest
{
    public string Email { get; set; } = string.Empty;
}

public class BookingDto
{
    public int Id { get; set; }
    public int? ResourceId { get; set; }
    public int? SectionId { get; set; }

    /// <summary>
    /// When set, this booking reserves a combinable-resource group instead of a single resource.
    /// <see cref="MemberResourceIds"/> carries the group's member ids for the conflict check; both are
    /// resolved server-side. Only <see cref="ResourceGroupId"/> is persisted on the booking entity.
    /// </summary>
    public int? ResourceGroupId { get; set; }

    /// <summary>
    /// Member resource ids the server resolved for <see cref="ResourceGroupId"/> while auto-assigning.
    /// Never read back from a client request — the booking path always re-resolves the members from
    /// the persisted group, so a caller can't shrink this list to dodge a conflict check.
    /// </summary>
    public IReadOnlyList<int>? MemberResourceIds { get; set; }

    public int VenueId { get; set; }
    public DateTime Date { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerName { get; set; }
    /// <summary>Party size. Bounded to [<see cref="BookingLimits.MinPartySize"/>, <see cref="BookingLimits.MaxPartySize"/>] on create.</summary>
    [Range(BookingLimits.MinPartySize, BookingLimits.MaxPartySize)]
    public int PartySize { get; set; }
    public bool isHeld { get; set; }
    public string? SpecialRequests { get; set; }
    public string? BookingRef { get; set; }
    public DateTime? EndTime { get; set; }

    public string? ResourceName { get; set; }
    public string? SectionName { get; set; }
    public int? ResourceCapacity { get; set; }
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Optional hold ID obtained from POST /api/holds.
    /// Provide this when creating a booking to validate and consume the hold.
    /// Not returned in responses.
    /// </summary>
    public string? HoldId { get; set; }
}
