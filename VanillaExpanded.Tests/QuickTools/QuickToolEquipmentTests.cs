using Moq;
using VanillaExpanded.QuickTools;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.QuickTools;

/// <summary>Exercises client native flip sequences with real installed slot implementations.</summary>
public sealed class QuickToolEquipmentTests
{
    #region Successful movements
    /// <summary>Restores the exact original stack and tool home after a first selection.</summary>
    [Fact]
    public void SelectThenRestore_PreservesExactReferences()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        Assert.Same(pick, f.Hotbar[0].Itemstack);
        Assert.Same(original, f.Hotbar[1].Itemstack);
        Assert.True(f.Equipment.HasSession);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Equipment.Restore());
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.False(f.Equipment.HasSession);
        Assert.Equal(2, f.Packets.Count);
    }

    /// <summary>Carries the first displaced item through two selections, including an offhand light.</summary>
    [Fact]
    public void ToolLightToolRestore_PreservesAllHomesAndOriginal()
    {
        var f = new Fixture();
        ItemStack original = f.PutLight(f.Hotbar[0], 1, 6, offhandCompatible: true);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        ItemStack light = f.PutLight(f.Offhand, 3, 20, offhandCompatible: true);
        ItemStack axe = f.PutTool(f.Hotbar[2], 4, EnumTool.Axe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select(QuickToolLayout.LightId));
        Assert.Same(light, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Same(original, f.Offhand.Itemstack);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Axe"));
        Assert.Same(axe, f.Hotbar[0].Itemstack);
        Assert.Same(light, f.Offhand.Itemstack);
        Assert.Same(original, f.Hotbar[2].Itemstack);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Equipment.Restore());
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Same(light, f.Offhand.Itemstack);
        Assert.Same(axe, f.Hotbar[2].Itemstack);
    }

    /// <summary>An initially empty hand returns to empty without creating or deleting a stack.</summary>
    [Fact]
    public void EmptyHand_RestoresEmpty()
    {
        var f = new Fixture();
        ItemStack pick = f.PutTool(f.Hotbar[1], 1, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        Assert.Null(f.Hotbar[1].Itemstack);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Equipment.Restore());
        Assert.Null(f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
    }

    /// <summary>Selecting a candidate already held cannot create artificial restoration state.</summary>
    [Fact]
    public void AlreadyHeldCandidate_IsNoOp()
    {
        var f = new Fixture();
        ItemStack pick = f.PutTool(f.Hotbar[0], 1, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.NoOp, f.Select("tool:Pickaxe"));
        Assert.Same(pick, f.Hotbar[0].Itemstack);
        Assert.False(f.Equipment.HasSession);
        Assert.Empty(f.Packets);
    }

    /// <summary>Two semantic entries resolving to one held stack do not create another movement.</summary>
    [Fact]
    public void ToolAndLightAliases_KeepOneSessionAndOneStack()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        var dual = new ItemStack(new TestItem(2, EnumTool.Pickaxe, 20, true));
        f.Hotbar[1].Itemstack = dual;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        Assert.Equal(QuickToolEquipmentResult.NoOp, f.Select(QuickToolLayout.LightId));
        Assert.Single(f.Packets);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Equipment.Restore());
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(dual, f.Hotbar[1].Itemstack);
        Assert.Equal(2, f.Packets.Count);
    }
    #endregion

    #region Failure and continuity
    /// <summary>Breakage is detected from current metadata even when reference and quantity are unchanged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrokenTrackedStack_InvalidatesAtActionTime(bool breakOriginal)
    {
        var f = new Fixture();
        ItemStack original = f.PutTool(f.Hotbar[0], 1, EnumTool.Axe);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        (breakOriginal ? original : pick).Attributes.SetInt("testRemaining", 0);
        Assert.Equal(QuickToolEquipmentResult.SessionInvalidated, f.Equipment.Restore());
        Assert.Single(f.Packets);
        Assert.Same(pick, f.Hotbar[0].Itemstack);
        Assert.Same(original, f.Hotbar[1].Itemstack);
    }

    /// <summary>An ambiguous original or candidate reference rejects a new session before its first flip.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousInitialReference_RejectsBeforeMovement(bool duplicateCandidate)
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        f.Backpack[0].Itemstack = duplicateCandidate ? pick : original;
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Select("tool:Pickaxe"));
        Assert.Empty(f.Packets);
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.False(f.Equipment.HasSession);
    }

    /// <summary>The same tracked object in two locations cannot be resolved safely.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousTrackedReference_InvalidatesBeforeMovement(bool duplicateCurrent)
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.Backpack[0].Itemstack = duplicateCurrent ? pick : original;
        Assert.Equal(QuickToolEquipmentResult.SessionInvalidated, f.Equipment.Restore());
        Assert.Single(f.Packets);
        Assert.False(f.Equipment.HasSession);
    }

    /// <summary>Moving the current tool out of the active hand ends restoration at the next action.</summary>
    [Fact]
    public void MovedCurrentTool_InvalidatesWithoutOverridingHeldItem()
    {
        var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.Backpack[0].Itemstack = pick;
        ItemStack held = f.PutPlain(f.Hotbar[0], 3);
        Assert.Equal(QuickToolEquipmentResult.SessionInvalidated, f.Equipment.Restore());
        Assert.Same(held, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Backpack[0].Itemstack);
        Assert.Single(f.Packets);
    }

    /// <summary>Clearing lifetime inside a flip stops the chain and cannot recreate restoration history.</summary>
    [Fact]
    public void ClearDuringChain_StopsAfterCurrentFlip()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        ItemStack axe = f.PutTool(f.Hotbar[2], 3, EnumTool.Axe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.Packets.Clear();
        f.Hotbar.SlotModified += _ => f.Equipment.Clear();
        Assert.Equal(QuickToolEquipmentResult.Interrupted, f.Select("tool:Axe"));
        Assert.Single(f.Packets);
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Same(axe, f.Hotbar[2].Itemstack);
        Assert.False(f.Equipment.HasSession);
    }

    /// <summary>An offhand-incompatible displaced item rejects the entire selection unchanged.</summary>
    [Fact]
    public void IncompatibleOffhand_ReturnsRejectedWithoutMutation()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack light = f.PutLight(f.Offhand, 2, 20, offhandCompatible: true);
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Select(QuickToolLayout.LightId));
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(light, f.Offhand.Itemstack);
        Assert.False(f.Equipment.HasSession);
    }

    /// <summary>Replacing a recorded item with an identical-looking stack invalidates restoration.</summary>
    [Fact]
    public void ReplacedOriginal_InvalidatesWithoutSubstitution()
    {
        var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        ItemStack replacement = f.PutPlain(f.Hotbar[1], 1);
        f.Equipment.ValidateRestoration();
        Assert.False(f.Equipment.HasSession);
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Equipment.Restore());
        Assert.Same(replacement, f.Hotbar[1].Itemstack);
    }

    /// <summary>Moved original references can still restore, while unrelated contents remain untouched.</summary>
    [Fact]
    public void MovedOriginalAndBlockedHome_UsesFirstCompatibleFallback()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.Hotbar[3].Itemstack = original;
        ItemStack unrelated = f.PutPlain(f.Hotbar[1], 3);
        f.Equipment.ValidateRestoration();
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Equipment.Restore());
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(unrelated, f.Hotbar[1].Itemstack);
        Assert.Same(pick, f.Hotbar[2].Itemstack);
        Assert.Null(f.Hotbar[3].Itemstack);
    }

    /// <summary>A full eligible inventory leaves the entire blocked restoration unchanged.</summary>
    [Fact]
    public void NoFallback_RejectsRestoreWithoutMovingAnyStack()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.Hotbar[3].Itemstack = original;
        ItemStack blocked = f.PutPlain(f.Hotbar[1], 3);
        f.PutPlain(f.Hotbar[2], 4);
        f.PutPlain(f.Hotbar[4], 5);
        for (int i = 0; i < f.Backpack.Count; i++) f.PutPlain(f.Backpack[i], 6 + i);
        f.Equipment.ValidateRestoration();
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Equipment.Restore());
        Assert.Same(pick, f.Hotbar[0].Itemstack);
        Assert.Same(blocked, f.Hotbar[1].Itemstack);
        Assert.Same(original, f.Hotbar[3].Itemstack);
        Assert.True(f.Equipment.HasSession);
    }

    /// <summary>Selecting tracked original A collapses to restoration without duplicating its reference.</summary>
    [Fact]
    public void SelectingOriginalTool_RestoresInsteadOfDuplicating()
    {
        var f = new Fixture();
        ItemStack original = f.PutTool(f.Hotbar[0], 1, EnumTool.Axe);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Axe"));
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.False(f.Equipment.HasSession);
    }

    /// <summary>A whole-stack capacity mismatch in offhand rejects before any assignment.</summary>
    [Fact]
    public void OffhandCapacity_RejectsBeforeMutation()
    {
        var f = new Fixture();
        ItemStack original = f.PutLight(f.Hotbar[0], 1, 8, offhandCompatible: true);
        original.StackSize = 2;
        ItemStack light = f.PutLight(f.Offhand, 2, 20, offhandCompatible: true);
        f.Offhand.MaxSlotStackSize = 1;
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Select(QuickToolLayout.LightId));
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(light, f.Offhand.Itemstack);
    }

    /// <summary>A native callback fault after local mutation interrupts packet creation and clears history.</summary>
    [Fact]
    public void NotificationFailure_ReportsInterruptedLocalState()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        f.Hotbar.SlotModified += _ => throw new InvalidOperationException("observer failure");
        Assert.Equal(QuickToolEquipmentResult.Interrupted, f.Select("tool:Pickaxe"));
        Assert.Same(pick, f.Hotbar[0].Itemstack);
        Assert.Same(original, f.Hotbar[1].Itemstack);
        Assert.False(f.Equipment.HasSession);
        Assert.Empty(f.Packets);
    }

    /// <summary>A nested request during publication cannot act on unconfirmed session state.</summary>
    [Fact]
    public void OverlappingNotificationRequest_IsRejected()
    {
        var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        f.PutTool(f.Hotbar[2], 3, EnumTool.Axe);
        QuickToolCandidate axe = new ToolCandidateProvider(EnumTool.Axe).Resolve(f.Manager.Object, f.Offhand)!;
        QuickToolCandidate hint = axe;
        QuickToolEquipmentResult nested = QuickToolEquipmentResult.LocallyApplied;
        f.Hotbar.SlotModified += _ => nested = f.Equipment.Select("tool:Axe", hint);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        Assert.Equal(QuickToolEquipmentResult.Rejected, nested);
        Assert.Same(axe.Stack, f.Hotbar[2].Itemstack);
    }

    /// <summary>A duplicate with matching item code cannot replace the selected exact stack.</summary>
    [Fact]
    public void DuplicateToolReplacement_InvalidatesInsteadOfSubstituting()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        ItemStack duplicate = f.PutTool(f.Backpack[0], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.Hotbar[0].Itemstack = duplicate;
        f.Equipment.ValidateRestoration();
        Assert.False(f.Equipment.HasSession);
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Equipment.Restore());
        Assert.Same(duplicate, f.Hotbar[0].Itemstack);
        Assert.Same(original, f.Hotbar[1].Itemstack);
    }

    /// <summary>Breaking a tracked current tool ends restoration before another reference can be moved.</summary>
    [Fact]
    public void ConsumedCurrentTool_InvalidatesRestoration()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        pick.StackSize = 0;
        f.Equipment.ValidateRestoration();
        Assert.False(f.Equipment.HasSession);
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Equipment.Restore());
        Assert.Same(pick, f.Hotbar[0].Itemstack);
        Assert.Same(original, f.Hotbar[1].Itemstack);
    }

    /// <summary>Manual active-slot intent ends the prior restoration history without moving stacks.</summary>
    [Fact]
    public void ManualHotbarChange_DiscardsSessionOnly()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.Equipment.OnManualActiveSlotChanged();
        Assert.False(f.Equipment.HasSession);
        Assert.Same(pick, f.Hotbar[0].Itemstack);
        Assert.Same(original, f.Hotbar[1].Itemstack);
    }

    /// <summary>An unrelated inventory edit cannot erase an otherwise intact restoration session.</summary>
    [Fact]
    public void UnrelatedEdit_PreservesSession()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        ItemStack unrelated = f.PutPlain(f.Backpack[1], 3);
        f.Equipment.ValidateRestoration();
        Assert.True(f.Equipment.HasSession);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Equipment.Restore());
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Same(unrelated, f.Backpack[1].Itemstack);
    }

    /// <summary>A quantity change to recorded original A prevents a later swap from moving the wrong whole stack.</summary>
    [Fact]
    public void OriginalQuantityChange_InvalidatesWithoutMovement()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        original.StackSize = 2;
        f.Equipment.ValidateRestoration();
        Assert.False(f.Equipment.HasSession);
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Equipment.Restore());
        Assert.Same(pick, f.Hotbar[0].Itemstack);
        Assert.Same(original, f.Hotbar[1].Itemstack);
    }

    /// <summary>An unknown semantic entry cannot use a valid displayed slot to bypass provider selection.</summary>
    [Fact]
    public void UnknownEntry_RejectsBeforeNativePacket()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Equipment.Select("tool:Unsupported", displayed));
        Assert.Empty(f.Packets);
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
    }

    /// <summary>A corrective replacement ends restoration even when the item looks identical.</summary>
    [Fact]
    public void CorrectiveReplacement_InvalidatesOriginal()
    {
        var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.PutPlain(f.Hotbar[1], 1);
        f.Equipment.ValidateRestoration();
        Assert.False(f.Equipment.HasSession);
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Equipment.Restore());
    }

    /// <summary>An equal-looking replacement outside an expected native update cannot be treated as A.</summary>
    [Fact]
    public void UnexpectedReplacement_InvalidatesEvenWithEqualContents()
    {
        var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.PutPlain(f.Hotbar[1], 1);
        f.Equipment.ValidateRestoration();
        Assert.False(f.Equipment.HasSession);
    }

    /// <summary>A chained selection emits two ordinary packets in the precise return-then-equip order.</summary>
    [Fact]
    public void ChainedSelection_SendsNativeFlipsInOrder()
    {
        var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        f.PutTool(f.Hotbar[2], 3, EnumTool.Axe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.Packets.Clear();
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Axe"));
        Assert.Collection(f.Packets,
            packet => Assert.Equal(new NativeFlipPacket(f.Hotbar, f.Hotbar, 0, 1), packet),
            packet => Assert.Equal(new NativeFlipPacket(f.Hotbar, f.Hotbar, 0, 2), packet));
    }

    /// <summary>A restriction introduced after the first native flip stops the sequence with actual contents intact.</summary>
    [Fact]
    public void SecondFlipRejected_ReportsPartialStateWithoutRollback()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        ItemStack axe = f.PutTool(f.Hotbar[2], 3, EnumTool.Axe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.Packets.Clear();
        f.Hotbar.SlotModified += slot =>
        {
            if (slot == 0) f.Hotbar[2].MaxSlotStackSize = 0;
        };
        Assert.Equal(QuickToolEquipmentResult.Interrupted, f.Select("tool:Axe"));
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Same(axe, f.Hotbar[2].Itemstack);
        Assert.False(f.Equipment.HasSession);
        Assert.Single(f.Packets);
    }

    /// <summary>A callback changing C's source after returning B cannot make the client equip that replacement.</summary>
    [Fact]
    public void CandidateSourceChangesAfterFirstFlip_StopsBeforeSecondPacket()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        f.PutTool(f.Hotbar[2], 3, EnumTool.Axe);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Select("tool:Pickaxe"));
        f.Packets.Clear();
        ItemStack replacement = new(new TestItem(4, EnumTool.Axe));
        f.Hotbar.SlotModified += slot =>
        {
            if (slot == 0) f.Hotbar[2].Itemstack = replacement;
        };
        Assert.Equal(QuickToolEquipmentResult.Interrupted, f.Select("tool:Axe"));
        Assert.Single(f.Packets);
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Same(replacement, f.Hotbar[2].Itemstack);
        Assert.False(f.Equipment.HasSession);
    }

    /// <summary>A native first-flip rejection leaves both client slots unchanged and sends no packet.</summary>
    [Fact]
    public void NativeFirstFlipRejected_LeavesSnapshotUnchanged()
    {
        var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutTool(f.Hotbar[1], 2, EnumTool.Pickaxe);
        var desired = new Dictionary<ItemSlot, ItemStack?>
        {
            [f.Hotbar[0]] = pick,
            [f.Hotbar[1]] = original
        };
        QuickToolMovementPlan plan = QuickToolMovementPlan.TryCreate(
            new QuickToolInventoryView(f.Manager.Object, allowGenericFixture: true), f.Offhand, desired)!;
        Assert.NotNull(plan);
        f.Hotbar[1].MaxSlotStackSize = 0;
        Assert.Equal(QuickToolMovementStatus.Rejected,
            plan.Execute(f.Hotbar[0], [f.Hotbar[1]], packet => f.Packets.Add(packet)));
        Assert.Empty(f.Packets);
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
    }
    #endregion

    /// <summary>Supplies real slots and a narrow mocked player inventory manager.</summary>
    private sealed class Fixture
    {
        public InventoryGeneric Hotbar { get; } = new(6, "hotbar", "equipment-test", null!, (i, inv) => i == 5 ? new ItemSlotOffhand(inv) : new ItemSlotSurvival(inv));
        public InventoryGeneric Backpack { get; } = new(3, "backpack", "equipment-test", null!, (i, inv) => new ItemSlotBagContent(inv, 0, i, EnumItemStorageFlags.General));
        public Mock<IPlayerInventoryManager> Manager { get; } = new();
        public ItemSlot Offhand => Hotbar[5];
        public QuickToolEquipment Equipment { get; }
        public List<object> Packets { get; } = [];

        /// <summary>Initializes installed slot callbacks and the native packet factory fixture.</summary>
        public Fixture()
        {
            var api = new Mock<ICoreAPI>();
            api.Setup(x => x.World).Returns(new Mock<IWorldAccessor>().Object);
            Hotbar.Api = api.Object;
            Backpack.Api = api.Object;
            var hotbarPackets = new Mock<IInventoryNetworkUtil>();
            hotbarPackets.Setup(x => x.GetFlipSlotsPacket(It.IsAny<IInventory>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((IInventory source, int sourceIndex, int targetIndex)
                    => new NativeFlipPacket(Hotbar, source, sourceIndex, targetIndex));
            var backpackPackets = new Mock<IInventoryNetworkUtil>();
            backpackPackets.Setup(x => x.GetFlipSlotsPacket(It.IsAny<IInventory>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((IInventory source, int sourceIndex, int targetIndex)
                    => new NativeFlipPacket(Backpack, source, sourceIndex, targetIndex));
            Hotbar.InvNetworkUtil = hotbarPackets.Object;
            Backpack.InvNetworkUtil = backpackPackets.Object;
            Manager.Setup(x => x.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns(Hotbar);
            Manager.Setup(x => x.GetOwnInventory(GlobalConstants.backpackInvClassName)).Returns(Backpack);
            Manager.Setup(x => x.ActiveHotbarSlot).Returns(() => Hotbar[0]);
            Manager.Setup(x => x.OffhandHotbarSlot).Returns(() => Offhand);
            Manager.Setup(x => x.ActiveHotbarSlotNumber).Returns(0);
            Equipment = new QuickToolEquipment(Manager.Object, Offhand, packet => Packets.Add(packet), allowGenericFixture: true);
        }

        /// <summary>Resolves the current winner before the equipment owner reruns the provider.</summary>
        public QuickToolEquipmentResult Select(string id)
        {
            IQuickToolCandidateProvider provider = id == QuickToolLayout.LightId
                ? new LightCandidateProvider()
                : new ToolCandidateProvider(Enum.Parse<EnumTool>(id[5..]));
            QuickToolCandidate? candidate = provider.Resolve(Manager.Object, Offhand);
            Assert.NotNull(candidate);
            return Equipment.Select(id, candidate!);
        }

        /// <summary>Places an ordinary item at one current slot.</summary>
        public ItemStack PutPlain(ItemSlot slot, int id)
        {
            var stack = new ItemStack(new TestItem(id));
            slot.Itemstack = stack;
            return stack;
        }

        /// <summary>Places a tool with stable category metadata and usable durability.</summary>
        public ItemStack PutTool(ItemSlot slot, int id, EnumTool category)
        {
            var stack = new ItemStack(new TestItem(id, category));
            slot.Itemstack = stack;
            return stack;
        }

        /// <summary>Places a positive-light collectible with explicit offhand storage flags.</summary>
        public ItemStack PutLight(ItemSlot slot, int id, byte brightness, bool offhandCompatible)
        {
            var stack = new ItemStack(new TestItem(id, null, brightness, offhandCompatible));
            slot.Itemstack = stack;
            return stack;
        }
    }

    /// <summary>Exposes stable installed collectible methods without a game world dependency.</summary>
    private sealed class TestItem(int id, EnumTool? category = null, byte brightness = 0, bool offhandCompatible = false) : MockItem(id, brightness)
    {
        /// <summary>Returns the intended category.</summary>
        public override EnumTool? GetTool(ItemSlot slot) => category;
        /// <summary>Returns one stable tier.</summary>
        public override int GetToolTier(ItemSlot slot) => 1;
        /// <summary>Returns durable metadata only for tools.</summary>
        public override int GetMaxDurability(ItemStack stack) => category is null ? 0 : 100;
        /// <summary>Returns a usable remaining absolute durability.</summary>
        public override int GetRemainingDurability(ItemStack stack) => category is null ? 0 : stack.Attributes.GetInt("testRemaining", 20);
        /// <summary>Permits only designated test lights in offhand storage.</summary>
        public override EnumItemStorageFlags GetStorageFlags(ItemStack stack)
            => offhandCompatible ? EnumItemStorageFlags.General | EnumItemStorageFlags.Offhand : EnumItemStorageFlags.General;
    }

    /// <summary>Captures the native inventory packet's actual source and target addresses.</summary>
    private sealed record NativeFlipPacket(IInventory TargetInventory, IInventory SourceInventory, int SourceIndex, int TargetIndex);
}
