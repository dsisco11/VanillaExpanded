using Moq;
using VanillaExpanded.Network;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects complete attached-container gestures through registered ticks and interaction entry points.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class EntityAttachedContainerGestureTests
{
    #region Public API
    #region Timing and submission
    /// <summary>Uses the configured delay boundary and submits exactly once with the mapped attachment.</summary>
    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    public void CompleteGesture_SendsExactlyOnceAtConfiguredDelay(float delay)
    {
        using var test = new AttachedGestureCase();
        VanillaExpandedModSystem.Config.AutoStashDelay = delay;
        var handling = EnumHandling.PassThrough;
        Assert.False(test.Start(ref handling));
        Assert.Equal(EnumHandling.PreventSubsequent, handling);
        float justBefore = MathF.BitDecrement(delay);
        test.Tick(justBefore);
        Assert.Empty(test.Channel.SentPackets);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Once);
        Assert.Equal(justBefore / delay, test.Progress.Object.Progress);
        Assert.Equal("AutoStashing", test.Progress.Object.Text);
        test.Tick(delay - justBefore);
        var packet = Assert.IsType<Packet_RequestEntityAutoStash>(Assert.Single(test.Channel.SentPackets));
        Assert.Equal(42, packet.EntityId);
        Assert.Equal(1, packet.AttachmentSlotIndex);
        Assert.Equal(1f, test.Progress.Object.Progress);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Tick(1);
        Assert.Single(test.Channel.SentPackets);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Once);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Player.Verify(player => player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract), Times.Once);
    }

    /// <summary>Repeated Begin on the same target preserves elapsed time, including after submission.</summary>
    [Fact]
    public void RepeatedBegin_DoesNotResetElapsedTimeOrResubmit()
    {
        using var test = new AttachedGestureCase();
        test.Controller.Begin(test.Attachable, 1);
        test.Tick(0.25f);
        test.Controller.Begin(test.Attachable, 1);
        test.Tick(0.25f);
        Assert.Single(test.Channel.SentPackets);
        test.Controller.Begin(test.Attachable, 1);
        test.Tick(1);
        Assert.Single(test.Channel.SentPackets);
    }

    /// <summary>Creates progress at the exact grace boundary and removes the same bar when canceled.</summary>
    [Fact]
    public void Progress_BeginsAtGraceBoundaryAndCancels()
    {
        using var test = new AttachedGestureCase();
        test.Controller.Begin(test.Attachable, 1);
        float grace = VanillaExpanded.AutoStashing.BlockBehaviorAutoStashable.PreStashGracePeriodSeconds;
        float before = MathF.BitDecrement(grace);
        test.Tick(before);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Never);
        test.Tick(grace - before);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Once);
        Assert.Equal(grace / VanillaExpandedModSystem.Config.AutoStashDelay, test.Progress.Object.Progress);
        Assert.Equal("AutoStashing", test.Progress.Object.Text);
        test.Mouse.Right = false;
        test.Tick(0);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        Assert.Empty(test.Channel.SentPackets);
    }
    #endregion
    #region Cancellation and lifetime
    /// <summary>Each invalidated gesture cancels without a delayed packet and can restart with a fresh delay.</summary>
    [Theory]
    [InlineData("right")]
    [InlineData("ctrl")]
    [InlineData("shift")]
    [InlineData("entity")]
    [InlineData("slot")]
    [InlineData("invalidselection")]
    [InlineData("disabled")]
    public void InterruptedGesture_CancelsAndRestarts(string reason)
    {
        using var test = new AttachedGestureCase();
        test.Controller.Begin(test.Attachable, 1);
        test.Tick(0.25f);
        switch (reason)
        {
            case "right": test.Mouse.Right = false; break;
            case "ctrl": test.PlayerEntity.Controls.CtrlKey = false; break;
            case "shift": test.PlayerEntity.Controls.ShiftKey = false; break;
            case "entity":
                var otherHost = new EntityAgent { EntityId = 99, World = test.Fixture.World };
                typeof(Entity).GetProperty(nameof(Entity.Properties))!.SetValue(otherHost, test.Host.Properties);
                test.Selection.Entity = otherHost;
                break;
            case "slot": test.Selection.SelectionBoxIndex = 2; break;
            case "invalidselection": test.Selection.SelectionBoxIndex = 0; break;
            case "disabled": VanillaExpandedModSystem.Config.EnableAutoStash = false; break;
        }
        test.Tick(1);
        Assert.Empty(test.Channel.SentPackets);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Mouse.Right = true;
        test.PlayerEntity.Controls.CtrlKey = test.PlayerEntity.Controls.ShiftKey = true;
        test.Host.EntityId = 42;
        test.Selection.Entity = test.Host;
        test.Selection.SelectionBoxIndex = 1;
        VanillaExpandedModSystem.Config.EnableAutoStash = true;
        test.Tick(1);
        Assert.Empty(test.Channel.SentPackets);
        test.Controller.Begin(test.Attachable, 1);
        test.Tick(0.25f);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Exactly(2));
        Assert.Empty(test.Channel.SentPackets);
        test.Tick(0.25f);
        Assert.Single(test.Channel.SentPackets);
    }

    /// <summary>Beginning a different target resets elapsed time and uses the new entity identity.</summary>
    [Fact]
    public void BeginDifferentTarget_RestartsDelay()
    {
        using var test = new AttachedGestureCase();
        test.Controller.Begin(test.Attachable, 1);
        test.Tick(0.25f);
        test.Host.EntityId = 99;
        test.Controller.Begin(test.Attachable, 1);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Tick(0.25f);
        Assert.Empty(test.Channel.SentPackets);
        test.Tick(0.25f);
        var packet = Assert.IsType<Packet_RequestEntityAutoStash>(Assert.Single(test.Channel.SentPackets));
        Assert.Equal(99, packet.EntityId);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Exactly(2));
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Exactly(2));
    }

    /// <summary>Changing to another valid attachment resets elapsed time and selects the new slot.</summary>
    [Fact]
    public void BeginDifferentAttachment_RestartsDelayAndUsesNewSlot()
    {
        using var test = new AttachedGestureCase();
        test.Controller.Begin(test.Attachable, 1);
        test.Tick(0.25f);
        test.Selection.SelectionBoxIndex = 2;
        test.Controller.Begin(test.Attachable, 0);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Tick(0.25f);
        Assert.Empty(test.Channel.SentPackets);
        test.Tick(0.25f);
        var packet = Assert.IsType<Packet_RequestEntityAutoStash>(Assert.Single(test.Channel.SentPackets));
        Assert.Equal(0, packet.AttachmentSlotIndex);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Exactly(2));
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Exactly(2));
    }

    /// <summary>Disposal unregisters the captured listener and cancels pending state even if a stale callback is invoked.</summary>
    [Fact]
    public void Dispose_UnregistersAndCancelsPendingGesture()
    {
        using var test = new AttachedGestureCase();
        test.Controller.Begin(test.Attachable, 1);
        test.Tick(0.25f);
        test.System.Dispose();
        test.Events.Verify(events => events.UnregisterGameTickListener(71), Times.Once);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Tick(1);
        Assert.Empty(test.Channel.SentPackets);
    }
    #endregion
    #region Interaction entry
    /// <summary>Both entry and ticks use mounted-seat controls instead of the player body controls.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MountedPlayer_UsesSeatControls(bool seatHeld)
    {
        using var test = new AttachedGestureCase();
        var seat = new Mock<Vintagestory.API.Common.IMountableSeat>();
        var controls = new Vintagestory.API.Common.EntityControls { CtrlKey = seatHeld, ShiftKey = seatHeld };
        seat.Setup(value => value.Controls).Returns(controls);
        typeof(Vintagestory.API.Common.EntityAgent).GetProperty("MountedOn")!.SetValue(test.PlayerEntity, seat.Object);
        test.PlayerEntity.Controls.CtrlKey = test.PlayerEntity.Controls.ShiftKey = !seatHeld;
        var handling = EnumHandling.PassThrough;
        Assert.Equal(!seatHeld, test.Start(ref handling));
        Assert.Equal(seatHeld ? EnumHandling.PreventSubsequent : EnumHandling.PassThrough, handling);
        test.Tick(0.5f);
        Assert.Equal(seatHeld ? 1 : 0, test.Channel.SentPackets.Count);
    }
    /// <summary>Each isolated entry prerequisite preserves vanilla handling and prevents starting a pending request.</summary>
    [Theory]
    [InlineData("ctrl")]
    [InlineData("shift")]
    [InlineData("disabled")]
    [InlineData("server")]
    [InlineData("selection")]
    [InlineData("nullselection")]
    [InlineData("unmapped")]
    [InlineData("nomatch")]
    [InlineData("controller")]
    [InlineData("mode")]
    public void IneligibleEntry_DoesNotStartGesture(string reason)
    {
        using var test = new AttachedGestureCase();
        switch (reason)
        {
            case "ctrl": test.PlayerEntity.Controls.CtrlKey = false; break;
            case "shift": test.PlayerEntity.Controls.ShiftKey = false; break;
            case "disabled": VanillaExpandedModSystem.Config.EnableAutoStash = false; break;
            case "server": test.Fixture.WorldMock.Setup(world => world.Side).Returns(EnumAppSide.Server); break;
            case "selection": test.Selection.SelectionBoxIndex = 0; break;
            case "nullselection": test.PlayerEntity.EntitySelection = null; break;
            case "unmapped": test.SlotConfigurations[1].AttachmentPointCode = "missing"; break;
            case "nomatch": test.Fixture.BackpackInventory[0].Itemstack = null; break;
            case "controller": test.System.Dispose(); break;
        }
        var handling = EnumHandling.PassThrough;
        Assert.True(test.Start(ref handling, reason == "mode" ? EnumInteractMode.Attack : EnumInteractMode.Interact));
        Assert.Equal(EnumHandling.PassThrough, handling);
        test.Tick(1);
        Assert.Empty(test.Channel.SentPackets);
    }
    #endregion
    #endregion
}




