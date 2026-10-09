using VanillaExpanded.Tests.IntegrationHarness;

namespace VanillaExpanded.IntegrationTests.AutoStashing;

/// <summary>Owns the dedicated process lifetime of irreversible reverse patches and ordinary production dispatch patches.</summary>
[Trait("Category", "Integration")]
public sealed class HarmonyDispatchIntegrationTests
{
    #region Public API
    /// <summary>Runs all real patched entries together so process exit releases the reverse-patched global stub.</summary>
    [Fact]
    public void ProductionPatches_DispatchAndCleanUpWithinIsolatedHost()
    {
        EngineDispatchHarness.RunIsolatedDispatchChecks();
    }
    #endregion
}
