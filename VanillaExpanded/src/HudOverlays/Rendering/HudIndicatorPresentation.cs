using System;
using System.Collections.Generic;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns common style selection, cached text, measurement, and transition lifetime for stateful indicators.</summary>
internal abstract class HudIndicatorPresentation<TStyle> : IDisposable where TStyle : struct, Enum
{
    private readonly TStyle defaultStyle;
    private readonly TStyle textStyle;
    private readonly HudIndicatorTextPresentation text;

    #region Public API
    /// <summary>Gets the indicator's shared fade or flashing visibility transition.</summary>
    public HudOverlayVisibilityTransition Visibility { get; }
    /// <summary>Gets the prepared footprint in GUI units.</summary>
    public SizeF Size { get; protected set; }

    /// <summary>Changes only presentation resources, preserving sampled content and an active visibility transition.</summary>
    public bool SetStyle(TStyle value)
    {
        TStyle next = Enum.IsDefined(value) ? value : defaultStyle;
        if (EqualityComparer<TStyle>.Default.Equals(next, Style)) return false;
        // Artwork and text belong to the old mode; content and feature timers remain valid for the next preparation.
        ReleaseArtwork();
        text.Reset();
        Context = null;
        Size = SizeF.Empty;
        Style = next;
        return true;
    }

    /// <summary>Retains prepared content until the visibility exit completes.</summary>
    public void Hide() => Visibility.Hide(Reset);

    /// <summary>Releases presentation resources and sampled contents at world exit or completed fade-out.</summary>
    public void Reset()
    {
        Visibility.Reset();
        text.Reset();
        ReleaseArtwork();
        ClearContent();
        Context = null;
        Size = SizeF.Empty;
    }

    /// <summary>Ends common ownership; subclasses may additionally dispose their feature-specific helpers.</summary>
    public virtual void Dispose()
    {
        Reset();
        text.Dispose();
    }
    #endregion

    #region Protected API
    /// <summary>Defines this indicator's supported enum modes, optional text factory, and transition behavior.</summary>
    protected HudIndicatorPresentation(TStyle defaultStyle, TStyle textStyle,
        HudIndicatorTextPresentation? text = null, bool flash = false, Func<long>? clockMs = null)
    {
        this.defaultStyle = defaultStyle;
        this.textStyle = textStyle;
        Style = defaultStyle;
        this.text = text ?? new();
        Visibility = new(flash: flash, clockMs: clockMs);
    }

    /// <summary>Gets the validated style selected for the next preparation.</summary>
    protected TStyle Style { get; private set; }
    /// <summary>Gets or sets the context for currently prepared drawing resources.</summary>
    protected HudOverlayPreparationContext? Context { get; set; }
    /// <summary>Identifies the artwork-free text branch without depending on enum names or numeric values.</summary>
    protected bool IsTextStyle => EqualityComparer<TStyle>.Default.Equals(Style, textStyle);

    /// <summary>Prepares and measures the shared text branch when selected, leaving artwork preparation to the subclass otherwise.</summary>
    protected bool TryPrepareText(HudOverlayPreparationContext preparation, string shortName, string? reading)
    {
        if (!IsTextStyle) return false;
        text.Prepare(preparation, shortName, reading);
        Context = preparation;
        Size = text.Size;
        Visibility.Show();
        return true;
    }

    /// <summary>Draws the selected text branch with optional feature urgency, returning whether it handled rendering.</summary>
    protected bool TryDrawText(IRenderAPI renderer, ElementBounds bounds, float opacity = 1)
    {
        if (!IsTextStyle) return false;
        text.Draw(renderer, bounds, opacity);
        return true;
    }

    /// <summary>Releases only feature artwork and bindings, preserving sampled content and timing across style changes.</summary>
    protected abstract void ReleaseArtwork();
    /// <summary>Clears feature content and timers after completed exits or world changes.</summary>
    protected abstract void ClearContent();
    #endregion
}
