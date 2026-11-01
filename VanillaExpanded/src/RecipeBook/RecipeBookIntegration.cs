using System;
using System.Collections.Generic;
using HarmonyLib;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace VanillaExpanded.RecipeBook;

[HarmonyPatch(typeof(GuiDialogInventory), "ComposeSurvivalInvDialog")]
internal static class RecipeBookDialogPatch
{
    internal static event Action<GuiDialogInventory, GuiComposer>? Composed;

    [HarmonyPostfix]
    private static void AfterCompose(GuiDialogInventory __instance, GuiComposer ___survivalInvDialog) =>
        Composed?.Invoke(__instance, ___survivalInvDialog);
}

public sealed class RecipeBookSystem : ModSystem, ILiveConfigurable
{
    private const string BookIcon = "vanillaexpanded-recipe-book";
    private ICoreClientAPI? api;
    private IconRendererDelegate? bookIcon;
    private readonly Dictionary<string, IconRendererDelegate> categoryIcons = new();
    private readonly Dictionary<GuiDialog, GuiDialogRecipeBook> dialogs = new();
    private readonly Dictionary<GuiDialog, (GuiComposer Composer, Action Opened)> attachments = new();
    private readonly System.Func<ICoreClientAPI, (CraftingCatalogue Catalogue, CraftingAllocator Allocator)> build;
    private (CraftingCatalogue Catalogue, CraftingAllocator Allocator, RecipeBookIconCache Icons)? context;
    private bool rememberOpen;
    private RecipeBookBrowsingState browsingState = new();

    public RecipeBookSystem() : this(BuildCatalogue) { }

    internal RecipeBookSystem(System.Func<ICoreClientAPI, (CraftingCatalogue, CraftingAllocator)> build) =>
        this.build = build;

    internal CraftingCatalogue? Catalogue => context?.Catalogue;
    internal RecipeBookIconCache? IconCache => context?.Icons;

    public override bool ShouldLoad(ICoreAPI api)
    {
        VanillaExpandedModSystem.EnsureConfigLoaded(api);
        return ShouldLoad(api.Side);
    }

    public override bool ShouldLoad(EnumAppSide forSide) =>
        forSide == EnumAppSide.Client && VanillaExpandedModSystem.Config.EnableRecipeBook;

    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        RecipeBookDialogPatch.Composed += Attach;
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
        if (!VanillaExpandedModSystem.Config.EnableRecipeBook) return;
        var current = build(api!);
        context = (current.Catalogue, current.Allocator, new RecipeBookIconCache(api!));
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (this.api == null || VanillaExpandedModSystem.Config.EnableRecipeBook) return;
        CraftingInventorySnapshot.EnsureThread(RuntimeEnv.MainThreadId);
        foreach (var attachment in attachments.Values)
        {
            attachment.Composer.GetToggleButton("vanillaexpanded-crafting-toggle").Enabled = false;
            SetOpenerState(attachment.Composer, false);
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
            api.Logger.Warning("[VanillaExpanded] Recipe book requires the native 3x3 crafting grid.");
            return;
        }
        var output = composer.GetElement("outputslot")?.Bounds;
        if (output == null)
        {
            api.Logger.Warning("[VanillaExpanded] Recipe book requires the native crafting output slot.");
            return;
        }
        if (bookIcon == null)
        {
            bookIcon = api.Gui.Icons.SvgIconSource(api.Assets.Get(
                new AssetLocation("vanillaexpanded:textures/gui/recipe-book.svg")));
            api.Gui.Icons.CustomIcons[BookIcon] = bookIcon;
        }
        foreach (string code in RecipeBookCategoryIcons.Codes)
        {
            string id = RecipeBookCategoryIcons.Id(code);
            if (categoryIcons.ContainsKey(id)) continue;
            var icon = api.Gui.Icons.SvgIconSource(api.Assets.Get(RecipeBookCategoryIcons.Asset(code)));
            categoryIcons.Add(id, icon);
            api.Gui.Icons.CustomIcons[id] = icon;
        }
        var buttonBounds = ElementBounds.Fixed(-32, 0, 28, 28)
            .WithAlignment(EnumDialogArea.LeftMiddle).WithParent(output);
        output.WithChild(buttonBounds);
        composer.AddInteractiveElement(new GuiElementRecipeBookButton(api, BookIcon, on =>
        {
            if (!VanillaExpandedModSystem.Config.EnableRecipeBook) return;
            if (on) Open(parent, composer);
            else
            {
                rememberOpen = false;
                Close(parent);
            }
            SetOpenerState(composer, dialogs.TryGetValue(parent, out var dialog) && dialog.IsOpened());
        }, buttonBounds)
        {
            Enabled = VanillaExpandedModSystem.Config.EnableRecipeBook
        },
            "vanillaexpanded-crafting-toggle");
        composer.AddInteractiveElement(new GuiElementRecipeBookRefresh(api, ElementBounds.Fixed(0, 0, 0, 0),
            grid, () =>
            {
                if (VanillaExpandedModSystem.Config.EnableRecipeBook
                    && dialogs.TryGetValue(parent, out var dialog)) dialog.NativeGridDirty();
            }), "vanillaexpanded-crafting-refresh");
        composer.ReCompose();
        void Restore()
        {
            if (VanillaExpandedModSystem.Config.EnableRecipeBook && rememberOpen && parent.IsOpened()
                && attachments.TryGetValue(parent, out var current) && ReferenceEquals(current.Composer, composer)
                && parent.Composers.ContainsKey("maininventory")
                && ReferenceEquals(parent.Composers["maininventory"], composer))
                Open(parent, composer, false);
        }
        attachments[parent] = (composer, Restore);
        parent.OnOpened += Restore;
        if (rememberOpen && parent.IsOpened())
            api.Event.EnqueueMainThreadTask(Restore, "vanillaexpanded-crafting-restore");
    }

    private void Open(GuiDialog parent, GuiComposer composer, bool withFocus = true)
    {
        if (api == null || !VanillaExpandedModSystem.Config.EnableRecipeBook
            || dialogs.TryGetValue(parent, out var open) && open.IsOpened()) return;
        if (context is not { } current)
        {
            api.TriggerIngameError(this, "recipebook", Lang.Get("vanillaexpanded:recipebook-loading"));
            return;
        }
        GuiDialogRecipeBook dialog;
        try { dialog = new(api, parent, composer, current.Catalogue, current.Allocator, current.Icons, browsingState); }
        catch (NotSupportedException ex)
        {
            api.Logger.Warning("[VanillaExpanded] Recipe book inventory is unsupported: {0}", ex.Message);
            api.TriggerIngameError(this, "recipebook", Lang.Get(CraftingTransferPlanner.Unsafe));
            return;
        }
        dialogs[parent] = dialog;
        bool opened = false;
        dialog.CloseRequestedByUser += () => rememberOpen = false;
        dialog.Disposed += () =>
        {
            if (!dialogs.TryGetValue(parent, out var active) || !ReferenceEquals(active, dialog)) return;
            if (opened) browsingState = dialog.BrowsingState;
            dialogs.Remove(parent);
            if (attachments.TryGetValue(parent, out var attachment) && ReferenceEquals(attachment.Composer, composer))
                SetOpenerState(composer, false);
        };
        if (dialog.TryOpen(withFocus))
        {
            opened = true;
            rememberOpen = true;
            SetOpenerState(composer, true);
        }
        else Close(parent);
    }

    private static void SetOpenerState(GuiComposer composer, bool open)
    {
        var button = (GuiElementRecipeBookButton)composer.GetToggleButton("vanillaexpanded-crafting-toggle");
        button.Visible = button.Enabled;
        button.SetValue(open && button.Enabled);
    }

    private void Close(GuiDialog parent)
    {
        if (!dialogs.TryGetValue(parent, out var dialog)) return;
        dialog.CloseWithParent();
    }

    private void Clear()
    {
        CraftingInventorySnapshot.EnsureThread(RuntimeEnv.MainThreadId);
        foreach (var parent in new List<GuiDialog>(dialogs.Keys)) Close(parent);
        foreach (var attachment in attachments) attachment.Key.OnOpened -= attachment.Value.Opened;
        attachments.Clear();
        browsingState = new();
        context?.Icons.Dispose();
        context = null;
    }

    public override void Dispose()
    {
        RecipeBookDialogPatch.Composed -= Attach;
        if (api != null)
        {
            if (bookIcon != null) api.Gui.Icons.CustomIcons.Remove(BookIcon);
            bookIcon = null;
            foreach (var icon in categoryIcons)
                if (api.Gui.Icons.CustomIcons.TryGetValue(icon.Key, out var current) && ReferenceEquals(current, icon.Value))
                    api.Gui.Icons.CustomIcons.Remove(icon.Key);
            categoryIcons.Clear();
            api.Event.LevelFinalize -= Initialize;
            api.Event.LeaveWorld -= Clear;
            Clear();
        }
        api = null;
        base.Dispose();
    }
}
