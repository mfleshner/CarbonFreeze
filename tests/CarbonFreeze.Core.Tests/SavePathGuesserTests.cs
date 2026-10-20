using CarbonFreeze.Core.Models;
using CarbonFreeze.Core.Services;
using CarbonFreeze.Core.Steam;

namespace CarbonFreeze.Core.Tests;

public class SavePathGuesserTests
{
    private static readonly SteamUser User = new("76561197983122154", "22856426", "matt", "Matt", true);
    private const string Steam = @"C:\Program Files (x86)\Steam";
    private static readonly SteamGame Game = new("4078430", "STAR WARS: Galactic Racer\u2122", "STAR WARS Galactic Racer", @"N:\SteamLibrary");

    private static string Cache(string appId, params (string Path, int Root)[] files)
    {
        var entries = string.Join("\n", files.Select(f =>
            $"\t\"{f.Path}\"\n\t{{\n\t\t\"root\"\t\t\"{f.Root}\"\n\t\t\"size\"\t\t\"1\"\n\t\t\"syncstate\"\t\t\"4\"\n\t}}"));
        return $"\"{appId}\"\n{{\n\t\"ChangeNumber\"\t\t\"0\"\n\t\"ostype\"\t\t\"0\"\n{entries}\n}}";
    }

    private static string? Guess(string appId, params (string Path, int Root)[] files) =>
        SavePathGuesser.FromRemoteCache(VdfParser.Parse(Cache(appId, files)), appId, User, Steam, Game);

    [Fact]
    public void Unreal_game_under_LocalAppData_with_steamid_folder()
    {
        // Real shape from STAR WARS: Galactic Racer.
        var t = Guess("4078430",
            ("StarWarsGalacticRacer/Saved/SaveGames/76561197983122154/GriffinRun.0.6.sav", 3),
            ("StarWarsGalacticRacer/Saved/SaveGames/76561197983122154/GriffinSave.6.sav", 3));
        Assert.Equal(@"%LOCALAPPDATA%\StarWarsGalacticRacer\Saved\SaveGames\{SteamId64}", t);
    }

    [Fact]
    public void Stops_at_steamid_folder_even_when_all_files_share_a_deeper_prefix()
    {
        // Rogue Legacy 2 with a single profile: the deepest common dir is Profile0, but the
        // per-user folder is the right swap unit.
        var t = Guess("1253920",
            ("Cellar Door Games/Rogue Legacy 2/Saves/Steam/76561197983122154/Profile0/Equipment.rc2dat", 12),
            ("Cellar Door Games/Rogue Legacy 2/Saves/Steam/76561197983122154/Profile0/GameMode.rc2dat", 12));
        Assert.Equal(@"{LocalLow}\Cellar Door Games\Rogue Legacy 2\Saves\Steam\{SteamId64}", t);
    }

    [Fact]
    public void SteamId3_folder_is_tokenised()
    {
        var t = Guess("1030300",
            ("Team Cherry/Hollow Knight Silksong/22856426/user1.dat", 12),
            ("Team Cherry/Hollow Knight Silksong/22856426/shared.dat", 12));
        Assert.Equal(@"{LocalLow}\Team Cherry\Hollow Knight Silksong\{SteamId3}", t);
    }

    [Fact]
    public void Documents_root_and_Roaming_root()
    {
        Assert.Equal(@"{Documents}\Saved Games\Hades",
            Guess("1145360", ("Saved Games/Hades/Profile1.sav", 2), ("Saved Games/Hades/Profile2.sav", 2)));
        Assert.Equal(@"%APPDATA%\Brotato\{SteamId64}",
            Guess("1942280", ("Brotato/76561197983122154/save.json", 4)));
    }

    [Fact]
    public void Prefers_files_whose_path_mentions_save_over_config_files()
    {
        var t = Guess("1817230",
            ("TangoGameworks/Hi-Fi RUSH (STEAM)/Saved/Config/WindowsNoEditor/Engine.ini", 9),
            ("TangoGameworks/Hi-Fi RUSH (STEAM)/Saved/Config/WindowsNoEditor/Game.ini", 9),
            ("TangoGameworks/Hi-Fi RUSH (STEAM)/Saved/Config/WindowsNoEditor/Input.ini", 9),
            ("TangoGameworks/Hi-Fi RUSH (STEAM)/Saved/SaveGames/Slot1.sav", 9));
        // "SaveGames" beats the broad "Saved" match, so the config files are left out.
        Assert.Equal(@"{SavedGames}\TangoGameworks\Hi-Fi RUSH (STEAM)\Saved\SaveGames", t);

        var t2 = Guess("1",
            ("Studio/Game/Config/settings.ini", 3),
            ("Studio/Game/Config/keys.ini", 3),
            ("Studio/Game/Profiles/slot1.sav", 3));
        Assert.Equal(@"%LOCALAPPDATA%\Studio\Game\Profiles", t2);
    }

    [Fact]
    public void Majority_root_wins_and_unknown_roots_are_ignored()
    {
        var t = Guess("1",
            ("Studio/Game/a.sav", 3),
            ("Studio/Game/b.sav", 3),
            ("Library/Application Support/Game/c.sav", 7));
        Assert.Equal(@"%LOCALAPPDATA%\Studio\Game", t);

        Assert.Null(Guess("1", ("Library/Application Support/Game/c.sav", 7)));
    }

    [Fact]
    public void Steam_cloud_api_root_maps_to_userdata_remote_folder()
    {
        Assert.Equal(Steam + @"\userdata\{SteamId3}\1338770\remote\Profiles",
            Guess("1338770", ("Profiles/default/profile.xml", 0), ("Profiles/other/profile.xml", 0)));

        // Files directly in the remote folder: the folder itself is the save location.
        Assert.Equal(Steam + @"\userdata\{SteamId3}\206420\remote",
            Guess("206420", ("save1.dat", 0), ("save2.dat", 0)));
    }

    [Fact]
    public void Files_directly_in_a_system_root_give_no_guess()
    {
        Assert.Null(Guess("1", ("a.sav", 3), ("b.sav", 3)));
        Assert.Null(Guess("1"));
    }

    [Fact]
    public void Game_install_root_uses_library_and_installdir()
    {
        var t = Guess("4078430", ("Saves/slot1.sav", 1), ("Saves/slot2.sav", 1));
        Assert.Equal(@"N:\SteamLibrary\steamapps\common\STAR WARS Galactic Racer\Saves", t);
    }

    [Fact]
    public void Common_folder_scan_tries_installdir_then_cleaned_names_and_one_level_under_LocalLow()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\L", @"C:\L\StarWarsGalacticRacer",
            @"C:\LL", @"C:\LL\Team Cherry", @"C:\LL\Team Cherry\Hollow Knight",
        };
        var bases = new List<SavePathGuesser.BaseFolder>
        {
            new("%LOCALAPPDATA%", @"C:\L"),
            new("{LocalLow}", @"C:\LL", ScanOneLevel: true),
            new("{Documents}", @"C:\Missing"),
        };
        IEnumerable<string> List(string p) => existing.Where(e =>
            e.Length > p.Length && e.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase) && e.IndexOf('\\', p.Length + 1) < 0);

        Assert.Equal(@"%LOCALAPPDATA%\StarWarsGalacticRacer",
            SavePathGuesser.FromCommonFolders("STAR WARS: Galactic Racer\u2122", "STAR WARS Galactic Racer", bases, existing.Contains, List));
        Assert.Equal(@"{LocalLow}\Team Cherry\Hollow Knight",
            SavePathGuesser.FromCommonFolders("Hollow Knight", "Hollow Knight", bases, existing.Contains, List));
        Assert.Null(SavePathGuesser.FromCommonFolders("Nothing Here", "nothing", bases, existing.Contains, List));
    }

    [Fact]
    public void Name_candidates_drop_symbols_and_duplicates()
    {
        var c = SavePathGuesser.NameCandidates("STAR WARS: Galactic Racer\u2122", "STAR WARS Galactic Racer");
        Assert.Equal(new[] { "STAR WARS Galactic Racer", "STARWARSGalacticRacer" }, c);
    }
}
