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
    // Server spawn state outlives the optional rendering resources.
    private Vec3d? spawnPosition;
    #endregion

    #region ModSystem Overrides
    /// <summary>Loads the spawn decal system only on the client.</summary>
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Client;
    }

    /// <summary>Registers spawn updates and applies the current decal configuration.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        // Register network handler
        var channel = api.Network.GetChannel(Mod.Info.ModID);
        channel?.SetMessageHandler<Packet_TemporalSpawn>(OnTemporalSpawnPacket);

        ApplyConfig(api);
    }

    /// <summary>Releases the renderer and client references.</summary>
    public override void Dispose()
    {
        DisposeRenderer();

        spawnPosition = null;
        capi = null;
        base.Dispose();
    }

    /// <summary>Applies live configuration on the client.</summary>
    public void OnConfigReloaded(ICoreAPI api)
    {
        if (api is not ICoreClientAPI clientApi) return;
        ApplyConfig(clientApi);
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Retains the latest spawn position and updates the decal when rendering is enabled.
    /// </summary>
    public void SetSpawnPosition(Vec3d position)
    {
        spawnPosition = position.Clone();
        renderer?.SetSpawnPosition(spawnPosition);
    }

    /// <summary>
    /// Clears the retained spawn position and triggers fade-out on an existing decal.
    /// </summary>
    public void ClearSpawnPosition()
    {
        spawnPosition = null;
        renderer?.ClearSpawnPosition();
    }
    #endregion

    #region Live Reload
    /// <summary>Creates, removes, or resizes the renderer to match configuration.</summary>
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
            if (spawnPosition is not null) renderer.SetSpawnPosition(spawnPosition);
            lastDecalSize = VanillaExpandedModSystem.Config.SpawnDecalSize;
            return;
        }

        float currentSize = VanillaExpandedModSystem.Config.SpawnDecalSize;
        if (lastDecalSize is null || Math.Abs(lastDecalSize.Value - currentSize) > 0.0001f)
        {
            renderer.ReloadMesh();
            lastDecalSize = currentSize;
        }
    }

    /// <summary>Unregisters the renderer from its owning stage before disposing its mesh.</summary>
    private void DisposeRenderer()
    {
        if (renderer is null || capi is null) return;

        capi.Event.UnregisterRenderer(renderer, SpawnDecalRenderer.RenderStage);
        renderer.Dispose();
        renderer = null;
        lastDecalSize = null;
    }
    #endregion

    #region Network Handlers
    /// <summary>Updates the decal position or starts its fade from server spawn data.</summary>
    private void OnTemporalSpawnPacket(Packet_TemporalSpawn packet)
    {
        // Keep authoritative updates even while the decal is hidden, so re-enabling uses current state.
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
