using System;
using System.Drawing;
using System.Globalization;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Owns bow applicability, stable ammunition samples, notifications, and passive presentation.</summary>
internal sealed class BowAmmunitionOverlay : IHudOverlay, IHudOverlayVisibilityTransition
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
    private HudOverlayIconTextPresentation? preparedPresentation;
    private readonly HudIndicatorTextPresentation textStyle;
    private HudIndicatorStyle style;
    private bool styleChanged, preparedText;
    /// <summary>Controls visibility independently of the current ammunition sample.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new();
    /// <summary>Retains the last preparation for the shared host's exit transition.</summary>
    public void HidePresentation() => ClearSample();
    #region Public API
    #region Lifecycle
    /// <summary>Uses cached native icon/count presentation and muted text for the generic empty-arrow state.</summary>
    public BowAmmunitionOverlay(Action invalidate, HudOverlayIconTextPresentation? presentation = null,
        HudOverlayIconTextPresentation? emptyPresentation = null, HudIndicatorTextPresentation? textStyle = null)
    {
        this.invalidate = invalidate;
        this.presentation = presentation ?? new(iconSize: HudOverlaySizes.Small, gap: HudOverlaySizes.ReadingGap, fontSize: HudOverlaySizes.Text, circularIconBackground: true, useItemPresentation: true)
        { Direction = VanillaExpanded.HudOverlays.Layout.HudOverlayDirection.Vertical };
        this.emptyPresentation = emptyPresentation ?? new(CreateMutedText);
        this.textStyle = textStyle ?? new();
    }
    /// <summary>Changes symbol style without changing the chosen arrow, inventory subscriptions, or visibility.</summary>
    public void SetStyle(HudIndicatorStyle value)
    {
        var next = value == HudIndicatorStyle.Text ? value : HudIndicatorStyle.Icon;
        if (next == style) return;
        presentation.Reset(); emptyPresentation.Reset(); textStyle.Reset();
        preparedPresentation = null; preparedText = false;
        style = next; styleChanged = true;
        invalidate();
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
    /// <summary>Suspends subscriptions and starts the presentation exit, preserving registration ownership.</summary>
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
        ReleasePresentation();
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
            finally { emptyPresentation.Dispose(); textStyle.Dispose(); }
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
        if (player is null) return HudOverlayChange.None;
        if (player.InventoryManager?.ActiveHotbarSlot?.Itemstack is null) return HudOverlayChange.None;

        subscriptions?.Reconcile();
        ItemBow bow = (ItemBow)player.InventoryManager.ActiveHotbarSlot.Itemstack.Collectible;
        BowAmmunitionSample next = BowAmmunitionSampler.Sample(bow, player.Entity!);
        bool changed = styleChanged || Sample == null || Sample.Quantity != next.Quantity
            || !ReferenceEquals(Sample.Icon?.Collectible, next.Icon?.Collectible)
            || (Sample.Icon != null && next.Icon != null && !System.Linq.Enumerable.SequenceEqual(Sample.Icon.ToBytes(), next.Icon.ToBytes()));
        Sample = next;
        styleChanged = false;
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
        if (Sample != null)
        {
            if (style == HudIndicatorStyle.Text)
            {
                textStyle.Prepare(context, "ammunition", Sample.Quantity.ToString(CultureInfo.InvariantCulture));
                preparedText = true; preparedPresentation = null; Visibility.Show();
                return;
            }
            CurrentPresentation.Prepare(context);
            preparedText = false;
            preparedPresentation = CurrentPresentation;
            Visibility.Show();
        }
    }
    /// <summary>Reports cached dimensions without accessing inventory.</summary>
    public SizeF Measure() => Sample == null ? SizeF.Empty : style == HudIndicatorStyle.Text ? textStyle.Size : CurrentPresentation.Size;
    /// <summary>Draws the cached icon/count or muted empty state without gameplay queries.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        if (preparedText) textStyle.Draw(renderer, bounds);
        else preparedPresentation?.Draw(renderer, bounds, clip, deltaTime);
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
        Visibility.Hide(ReleasePresentation);
    }
    /// <summary>Releases cached artwork after fading or immediately at world exit.</summary>
    private void ReleasePresentation()
    {
        Visibility.Reset();
        textStyle.Reset(); preparedText = false;
        preparedPresentation = null;
        try { presentation.Reset(); }
        finally { emptyPresentation.Reset(); }
    }
    /// <summary>Renders the generic arrow glyph and localized empty state with muted accessible contrast.</summary>
    private static LoadedTexture CreateMutedText(HudOverlayPreparationContext context, string text)
    {
        var font = CairoFont.WhiteSmallText().WithFontSize(HudOverlaySizes.Text).WithColor(new double[] { .7, .7, .7, 1 })
            .WithStroke(new double[] { 0, 0, 0, .85 }, 1.5);
        return context.Api.Gui.TextTexture.GenTextTexture(text, font);
    }
    #endregion
}
