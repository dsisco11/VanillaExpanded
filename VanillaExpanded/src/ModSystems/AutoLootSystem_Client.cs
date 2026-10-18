using HarmonyLib;
using VanillaExpanded.AutoLoot;
using VanillaExpanded.ModSystems;
using VanillaExpanded.RadialProgress;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded;

internal sealed class AutoLootSystem_Client : ModSystem, ILiveConfigurable
{
    private const string PatchOwner = Constants.ModId + ".autoloot.client";
    private readonly IProgressSystemProvider? progressSystem;
    private Harmony? harmony;
    internal static AutoLootSystem_Client? Active { get; private set; }
    internal AutoLootClient? Client { get; private set; }

    public AutoLootSystem_Client() { }

    internal AutoLootSystem_Client(IProgressSystemProvider progressSystem)
    {
        this.progressSystem = progressSystem;
    }

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        try
        {
            harmony = new Harmony(PatchOwner);
            harmony.CreateClassProcessor(typeof(AutoLootInteractionPatch)).Patch();
            Client = new AutoLootClient(api, progressSystem);
            Active = this;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (!VanillaExpandedModSystem.Config.EnableAutoLoot)
        {
            Client?.Cancel();
        }
    }

    public override void Dispose()
    {
        if (Active == this)
        {
            Active = null;
        }
        Client?.Dispose();
        Client = null;
        harmony?.UnpatchAll(PatchOwner);
        harmony = null;
        base.Dispose();
    }
}
