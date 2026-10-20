namespace SaveSwitch.App.Services;

/// <summary>Thin wrapper around Explorer so view models never touch Process directly.</summary>
public interface IShellService
{
    /// <summary>Open a folder in Explorer. Returns false (no throw) when the folder does not exist.</summary>
    bool OpenFolder(string path);

    /// <summary>Open Explorer with the given file selected. Falls back to the parent folder.</summary>
    bool RevealFile(string filePath);
}
