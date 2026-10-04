using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Registers the client quick-tool owner using the game's ordinary inventory networking.</summary>
internal sealed class QuickToolSystem : ModSystem
{
    private QuickToolClientOperations? client;
    private QuickToolClientIntegration? integration;

    #region Host lifecycle
    /// <summary>Attaches the client equipment owner and its lifecycle hooks.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        client = new QuickToolClientOperations(api);
        integration = new QuickToolClientIntegration(api, client);
    }

    /// <summary>Loads input and equipment integration only on the client.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>Releases the client owner and its subscriptions on shutdown.</summary>
    public override void Dispose()
    {
        integration?.Dispose();
        integration = null;
        client?.Dispose();
        client = null;
        base.Dispose();
    }
    #endregion

    #region Client API
    /// <summary>Gets the single client equipment owner.</summary>
    internal QuickToolClientOperations? Client => client;
    #endregion
}
