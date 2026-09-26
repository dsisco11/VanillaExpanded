using VanillaExpanded.ModSystems;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Replaces the vanilla tool-mode grid with the shared radial menu.</summary>
internal sealed class ToolModeRadialMenuSystem : ModSystem, ILiveConfigurable
{
    private ICoreClientAPI? api;
    private IRadialMenu? menu;

    /// <summary>Gets the initialized client owner used by the base-dialog Harmony prefix.</summary>
    internal static ToolModeRadialMenuSystem? Active { get; private set; }

    /// <inheritdoc />
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <inheritdoc />
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        menu = api.ModLoader.GetModSystem<RadialMenuSystem>();
        Active = this;
    }

    /// <inheritdoc />
    public void OnConfigReloaded(ICoreAPI api)
    {
        if (!VanillaExpandedModSystem.Config.EnableToolModeRadialMenu) menu?.Cancel();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        menu?.Cancel();
        if (Active == this) Active = null;
        menu = null;
        api = null;
        base.Dispose();
    }

    internal bool TryOpen()
    {
        if (api?.World.Player is not IClientPlayer player || menu is null)
            return false;

        ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
        CollectibleObject? collectible = slot.Itemstack?.Collectible;
        if (collectible is null) return false;

        BlockSelection? blockSelection = player.CurrentBlockSelection;
        SkillItem[]? modes = collectible.GetToolModes(slot, player, blockSelection!);
        int currentMode = collectible.GetToolMode(slot, player, blockSelection!);
        if (!ToolModeMenuContentFactory.TryCreate(modes, currentMode, Lang.Get("Current mode"), out ToolModeMenuContent? content))
            return false;
        if (menu.IsOpen) return true;

        return menu.Open(content!.Layout, content.Entries,
            id => SelectMode(id, collectible, slot, player, blockSelection), static () => { });
    }

    private void SelectMode(string id, CollectibleObject collectible, ItemSlot slot, IClientPlayer player, BlockSelection? blockSelection)
    {
        if (ToolModeMenuContentFactory.TryGetMode(id, out int mode))
            collectible.SetToolMode(slot, player, blockSelection!, mode);
    }
}