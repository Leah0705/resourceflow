using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Utilities;

/// <summary>
/// The stand-in booking the admin's confirmation-email preview renders. It carries no persisted
/// row and is never saved: it exists so the real template can be run against a location's own
/// branding, hours, reference format and floor plan without a guest having booked anything.
/// </summary>
public static class EmailPreviewSample
{
    public const string CustomerName = "Alex Morgan";
    public const string CustomerEmail = "alex.morgan@example.com";
    public const string SpecialRequests = "Projector needed if one is free. We're celebrating an anniversary.";
    public const int PartySize = 2;

    /// <summary>
    /// The hour the sample slot prefers, when the location's service that day reaches it.
    /// An evening slot is what most confirmations are, and it exercises the template's longer
    /// time range; a midday-only location gets its own opening time instead.
    /// </summary>
    public const int PreferredHour = 19;

    /// <summary>
    /// Fixed rather than generated, so refreshing the preview does not reshuffle the reference
    /// under the admin. Both shapes are real ones — see <see cref="BookingRefGenerator"/> and
    /// <see cref="NumericBookingRefGenerator"/>.
    /// </summary>
    public static string RefFor(BookingRefFormat format) => format switch
    {
        BookingRefFormat.Numeric => "48273910",
        _ => "golden-cedar-river-0482"
    };

    /// <summary>
    /// Stands in for a location on an instance that has none yet, so the preview still shows the
    /// brand colour, header and footer rather than failing. Id 0 marks it as unsaved.
    /// </summary>
    public static Venue PlaceholderVenue => new()
    {
        Name = "Your Venue",
        Address = "1 Example Street, Your Town",
        Timezone = "UTC",
    };

    /// <summary>
    /// 
    /// </summary>
    /// <seealso>EmailPreviewSampleTests.BookingFor_SkipsForwardToTheNextDayTheLocationOpens</seealso>
    /// <seealso>EmailPreviewSampleTests.BookingFor_SitsAtSevenWhenTheDaysServiceReachesIt</seealso>
    /// <seealso>EmailPreviewSampleTests.BookingFor_SitsAtOpeningTimeWhenServiceEndsBeforeSeven</seealso>
    /// <seealso>EmailPreviewSampleTests.BookingFor_TakesTheFirstResourceOfTheFirstSection</seealso>
    public static Booking BookingFor(Venue venue, DateTime nowUtc)
    {
        DateTime localStart = NextSlotLocal(venue, nowUtc);
        DateTime startUtc = TimeZoneHelper.ConvertLocalToUtc(
            DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), venue.Timezone);

        Section? section = venue.Sections.OrderBy(s => s.SortOrder).FirstOrDefault();
        Resource? resource = section?.Resources.OrderBy(t => t.Id).FirstOrDefault();

        return new Booking
        {
            VenueId = venue.Id,
            Venue = venue,
            BookingRef = RefFor(venue.BookingRefFormat),
            CustomerName = CustomerName,
            CustomerEmail = CustomerEmail,
            SpecialRequests = SpecialRequests,
            PartySize = PartySize,
            Section = section,
            SectionId = section?.Id,
            Resource = resource,
            ResourceId = resource?.Id,
            Date = startUtc,
            EndTime = startUtc.AddMinutes(BookingDuration.For(venue, PartySize)),
        };
    }

    private static DateTime NextSlotLocal(Venue venue, DateTime nowUtc)
    {
        DateTime day = TimeZoneHelper.ConvertUtcToLocal(nowUtc, venue.Timezone).Date.AddDays(1);

        // An OpenDays naming no recognisable day means every day (ServiceWindowHelper.IsOpenOn),
        // and anything it does name is inside 1..7, so a week's walk always lands on an open day.
        // The bound is there so a future change to that reading cannot spin the request forever.
        for (int ahead = 0; ahead < 7 && !ServiceWindowHelper.IsOpenOn(venue, IsoDay.Of(day)); ahead++)
        {
            day = day.AddDays(1);
        }

        return day.AddMinutes(StartMinutesOfDay(venue, IsoDay.Of(day)));
    }

    private static int StartMinutesOfDay(Venue venue, int isoDay)
    {
        (string open, string close) = OpeningHoursHelper.GetHoursForDay(venue, isoDay);
        if (!OpeningHoursHelper.TryParseTime(open, out int openHour, out int openMinute)
            || !OpeningHoursHelper.TryParseTime(close, out int closeHour, out int closeMinute))
        {
            return PreferredHour * 60;
        }

        int openMinutes = (openHour * 60) + openMinute;
        int closeMinutes = (closeHour * 60) + closeMinute;
        // A close earlier than the open runs past midnight, so the service is measured forward
        // from the open rather than as a difference between two times of day.
        int serviceLength = closeMinutes > openMinutes
            ? closeMinutes - openMinutes
            : (1440 - openMinutes) + closeMinutes;

        int preferredOffset = (((PreferredHour * 60) - openMinutes) + 1440) % 1440;
        return preferredOffset < serviceLength ? openMinutes + preferredOffset : openMinutes;
    }
}
