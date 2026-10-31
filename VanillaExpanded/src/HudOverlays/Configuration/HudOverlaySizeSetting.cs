using System;
using System.Drawing;

namespace VanillaExpanded.HudOverlays.Configuration;

/// <summary>Binds an absolute artwork width to proportional sizing of the complete indicator.</summary>
public sealed class HudOverlaySizeSetting
{
    public const int Minimum = 6;
    public const int Maximum = 300;
    public const int Step = 2;
    private readonly HudOverlaySetting<int> setting;

    public int DefaultSize { get; }
    public int Value => setting.Value;
    public double Scale => (double)Value / DefaultSize;

    public HudOverlaySizeSetting(int defaultSize, Func<int> read, Action<int> write, Action commit)
    {
        if (defaultSize != Normalize(defaultSize)) throw new ArgumentOutOfRangeException(nameof(defaultSize));
        DefaultSize = defaultSize;
        setting = new(read, write, commit);
    }

    public void Set(int value) => setting.Set(Normalize(value));

    /// <summary>Clamps persisted values and rounds halfway values up to the nearest two-unit step.</summary>
    public static int Normalize(int value) => (Math.Clamp(value, Minimum, Maximum) + Step / 2) / Step * Step;

    public SizeF Apply(SizeF size) => new((float)(size.Width * Scale), (float)(size.Height * Scale));
}
