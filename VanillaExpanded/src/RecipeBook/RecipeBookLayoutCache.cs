using System;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;

namespace VanillaExpanded.RecipeBook;

internal readonly record struct RecipeBookMeasurementInputs(GuiMeasurementInputs Gui, int TileSize)
{
    /// <summary>Combines the current shared GUI measurement inputs with the supplied recipe tile size.</summary>
    internal static RecipeBookMeasurementInputs Capture(int tileSize) => new(GuiMeasurementInputs.Capture(), tileSize);

    /// <summary>Checks whether tile size, GUI scale, and language snapshots describe equivalent recipe measurements.</summary>
    internal bool Matches(RecipeBookMeasurementInputs other) => TileSize == other.TileSize && Gui.Matches(other.Gui);
}

internal sealed class RecipeBookLayoutCache(Action measure)
{
    private readonly record struct PlacementInputs(bool Movable, double Scale, double MinimumWidth,
        double MinimumHeight, GuiBoundsSnapshot Window, GuiBoundsSnapshot Parent, GuiBoundsSnapshot Grid,
        GuiBoundsSnapshot Output, GuiBoundsSnapshot Dialog)
    {
        /// <summary>Captures placement dependencies for the selected mode, excluding manual position from Movable size inputs.</summary>
        internal static PlacementInputs Capture(bool movable, ElementBounds dialog, ElementBounds parent,
            ElementBounds grid, ElementBounds output, ElementBounds window, double minimumWidth, double minimumHeight) =>
            new(movable, GuiElement.scaled(1), minimumWidth, minimumHeight, GuiBoundsSnapshot.CaptureInnerSize(window),
                movable ? default : GuiBoundsSnapshot.CapturePositionAndOuterWidth(parent),
                movable ? default : GuiBoundsSnapshot.CaptureOuterRect(grid),
                movable ? default : GuiBoundsSnapshot.CaptureOuterRect(output),
                movable ? GuiBoundsSnapshot.CaptureFixedSize(dialog) : default);

        /// <summary>Compares movement mode, scale, minimum dimensions, and the captured geometry and bounds identities.</summary>
        internal bool Matches(PlacementInputs other) => Movable == other.Movable && Scale == other.Scale
            && MinimumWidth == other.MinimumWidth && MinimumHeight == other.MinimumHeight
            && Window.Matches(other.Window) && Parent.Matches(other.Parent) && Grid.Matches(other.Grid)
            && Output.Matches(other.Output) && Dialog.Matches(other.Dialog);
    }

    private RecipeBookMeasurementInputs? measurementInputs;
    private PlacementInputs? placementInputs;
    private GuiDialogPlacement? placement;

    /// <summary>
    /// Invokes the measurement callback only when inputs change, returning whether it ran.
    /// Clears prior measurement and placement entries before recomputing, even if minimum dimensions remain unchanged.
    /// </summary>
    internal bool EnsureMeasurements(RecipeBookMeasurementInputs inputs)
    {
        if (measurementInputs is { } previous && previous.Matches(inputs)) return false;
        measurementInputs = null;
        placementInputs = null;
        placement = null;
        measure();
        measurementInputs = inputs;
        return true;
    }

    /// <summary>
    /// Reuses successful placement or recomputes it when dependencies change, returning null when the dialog cannot fit.
    /// Clamps live Movable coordinates without invalidating size placement and clears prior success before recomputation.
    /// </summary>
    internal GuiDialogPlacement? EnsurePlacement(bool movable, ElementBounds dialog, ElementBounds parent,
        ElementBounds grid, ElementBounds output, ElementBounds window, double minimumWidth, double minimumHeight)
    {
        var inputs = PlacementInputs.Capture(movable, dialog, parent, grid, output, window, minimumWidth, minimumHeight);
        if (placementInputs is not { } previous || !previous.Matches(inputs))
        {
            placementInputs = null;
            placement = null;
            var bounds = movable
                ? RecipeBookLayout.PlaceMovable(dialog, window, minimumWidth, minimumHeight)
                : RecipeBookLayout.Place(parent, grid, output, window, minimumWidth, minimumHeight);
            if (bounds == null) return null;
            placement = new(window, bounds.fixedX, bounds.fixedY, bounds.fixedWidth, bounds.fixedHeight);
            placementInputs = inputs;
        }
        var result = placement!.Value;
        return movable
            ? result with
            {
                X = Math.Clamp(dialog.fixedX, 0, window.InnerWidth / inputs.Scale - result.Width),
                Y = Math.Clamp(dialog.fixedY, 0, window.InnerHeight / inputs.Scale - result.Height)
            }
            : result;
    }

    /// <summary>
    /// Commits composed geometry and the Movable root's identity so replacement or clamping does not cause another cache miss.
    /// Requires a successful placement and bounds parented to that placement's window.
    /// </summary>
    internal void AcceptComposedBounds(ElementBounds bounds)
    {
        if (placementInputs is not { } inputs || placement is not { } result)
            throw new InvalidOperationException("Recipe book composition requires a successful placement.");
        if (!ReferenceEquals(bounds.ParentBounds, result.Window))
            throw new InvalidOperationException("Recipe book composition must use the placement window.");
        placement = result with
        {
            X = bounds.fixedX, Y = bounds.fixedY, Width = bounds.fixedWidth, Height = bounds.fixedHeight
        };
        if (inputs.Movable) placementInputs = inputs with { Dialog = GuiBoundsSnapshot.CaptureFixedSize(bounds) };
    }
}
