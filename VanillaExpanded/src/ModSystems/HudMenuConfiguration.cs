using Vintagestory.API.Common;

namespace VanillaExpanded.ModSystems;

/// <summary>Persists HUD menu edits to the existing configuration and broadcasts the existing reload contract.</summary>
internal static class HudMenuConfiguration
{
    #region Public API
    /// <summary>Saves before notifying consumers, so failed storage does not publish an unpersisted edit.</summary>
    public static void Commit(ICoreAPI api)
    {
        api.StoreModConfig(VanillaExpandedModSystem.Config, Constants.ConfigFileName);
        LiveConfigReload.NotifyAll(api);
    }
    #endregion
}
