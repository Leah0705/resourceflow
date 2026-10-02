using ResourceFlowApi.Core.Application.DTOs;

namespace ResourceFlowApi.Core.Application.Interfaces;

/// <summary>
/// Computes bookable time slots for a venue on a given date, accounting for
/// opening hours, walk-in-only days, paused bookings, existing bookings, and resource holds.
/// Walk-in-only resources, and groups containing one, are never offered, and a slot the party would
/// push over <c>Venue.MaxGuestsPerSlot</c> is closed.
/// </summary>
public interface IAvailabilityService
{
    Task<AvailabilityResponseDto> GetAvailabilityAsync(int venueId, DateTime bookingDate, int partySize);
}
