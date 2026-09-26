using System;

using VanillaExpanded.Network;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.SpawnDecal;

using VanillaExpanded.ModSystems;

/// <summary>
/// Client-side mod system for managing the spawn decal renderer.
/// </summary>
public class SpawnDecalClientSystem : ModSystem, ILiveConfigurable
{
    #region Fields
    private ICoreClientAPI? capi;
    private SpawnDecalRenderer? renderer;
    private float? lastDecalSize;
    private bool? lastUseTemporalGear;
    #endregion

    #region ModSystem Overrides
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        // Register network handler
        var channel = api.Network.GetChannel(Mod.Info.ModID);
        channel?.SetMessageHandler<Packet_TemporalSpawn>(OnTemporalSpawnPacket);

        ApplyConfig(api);
    }

    public override void Dispose()
    {
        DisposeRenderer();

        capi = null;
        base.Dispose();
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (api is not ICoreClientAPI clientApi) return;
        ApplyConfig(clientApi);
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

    #region Live Reload
    private void ApplyConfig(ICoreClientAPI api)
    {
        if (!VanillaExpandedModSystem.Config.EnableSpawnDecal)
        {
            DisposeRenderer();
            return;
        }

        if (renderer is null)
        {
            renderer = new SpawnDecalRenderer(api);
            lastDecalSize = VanillaExpandedModSystem.Config.SpawnDecalSize;
            lastUseTemporalGear = VanillaExpandedModSystem.Config.UseTemporalGearSpawnMarker;
            return;
        }

        float currentSize = VanillaExpandedModSystem.Config.SpawnDecalSize;
        bool useTemporalGear = VanillaExpandedModSystem.Config.UseTemporalGearSpawnMarker;
        if (lastDecalSize is null
            || Math.Abs(lastDecalSize.Value - currentSize) > 0.0001f
            || lastUseTemporalGear != useTemporalGear)
        {
            renderer.ReloadMesh();
            lastDecalSize = currentSize;
            lastUseTemporalGear = useTemporalGear;
        }
    }

    private void DisposeRenderer()
    {
        if (renderer is null || capi is null) return;

        capi.Event.UnregisterRenderer(renderer, EnumRenderStage.OIT);
        renderer.Dispose();
        renderer = null;
        lastDecalSize = null;
        lastUseTemporalGear = null;
    }
    #endregion

    #region Network Handlers
    private void OnTemporalSpawnPacket(Packet_TemporalSpawn packet)
    {
        if (!VanillaExpandedModSystem.Config.EnableSpawnDecal) return;

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
