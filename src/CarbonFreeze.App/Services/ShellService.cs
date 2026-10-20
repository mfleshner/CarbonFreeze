using System.Diagnostics;
using System.IO;

namespace CarbonFreeze.App.Services;

public sealed class ShellService : IShellService
{
    public bool OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        return true;
    }

    public bool RevealFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        if (File.Exists(filePath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
            return true;
        }

        var parent = Path.GetDirectoryName(filePath);
        return parent is not null && OpenFolder(parent);
    }
}
