using ResourceFlowApi.Core.Application.Utilities;

namespace ResourceFlowApi.Tests.Core;

public class WaitlistReadyCopyTests
{
    [Fact]
    public void Build_UsesTheGuestsLocale()
    {
        (string subject, string html) = WaitlistReadyCopy.Build("fr", "Chez Nous", "Ada", 12, "https://x.test/waitlist/abc");

        Assert.Equal("Votre ressource à Chez Nous est prête", subject);
        Assert.Contains("Bonjour Ada,", html);
        Assert.Contains("Ticket n&#176; 12", html);
        Assert.Contains("href=\"https://x.test/waitlist/abc\"", html);
    }

    [Fact]
    public void BuildPush_UsesTheGuestsLocale()
    {
        (string title, string body) = WaitlistReadyCopy.BuildPush("es", "Chez Nous", 12);

        Assert.Equal("Su recurso en Chez Nous está listo", title);
        Assert.Equal("Número 12: Su recurso está listo. Acérquese a recepción, por favor.", body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("xx")]
    public void Build_FallsBackToEnglishForAnUnknownLocale(string? locale)
    {
        (string subject, _) = WaitlistReadyCopy.Build(locale, "Test Venue", "Ada", 1, "https://x.test");

        Assert.Equal("Your resource at Test Venue is ready", subject);
    }

    [Fact]
    public void Build_EncodesTheGuestsName()
    {
        (_, string html) = WaitlistReadyCopy.Build("en", "Test Venue", "<b>Ada</b>", 1, "https://x.test");

        Assert.Contains("&lt;b&gt;Ada&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>Ada</b>", html);
    }

    [Fact]
    public void StatusLink_PointsAtTheGuestsStatusPage()
    {
        var entry = new ResourceFlowApi.Core.Domain.WaitlistEntry { Ref = "abc234" };

        Assert.Equal("https://x.test/waitlist/abc234", WaitlistLinks.Status("https://x.test/", entry));
    }
}
