namespace ResourceFlowApi.Core.Application.DTOs;

public class TimeSlotDto
{
    public string Time { get; set; } = string.Empty; // e.g. "12:15"
    public bool IsAvailable { get; set; }
    public List<int> AvailableResourceIds { get; set; } = new();

    /// <summary>
    /// Combinable-resource group ids bookable for this slot — a group is listed when its
    /// <c>CombinedCapacity</c> fits the party, the oversize cap is satisfied, and every member resource is
    /// free (no booking conflict, no hold) for the slot's duration. Parallel to
    /// <see cref="AvailableResourceIds"/> rather than overloading it so the two id spaces stay distinct.
    /// </summary>
    public List<int> AvailableGroupIds { get; set; } = new();

    public string Category { get; set; } = "AM"; // Local time: "AM" or "PM".
}

public class AvailabilityResponseDto
{
    public int VenueId { get; set; }
    public DateTime Date { get; set; }
    public List<TimeSlotDto> Slots { get; set; } = new();
}
