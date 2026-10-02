using ResourceFlowApi.Core.Application;

namespace ResourceFlowApi.Tests.Core;

public class LucideIconPathsTests
{
    [Theory]
    [InlineData("building")]
    [InlineData("briefcase")]
    [InlineData("laptop")]
    [InlineData("calendar")]
    [InlineData("flame")]
    [InlineData("leaf")]
    [InlineData("star")]
    [InlineData("heart")]
    [InlineData("graduation-cap")]
    [InlineData("book-open")]
    [InlineData("dumbbell")]
    [InlineData("map-pin")]
    [InlineData("users")]
    [InlineData("music")]
    [InlineData("camera")]
    public void Get_KnownIcon_ReturnsSvgPath(string icon)
    {
        string? result = LucideIconPaths.Get(icon);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Get_UnknownIcon_ReturnsNull()
    {
        Assert.Null(LucideIconPaths.Get("not-an-icon"));
        Assert.Null(LucideIconPaths.Get(""));
        Assert.Null(LucideIconPaths.Get("rocket"));
    }

    [Theory]
    [InlineData("BUILDING")]
    [InlineData("Briefcase")]
    [InlineData("GRADUATION-CAP")]
    [InlineData("Book-Open")]
    public void Get_IsCaseInsensitive(string icon)
    {
        Assert.NotNull(LucideIconPaths.Get(icon));
    }
}
