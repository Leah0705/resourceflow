using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Interfaces;

/// <summary>
/// Encapsulates the multi-filter bookings grid query (<c>GET /admin/bookings</c>).
/// Implementations resolve status normalization, grid-mode date semantics, venue
/// timezone day ranges, and case-insensitive email / booking-ref substring matches,
/// returning materialized <see cref="Booking"/> entities with Venue/Section/Resource
/// navigation properties populated, ordered by <see cref="Booking.Date"/> ascending.
/// </summary>
public interface IBookingFilterRepository
{
    Task<List<Booking>> QueryAsync(BookingFilter filter);
}
