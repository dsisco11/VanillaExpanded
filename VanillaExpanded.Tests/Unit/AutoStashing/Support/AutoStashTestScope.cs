namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Restores mutable AutoStash settings after an isolated test, including exceptional exits.</summary>
internal sealed class AutoStashTestScope : IDisposable
{
    private readonly VanillaExpandedConfig config = VanillaExpandedModSystem.Config;
    private readonly bool enabled;
    private readonly float delay;

    #region Public API
    /// <summary>Captures settings and supplies deterministic defaults for a test.</summary>
    public AutoStashTestScope()
    {
        enabled = config.EnableAutoStash;
        delay = config.AutoStashDelay;
        config.EnableAutoStash = true;
        config.AutoStashDelay = 0.5f;
    }

    /// <summary>Restores settings on the original configuration object.</summary>
    public void Dispose()
    {
        config.EnableAutoStash = enabled;
        config.AutoStashDelay = delay;
    }
    #endregion
}
