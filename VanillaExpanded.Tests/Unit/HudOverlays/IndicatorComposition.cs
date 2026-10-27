using System.Drawing;
using System.Reflection;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Exercises a real native view/composer generation with synthetic external upload services.</summary>
internal sealed class IndicatorComposition : IDisposable
{
    #region Public API
    /// <summary>Gets the actual native composer and registered elements.</summary>
    public GuiComposer Composer { get; }
    /// <summary>Gets the visual bookkeeping owned independently of its controller.</summary>
    public INativeView View { get; }
    /// <summary>Mounts and resolves one native generation before either rendering pass.</summary>
    public IndicatorComposition(ICoreClientAPI api, INativeView view, HudOverlayPreparationContext context, SizeF size,
        RectangleF? member = null)
    {
        View = view;
        var root = ElementBounds.Fixed(0, 0, 900, 900).WithEmptyParent();
        Composer = ((GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { api, root, "indicator-composition" }, null)!).OnlyDynamic();
        view.Prepare(context);
        var area = member ?? new RectangleF(19, 31, size.Width, size.Height);
        view.AddTo(Composer, ElementBounds.Fixed(area.X, area.Y, area.Width, area.Height), "test");
        Composer.Compose(false); view.LayoutCompleted(new(0, 0, 1800, 1800)); view.SetOpacity(1);
    }
    /// <summary>Updates prepared content without rebuilding the native component tree.</summary>
    public void Update(HudOverlayPreparationContext context)
    {
        View.Prepare(context);
        Composer.Bounds.MarkDirtyRecursive(); Composer.Bounds.CalcWorldBounds();
        View.LayoutCompleted(new(0, 0, 1800, 1800));
    }
    /// <summary>Releases composer-owned resources before retiring visual bookkeeping.</summary>
    public void Dispose()
    {
        Composer.Dispose(); View.Dispose();
    }
    #endregion
}
