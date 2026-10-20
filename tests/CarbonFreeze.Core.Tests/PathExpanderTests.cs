using CarbonFreeze.Core.Services;

namespace CarbonFreeze.Core.Tests;

public class PathExpanderTests
{
    [Fact]
    public void Expands_environment_variables()
    {
        var expander = new PathExpander(new FakeSteamLocator());
        var expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Foo", "Bar"));
        Assert.Equal(expected, expander.Expand(@"%LOCALAPPDATA%\Foo\Bar\"));
    }

    [Fact]
    public void Expands_steam_tokens_from_locator()
    {
        var steam = new FakeSteamLocator();
        var expander = new PathExpander(steam);
        var result = expander.Expand(@"C:\Games\{SteamId64}\{steamid3}");
        Assert.Equal(@"C:\Games\76561198012345678\52079950", result);
    }

    [Fact]
    public void Throws_PathTokenException_when_no_steam_user()
    {
        var expander = new PathExpander(new FakeSteamLocator { User = null });
        var ex = Assert.Throws<PathTokenException>(() => expander.Expand(@"C:\Games\{SteamId64}"));
        Assert.Contains("SteamId64", ex.Message);
        Assert.Contains("Steam user", ex.Message);
    }

    [Fact]
    public void Throws_PathTokenException_for_unknown_token()
    {
        var expander = new PathExpander(new FakeSteamLocator());
        Assert.Throws<PathTokenException>(() => expander.Expand(@"C:\Games\{Nope}"));
    }

    [Fact]
    public void Expands_LocalLow()
    {
        var expander = new PathExpander(new FakeSteamLocator());
        var result = expander.Expand(@"{LocalLow}\Team Cherry");
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expected = Path.GetFullPath(Path.Combine(local, "..", "LocalLow", "Team Cherry"));
        Assert.Equal(expected, result);
        Assert.DoesNotContain("..", result);
    }

    [Fact]
    public void Expands_UserProfile_Documents_and_SavedGames()
    {
        var expander = new PathExpander(new FakeSteamLocator());
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\'),
            expander.Expand("{UserProfile}"));
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments).TrimEnd('\\'),
            expander.Expand("{Documents}"));
        var saved = expander.Expand("{SavedGames}");
        Assert.True(Path.IsPathRooted(saved));
        Assert.EndsWith("Saved Games", saved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keeps_drive_root_separator()
    {
        var expander = new PathExpander(new FakeSteamLocator());
        Assert.Equal(@"C:\", expander.Expand(@"C:\"));
    }

    [Fact]
    public void Rejects_empty_template()
    {
        var expander = new PathExpander(new FakeSteamLocator());
        Assert.Throws<ArgumentException>(() => expander.Expand("  "));
    }

    [Fact]
    public void Describes_all_six_tokens()
    {
        var tokens = new PathExpander(new FakeSteamLocator()).DescribeTokens();
        Assert.Equal(6, tokens.Count);
        foreach (var t in new[] { "{SteamId64}", "{SteamId3}", "{UserProfile}", "{Documents}", "{SavedGames}", "{LocalLow}" })
            Assert.True(tokens.ContainsKey(t), t);
    }
}
