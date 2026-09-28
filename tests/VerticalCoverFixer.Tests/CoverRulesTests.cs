using VerticalCoverFixer.Core;

namespace VerticalCoverFixer.Tests;

public class CoverRulesTests
{
    [Theory]
    [InlineData(600, 900, CoverShape.Vertical)]   // Steam library art
    [InlineData(264, 352, CoverShape.Vertical)]   // IGDB cover
    [InlineData(512, 512, CoverShape.Square)]
    [InlineData(560, 600, CoverShape.Square)]     // within 10% of square
    [InlineData(460, 215, CoverShape.Horizontal)] // Steam header
    [InlineData(630, 500, CoverShape.Horizontal)] // itch.io
    [InlineData(0, 900, CoverShape.Unknown)]
    public void ClassifiesByAspect(int width, int height, CoverShape expected)
    {
        Assert.Equal(expected, CoverShapes.Classify(width, height));
    }

    [Fact]
    public void RulesReplaceOnlyTheChosenShapes()
    {
        var rules = new ReplaceRules { ReplaceSquare = false };

        Assert.True(rules.Applies(CoverShape.Horizontal));
        Assert.True(rules.Applies(CoverShape.Missing));
        Assert.False(rules.Applies(CoverShape.Square));
        Assert.False(rules.Applies(CoverShape.Vertical));
        Assert.False(rules.Applies(CoverShape.Unknown));
    }

    [Fact]
    public void SteamUrlsNeedANumericAppId()
    {
        Assert.Equal(3, SteamCdn.VerticalCoverUrls("620").Count());
        Assert.All(SteamCdn.VerticalCoverUrls("620"), url => Assert.Contains("/620/library_600x900", url));
        Assert.Empty(SteamCdn.VerticalCoverUrls("not-a-number"));
        Assert.Empty(SteamCdn.VerticalCoverUrls(""));
    }

    [Fact]
    public void OnlySteamAndGogHaveSteamGridDbPlatforms()
    {
        Assert.Equal("steam", LibraryIds.SteamGridDbPlatform(LibraryIds.Steam));
        Assert.Equal("gog", LibraryIds.SteamGridDbPlatform(LibraryIds.Gog));
        Assert.Null(LibraryIds.SteamGridDbPlatform(Guid.Empty));
    }
}
