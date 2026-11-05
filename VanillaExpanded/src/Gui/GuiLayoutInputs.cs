using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Gui;

internal readonly record struct GuiMeasurementInputs(double Scale,
    GuiLanguageInputs CurrentLanguage, GuiLanguageInputs DefaultLanguage)
{
    /// <summary>Captures the GUI scale and current/default language snapshots used for text measurement.</summary>
    internal static GuiMeasurementInputs Capture() => new(GuiElement.scaled(1),
        GuiLanguageInputs.Capture(Lang.CurrentLocale), GuiLanguageInputs.Capture(Lang.DefaultLocale));

    /// <summary>Checks whether GUI scale and both language snapshots still describe equivalent measurement inputs.</summary>
    internal bool Matches(GuiMeasurementInputs other) => Scale == other.Scale
        && CurrentLanguage.Matches(other.CurrentLanguage) && DefaultLanguage.Matches(other.DefaultLanguage);
}

internal readonly record struct GuiBoundsSnapshot(ElementBounds? Bounds, double X, double Y, double Width, double Height)
{
    /// <summary>Captures bounds identity and inner dimensions in screen pixels, excluding position.</summary>
    internal static GuiBoundsSnapshot CaptureInnerSize(ElementBounds bounds) =>
        new(bounds, 0, 0, bounds.InnerWidth, bounds.InnerHeight);

    /// <summary>Captures bounds identity, absolute position, and padded width in screen pixels, excluding height.</summary>
    internal static GuiBoundsSnapshot CapturePositionAndOuterWidth(ElementBounds bounds) =>
        new(bounds, bounds.absX, bounds.absY, bounds.OuterWidth, 0);

    /// <summary>Captures bounds identity, absolute position, and padded dimensions in screen pixels.</summary>
    internal static GuiBoundsSnapshot CaptureOuterRect(ElementBounds bounds) =>
        new(bounds, bounds.absX, bounds.absY, bounds.OuterWidth, bounds.OuterHeight);

    /// <summary>Captures bounds identity and unscaled fixed dimensions, excluding position and padding.</summary>
    internal static GuiBoundsSnapshot CaptureFixedSize(ElementBounds bounds) =>
        new(bounds, 0, 0, bounds.fixedWidth, bounds.fixedHeight);

    /// <summary>Compares bounds reference identity and captured scalar values without reading live geometry.</summary>
    internal bool Matches(GuiBoundsSnapshot other) => ReferenceEquals(Bounds, other.Bounds)
        && X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
}

// Store geometry, not composer-owned mutable bounds, so a cached result cannot move the live root.
internal readonly record struct GuiDialogPlacement(ElementBounds Window, double X, double Y,
    double Width, double Height)
{
    /// <summary>Creates fresh fixed bounds at the stored GUI-unit position and dimensions, parented to the placement window.</summary>
    internal ElementBounds ToBounds() => ElementBounds.Fixed(X, Y, Width, Height).WithParent(Window);
}
