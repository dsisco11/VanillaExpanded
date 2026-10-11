using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Owns bow applicability, stable ammunition samples, notifications, and passive presentation.</summary>
internal sealed class BowAmmunitionOverlay : IHudOverlay
{
    private readonly Action invalidate;
    private readonly HudOverlayIconTextPresentation presentation;
    private readonly HudOverlayIconTextPresentation emptyPresentation;
    private ICoreClientAPI? api;
    private IPlayer? player;
    private BowAmmunitionInventorySubscriptions? subscriptions;
    private bool enabled = true;
    private bool disposed;
    internal BowAmmunitionSample? Sample { get; private set; }
    #region Public API
    #region Lifecycle
    /// <summary>Uses cached native icon/count presentation and muted text for the generic empty-arrow state.</summary>
    public BowAmmunitionOverlay(Action invalidate, HudOverlayIconTextPresentation? presentation = null,
        HudOverlayIconTextPresentation? emptyPresentation = null)
    {
        this.invalidate = invalidate;
        this.presentation = presentation ?? new(iconSize: 24, textBeforeIcon: true, gap: 1, fontSize: 14, circularIconBackground: true, useItemPresentation: true);
        this.emptyPresentation = emptyPresentation ?? new(CreateMutedText);
    }
    /// <summary>Binds one fresh player session with no retained arrow sample.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (this.api != null) throw new InvalidOperationException("A bow overlay session is already bound.");
        this.api = api;
        player = api.World.Player;
        if (enabled && player != null) subscriptions = new(api, player, invalidate);
    }
    /// <summary>Suspends subscriptions and presentation resources immediately, preserving registration ownership.</summary>
    public bool SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (enabled == value) return value;
        enabled = value;
        if (!value)
        {
            subscriptions?.Dispose(); subscriptions = null;
            ClearSample();
        }
        else if (api != null && player != null)
        {
            subscriptions = new(api, player, invalidate);
            invalidate();
        }
        return value;
    }
    /// <summary>Detaches session handlers and clears owned resources without disposing borrowed objects.</summary>
    public void EndSession()
    {
        subscriptions?.Dispose(); subscriptions = null;
        api = null; player = null;
        ClearSample();
    }
    /// <summary>Finally releases the consumer and its presentation helpers exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { EndSession(); }
        finally
        {
            try { presentation.Dispose(); }
            finally { emptyPresentation.Dispose(); }
        }
    }
    #endregion
    #region Sampling
    /// <summary>Checks only current hand, player identity and life state; switching away discards stale content.</summary>
    public bool IsApplicable()
    {
        bool applicable = enabled && api != null && player != null && ReferenceEquals(api.World.Player, player)
            && player.Entity?.Alive == true && BowAmmunitionSelector.IsSupported(player.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible);
        if (!applicable && Sample != null) ClearSample();
        return applicable;
    }
    /// <summary>Reconciles available notifications and samples current synchronized ammunition without prediction.</summary>
    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        subscriptions?.Reconcile();
        ItemBow bow = (ItemBow)player!.InventoryManager.ActiveHotbarSlot.Itemstack.Collectible;
        BowAmmunitionSample next = BowAmmunitionSampler.Sample(bow, player.Entity!);
        bool changed = Sample == null || Sample.Quantity != next.Quantity
            || !ReferenceEquals(Sample.Icon?.Collectible, next.Icon?.Collectible)
            || (Sample.Icon != null && next.Icon != null && !System.Linq.Enumerable.SequenceEqual(Sample.Icon.ToBytes(), next.Icon.ToBytes()));
        Sample = next;
        if (!changed) return HudOverlayChange.None;
        if (next.Icon == null) emptyPresentation.SetContent(null, "vanillaexpanded:bow-ammunition-empty");
        else presentation.SetContent(next.Icon, "vanillaexpanded:bow-ammunition-count", next.Quantity);
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion
    #region Presentation
    /// <summary>Prepares only sampled content in the shared scheduler's resource boundary.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Sample != null) CurrentPresentation.Prepare(context);
    }
    /// <summary>Reports cached dimensions without accessing inventory.</summary>
    public SizeF Measure() => Sample == null ? SizeF.Empty : CurrentPresentation.Size;
    /// <summary>Draws the cached icon/count or muted empty state without gameplay queries.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        if (Sample != null) CurrentPresentation.Draw(renderer, bounds, clip, deltaTime);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Selects the cached presentation using sampled state only.</summary>
    private HudOverlayIconTextPresentation CurrentPresentation => Sample?.Icon == null ? emptyPresentation : presentation;
    /// <summary>Releases both possible cached presentations and removes the old sampled arrow.</summary>
    private void ClearSample()
    {
        Sample = null;
        try { presentation.Reset(); }
        finally { emptyPresentation.Reset(); }
    }
    /// <summary>Renders the generic arrow glyph and localized empty state with muted accessible contrast.</summary>
    private static LoadedTexture CreateMutedText(HudOverlayPreparationContext context, string text)
    {
        var font = CairoFont.WhiteSmallText().WithFontSize(14).WithColor(new double[] { .7, .7, .7, 1 })
            .WithStroke(new double[] { 0, 0, 0, .85 }, 1.5);
        return context.Api.Gui.TextTexture.GenTextTexture(text, font);
    }
    #endregion
}
