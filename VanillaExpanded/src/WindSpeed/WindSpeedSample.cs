using System;

namespace VanillaExpanded.WindSpeed;

/// <summary>Classifies native local wind magnitude with stable light, moderate, and strong bands.</summary>
internal static class WindSpeedSample
{
    #region Public API
    /// <summary>Retains each active band until its lower exit boundary is crossed; invalid readings are calm.</summary>
    public static int Classify(float speed, int previous = 0)
    {
        if (!float.IsFinite(speed) || speed <= 0) return 0;
        ReadOnlySpan<float> entry = [.05f, .30f, .70f];
        ReadOnlySpan<float> exit = [.035f, .25f, .60f];
        int level = 0;
        // Check all boundaries so abrupt changes can cross several bands in one refresh.
        for (int index = 0; index < entry.Length; index++)
            if (speed > (previous > index ? exit[index] : entry[index])) level = index + 1;
        return level;
    }
    #endregion
}
