namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Runs explicitly requested clock shader checks in a hidden native context.</summary>
public sealed class ClockGpuFactAttribute : FactAttribute
{
    #region Public API
    /// <summary>Keeps ordinary unit runs independent of an installed graphics driver.</summary>
    public ClockGpuFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VE_CLOCK_GPU_TESTS") != "1")
            Skip = "Set VE_CLOCK_GPU_TESTS=1 and include the installed game Lib directory on PATH to run hidden GPU checks.";
    }
    #endregion
}
