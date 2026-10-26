using System;
using System.Collections.Generic;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Transfers unique component ownership to one composer and cleans up failed mounting.</summary>
internal abstract class NativeView : INativeView
{
    private readonly List<GuiElement> elements = new();
    private readonly ICoreClientAPI api;
    private bool attemptedMount, mounted, disposed;

    #region Public API
    #region Composition and preparation
    /// <summary>Mounts once; failure retires the failed composer and all untransferred elements.</summary>
    public void AddTo(GuiComposer composer, ElementBounds memberBounds, string keyPrefix)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(memberBounds);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        if (attemptedMount) throw new InvalidOperationException("A native view can belong to only one composer.");
        if (composer.Composed) throw new InvalidOperationException("Mount components before composing the native tree.");
        if (!ReferenceEquals(composer.Api, api)) throw new ArgumentException("Composer and elements must borrow the same API.", nameof(composer));
        if (ReferenceEquals(composer.CurParentBounds, memberBounds))
            throw new ArgumentException("Member bounds must be distinct from the composer's current parent.", nameof(memberBounds));
        attemptedMount = true;
        try
        {
            composer.BeginChildElements(memberBounds);
            try { AddElements(composer, keyPrefix); }
            finally { composer.EndChildElements(); }
            mounted = true;
        }
        catch (Exception mountingFailure)
        {
            var failures = new List<Exception> { mountingFailure };
            // A native add can fail after entering one dictionary. Retire the entire failed generation;
            // custom elements tolerate duplicate cleanup, including untransferred leftovers below.
            try { composer.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { Dispose(); } catch (Exception error) { failures.Add(error); }
            throw new AggregateException("Native view mounting failed.", failures);
        }
    }
    /// <summary>Updates component resources from stable feature data before native layout.</summary>
    public abstract void Prepare(HudOverlayPreparationContext context);
    /// <summary>Caches component geometry after native bounds resolve.</summary>
    public abstract void LayoutCompleted(RectangleF clip);
    #endregion
    #region Rendering and lifetime
    /// <summary>Updates per-view opacity independently of prepared resources.</summary>
    public abstract void SetOpacity(float opacity);
    /// <summary>Submits prepared backdrop geometry without arranging components.</summary>
    public abstract void CollectBackdrops(IRenderAPI renderer);
    /// <summary>Binds native component failures to the host's feature-specific error boundary.</summary>
    public abstract void SetFailureHandler(Action<Exception> failure);
    /// <summary>Releases unmounted elements; mounted elements remain exclusively composer-owned.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        if (!mounted)
            foreach (var element in elements)
                try { element.Dispose(); }
                catch (Exception error) { (failures ??= new()).Add(error); }
        elements.Clear();
        if (failures != null) throw new AggregateException(failures);
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Retains one borrowed API identity for the entire visual instance.</summary>
    protected NativeView(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        this.api = api;
    }
    /// <summary>Tracks an element's ownership before any potentially failing native registration.</summary>
    protected T Own<T>(T element) where T : GuiElement
    {
        ThrowIfDisposed();
        if (mounted) throw new InvalidOperationException("Mounted component ownership is already transferred.");
        elements.Add(element);
        return element;
    }
    /// <summary>Registers the view's unique elements under the current native parent.</summary>
    protected abstract void AddElements(GuiComposer composer, string prefix);
    /// <summary>Rejects changes after the view has relinquished its bookkeeping.</summary>
    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    #endregion
}
