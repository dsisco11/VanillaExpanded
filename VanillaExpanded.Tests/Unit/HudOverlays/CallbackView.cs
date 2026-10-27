using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Provides a real native component with observable render callbacks for host boundary tests.</summary>
internal sealed class CallbackView : NativeView
{
    private readonly CallbackElement element;
    private readonly Action? backdrop;

    #region Public API
    /// <summary>Gets the native component's resolved bounds for geometry forwarding assertions.</summary>
    public ElementBounds Bounds => element.Bounds;
    #region Preparation
    /// <summary>Creates one uniquely owned passive component without feature sampling or resource allocation.</summary>
    public CallbackView(ICoreClientAPI api, Action<ElementBounds, float>? draw = null, Action? backdrop = null) : base(api)
    {
        element = Own(new CallbackElement(api, draw)); this.backdrop = backdrop;
    }
    /// <summary>Requires no resource preparation for the synthetic component.</summary>
    public override void Prepare(HudOverlayPreparationContext context) { }
    /// <summary>Caches the host's resolved geometry and clip before rendering.</summary>
    public override void LayoutCompleted(RectangleF clip) => element.State.SetGeometry(new((float)element.Bounds.renderX,
        (float)element.Bounds.renderY, (float)element.Bounds.OuterWidth, (float)element.Bounds.OuterHeight), clip);
    #endregion
    #region Rendering state
    /// <summary>Retains the host multiplier independently of component preparation.</summary>
    public override void SetOpacity(float opacity) => element.State.SetOpacity(opacity);
    /// <summary>Exposes backdrop ordering and failure behavior through the actual native view boundary.</summary>
    public override void CollectBackdrops(IRenderAPI renderer)
    {
        using var opacity = new DrawOpacity(element.State.Opacity); backdrop?.Invoke();
    }
    /// <summary>Uses the same per-feature native failure routing as production components.</summary>
    public override void SetFailureHandler(Action<Exception> failure) => element.State.Failure = failure;
    #endregion
    #endregion

    #region Protected API
    /// <summary>Registers the observable component with the owning native composer.</summary>
    protected override void AddElements(GuiComposer composer, string prefix) => composer.AddInteractiveElement(element, prefix + ":callback");
    #endregion

    /// <summary>Records native component drawing without providing a manual indicator dispatch API.</summary>
    private sealed class CallbackElement : GuiElement
    {
        private readonly Action<ElementBounds, float>? draw;
        private readonly Action submit;
        private float elapsed;
        private bool disposed;

        #region Public API
        /// <summary>Gets cached geometry, fade and failure isolation.</summary>
        public ElementState State { get; } = new(_ => { });
        /// <summary>Creates a passive native probe for rendering boundaries.</summary>
        public CallbackElement(ICoreClientAPI api, Action<ElementBounds, float>? draw) : base(api, ElementBounds.Fill)
        {
            this.draw = draw; submit = Submit;
        }
        /// <summary>Invokes the observation inside the production cached scissor boundary.</summary>
        public override void RenderInteractiveElements(float deltaTime)
        {
            elapsed = deltaTime;
            if (!disposed) State.Render(api.Render, submit);
        }
        /// <summary>Leaves passive mouse presses unhandled.</summary>
        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
        /// <summary>Stops callbacks after the native composer retires.</summary>
        public override void Dispose() => disposed = true;
        #endregion

        #region Private
        /// <summary>Exposes settled opacity to existing effect assertions without sharing production resources.</summary>
        private void Submit()
        {
            using var opacity = new DrawOpacity(State.Opacity); draw?.Invoke(Bounds, elapsed);
        }
        #endregion
    }
}
