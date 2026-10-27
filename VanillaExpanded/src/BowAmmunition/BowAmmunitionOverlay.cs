using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Owns bow applicability, stable ammunition samples, notifications, and passive presentation.</summary>
internal sealed class BowAmmunitionOverlay : IHudOverlay, INativePresentation, IHudOverlayVisibilityTransition
{
    private readonly Action invalidate;
    private readonly BowAmmunitionPresentation presentation;
    private ICoreClientAPI? api;
    private IPlayer? player;
    private BowAmmunitionInventorySubscriptions? subscriptions;
    private bool enabled = true;
    private bool disposed;
    internal BowAmmunitionSample? Sample { get; private set; }
    private bool styleChanged;
    /// <summary>Controls visibility independently of the current ammunition sample.</summary>
    public HudOverlayVisibilityTransition Visibility => presentation.Visibility;
    /// <summary>Retains the last preparation for the shared host's exit transition.</summary>
    public void HidePresentation() => ClearSample();
    #region Public API
    #region Lifecycle
    /// <summary>Retains one gameplay owner and prepared native presentation state.</summary>
    public BowAmmunitionOverlay(Action invalidate, BowAmmunitionPresentation? presentation = null)
    {
        this.invalidate = invalidate;
        this.presentation = presentation ?? new();
    }
    /// <summary>Changes visual structure without changing selection or inventory subscriptions.</summary>
    public void SetStyle(HudIndicatorStyle value)
    {
        if (!presentation.SetStyle(value)) return;
        styleChanged = true; invalidate();
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
        presentation.Reset();
    }
    /// <summary>Finally releases the consumer and its prepared presentation state exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { EndSession(); } finally { presentation.Dispose(); }
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
        presentation.SetContent(next);
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion
    #region Presentation
    /// <summary>Creates independent native text and arrow elements without sampling.</summary>
    public INativeView CreateView(ICoreClientAPI api, NativeViewRole role) => presentation.CreateView(api);
    /// <summary>Ammunition has no shared per-frame visual effect.</summary>
    public void BeginFrame(float deltaTime) { }
    /// <summary>Prepares localized snapshot data before native component composition.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Sample != null) presentation.Prepare(context);
    }
    /// <summary>Reports cached dimensions without accessing inventory.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Requires native rendering through the registered component elements.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        throw new InvalidOperationException("Native indicator components render through their composer.");
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Clears the current sample while retaining prepared arrow data for a visible exit.</summary>
    private void ClearSample()
    {
        Sample = null;
        presentation.Hide();
    }

    #endregion
}
