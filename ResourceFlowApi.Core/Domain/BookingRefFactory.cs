namespace ResourceFlowApi.Core.Domain;

/// <summary>
/// Picks the booking-reference generator a venue has been configured to use. Every place
/// that mints a <see cref="Booking.BookingRef"/> goes through here, so a venue's
/// <see cref="Venue.BookingRefFormat"/> is the single point that decides the shape.
/// </summary>
public static class BookingRefFactory
{
    public static string GenerateFor(BookingRefFormat format) => format switch
    {
        BookingRefFormat.Numeric => NumericBookingRefGenerator.Generate(),
        _ => BookingRefGenerator.Generate()
    };

    public static string GenerateFor(Venue venue) => GenerateFor(venue.BookingRefFormat);
}
