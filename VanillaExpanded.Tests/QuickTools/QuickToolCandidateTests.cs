using Moq;
using VanillaExpanded.QuickTools;
using VanillaExpanded.Lighting;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.QuickTools;

/// <summary>Checks fixed positions separately from tool and virtual candidate selection.</summary>
public sealed class QuickToolCandidateTests
{
    #region Layout
    /// <summary>Verifies Light owns the top position and every tool retains canonical clockwise order.</summary>
    [Fact]
    public void CanonicalLayout_IsIndependentOfInventoryAndEnumEnumeration()
    {
        string[] categories = ["Knife", "Pickaxe", "Axe", "Sword", "Shovel", "Hammer", "Spear", "Bow", "Shears", "Sickle", "Hoe", "Saw", "Chisel", "Scythe", "Sling", "Wrench", "Probe", "Meter", "Drill", "Firearm", "Crossbow", "Javelin", "Pike", "Shield", "Club", "Mace", "Warhammer", "Poleaxe", "Halberd", "Polearm", "Staff", "Tongs", "Crowbar"];
        Assert.Equal(34, QuickToolLayout.WedgeIds.Count);
        Assert.Equal(QuickToolLayout.LightId, QuickToolLayout.WedgeIds[0]);
        for (int i = 0; i < categories.Length; i++)
        {
            Assert.Equal($"tool:{categories[i]}", QuickToolLayout.WedgeIds[i + 1]);
            Assert.Equal(QuickToolLayout.WedgeIds[i + 1], QuickToolLayout.GetToolId(Enum.Parse<EnumTool>(categories[i])));
        }
        var available = new[] { QuickToolLayout.LightId, "tool:Knife" };
        Assert.Equal(available, QuickToolLayout.CreateLayout(available).WedgeIds);
        Assert.Equal(QuickToolLayout.LightId, QuickToolLayout.CreateLayout(available).HitTest(0, -60, 0, 0, 100));
        Assert.Equal("unequip", QuickToolLayout.CreateLayout([]).CenterId);
        Assert.Null(QuickToolLayout.GetToolId((EnumTool)500));
    }

    /// <summary>Accepts dynamically discovered concrete tool tags outside the legacy EnumTool catalog.</summary>
    [Fact]
    public void DynamicToolTags_CreateSelectableLayoutEntries()
    {
        string[] available = [QuickToolLayout.GetToolId(["tool-cleaver"]), QuickToolLayout.GetToolId(["tool-solderingiron"])];

        Assert.Equal(available, QuickToolLayout.CreateLayout(available).WedgeIds);
    }

    /// <summary>Keeps a multi-category tool group distinct from a group containing only a subset of its tags.</summary>
    [Fact]
    public void ToolTagGroups_DistinguishSubsetAndMultiCategoryTools()
    {
        string pickaxe = QuickToolLayout.GetToolId(["tool-pickaxe"]);
        string prospectingPick = QuickToolLayout.GetToolId(["tool-prospectingpick", "tool-pickaxe"]);

        var layout = QuickToolLayout.CreateLayout([pickaxe, prospectingPick]);

        Assert.NotEqual(pickaxe, prospectingPick);
        Assert.Equal("tool:pickaxe", pickaxe);
        Assert.Equal("tool:pickaxe+prospectingpick", prospectingPick);
        Assert.Equal([pickaxe, prospectingPick], layout.WedgeIds);
    }
    #endregion

    #region Ranking
    /// <summary>Confirms tier precedence, lower absolute durability, stable ties and broken exclusion.</summary>
    [Fact]
    public void ToolRanking_PrefersTierThenWornThenStableSlot()
    {
        var f = new Fixture();
        f.Put(f.Hotbar[0], 1, EnumTool.Pickaxe, 2, 10);
        f.Put(f.Hotbar[1], 2, EnumTool.Pickaxe, 3, 80);
        f.Put(f.Hotbar[2], 3, EnumTool.Pickaxe, 3, 6);
        f.Put(f.Hotbar[3], 4, EnumTool.Pickaxe, 3, 6);
        f.Put(f.Hotbar[4], 5, EnumTool.Pickaxe, 3, 0);
        f.Put(f.Backpack[0], 6, EnumTool.Pickaxe, 3, 1);
        var provider = new ToolCandidateProvider(EnumTool.Pickaxe);
        Assert.Same(f.Backpack[0], provider.Resolve(f.Manager.Object, f.Offhand)?.Slot);
        f.Backpack[0].Itemstack = null;
        Assert.Same(f.Hotbar[2], provider.Resolve(f.Manager.Object, f.Offhand)?.Slot);
    }

    /// <summary>Excludes unrelated or opened inventories and special slots.</summary>
    [Fact]
    public void ToolRanking_ExcludesUnsupportedSlotsAndContainers()
    {
        var f = new Fixture();
        f.Put(f.Offhand, 1, EnumTool.Axe, 9, 1);
        f.Put(f.External[0], 2, EnumTool.Axe, 9, 1);
        f.Put(f.Backpack[0], 3, EnumTool.Axe, 1, 10);
        Assert.Same(f.Backpack[0], new ToolCandidateProvider(EnumTool.Axe).Resolve(f.Manager.Object, f.Offhand)?.Slot);
        f.Backpack[0].Itemstack = null;
        Assert.Null(new ToolCandidateProvider(EnumTool.Axe).Resolve(f.Manager.Object, f.Offhand));
    }
    /// <summary>The held tool is omitted while the next eligible tool remains selectable.</summary>
    [Fact]
    public void ToolRanking_ExcludesActiveHand()
    {
        var f = new Fixture();
        f.Put(f.Hotbar[0], 1, EnumTool.Axe, 9, 1);
        f.Put(f.Hotbar[1], 2, EnumTool.Axe, 2, 5);
        var provider = new ToolCandidateProvider(EnumTool.Axe);
        Assert.Same(f.Hotbar[1], provider.Resolve(f.Manager.Object, f.Offhand, f.Hotbar[0])?.Slot);
        f.Hotbar[1].Itemstack = null;
        Assert.Null(provider.Resolve(f.Manager.Object, f.Offhand, f.Hotbar[0]));
    }
    /// <summary>Excludes broken durable tools and ranks non-wearing tools after worn tools of equal tier.</summary>
    [Fact]
    public void ToolRanking_HandlesBrokenAndNonWearingMetadata()
    {
        var f = new Fixture();
        f.Hotbar[0].Itemstack = new ItemStack(new RankedItem(1, EnumTool.Hammer, 2, 0));
        f.Hotbar[1].Itemstack = new ItemStack(new RankedItem(2, EnumTool.Hammer, 2, 0, 0));
        Assert.Same(f.Hotbar[1], new ToolCandidateProvider(EnumTool.Hammer).Resolve(f.Manager.Object, f.Offhand)?.Slot);
        f.Put(f.Hotbar[2], 3, EnumTool.Hammer, 2, 4);
        Assert.Same(f.Hotbar[2], new ToolCandidateProvider(EnumTool.Hammer).Resolve(f.Manager.Object, f.Offhand)?.Slot);
    }
    /// <summary>Bad modded ranking metadata is skipped with diagnostic feedback.</summary>
    [Fact]
    public void ToolRanking_SkipsIncomparableModdedMetadata()
    {
        var f = new Fixture();
        f.Hotbar[1].Itemstack = new ItemStack(new FaultyItem(1));
        f.Put(f.Hotbar[2], 2, EnumTool.Axe, 2, 5);
        var messages = new List<string>();
        Assert.Same(f.Hotbar[2], new ToolCandidateProvider(EnumTool.Axe, messages.Add).Resolve(f.Manager.Object, f.Offhand)?.Slot);
        Assert.Single(messages);
    }
    #endregion

    #region Virtual selection
    /// <summary>Preserves hand priority, hotbar priority, and first-slot equal-brightness ties.</summary>
    [Fact]
    public void LightSelection_PreservesLegacyPriorityAndExactWinner()
    {
        var f = new Fixture();
        f.PutLight(f.Offhand, 1, 5);
        f.PutLight(f.Hotbar[0], 2, 10);
        f.PutLight(f.Hotbar[1], 3, 12);
        f.PutLight(f.Hotbar[2], 4, 12);
        f.PutLight(f.Backpack[0], 5, 30);
        var provider = new LightCandidateProvider();
        Assert.Same(f.Offhand, provider.Resolve(f.Manager.Object, f.Offhand)?.Slot);
        f.Offhand.Itemstack = null;
        Assert.Same(f.Hotbar[0], provider.Resolve(f.Manager.Object, f.Offhand)?.Slot);
        f.Hotbar[0].Itemstack = null;
        Assert.Same(f.Hotbar[1], provider.Resolve(f.Manager.Object, f.Offhand)?.Slot);
        f.Hotbar[1].Itemstack = null;
        f.Hotbar[2].Itemstack = null;
        Assert.Same(f.Backpack[0], provider.Resolve(f.Manager.Object, f.Offhand)?.Slot);
        f.Backpack[0].Itemstack = null;
        Assert.Null(provider.Resolve(f.Manager.Object, f.Offhand));
    }
    /// <summary>The quick-tool light entry skips the held light while legacy selection retains it.</summary>
    [Fact]
    public void LightSelection_ExcludesActiveHandOnlyWhenRequested()
    {
        var f = new Fixture();
        f.PutLight(f.Hotbar[0], 1, 20);
        f.PutLight(f.Hotbar[1], 2, 10);
        var provider = new LightCandidateProvider();
        Assert.Same(f.Hotbar[0], provider.Resolve(f.Manager.Object, f.Offhand)?.Slot);
        Assert.Same(f.Hotbar[1], provider.Resolve(f.Manager.Object, f.Offhand, f.Hotbar[0])?.Slot);
    }
    #endregion

    #region Cache
    /// <summary>Checks dirty burst coalescing, stable geometry, stale rejection and teardown.</summary>
    [Fact]
    public void Cache_CoalescesInvalidationAndRejectsStaleCandidate()
    {
        var f = new Fixture();
        f.Put(f.Hotbar[1], 1, EnumTool.Axe, 2, 20);
        int schedules = 0;
        using var cache = new QuickToolCandidateCache(() => schedules++);
        cache.Bind(f.Manager.Object, f.Offhand);
        Assert.Equal(1, cache.RefreshCount);
        schedules = 0;
        int refreshed = 0;
        cache.Refreshed += () => refreshed++;
        var old = cache.GetCached("tool:Axe");
        Assert.NotNull(old);
        string[] before = cache.CreateEntries(false).Select(e => e.Id).ToArray();
        f.Put(f.Hotbar[2], 2, EnumTool.Axe, 2, 2);
        cache.Invalidate();
        cache.Invalidate();
        Assert.Equal(1, schedules);
        Assert.False(cache.Revalidate("tool:Axe", old!, f.Hotbar[0], out _));
        cache.RefreshPending();
        Assert.Same(f.Hotbar[2], cache.GetCached("tool:Axe")?.Slot);
        Assert.Equal(before, cache.CreateEntries(false).Select(e => e.Id));
        Assert.Equal(2, cache.RefreshCount);
        Assert.Equal(1, refreshed);
        cache.Clear();
        Assert.Null(cache.GetCached("tool:Axe"));
    }
    /// <summary>Checks dirty-slot updates and detachment from a replaced inventory.</summary>
    [Fact]
    public void Cache_ObservesDirtySlotsAndDetachesReplacedInventory()
    {
        var f = new Fixture();
        f.Put(f.Hotbar[1], 1, EnumTool.Pickaxe, 2, 20);
        int schedules = 0;
        using var cache = new QuickToolCandidateCache(() => schedules++);
        cache.Bind(f.Manager.Object, f.Offhand);
        schedules = 0;
        f.Put(f.Hotbar[2], 2, EnumTool.Pickaxe, 2, 3);
        f.Hotbar.DidModifyItemSlot(f.Hotbar[2]);
        f.Hotbar.DidModifyItemSlot(f.Hotbar[1]);
        Assert.True(cache.IsDirty);
        Assert.Equal(1, schedules);
        cache.RefreshPending();
        Assert.Same(f.Hotbar[2], cache.GetCached("tool:Pickaxe")?.Slot);
        ((RankedItem)f.Hotbar[1].Itemstack!.Collectible).Remaining = 1;
        f.Hotbar.DidModifyItemSlot(f.Hotbar[1]);
        cache.RefreshPending();
        Assert.Same(f.Hotbar[1], cache.GetCached("tool:Pickaxe")?.Slot);

        InventoryGeneric old = f.Hotbar;
        var replacement = new InventoryGeneric(2, "hotbar", "replacement", null!, (i, inv) => new ItemSlotSurvival(inv));
        replacement.Api = old.Api;
        f.Manager.Setup(x => x.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns(replacement);
        f.Manager.Setup(x => x.ActiveHotbarSlot).Returns(() => replacement[0]);
        cache.ReconcileTopology();
        cache.RefreshPending();
        Assert.Null(cache.GetCached("tool:Pickaxe"));
        old[1].Itemstack = null;
        old.DidModifyItemSlot(old[1]);
        Assert.False(cache.IsDirty);
        cache.Clear();
        replacement.DidModifyItemSlot(replacement[0]);
        Assert.False(cache.IsDirty);
    }
    /// <summary>Checks bag-content topology and hand-selection events invalidate virtual candidates.</summary>
    [Fact]
    public void Cache_ObservesBagTopologyAndActiveHandChanges()
    {
        var f = new Fixture();
        f.PutLight(f.Backpack[0], 1, 12);
        using var cache = new QuickToolCandidateCache();
        cache.Bind(f.Manager.Object, f.Offhand);
        Assert.Same(f.Backpack[0], cache.GetCached(QuickToolLayout.LightId)?.Slot);
        f.Backpack[0] = new ItemSlotBagContent(f.Backpack, 0, 0, EnumItemStorageFlags.General);
        cache.ReconcileTopology();
        Assert.True(cache.IsDirty);
        cache.RefreshPending();
        Assert.Null(cache.GetCached(QuickToolLayout.LightId));

        var events = new Mock<Vintagestory.API.Client.IClientEventAPI>();
        cache.ObserveActiveSlot(events.Object);
        events.Raise(e => e.AfterActiveSlotChanged += null!, new ActiveSlotChangeEventArgs(0, 1));
        Assert.True(cache.IsDirty);
        events.Raise(e => e.LeaveWorld += null!);
        Assert.Null(cache.GetCached(QuickToolLayout.LightId));
        cache.RefreshPending();
        Assert.False(cache.IsDirty);
    }
    /// <summary>Untagged numeric metadata does not create a quick-tool entry.</summary>
    [Fact]
    public void Cache_IgnoresUntaggedNumericCategory()
    {
        var f = new Fixture();
        f.Put(f.Hotbar[1], 1, (EnumTool)500, 5, 1);
        var diagnostics = new List<string>();
        using var cache = new QuickToolCandidateCache(diagnostic: diagnostics.Add);
        cache.Bind(f.Manager.Object, f.Offhand);
        Assert.Null(cache.GetCached("tool:500"));
        Assert.Empty(diagnostics);
        Assert.Single(cache.CreateEntries(false));
    }

    /// <summary>Changed quantity and hand identity cannot reuse an earlier displayed hint.</summary>
    [Fact]
    public void Cache_RejectsChangedQuantityAndDestination()
    {
        var f = new Fixture();
        f.Put(f.Hotbar[1], 1, EnumTool.Axe, 2, 10);
        using var cache = new QuickToolCandidateCache();
        cache.Bind(f.Manager.Object, f.Offhand);
        QuickToolCandidate displayed = cache.GetCached("tool:Axe")!;
        Assert.True(cache.Revalidate("tool:Axe", displayed, f.Hotbar[0], out _));
        displayed.Stack.StackSize = 2;
        Assert.False(cache.Revalidate("tool:Axe", displayed, f.Hotbar[0], out _));
        displayed.Stack.StackSize = 1;
        Assert.False(cache.Revalidate("tool:Axe", displayed, f.Hotbar[2], out _));
    }
    /// <summary>Only current candidates are presented, preserving provider order across reorderings.</summary>
    [Fact]
    public void Cache_EmptyAndReorderedInventoriesPresentOnlyCandidates()
    {
        var f = new Fixture();
        using var cache = new QuickToolCandidateCache();
        cache.Bind(f.Manager.Object, f.Offhand);
        var empty = cache.CreateEntries(false);
        Assert.Single(empty);
        Assert.Equal("unequip", empty[0].Id);
        Assert.False(empty[0].Enabled);

        f.Put(f.Hotbar[1], 1, EnumTool.Knife, 2, 8);
        f.Put(f.Hotbar[2], 2, EnumTool.Knife, 2, 8);
        cache.Invalidate();
        cache.RefreshPending();
        Assert.Same(f.Hotbar[1], cache.GetCached("tool:Knife")?.Slot);
        (f.Hotbar[1].Itemstack, f.Hotbar[2].Itemstack) = (f.Hotbar[2].Itemstack, f.Hotbar[1].Itemstack);
        cache.Invalidate();
        cache.RefreshPending();
        Assert.Same(f.Hotbar[1], cache.GetCached("tool:Knife")?.Slot);
        Assert.Equal(new[] { "tool:Knife", "unequip" }, cache.CreateEntries(false).Select(entry => entry.Id));
    }
    #endregion

    /// <summary>Provides supported slot kinds and a separate excluded inventory.</summary>
    private sealed class Fixture
    {
        public InventoryGeneric Hotbar { get; } = new(6, "hotbar", "test", null!, (i, inv) => i == 5 ? new ItemSlotOffhand(inv) : new ItemSlotSurvival(inv));
        public InventoryGeneric Backpack { get; } = new(2, "backpack", "test", null!, (i, inv) => new ItemSlotBagContent(inv, 0, i, EnumItemStorageFlags.General));
        public InventoryGeneric External { get; } = new(1, "external", "test", null!);
        public Mock<IPlayerInventoryManager> Manager { get; } = new();
        public ItemSlot Offhand => Hotbar[5];

        /// <summary>Wires the owned inventories and selected hand to the manager.</summary>
        public Fixture()
        {
            var api = new Mock<ICoreAPI>();
            api.Setup(x => x.World).Returns(new Mock<IWorldAccessor>().Object);
            Hotbar.Api = api.Object;
            Backpack.Api = api.Object;
            External.Api = api.Object;
            Manager.Setup(x => x.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns(Hotbar);
            Manager.Setup(x => x.GetOwnInventory(GlobalConstants.backpackInvClassName)).Returns(Backpack);
            Manager.Setup(x => x.ActiveHotbarSlot).Returns(() => Hotbar[0]);
        }

        /// <summary>Places a ranked tool in an actual supported slot.</summary>
        public void Put(ItemSlot slot, int id, EnumTool category, int tier, int durability)
            => slot.Itemstack = new ItemStack(new RankedItem(id, category, tier, durability));

        /// <summary>Places one representative collectible recognized by the shared light selector.</summary>
        public void PutLight(ItemSlot slot, int id, byte brightness)
            => slot.Itemstack = new ItemStack(MockItem.CreateLightSource(id, brightness));
    }

    /// <summary>Models a collectible whose modded classification throws.</summary>
    private sealed class FaultyItem(int id) : MockItem(id)
    {
        /// <summary>Throws to exercise unsupported metadata handling.</summary>
        public override EnumTool? GetTool(ItemSlot slot) => throw new InvalidOperationException("incomparable metadata");
    }
    /// <summary>Exposes controlled ranking metadata through the installed virtual collectible methods.</summary>
    private sealed class RankedItem(int id, EnumTool category, int tier, int durability, int maxDurability = 100) : MockItem(id)
    {
        /// <summary>Gets or changes remaining wear without replacing the stack reference.</summary>
        public int Remaining { get; set; } = durability;
        /// <summary>Returns the requested tool category.</summary>
        public override EnumTool? GetTool(ItemSlot slot) => category;
        /// <summary>Returns the requested tool tier.</summary>
        public override int GetToolTier(ItemSlot slot) => tier;
        /// <summary>Models a durable tool for ranking.</summary>
        public override int GetMaxDurability(ItemStack stack) => maxDurability;
        /// <summary>Returns remaining absolute durability.</summary>
        public override int GetRemainingDurability(ItemStack stack) => Remaining;
    }
}
