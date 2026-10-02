using ResourceFlowApi.Core.Application.Utilities;

namespace ResourceFlowApi.Tests.Core;

/// <summary>
/// A reminder push is composed on the server in the locale the guest opted in under, so each
/// of the four UI languages is pinned here word for word, along with the day-relative forms
/// ("tomorrow", "today", a date) that the frontend's i18next bundle would otherwise own.
/// </summary>
public class GuestReminderCopyTests
{
    private const string Venue = "Central Workspace";
    private const string Ref = "swift-cedar-harbor";

    private static readonly DateTime Slot = new(2026, 9, 11, 19, 30, 0);

    [Theory]
    [InlineData("en", "Your resource at Central Workspace", "Tomorrow at 19:30", "Today at 19:30", " at 19:30", "2 participants", "Ref swift-cedar-harbor")]
    [InlineData("fr", "Votre ressource à Central Workspace", "Demain à 19:30", "Aujourd'hui à 19:30", " à 19:30", "2 participants", "Réf. swift-cedar-harbor")]
    [InlineData("es", "Su recurso en Central Workspace", "Mañana a las 19:30", "Hoy a las 19:30", " a las 19:30", "2 participantes", "Ref. swift-cedar-harbor")]
    [InlineData("de", "Ihre Ressource bei Central Workspace", "Morgen um 19:30", "Heute um 19:30", " um 19:30", "2 Teilnehmende", "Ref. swift-cedar-harbor")]
    public void Build_UsesTheGuestsLocale(
        string locale, string title, string tomorrow, string today, string datedInfix, string guests, string reference)
    {
        (string tomorrowTitle, string tomorrowBody) = GuestReminderCopy.Build(locale, Venue, Slot, Slot.AddDays(-1).AddHours(-3), 2, Ref);
        (_, string todayBody) = GuestReminderCopy.Build(locale, Venue, Slot, Slot.AddHours(-2), 2, Ref);
        (_, string datedBody) = GuestReminderCopy.Build(locale, Venue, Slot, Slot.AddDays(-6), 2, Ref);

        Assert.Equal(title, tomorrowTitle);
        Assert.Equal($"{tomorrow} · {guests} · {reference}", tomorrowBody);
        Assert.Equal($"{today} · {guests} · {reference}", todayBody);
        Assert.Contains(datedInfix, datedBody);
        Assert.Contains("11", datedBody);
        Assert.DoesNotContain(tomorrow, datedBody);
        Assert.DoesNotContain(today, datedBody);
    }

    [Theory]
    [InlineData("en", "1 participant")]
    [InlineData("fr", "1 participant")]
    [InlineData("es", "1 participante")]
    [InlineData("de", "1 Teilnehmer")]
    public void Build_UsesTheSingularForOneGuest(string locale, string oneGuest)
    {
        (_, string body) = GuestReminderCopy.Build(locale, Venue, Slot, Slot.AddDays(-1), 1, Ref);

        Assert.Contains($" · {oneGuest} · ", body);
    }

    [Fact]
    public void Build_MatchesTheLocaleCaseInsensitively()
    {
        (string title, _) = GuestReminderCopy.Build("FR", Venue, Slot, Slot.AddDays(-1), 2, Ref);

        Assert.Equal("Votre ressource à Central Workspace", title);
    }

    [Theory]
    [InlineData("pt")]
    [InlineData("")]
    [InlineData(null)]
    public void Build_FallsBackToEnglishForAnUnknownLocale(string? locale)
    {
        (string title, string body) = GuestReminderCopy.Build(locale, Venue, Slot, Slot.AddDays(-1), 2, Ref);

        Assert.Equal("Your resource at Central Workspace", title);
        Assert.Equal("Tomorrow at 19:30 · 2 participants · Ref swift-cedar-harbor", body);
    }
}
