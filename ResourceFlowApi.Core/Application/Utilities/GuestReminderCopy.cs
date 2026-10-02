using System.Globalization;

namespace ResourceFlowApi.Core.Application.Utilities;

/// <summary>
/// The words in a booking reminder, in the four UI languages. A push arrives outside the app,
/// so it cannot borrow the frontend's i18next bundle the way every other guest string does; the
/// locale the guest opted in under travels with the subscription instead.
/// </summary>
/// <seealso>GuestReminderCopyTests.Build_UsesTheGuestsLocale</seealso>
/// <seealso>GuestReminderCopyTests.Build_FallsBackToEnglishForAnUnknownLocale</seealso>
public static class GuestReminderCopy
{
    private sealed record Strings(
        string Title,
        string Tomorrow,
        string Today,
        string OnDate,
        string GuestsOne,
        string GuestsMany,
        string Reference);

    private static readonly Dictionary<string, Strings> Copy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new("Your resource at {0}", "Tomorrow at {0}", "Today at {0}", "{0} at {1}", "1 participant", "{0} participants", "Ref {0}"),
        ["fr"] = new("Votre ressource à {0}", "Demain à {0}", "Aujourd'hui à {0}", "{0} à {1}", "1 participant", "{0} participants", "Réf. {0}"),
        ["es"] = new("Su recurso en {0}", "Mañana a las {0}", "Hoy a las {0}", "{0} a las {1}", "1 participante", "{0} participantes", "Ref. {0}"),
        ["de"] = new("Ihre Ressource bei {0}", "Morgen um {0}", "Heute um {0}", "{0} um {1}", "1 Teilnehmer", "{0} Teilnehmende", "Ref. {0}"),
    };

    private static readonly Dictionary<string, CultureInfo> Cultures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = CultureInfo.GetCultureInfo("en-GB"),
        ["fr"] = CultureInfo.GetCultureInfo("fr-FR"),
        ["es"] = CultureInfo.GetCultureInfo("es-ES"),
        ["de"] = CultureInfo.GetCultureInfo("de-DE"),
    };

    public static (string Title, string Body) Build(
        string? locale,
        string venueName,
        DateTime bookingLocal,
        DateTime nowLocal,
        int partySize,
        string bookingRef)
    {
        string key = locale is not null && Copy.ContainsKey(locale) ? locale : "en";
        Strings s = Copy[key];
        CultureInfo culture = Cultures[key];

        string time = bookingLocal.ToString("t", culture);
        int daysAway = (bookingLocal.Date - nowLocal.Date).Days;
        string when = daysAway switch
        {
            0 => string.Format(culture, s.Today, time),
            1 => string.Format(culture, s.Tomorrow, time),
            _ => string.Format(culture, s.OnDate, bookingLocal.ToString("ddd d MMM", culture), time),
        };
        string guests = partySize == 1 ? s.GuestsOne : string.Format(culture, s.GuestsMany, partySize);

        return (
            string.Format(culture, s.Title, venueName),
            $"{when} · {guests} · {string.Format(culture, s.Reference, bookingRef)}");
    }
}
