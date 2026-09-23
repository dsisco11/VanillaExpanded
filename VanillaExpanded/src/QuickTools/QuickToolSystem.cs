using VanillaExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Registers the client quick-tool owner using the game's ordinary inventory networking.</summary>
internal sealed class QuickToolSystem : ModSystem, ILiveConfigurable
{
    private QuickToolClientOperations? client;

    #region Host lifecycle
    /// <summary>Attaches the client equipment owner and its lifecycle hooks.</summary>
    public override void StartClientSide(ICoreClientAPI api) => client = new QuickToolClientOperations(api);

    /// <summary>Clears transient restoration history when the feature is disabled.</summary>
    public void OnConfigReloaded(ICoreAPI api)
    {
        if (!VanillaExpandedModSystem.Config.EnableQuickTools) client?.Clear();
    }

    /// <summary>Releases the client owner and its subscriptions on shutdown.</summary>
    public override void Dispose()
    {
        client?.Dispose();
        client = null;
        base.Dispose();
    }
    #endregion

    #region Client API
    /// <summary>Gets the operations consumed by the later keybind integration.</summary>
    internal QuickToolClientOperations? Client => client;
    #endregion
}
