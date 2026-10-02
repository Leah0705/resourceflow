using System.Globalization;

namespace ResourceFlowApi.Core.Application.Utilities;

/// <summary>
/// The words in a "your resource is ready" email and push, in the four UI languages. Like
/// <see cref="GuestReminderCopy"/>, it goes out beyond the app's i18next bundle, so the locale
/// the guest joined under travels with the entry.
/// </summary>
/// <seealso>WaitlistReadyCopyTests.Build_UsesTheGuestsLocale</seealso>
/// <seealso>WaitlistReadyCopyTests.Build_FallsBackToEnglishForAnUnknownLocale</seealso>
/// <seealso>WaitlistReadyCopyTests.BuildPush_UsesTheGuestsLocale</seealso>
public static class WaitlistReadyCopy
{
    private sealed record Strings(string Subject, string Greeting, string Body, string Ticket, string Link);

    private static readonly Dictionary<string, Strings> Copy = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new("Your resource at {0} is ready", "Hi {0},", "Your resource is ready. Please check in at reception.", "Ticket #{0}", "View your place in the queue"),
        ["fr"] = new("Votre ressource à {0} est prête", "Bonjour {0},", "Votre ressource est prête. Merci de vous présenter à l'accueil.", "Ticket n° {0}", "Voir votre place dans la file"),
        ["es"] = new("Su recurso en {0} está listo", "Hola {0}:", "Su recurso está listo. Acérquese a recepción, por favor.", "Número {0}", "Ver su lugar en la fila"),
        ["de"] = new("Ihre Ressource bei {0} ist bereit", "Hallo {0},", "Ihre Ressource ist bereit. Bitte kommen Sie zum Empfang.", "Nummer {0}", "Ihren Platz in der Warteschlange ansehen"),
    };

    public static (string Subject, string Html) Build(string? locale, string venueName, string guestName, int number, string statusUrl)
    {
        Strings s = For(locale);
        CultureInfo culture = CultureInfo.InvariantCulture;

        string html = $"""
            <p>{Encode(string.Format(culture, s.Greeting, guestName))}</p>
            <p><strong>{Encode(s.Body)}</strong></p>
            <p>{Encode(string.Format(culture, s.Ticket, number))}</p>
            <p><a href="{Encode(statusUrl)}">{Encode(s.Link)}</a></p>
            """;

        return (string.Format(culture, s.Subject, venueName), html);
    }

    /// <summary>The push says what the email's subject and body say, without the greeting or link.</summary>
    public static (string Title, string Body) BuildPush(string? locale, string venueName, int number)
    {
        Strings s = For(locale);
        CultureInfo culture = CultureInfo.InvariantCulture;
        return (string.Format(culture, s.Subject, venueName), $"{string.Format(culture, s.Ticket, number)}: {s.Body}");
    }

    private static Strings For(string? locale) => Copy[locale is not null && Copy.ContainsKey(locale) ? locale : "en"];

    private static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
}
