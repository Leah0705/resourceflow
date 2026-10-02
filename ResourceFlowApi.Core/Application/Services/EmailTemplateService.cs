using System.Net;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Mappings;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

/// <inheritdoc cref="IEmailTemplateService" />
public sealed class EmailTemplateService : IEmailTemplateService
{
    // The en-dash (U+2013) is verbatim from the original inline subject and is deliberately not
    // a hyphen.
    public string BuildConfirmationSubject(Venue venue) =>
        $"Booking confirmed \u2013 {venue.Name}";

    public string BuildConfirmationEmail(Booking booking, Venue venue, BrandSettings brand, string websiteUrl)
    {
        DateTime localDate = TimeZoneHelper.ConvertUtcToLocal(booking.Date, venue.Timezone);
        string dateStr = DateFormatter.FormatLongDate(localDate);

        // EndTime is always set by CreateBookingAsync before this is called.
        DateTime localEndDate = TimeZoneHelper.ConvertUtcToLocal(booking.EndTime!.Value, venue.Timezone);
        string timeRangeStr = DateFormatter.FormatTimeRange(localDate, localEndDate);

        string primaryColor = brand.PrimaryColor ?? "#0a7ea4";
        string appName = brand.AppName ?? "ResourceFlow";
        string cleanWebsiteUrl = websiteUrl.TrimEnd('/');
        string lookupUrl = BookingLinks.Confirmation(cleanWebsiteUrl, booking);

        // Choose header: full-width banner for venue photos, small icon for brand SVG, plain text otherwise
        string headerHtml;
        if (!string.IsNullOrEmpty(venue.ImageUrl))
        {
            string imageSrc = venue.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? venue.ImageUrl
                : $"{cleanWebsiteUrl}/{venue.ImageUrl.TrimStart('/')}";
            headerHtml = EmailTemplateBuilder.BannerHeader(imageSrc, venue.Name, venue.Name, "Booking Confirmed");
        }
        else if (!string.IsNullOrEmpty(brand.FaviconIcon))
        {
            headerHtml = EmailTemplateBuilder.IconHeader(
                $"{cleanWebsiteUrl}/api/brand/pwa-icon.svg", appName, venue.Name, "Booking Confirmed");
        }
        else
        {
            headerHtml = $"""
                <tr><td style="padding:40px 40px 24px;text-align:center;background-color:#ffffff;">
                  <h1 style="margin:0;font-size:24px;font-weight:700;color:#111827;">{WebUtility.HtmlEncode(venue.Name)}</h1>
                  <p style="margin:8px 0 0;font-size:16px;color:#6b7280;">Booking Confirmed</p>
                </td></tr>
                """;
        }

        string directionsHtml = string.IsNullOrWhiteSpace(venue.Address)
            ? ""
            : $"""
               <div style='margin-top:16px;padding-top:16px;border-top:1px solid #f0f0f0;'>
                 <p style='margin:0 0 8px;font-size:14px;color:#6b7280;'>Location</p>
                 <p style='margin:0 0 12px;font-size:15px;color:#111827;'>{WebUtility.HtmlEncode(venue.Address)}</p>
                 <a href='https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(venue.Address)}'
                    style='color:{primaryColor};text-decoration:none;font-size:14px;font-weight:600;'>Get directions &rarr;</a>
               </div>
               """;

        string sectionHtml = booking.Section != null
            ? $"""
               <tr>
                 <td style='padding:12px 0;color:#6b7280;font-size:14px;'>Section</td>
                 <td style='padding:12px 0;font-size:14px;color:#111827;'>{WebUtility.HtmlEncode(booking.Section.Name)}</td>
               </tr>
               """
            : "";

        string? resourceLabel = booking.Resource?.Name ?? (booking.ResourceId.HasValue ? $"Resource #{booking.ResourceId}" : null);
        // Combinable-resource group booking: Resource is null, so fall back to a readable group label so
        // the confirmation email still tells the guest what they reserved.
        resourceLabel ??= booking.ResourceGroup is not null ? BookingMapper.GroupLabel(booking.ResourceGroup) : null;
        string resourceHtml = resourceLabel != null
            ? $"""
               <tr>
                 <td style='padding:12px 0;color:#6b7280;font-size:14px;'>Resource</td>
                 <td style='padding:12px 0;font-size:14px;color:#111827;'>{WebUtility.HtmlEncode(resourceLabel)}</td>
               </tr>
               """
            : "";

        string specialReqsHtml = string.IsNullOrWhiteSpace(booking.SpecialRequests)
            ? ""
            : $"""
               <tr>
                 <td style='padding:12px 0;color:#6b7280;font-size:14px;vertical-align:top;white-space:nowrap;'>Special requests</td>
                 <td style='padding:12px 0 12px 16px;font-size:14px;color:#111827;'>{WebUtility.HtmlEncode(booking.SpecialRequests)}</td>
               </tr>
               """;

        string greetingName = string.IsNullOrWhiteSpace(booking.CustomerName)
            ? "You're all set!"
            : $"You're all set, {WebUtility.HtmlEncode(booking.CustomerName)}!";

        string contentHtml = $"""
            <!-- Confirmation Hero -->
            <tr><td style="padding:0 40px;">
              <div style="background-color:{primaryColor}10;border-radius:12px;padding:24px;text-align:center;border:1px solid {primaryColor}20;">
                <p style="margin:0;font-size:18px;font-weight:600;color:{primaryColor};">{greetingName}</p>
                <p style="margin:4px 0 0;font-size:14px;color:{primaryColor};opacity:0.8;">We're looking forward to seeing you.</p>
              </div>
            </td></tr>

            <!-- Details -->
            <tr><td style="padding:32px 40px;">
              <table width="100%" cellpadding="0" cellspacing="0">
                <tr>
                  <td style="padding:12px 0;color:#6b7280;font-size:14px;width:140px;">Reference</td>
                  <td style="padding:12px 0;font-size:14px;font-weight:700;color:#111827;letter-spacing:0.05em;">{WebUtility.HtmlEncode(booking.BookingRef ?? "")}</td>
                </tr>
                <tr>
                  <td style="padding:12px 0;color:#6b7280;font-size:14px;">Date</td>
                  <td style="padding:12px 0;font-size:14px;color:#111827;">{dateStr}</td>
                </tr>
                <tr>
                  <td style="padding:12px 0;color:#6b7280;font-size:14px;">Time</td>
                  <td style="padding:12px 0;font-size:14px;color:#111827;">{timeRangeStr}</td>
                </tr>
                <tr>
                  <td style="padding:12px 0;color:#6b7280;font-size:14px;">Participants</td>
                  <td style="padding:12px 0;font-size:14px;color:#111827;">{booking.PartySize} {(booking.PartySize == 1 ? "participant" : "participants")}</td>
                </tr>
                {sectionHtml}
                {resourceHtml}
                {specialReqsHtml}
              </table>
              {directionsHtml}
            </td></tr>

            <!-- CTA -->
            <tr><td style="padding:0 40px 32px;text-align:center;">
              <a href="{lookupUrl}" style="display:inline-block;background-color:{primaryColor};color:#ffffff;padding:14px 28px;border-radius:8px;text-decoration:none;font-weight:600;font-size:15px;box-shadow:0 2px 4px rgba(0,0,0,0.1);">Manage your booking</a>
            </td></tr>

            <!-- Cancellation note -->
            <tr><td style="padding:0 40px 32px;text-align:center;">
              <p style="margin:0;font-size:14px;color:#6b7280;line-height:1.5;">Need to change or cancel your reservation?<br>Use the link above or visit our website.</p>
            </td></tr>
            """;

        return EmailTemplateBuilder.Wrap(primaryColor, appName, cleanWebsiteUrl, headerHtml, contentHtml);
    }
}
