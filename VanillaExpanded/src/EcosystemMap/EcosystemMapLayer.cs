using VanillaExpanded.EcosystemMap.Registration;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.EcosystemMap;

/// <summary>Routes ecosystem data through vanilla's world-map envelope and exposes its world lifecycle to the feature owner.</summary>
public sealed class EcosystemMapLayer : MapLayer
{
    public override string Title => Lang.Get("maplayer-vanillaexpandedecosystem");
    public override string LayerGroupCode => "vanillaexpandedecosystem";
    public override EnumMapAppSide DataSide => EnumMapAppSide.Server;
    public override bool RequireChunkLoaded => false;
    private EcosystemMapSystem? owner;

    #region Public API
    /// <summary>Creates an initially inactive layer; environmental capture does not depend on tab activation.</summary>
    public EcosystemMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink) { Active = false; }

    /// <summary>Starts ordinary acquisition after all map-layer constructors have registered vanilla's purge command.</summary>
    public override void OnLoaded()
    {
        owner = api.ModLoader.GetModSystem<EcosystemMapSystem>();
        owner.LoadLayer(this);
    }

    /// <summary>Accepts only server-originated payloads on the existing registered layer route.</summary>
    public override void OnDataFromServer(byte[] data) => owner?.Client?.Receive(data);

    /// <summary>Stops intake and joins this feature's writes rather than relying on vanilla's bounded map-thread wait.</summary>
    public override void OnShutDown() => owner?.StopWorld();

    /// <summary>Provides an idempotent map-manager disposal fallback.</summary>
    public override void Dispose() => owner?.StopWorld();
    #endregion
}
