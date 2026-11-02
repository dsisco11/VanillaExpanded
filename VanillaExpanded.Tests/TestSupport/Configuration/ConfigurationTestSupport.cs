using System.IO;

namespace VanillaExpanded.Tests.TestSupport.Configuration;

internal static class ConfigurationTestSupport
{
    internal static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VanillaExpanded.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the VanillaExpanded repository root.");
    }
}
