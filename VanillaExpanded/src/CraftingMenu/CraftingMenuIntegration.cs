using System;
using System.Collections.Generic;
using HarmonyLib;
using VanillaExpanded.ModSystems;
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

public sealed class CraftingMenuSystem : ModSystem, ILiveConfigurable
{
    private const string BookIcon = "vanillaexpanded-crafting-book";
    private ICoreClientAPI? api;
    private IconRendererDelegate? bookIcon;
    private readonly Dictionary<GuiDialog, GuiDialogCraftingMenu> dialogs = new();
    private readonly Dictionary<GuiDialog, (GuiComposer Composer, Action Opened)> attachments = new();
    private readonly System.Func<ICoreClientAPI, (CraftingCatalogue Catalogue, CraftingAllocator Allocator)> build;
    private (CraftingCatalogue Catalogue, CraftingAllocator Allocator, CraftingIconCache Icons)? context;
    private bool rememberOpen;

    public CraftingMenuSystem() : this(BuildCatalogue) { }

    internal CraftingMenuSystem(System.Func<ICoreClientAPI, (CraftingCatalogue, CraftingAllocator)> build) =>
        this.build = build;

    internal CraftingCatalogue? Catalogue => context?.Catalogue;
    internal CraftingIconCache? IconCache => context?.Icons;

    public override bool ShouldLoad(ICoreAPI api)
    {
        VanillaExpandedModSystem.EnsureConfigLoaded(api);
        return ShouldLoad(api.Side);
    }

    public override bool ShouldLoad(EnumAppSide forSide) =>
        forSide == EnumAppSide.Client && VanillaExpandedModSystem.Config.EnableCraftingMenu;

    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        CraftingMenuDialogPatch.Composed += Attach;
        api.Event.LevelFinalize += Initialize;
        api.Event.LeaveWorld += Clear;
    }

    internal static (CraftingCatalogue, CraftingAllocator) BuildCatalogue(ICoreClientAPI api)
    {
        var policy = new CraftingClientPolicy(api, api.World.Player);
        return (CraftingCatalogue.Build(api.World.GridRecipes, api.World, 3, 3, policy.CanAccess),
            new CraftingAllocator(api.World, api.World.Player));
    }

    private void Initialize()
    {
        CraftingInventorySnapshot.EnsureThread(RuntimeEnv.MainThreadId);
        Clear();
        if (!VanillaExpandedModSystem.Config.EnableCraftingMenu) return;
        var current = build(api!);
        context = (current.Catalogue, current.Allocator, new CraftingIconCache(api!));
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (this.api == null || VanillaExpandedModSystem.Config.EnableCraftingMenu) return;
        CraftingInventorySnapshot.EnsureThread(RuntimeEnv.MainThreadId);
        foreach (var attachment in attachments.Values)
        {
            attachment.Composer.GetToggleButton("vanillaexpanded-crafting-toggle").Enabled = false;
            var hint = attachment.Composer.GetHoverText("vanillaexpanded-crafting-toggle-hint");
            hint.SetAutoDisplay(false);
            hint.SetVisible(false);
        }
        Dispose();
    }

    internal void Attach(GuiDialog parent, GuiComposer composer)
    {
        if (api == null || composer.GetElement("vanillaexpanded-crafting-toggle") != null) return;
        Close(parent);
        if (attachments.Remove(parent, out var previous)) parent.OnOpened -= previous.Opened;
        if (api.World.Player.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName)
            is not InventoryCraftingGrid grid || grid.Count != 10)
        {
            api.Logger.Warning("[VanillaExpanded] Crafting menu requires the native 3x3 crafting grid.");
            return;
        }
        var bounds = composer.GetElement("craftinggrid").Bounds;
        if (bookIcon == null)
        {
            bookIcon = api.Gui.Icons.SvgIconSource(api.Assets.Get(
                new AssetLocation("vanillaexpanded:textures/gui/crafting-book.svg")));
            api.Gui.Icons.CustomIcons[BookIcon] = bookIcon;
        }
        var buttonBounds = ElementBounds.Fixed(bounds.fixedX, bounds.fixedY - 34, 28, 28);
        composer.AddInteractiveElement(new GuiElementCraftingMenuButton(api, BookIcon, _ =>
        {
            if (!VanillaExpandedModSystem.Config.EnableCraftingMenu) return;
            if (dialogs.TryGetValue(parent, out var open) && open.IsOpened())
            {
                rememberOpen = false;
                Close(parent);
            }
            else Open(parent, composer);
        }, buttonBounds)
        {
            Enabled = VanillaExpandedModSystem.Config.EnableCraftingMenu
        },
            "vanillaexpanded-crafting-toggle");
        composer.AddHoverText(Lang.Get("vanillaexpanded:craftingmenu-toggle"), CairoFont.WhiteSmallText(),
            160, buttonBounds.FlatCopy(), "vanillaexpanded-crafting-toggle-hint");
        composer.AddInteractiveElement(new GuiElementCraftingRefresh(api, ElementBounds.Fixed(0, 0, 0, 0),
            grid, () =>
            {
                if (VanillaExpandedModSystem.Config.EnableCraftingMenu
                    && dialogs.TryGetValue(parent, out var dialog)) dialog.NativeGridDirty();
            }), "vanillaexpanded-crafting-refresh");
        composer.ReCompose();
        void Restore()
        {
            if (VanillaExpandedModSystem.Config.EnableCraftingMenu && rememberOpen && parent.IsOpened()
                && attachments.TryGetValue(parent, out var current) && ReferenceEquals(current.Composer, composer)
                && parent.Composers.ContainsKey("maininventory")
                && ReferenceEquals(parent.Composers["maininventory"], composer))
                Open(parent, composer);
        }
        attachments[parent] = (composer, Restore);
        parent.OnOpened += Restore;
        if (rememberOpen && parent.IsOpened())
            api.Event.EnqueueMainThreadTask(Restore, "vanillaexpanded-crafting-restore");
    }

    private void Open(GuiDialog parent, GuiComposer composer)
    {
        if (api == null || !VanillaExpandedModSystem.Config.EnableCraftingMenu
            || dialogs.TryGetValue(parent, out var open) && open.IsOpened()) return;
        if (context is not { } current)
        {
            api.TriggerIngameError(this, "craftingmenu", Lang.Get("vanillaexpanded:craftingmenu-loading"));
            return;
        }
        GuiDialogCraftingMenu dialog;
        try { dialog = new(api, parent, composer, current.Catalogue, current.Allocator, current.Icons); }
        catch (NotSupportedException ex)
        {
            api.Logger.Warning("[VanillaExpanded] Crafting menu inventory is unsupported: {0}", ex.Message);
            api.TriggerIngameError(this, "craftingmenu", Lang.Get(CraftingTransferPlanner.Unsafe));
            return;
        }
        dialogs[parent] = dialog;
        dialog.CloseRequestedByUser += () => rememberOpen = false;
        dialog.Disposed += () =>
        {
            if (!dialogs.TryGetValue(parent, out var active) || !ReferenceEquals(active, dialog)) return;
            dialogs.Remove(parent);
        };
        if (dialog.TryOpen()) rememberOpen = true;
        else Close(parent);
    }

    private void Close(GuiDialog parent)
    {
        if (!dialogs.Remove(parent, out var dialog)) return;
        dialog.CloseWithParent();
    }

    private void Clear()
    {
        CraftingInventorySnapshot.EnsureThread(RuntimeEnv.MainThreadId);
        foreach (var parent in new List<GuiDialog>(dialogs.Keys)) Close(parent);
        foreach (var attachment in attachments) attachment.Key.OnOpened -= attachment.Value.Opened;
        attachments.Clear();
        context?.Icons.Dispose();
        context = null;
    }

    public override void Dispose()
    {
        CraftingMenuDialogPatch.Composed -= Attach;
        if (api != null)
        {
            if (bookIcon != null) api.Gui.Icons.CustomIcons.Remove(BookIcon);
            bookIcon = null;
            api.Event.LevelFinalize -= Initialize;
            api.Event.LeaveWorld -= Clear;
            Clear();
        }
        api = null;
        base.Dispose();
    }
}
