using Vintagestory.API.Common;

namespace VanillaExpanded.RadialProgress;

/// <summary>Adapts progress ownership to the existing radial progress mod system.</summary>
internal sealed class ProgressSystemProvider : IProgressSystemProvider
{
    private readonly ICoreAPI api;

    #region Public API
    /// <summary>Binds progress resolution to the owning interaction API.</summary>
    public ProgressSystemProvider(ICoreAPI api)
    {
        this.api = api;
    }

    /// <inheritdoc />
    public IRadialProgressBar? CreateProgressBar()
    {
        return api.ModLoader.GetModSystem<ModSystemRadialProgressBar>()?.AddProgressBar();
    }

    /// <inheritdoc />
    public void RemoveProgressBar(IRadialProgressBar progressBar)
    {
        api.ModLoader.GetModSystem<ModSystemRadialProgressBar>()?.RemoveProgressBar(progressBar);
    }
    #endregion
}
