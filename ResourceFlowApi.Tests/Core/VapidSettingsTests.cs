using ResourceFlowApi.Core.Application.Settings;

namespace ResourceFlowApi.Tests.Core;

public class VapidSettingsTests
{
    [Fact]
    public void IsConfigured_ReturnsFalse_WhenAllEmpty()
    {
        var settings = new VapidSettings();
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void IsConfigured_ReturnsTrue_WhenAllSet()
    {
        var settings = new VapidSettings
        {
            PublicKey = "publicKey",
            PrivateKey = "privateKey",
            Subject = "mailto:test@example.com"
        };
        Assert.True(settings.IsConfigured);
    }

    [Theory]
    [InlineData("pub", "", "sub")]
    [InlineData("", "priv", "sub")]
    [InlineData("pub", "priv", "")]
    [InlineData("   ", "priv", "sub")]
    public void IsConfigured_ReturnsFalse_WhenAnyFieldMissingOrWhitespace(string pub, string priv, string sub)
    {
        var settings = new VapidSettings
        {
            PublicKey = pub,
            PrivateKey = priv,
            Subject = sub
        };
        Assert.False(settings.IsConfigured);
    }
}
