using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaExpanded.HudOverlays.Configuration;

/// <summary>Validates enum type and membership before changing a feature's style.</summary>
public sealed class HudOverlayStyleSetting<T> : IHudOverlayStyleSetting where T : struct, Enum
{
    private readonly HudOverlaySetting<T> setting;
    #region Public API
    /// <summary>Offers the defined values of the feature's style enum.</summary>
    public HudOverlayStyleSetting(Func<T> read, Action<T> write, Action commit)
    {
        setting = new(read, write, commit);
        Choices = Array.AsReadOnly(Enum.GetValues<T>().Select(value => (Enum)(object)value).ToArray());
    }
    /// <inheritdoc />
    public Enum Value => setting.Value;
    /// <inheritdoc />
    public IReadOnlyList<Enum> Choices { get; }
    /// <inheritdoc />
    public void Set(Enum value)
    {
        if (value is not T typed || !Enum.IsDefined(typed)) throw new ArgumentOutOfRangeException(nameof(value));
        setting.Set(typed);
    }
    #endregion
}
