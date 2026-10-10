using System.Text.Json;

namespace VanillaExpanded.Tests.Unit.AnimalSexIndicators;

/// <summary>Checks that animal controls stay together and match persisted defaults.</summary>
public sealed class AnimalSexConfigurationTests
{
    #region Public API
    /// <summary>Groups all animal controls in their localized section and retains client-only ownership.</summary>
    [Fact]
    public void AnimalControlsHaveDedicatedSection()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VanillaExpanded.sln"))) root = root.Parent;
        Assert.NotNull(root);
        string assets = Path.Combine(root.FullName, "VanillaExpanded", "assets", "vanillaexpanded");
        using var patch = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "config", "configlib-patches.json")));
        using var language = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "lang", "en.json")));
        var heading = patch.RootElement.GetProperty("formatting").EnumerateArray().Single(entry =>
            entry.GetProperty("title").GetString() == "vanillaexpanded:config-section-animal-sex-indicators");
        float weight = heading.GetProperty("weight").GetSingle();
        var settings = patch.RootElement.GetProperty("settings").EnumerateObject()
            .SelectMany(category => category.Value.EnumerateObject())
            .Where(setting => setting.Name.StartsWith("Animal") || setting.Name.StartsWith("EnableAnimal")).ToArray();
        Assert.Equal(6, settings.Length);
        foreach (var setting in settings)
        {
            Assert.True(setting.Value.GetProperty("clientSide").GetBoolean());
            Assert.InRange(setting.Value.GetProperty("weight").GetSingle(), weight + 0.01f, weight + 0.99f);
            foreach (string field in new[] { "ingui", "comment" })
            {
                string key = setting.Value.GetProperty(field).GetString()!.Split(':')[1];
                Assert.False(string.IsNullOrWhiteSpace(language.RootElement.GetProperty(key).GetString()));
            }
        }
    }
    #endregion
}
