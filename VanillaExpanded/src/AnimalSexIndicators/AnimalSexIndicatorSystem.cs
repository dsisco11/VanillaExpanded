using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.AnimalSexIndicators;

/// <summary>Owns the client animal indicator renderer and its world lifecycle.</summary>
internal sealed class AnimalSexIndicatorSystem : ModSystem
{
    private ICoreClientAPI? api;
    private AnimalSexIndicatorRenderer? renderer;

    #region Public API
    /// <summary>Loads animal indicators only on the client.</summary>
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    /// <summary>Registers world lifecycle hooks without allocating GPU resources before entering a world.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.LeaveWorld += OnLeaveWorld;
    }

    /// <summary>Unsubscribes lifecycle hooks and releases any remaining renderer resources.</summary>
    public override void Dispose()
    {
        if (api is not null)
        {
            api.Event.LevelFinalize -= OnLevelFinalize;
            api.Event.LeaveWorld -= OnLeaveWorld;
        }
        OnLeaveWorld();
        api = null;
        base.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Creates the two shared icon resources once the client world is ready.</summary>
    private void OnLevelFinalize()
    {
        OnLeaveWorld();
        if (api is not null) renderer = new AnimalSexIndicatorRenderer(api);
    }

    /// <summary>Removes the renderer before releasing its GPU resources.</summary>
    private void OnLeaveWorld()
    {
        if (renderer is null || api is null) return;
        api.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
        renderer.Dispose();
        renderer = null;
    }
    #endregion
}
