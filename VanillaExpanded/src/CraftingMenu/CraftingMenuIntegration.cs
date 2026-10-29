using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace VanillaExpanded.CraftingMenu;

[HarmonyPatch(typeof(GuiDialogInventory), "ComposeSurvivalInvDialog")]
internal static class CraftingMenuDialogPatch
{
    internal static event Action<GuiDialogInventory, GuiComposer>? Composed;

    [HarmonyPostfix]
    private static void AfterCompose(GuiDialogInventory __instance, GuiComposer ___survivalInvDialog) =>
        Composed?.Invoke(__instance, ___survivalInvDialog);
}

public sealed class CraftingMenuSystem : ModSystem
{
    private ICoreClientAPI? api;
    private readonly Dictionary<GuiDialog, GuiDialogCraftingMenu> dialogs = new();
    private readonly System.Func<ICoreClientAPI, (CraftingCatalogue Catalogue, CraftingAllocator Allocator)> build;
    private (CraftingCatalogue Catalogue, CraftingAllocator Allocator)? context;

    public CraftingMenuSystem() : this(BuildCatalogue) { }

    internal CraftingMenuSystem(System.Func<ICoreClientAPI, (CraftingCatalogue, CraftingAllocator)> build) =>
        this.build = build;

    internal CraftingCatalogue? Catalogue => context?.Catalogue;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        CraftingMenuDialogPatch.Composed += Attach;
        api.Event.LevelFinalize += Initialize;
        api.Event.LeaveWorld += Clear;
    }

    private static (CraftingCatalogue, CraftingAllocator) BuildCatalogue(ICoreClientAPI api)
    {
        var policy = new CraftingClientPolicy(api, api.World.Player);
        return (CraftingCatalogue.Build(api.World.GridRecipes, api.World, 3, 3, policy.CanAccess),
            new CraftingAllocator(api.World, api.World.Player, policy.UnsupportedReason));
    }

    private void Initialize()
    {
        CraftingInventorySnapshot.EnsureThread(RuntimeEnv.MainThreadId);
        Clear();
        context = build(api!);
    }

    private void Attach(GuiDialogInventory parent, GuiComposer composer)
    {
        if (api == null || composer.GetElement("vanillaexpanded-crafting-toggle") != null) return;
        Close(parent);
        if (api.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName)
            is not InventoryCraftingGrid grid || grid.Count != 10)
        {
            api.Logger.Warning("[VanillaExpanded] Crafting menu requires the native 3x3 crafting grid.");
            return;
        }
        var bounds = composer.GetElement("craftinggrid").Bounds;
        var font = CairoFont.SmallButtonText(EnumButtonStyle.Normal);
        composer.AddInteractiveElement(new GuiElementTextButton(api,
            Lang.Get("vanillaexpanded:craftingmenu-toggle"), font,
            font.Clone().WithColor(GuiStyle.ActiveButtonTextColor), () =>
        {
            if (dialogs.TryGetValue(parent, out var open) && open.IsOpened()) { Close(parent); return true; }
            if (context is not { } current)
            {
                api.TriggerIngameError(this, "craftingmenu", Lang.Get("vanillaexpanded:craftingmenu-loading"));
                return true;
            }
            GuiDialogCraftingMenu dialog;
            try { dialog = new(api, parent, composer, current.Catalogue, current.Allocator); }
            catch (NotSupportedException ex)
            {
                api.Logger.Warning("[VanillaExpanded] Crafting menu inventory is unsupported: {0}", ex.Message);
                api.TriggerIngameError(this, "craftingmenu", Lang.Get(CraftingTransferPlanner.Unsafe));
                return true;
            }
            dialogs[parent] = dialog;
            dialog.Disposed += () => dialogs.Remove(parent);
            if (!dialog.TryOpen()) Close(parent);
            return true;
        }, ElementBounds.Fixed(bounds.fixedX, bounds.fixedY - 30, bounds.fixedWidth, 24)),
            "vanillaexpanded-crafting-toggle");
        composer.AddInteractiveElement(new GuiElementCraftingRefresh(api, ElementBounds.Fixed(0, 0, 0, 0),
            grid, () =>
            {
                if (dialogs.TryGetValue(parent, out var dialog)) dialog.NativeGridDirty();
            }), "vanillaexpanded-crafting-refresh");
        composer.ReCompose();
    }

    private void Close(GuiDialog parent)
    {
        if (!dialogs.Remove(parent, out var dialog)) return;
        dialog.TryClose();
        dialog.Dispose();
    }

    private void Clear()
    {
        CraftingInventorySnapshot.EnsureThread(RuntimeEnv.MainThreadId);
        foreach (var parent in new List<GuiDialog>(dialogs.Keys)) Close(parent);
        context = null;
    }

    public override void Dispose()
    {
        CraftingMenuDialogPatch.Composed -= Attach;
        if (api != null)
        {
            api.Event.LevelFinalize -= Initialize;
            api.Event.LeaveWorld -= Clear;
            Clear();
        }
        api = null;
        base.Dispose();
    }
}
