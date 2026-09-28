using VerticalCoverFixer.Core;

namespace VerticalCoverFixer.Tests;

public class SteamGridDbTests
{
    [Fact]
    public void ParsesSearchResults()
    {
        const string json = """
            {"success":true,"data":[
              {"id":1234,"name":"DOOM","release_date":1463097600,"types":["steam"],"verified":true},
              {"id":5678,"name":"DOOM II","release_date":null}
            ]}
            """;

        var games = SteamGridDb.ParseData<SgdbGame>(json);

        Assert.Equal(2, games.Count);
        Assert.Equal(1234, games[0].Id);
        Assert.Equal("DOOM", games[0].Name);
        Assert.Equal(2016, games[0].ReleaseYear);
        Assert.Null(games[1].ReleaseYear);
    }

    [Fact]
    public void ParsesGridsAndIgnoresExtraFields()
    {
        const string json = """
            {"success":true,"data":[
              {"id":1,"score":3,"style":"alternate","width":600,"height":900,"nsfw":false,"humor":false,"epilepsy":false,
               "notes":null,"mime":"image/png","language":"en","url":"https://cdn2.steamgriddb.com/grid/a.png",
               "thumb":"https://cdn2.steamgriddb.com/thumb/a.jpg","lock":false,"upvotes":3,"downvotes":0,
               "author":{"name":"someone","steam64":"1","avatar":"x"}}
            ]}
            """;

        var grid = Assert.Single(SteamGridDb.ParseData<SgdbGrid>(json));
        Assert.Equal(600, grid.Width);
        Assert.Equal(900, grid.Height);
        Assert.Equal("image/png", grid.Mime);
        Assert.Equal("https://cdn2.steamgriddb.com/grid/a.png", grid.Url);
    }

    [Fact]
    public void FailedReplyGivesEmptyList()
    {
        Assert.Empty(SteamGridDb.ParseData<SgdbGrid>("""{"success":false,"errors":["Game not found"]}"""));
    }

    [Fact]
    public void PicksOnlyUsableVerticalGridsInOrder()
    {
        var grids = new[]
        {
            Grid(1, 460, 215),
            Grid(2, 600, 900),
            Grid(3, 600, 900, nsfw: true),
            Grid(4, 342, 482, humor: true),
            Grid(5, 600, 900, mime: "image/webp"),
            Grid(6, 512, 512),
            Grid(7, 660, 930),
        };

        Assert.Equal(new[] { 2, 7 }, SteamGridDb.PickGrids(grids, allowNsfw: false, allowHumor: false).Select(g => g.Id));
        Assert.Equal(new[] { 2, 3, 4, 7 }, SteamGridDb.PickGrids(grids, allowNsfw: true, allowHumor: true).Select(g => g.Id));
    }

    [Theory]
    [InlineData("Half-Life™ 2", "half life 2")]
    [InlineData("The Witcher 3: Wild Hunt", "witcher 3 wild hunt")]
    [InlineData("Ratchet & Clank", "ratchet and clank")]
    [InlineData("  ", "")]
    public void NormalizesNames(string name, string expected)
    {
        Assert.Equal(expected, SteamGridDb.NormalizeName(name));
    }

    [Fact]
    public void PrefersExactNameMatchWithSameYear()
    {
        var results = new[]
        {
            Game(1, "DOOM Eternal", 2020),
            Game(2, "DOOM", 1993),
            Game(3, "Doom", 2016),
        };

        var match = SteamGridDb.PickGame(results, "DOOM", 2016, allowClosest: true);

        Assert.NotNull(match);
        Assert.True(match.Exact);
        Assert.Equal(3, match.Game.Id);
    }

    [Fact]
    public void ExactMatchWithoutYearTakesFirst()
    {
        var results = new[] { Game(2, "DOOM", 1993), Game(3, "Doom", 2016) };

        Assert.Equal(2, SteamGridDb.PickGame(results, "Doom", null, allowClosest: false)!.Game.Id);
    }

    [Fact]
    public void ClosestMatchOnlyWhenAllowed()
    {
        var results = new[] { Game(9, "Grand Theft Auto V Enhanced", 2025) };

        Assert.Null(SteamGridDb.PickGame(results, "GTA 5", null, allowClosest: false));

        var closest = SteamGridDb.PickGame(results, "GTA 5", null, allowClosest: true);
        Assert.NotNull(closest);
        Assert.False(closest.Exact);
        Assert.Null(SteamGridDb.PickGame([], "GTA 5", null, allowClosest: true));
    }

    [Fact]
    public void BuildsRequestUrls()
    {
        Assert.Equal("https://www.steamgriddb.com/api/v2/search/autocomplete/Half-Life%202",
            SteamGridDb.SearchUrl("Half-Life™ 2"));

        var url = SteamGridDb.GridsForPlatformUrl("steam", "620", allowNsfw: false, allowHumor: true);
        Assert.StartsWith("https://www.steamgriddb.com/api/v2/grids/steam/620?", url);
        Assert.Contains("dimensions=600x900,342x482,660x930", url);
        Assert.Contains("nsfw=false", url);
        Assert.Contains("humor=any", url);
    }

    private static SgdbGrid Grid(int id, int width, int height, bool nsfw = false, bool humor = false, string mime = "image/png") =>
        new() { Id = id, Width = width, Height = height, Nsfw = nsfw, Humor = humor, Mime = mime, Url = $"https://example/{id}.png" };

    private static SgdbGame Game(int id, string name, int year) =>
        new() { Id = id, Name = name, ReleaseDate = new DateTimeOffset(year, 6, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() };
}
