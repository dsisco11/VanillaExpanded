using System;
using System.Collections.Generic;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Routes one captured chisel menu between tool modes and carried material pages.</summary>
internal sealed class ChiselMaterialMenu
{
    private readonly ICoreClientAPI api;
    private readonly IRadialMenu menu;
    private readonly IClientPlayer player;
    private readonly ItemChisel chisel;
    private readonly ItemSlot toolSlot;
    private readonly ItemStack? toolStack;
    private readonly BlockSelection selection;
    private readonly BlockEntityChisel? target;
    private readonly IClientWorldAccessor world;
    private readonly Func<bool> worldActive;
    private SkillItem[] modes;
    private IReadOnlyList<ChiselMaterialCandidate>? candidates;
    private int page;
    private bool busy;
    private bool closed;

    #region Public API
    /// <summary>Captures the opening tool stack and target; later aiming does not redirect additions.</summary>
    internal ChiselMaterialMenu(ICoreClientAPI api, IRadialMenu menu, IClientPlayer player, ItemChisel chisel,
        ItemSlot toolSlot, BlockSelection selection, SkillItem[] modes, Func<bool> worldActive)
    {
        this.api = api;
        this.menu = menu;
        this.player = player;
        this.chisel = chisel;
        this.toolSlot = toolSlot;
        toolStack = toolSlot.Itemstack;
        this.selection = selection.Clone();
        target = api.World.BlockAccessor.GetBlockEntity(this.selection.Position) as BlockEntityChisel;
        world = api.World;
        this.modes = modes;
        this.worldActive = worldActive;
    }

    /// <summary>Handles picker navigation or the displayed vanilla mode using the same open dialog.</summary>
    internal RadialMenuSelectionResult Select(string id)
    {
        if (busy) return RadialMenuSelectionResult.KeepOpen;
        if (closed || !ContextCurrent()) return RadialMenuSelectionResult.Close;
        if (candidates is not null) return SelectMaterial(id);
        if (!ToolModeMenuContentFactory.TryGetMode(id, out int mode) || mode >= modes.Length)
            return RadialMenuSelectionResult.Close;
        // The displayed mode identifies the action; its current native index is resolved again before application.
        if (modes[mode]?.Code?.Path == "addmat")
        {
            if (!EmptyCursor()) return RadialMenuSelectionResult.KeepOpen;
            return ShowPicker();
        }
        bool cursorMaterial = player.InventoryManager.MouseItemSlot.Itemstack?.Block is not null;
        ToolModeSelection.Apply(api, chisel, toolSlot, player, selection, mode);
        return cursorMaterial ? ShowParent() : RadialMenuSelectionResult.Close;
    }

    /// <summary>Clears picker state on cancellation; native synchronous return is independent of this flag.</summary>
    internal void Cancel()
    {
        closed = true;
        candidates = null;
    }
    #endregion

    #region Private
    #region Selection
    /// <summary>Pages the existing snapshots or revalidates one displayed source before native execution.</summary>
    private RadialMenuSelectionResult SelectMaterial(string id)
    {
        if (id == ChiselMaterialMenuContentFactory.BackId) return ShowParent();
        if (id == ChiselMaterialMenuContentFactory.PreviousId || id == ChiselMaterialMenuContentFactory.NextId)
        {
            int lastPage = Math.Max(0, (candidates!.Count - 1) / ChiselMaterialMenuContentFactory.PageSize);
            page = Math.Clamp(page + (id == ChiselMaterialMenuContentFactory.NextId ? 1 : -1), 0, lastPage);
            var content = ChiselMaterialMenuContentFactory.Create(candidates, page);
            menu.UpdateLayout(content.Layout, content.Entries);
            return RadialMenuSelectionResult.KeepOpen;
        }
        if (!ChiselMaterialMenuContentFactory.TryGetCandidateIndex(id, out int index) || index >= candidates!.Count)
            return RadialMenuSelectionResult.KeepOpen;
        if (!EmptyCursor()) return RadialMenuSelectionResult.KeepOpen;
        bool applied;
        busy = true;
        try
        {
            applied = ChiselMaterialOperation.TryApply(api, chisel, toolSlot, player, selection, target!,
                candidates[index], () => !closed && ContextCurrent(), WorldCurrent);
        }
        finally { busy = false; }
        if (closed || !ContextCurrent()) return RadialMenuSelectionResult.Close;
        if (!player.InventoryManager.MouseItemSlot.Empty)
        {
            api.TriggerIngameError(this, "chisel-material-return", Lang.Get("vanillaexpanded:chisel-material-return"));
            return RadialMenuSelectionResult.KeepOpen;
        }
        // Rebuild from current target state after local application; stale choices refresh without consuming a replacement.
        return applied ? ShowParent() : ShowPicker();
    }
    #endregion

    #region Presentation
    /// <summary>Rebuilds eligible sources only when entering or refreshing the picker.</summary>
    private RadialMenuSelectionResult ShowPicker()
    {
        candidates = ChiselMaterialCandidates.Enumerate(api, player, selection.Position, target!);
        page = 0;
        var content = ChiselMaterialMenuContentFactory.Create(candidates);
        menu.UpdateLayout(content.Layout, content.Entries);
        return RadialMenuSelectionResult.KeepOpen;
    }

    /// <summary>Returns to fresh native chisel modes without selecting a mode or modifying carving attributes.</summary>
    private RadialMenuSelectionResult ShowParent()
    {
        candidates = null;
        SkillItem[]? refreshed = chisel.GetToolModes(toolSlot, player, selection);
        int current = chisel.GetToolMode(toolSlot, player, selection);
        if (!ToolModeMenuContentFactory.TryCreate(refreshed, current, Lang.Get("Current mode"),
            ToolModeMenuLayoutStrategyRegistry.Resolve(chisel), out ToolModeMenuContent? content))
            return RadialMenuSelectionResult.Close;
        modes = refreshed!;
        menu.UpdateLayout(content!.Layout, content.Entries);
        return RadialMenuSelectionResult.KeepOpen;
    }
    #endregion

    #region Guards
    /// <summary>Rejects an occupied cursor without touching any inventory stack.</summary>
    private bool EmptyCursor()
    {
        if (player.InventoryManager.MouseItemSlot.Empty) return true;
        api.TriggerIngameError(this, "chisel-material-cursor", Lang.Get("vanillaexpanded:chisel-material-cursor"));
        return false;
    }

    /// <summary>Requires the original active tool stack and the same block entity at the captured position.</summary>
    private bool ContextCurrent() => WorldCurrent() && ReferenceEquals(player.InventoryManager.ActiveHotbarSlot, toolSlot)
        && toolStack is not null && ReferenceEquals(toolSlot.Itemstack, toolStack)
        && ReferenceEquals(toolStack.Collectible, chisel) && target is not null
        && ReferenceEquals(world.BlockAccessor.GetBlockEntity(selection.Position), target);

    /// <summary>Allows known native remainder returns after picker closure, but never after leaving the world.</summary>
    private bool WorldCurrent() => worldActive() && ReferenceEquals(api.World, world) && ReferenceEquals(world.Player, player);
    #endregion
    #endregion
}
