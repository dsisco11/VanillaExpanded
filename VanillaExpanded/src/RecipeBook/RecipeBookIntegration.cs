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
    // Inventory recomposition replaces the controls and the composer the book must follow.
    internal static event Action<GuiDialogInventory, GuiComposer>? Composed;

    [HarmonyPostfix]
    private static void AfterCompose(GuiDialogInventory __instance, GuiComposer ___survivalInvDialog) =>
        Composed?.Invoke(__instance, ___survivalInvDialog);
}

/// <summary>
/// Connects the Recipe Book to inventory dialogs and owns its client/world lifecycle.
/// Recipe evaluation, browsing, and layout belong to the catalogue, allocator, and book dialog.
/// </summary>
public sealed class RecipeBookSystem : ModSystem, ILiveConfigurable
{
    internal const string RememberOpenSetting = "vanillaexpanded-recipebook-remember-open";
    internal const string TileSizeSetting = "vanillaexpanded-recipebook-tile-size";
    private const string BookIcon = "vanillaexpanded-recipe-book";

    private readonly System.Func<ICoreClientAPI, (CraftingCatalogue Catalogue, CraftingAllocator Allocator)> buildCatalogue;

    // Client-owned icon registrations survive world changes and are removed on system disposal.
    private ICoreClientAPI? api;
    private IconRendererDelegate? bookIcon;
    private readonly Dictionary<string, IconRendererDelegate> categoryIcons = new();

    // Each attachment tracks the exact inventory composer and its removable reopening callback.
    private readonly Dictionary<GuiDialog, GuiDialogRecipeBook> dialogs = new();
    private readonly Dictionary<GuiDialog, (GuiComposer Composer, Action Opened)> attachments = new();

    // World-owned recipe data is shared by dialogs; browsing state survives dialog replacement.
    private (CraftingCatalogue Catalogue, CraftingAllocator Allocator, RecipeBookIconCache Icons)? worldContext;
    private RecipeBookBrowsingState browsingState = new();

    // Restoration intent is separate from the persisted "Reopen with inventory" preference.
    private bool shouldRestoreBook;

    public RecipeBookSystem() : this(BuildCatalogue) { }

    internal RecipeBookSystem(System.Func<ICoreClientAPI, (CraftingCatalogue, CraftingAllocator)> build) =>
        buildCatalogue = build;

    internal CraftingCatalogue? Catalogue => worldContext?.Catalogue;
    internal RecipeBookIconCache? IconCache => worldContext?.Icons;

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

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (this.api == null || VanillaExpandedModSystem.Config.EnableRecipeBook) return;
        CraftingInventorySnapshot.EnsureThread(RuntimeEnv.MainThreadId);
        foreach (var attachment in attachments.Values)
        {
            attachment.Composer.GetToggleButton("vanillaexpanded-crafting-toggle").Enabled = false;
            SetOpenerState(attachment.Composer, false);
        }
        // Disabling removes the hooks; enabling again requires a world reload or restart.
        Dispose();
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
        var current = buildCatalogue(api!);
        worldContext = (current.Catalogue, current.Allocator, new RecipeBookIconCache(api!));
    }

    private void Clear()
    {
        CraftingInventorySnapshot.EnsureThread(RuntimeEnv.MainThreadId);
        // Closing a book invokes its disposal callback, which removes it from dialogs.
        foreach (var parent in new List<GuiDialog>(dialogs.Keys)) Close(parent);
        foreach (var attachment in attachments) attachment.Key.OnOpened -= attachment.Value.Opened;
        attachments.Clear();
        browsingState = new();
        worldContext?.Icons.Dispose();
        worldContext = null;
    }

    /// <summary>Attaches book controls and reopening behavior to a newly composed inventory.</summary>
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
        RegisterIcons(api);
        var buttonBounds = ElementBounds.Fixed(-32, 0, 28, 28)
            .WithAlignment(EnumDialogArea.LeftMiddle).WithParent(output);
        output.WithChild(buttonBounds);
        composer.AddInteractiveElement(new GuiElementRecipeBookButton(api, BookIcon,
            on => ToggleBook(parent, composer, on), buttonBounds)
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
        void Restore() => RestoreBook(parent, composer);
        attachments[parent] = (composer, Restore);
        parent.OnOpened += Restore;
        // A replacement composer can arrive while the inventory is already open.
        if (shouldRestoreBook && parent.IsOpened())
            api.Event.EnqueueMainThreadTask(Restore, "vanillaexpanded-crafting-restore");
    }

    private void RegisterIcons(ICoreClientAPI api)
    {
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
    }

    private void ToggleBook(GuiDialog parent, GuiComposer composer, bool open)
    {
        if (!VanillaExpandedModSystem.Config.EnableRecipeBook) return;
        if (open) Open(parent, composer);
        else
        {
            shouldRestoreBook = false;
            Close(parent);
        }
        SetOpenerState(composer, dialogs.TryGetValue(parent, out var dialog) && dialog.IsOpened());
    }

    private void RestoreBook(GuiDialog parent, GuiComposer composer)
    {
        if (api == null || !VanillaExpandedModSystem.Config.EnableRecipeBook || !shouldRestoreBook
            || !api.Settings.Bool.Get(RememberOpenSetting, true) || !parent.IsOpened()) return;

        // Queued callbacks must not reopen a book against an inventory composer that was replaced.
        if (!attachments.TryGetValue(parent, out var current) || !ReferenceEquals(current.Composer, composer)
            || !parent.Composers.ContainsKey("maininventory")
            || !ReferenceEquals(parent.Composers["maininventory"], composer)) return;

        Open(parent, composer, false);
    }

    private void Open(GuiDialog parent, GuiComposer composer, bool withFocus = true)
    {
        if (api == null || !VanillaExpandedModSystem.Config.EnableRecipeBook
            || dialogs.TryGetValue(parent, out var open) && open.IsOpened()) return;
        if (worldContext is not { } current)
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
        // Failed opens must not overwrite browsing state from the last successfully opened book.
        bool opened = false;
        dialog.CloseRequestedByUser += () => shouldRestoreBook = false;
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
            shouldRestoreBook = true;
            SetOpenerState(composer, true);
        }
        else Close(parent);
    }

    private void Close(GuiDialog parent)
    {
        if (!dialogs.TryGetValue(parent, out var dialog)) return;
        dialog.CloseWithParent();
    }

    private static void SetOpenerState(GuiComposer composer, bool open)
    {
        var button = (GuiElementRecipeBookButton)composer.GetToggleButton("vanillaexpanded-crafting-toggle");
        button.Visible = button.Enabled;
        button.SetValue(open && button.Enabled);
    }

    internal static int GetTileSize(ICoreClientAPI api)
    {
        int size = api.Settings.Int.Get(TileSizeSetting, (int)RecipeBookVisualMetrics.TileSize);
        if (size is >= RecipeBookVisualMetrics.MinimumTileSize and <= RecipeBookVisualMetrics.MaximumTileSize)
            return size;
        api.Logger.Warning("[VanillaExpanded] Invalid recipe tile size {0}; restoring the default.", size);
        api.Settings.Int[TileSizeSetting] = (int)RecipeBookVisualMetrics.TileSize;
        return (int)RecipeBookVisualMetrics.TileSize;
    }
}
