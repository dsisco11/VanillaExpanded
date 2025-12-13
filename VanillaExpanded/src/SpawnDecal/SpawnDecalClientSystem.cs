using VanillaExpanded.Network;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.SpawnDecal;

/// <summary>
/// Client-side mod system for managing the spawn decal renderer.
/// </summary>
public class SpawnDecalClientSystem : ModSystem
{
    #region Fields
    private ICoreClientAPI? capi;
    private SpawnDecalRenderer? renderer;
    #endregion

    #region ModSystem Overrides
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        // Create and register the renderer
        renderer = new SpawnDecalRenderer(api);
        api.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "spawndecal");

        // Register network handler
        var channel = api.Network.GetChannel(Mod.Info.ModID);
        channel?.SetMessageHandler<Packet_TemporalSpawn>(OnTemporalSpawnPacket);
    }

    public override void Dispose()
    {
        if (renderer != null && capi != null)
        {
            capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
            renderer.Dispose();
            renderer = null;
        }

        capi = null;
        base.Dispose();
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Sets the spawn position for the decal.
    /// </summary>
    public void SetSpawnPosition(Vec3d position)
    {
        renderer?.SetSpawnPosition(position);
    }

    /// <summary>
    /// Clears the spawn position, triggering fade-out.
    /// </summary>
    public void ClearSpawnPosition()
    {
        renderer?.ClearSpawnPosition();
    }
    #endregion

    #region Network Handlers
    private void OnTemporalSpawnPacket(Packet_TemporalSpawn packet)
    {
        if (packet.HasSpawn)
        {
            SetSpawnPosition(new Vec3d(packet.X, packet.Y, packet.Z));
        }
        else
        {
            ClearSpawnPosition();
        }
    }
    #endregion
}
