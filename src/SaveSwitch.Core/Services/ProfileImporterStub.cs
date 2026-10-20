// TEMPORARY SKELETON. Replace with ProfileImporter.cs and delete this file.
using SaveSwitch.Core.Models;

namespace SaveSwitch.Core.Services;

public sealed class ProfileImporter : IProfileImporter
{
    public ProfileImporter(IProfileService profiles, IPathExpander expander) { }
    public LegacyLayout? DetectLegacyLayout(GameEntry game) => throw new NotImplementedException();
    public LegacyLayout? DescribeLayout(string profilesDir) => throw new NotImplementedException();
    public ImportResult Import(GameEntry game, LegacyLayout layout, bool setActive, bool moveBackups) => throw new NotImplementedException();
}
