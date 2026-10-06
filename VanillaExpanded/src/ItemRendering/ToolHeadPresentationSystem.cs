using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.ItemRendering;

/// <summary>Owns the client renderer lifetime without patching engine rendering.</summary>
internal sealed class ToolHeadPresentationSystem : ModSystem
{
    internal static ToolHeadPresentationRenderer? Renderer { get; private set; }

    #region Public API
    /// <summary>Restricts the GUI renderer to the client host.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>Exposes the client-lifetime prepared-item renderer.</summary>
    public override void StartClientSide(ICoreClientAPI api) => Renderer = new ToolHeadPresentationRenderer(api);

    /// <summary>Releases the client owner without disposing borrowed meshes or shader programs.</summary>
    public override void Dispose()
    {
        Renderer = null;
        base.Dispose();
    }
    #endregion
}
