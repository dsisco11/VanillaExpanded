using Moq;
using VanillaExpanded.AutoLoot;
using VanillaExpanded.Tests.Unit.AutoLoot.Support;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.AutoLoot;

[CollectionDefinition("AutoLoot", DisableParallelization = true)]
public sealed class AutoLootTestCollection;

[Collection("AutoLoot")]
[Trait("Category", "Unit")]
public sealed class AutoLootClientTests
{
    [Theory]
    [InlineData(0.5f)]
    [InlineData(1.25f)]
    public void Hold_UsesExactGraceAndDelayAndCollectsOnce(float delay)
    {
        using var test = new AutoLootTestCase();
        VanillaExpandedModSystem.Config.AutoLootDelay = delay;
        VanillaExpandedModSystem.Config.EnableAutoStash = false;
        VanillaExpandedModSystem.Config.AutoStashGuiRendersTopmost = false;
        test.Begin();
        float beforeGrace = MathF.BitDecrement(0.1f);
        test.Tick(beforeGrace);
        test.ProgressProvider.Verify(value => value.CreateProgressBar(It.IsAny<bool>()), Times.Never);
        test.Tick(0.1f - beforeGrace);
        test.ProgressProvider.Verify(value => value.CreateProgressBar(true), Times.Once);
        Assert.Equal(0.1f / delay, test.Progress.Object.Progress);
        Assert.Equal("Auto Looting", test.Progress.Object.Text);
        test.Begin();
        test.Tick(delay - 0.1f);
        Assert.Single(test.Sent);
        Assert.DoesNotContain(test.Inventory, slot => !slot.Empty);
        Assert.Equal(3, test.Fixture.BackpackInventory[0].StackSize);
        test.Begin();
        test.Tick(delay);
        Assert.Single(test.Sent);
        test.ProgressProvider.Verify(value => value.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Fixture.ClientNetworkMock!.Verify(value => value.SendPacketClient(It.IsAny<object>()), Times.Never);
        test.Fixture.InventoryManagerMock.Verify(value => value.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        test.Fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
    }

    [Fact]
    public void ShortClickAndGuiRelease_DoNotCollectOrConsumeInput()
    {
        using var test = new AutoLootTestCase();
        test.Begin();
        test.Tick(0.05f);
        Assert.False(test.Release().Handled);
        test.Tick(1);
        Assert.Empty(test.Sent);
        test.ProgressProvider.Verify(value => value.CreateProgressBar(It.IsAny<bool>()), Times.Never);
        Assert.True(test.Dialog.IsOpened());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UiCursorMovement_IsIgnoredOnlyWhileMouseIsUngrabbed(bool grabbed)
    {
        using var test = new AutoLootTestCase();
        test.MouseGrabbed = grabbed;
        test.Begin();
        test.PlayerEntity.EntitySelection = null;
        test.Tick(0.5f);
        Assert.Equal(grabbed ? 0 : 1, test.Sent.Count);
    }

    [Theory]
    [InlineData("release")]
    [InlineData("closed")]
    [InlineData("session")]
    [InlineData("missing")]
    [InlineData("alive")]
    [InlineData("unharvested")]
    [InlineData("range")]
    [InlineData("dimension")]
    [InlineData("disabled")]
    [InlineData("pause")]
    [InlineData("world")]
    public void InvalidatedHold_CancelsWithoutResumingUntilRelease(string reason)
    {
        using var test = new AutoLootTestCase();
        test.Begin();
        test.Tick(0.25f);
        switch (reason)
        {
            case "release": test.Release(); break;
            case "closed": test.Dialog.TryClose(); break;
            case "session": test.Inventory.Close(test.Player.Object); break;
            case "missing": test.WorldEntity = null; break;
            case "alive": test.Corpse.Alive = true; break;
            case "unharvested": test.Corpse.WatchedAttributes.SetBool("harvested", false); break;
            case "range": test.PlayerEntity.Pos.X = 6; break;
            case "dimension": test.PlayerEntity.Pos.Dimension = 1; break;
            case "disabled": VanillaExpandedModSystem.Config.EnableAutoLoot = false; break;
            case "pause": test.Paused = true; break;
            case "world": test.Events.Raise(events => events.LeaveWorld += null); break;
        }
        test.Tick(1);
        Assert.Empty(test.Sent);
        test.ProgressProvider.Verify(value => value.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Paused = false;
        VanillaExpandedModSystem.Config.EnableAutoLoot = true;
        test.PlayerEntity.Pos.X = 0;
        test.PlayerEntity.Pos.Dimension = 0;
        test.WorldEntity = test.Corpse;
        test.Corpse.Alive = false;
        test.Corpse.WatchedAttributes.SetBool("harvested", true);
        test.Tick(1);
        Assert.Empty(test.Sent);
    }

    [Fact]
    public void ReleaseAndRepressBetweenTicks_RestartsDelay()
    {
        using var test = new AutoLootTestCase();
        test.Begin();
        test.Tick(0.25f);
        test.Release();
        test.Begin();
        test.Tick(0.25f);
        Assert.Empty(test.Sent);
        test.Tick(0.25f);
        Assert.Single(test.Sent);
    }

    [Fact]
    public void DelayedSessionReadiness_DoesNotAccumulateTimeOrForceOpening()
    {
        using var test = new AutoLootTestCase();
        test.Inventory.Close(test.Player.Object);
        test.Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories).Returns([]);
        test.Begin();
        test.Tick(2);
        test.Inventory.Open(test.Player.Object);
        test.Begin();
        test.Tick(2);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(It.IsAny<bool>()), Times.Never);
        Assert.Empty(test.Attempts);

        test.Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories).Returns([test.Inventory]);
        test.Tick(2);
        Assert.Empty(test.Attempts);
        test.Begin();
        test.Tick(0.25f);
        Assert.Equal(0.5f, test.Progress.Object.Progress);
        Assert.Empty(test.Sent);
        test.Tick(0.25f);
        Assert.Single(test.Sent);
        test.Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
    }

    [Fact]
    public void PolledReleaseWithoutMouseEvent_CleansUpAndRestartsTheEntireDelay()
    {
        using var test = new AutoLootTestCase();
        test.Begin();
        test.Tick(0.25f);
        test.Mouse.Right = false;
        test.Tick(0);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Mouse.Right = true;
        test.Tick(1);
        Assert.Empty(test.Sent);
        test.Begin();
        test.Tick(0.25f);
        Assert.Empty(test.Sent);
        test.Tick(0.25f);
        Assert.Single(test.Sent);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(true), Times.Exactly(2));
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Exactly(2));
    }

    [Fact]
    public void InteractingWithAnotherCorpse_CancelsRatherThanRetargetingTheHold()
    {
        using var test = new AutoLootTestCase();
        test.Begin();
        test.Tick(0.25f);
        var other = new EntityAgent { EntityId = 43, World = test.Fixture.World, Api = test.Fixture.Api };
        test.Controller.OnInteract(new EntityBehaviorHarvestable(other),
            test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Begin();
        test.Tick(1);
        Assert.Empty(test.Attempts);
        test.Release();
        test.Begin();
        test.Tick(0.5f);
        Assert.Single(test.Sent);
    }

    [Fact]
    public void ClosingLocalInventory_CancelsImmediatelyAndReopeningStillRequiresRelease()
    {
        using var test = new AutoLootTestCase();
        test.Begin();
        test.Tick(0.25f);
        test.Inventory.Close(test.Player.Object);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Inventory.Open(test.Player.Object);
        test.Begin();
        test.Tick(1);
        Assert.Empty(test.Sent);
        test.Release();
        test.Begin();
        test.Tick(0.5f);
        Assert.Single(test.Sent);
    }

    [Fact]
    public void ClosingAnotherViewersInventory_DoesNotCancelLocalHold()
    {
        using var test = new AutoLootTestCase();
        var otherPlayer = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
        otherPlayer.Setup(player => player.PlayerUID).Returns("other-player");
        test.Inventory.Open(otherPlayer.Object);
        test.Begin();
        test.Tick(0.25f);
        test.Inventory.Close(otherPlayer.Object);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Never);
        test.Tick(0.25f);
        Assert.Single(test.Sent);
    }

    [Fact]
    public void EmptyInventoryAfterCollection_DoesNotRearmFromServerCorrection()
    {
        using var test = new AutoLootTestCase();
        test.Begin();
        test.Tick(0.5f);
        test.Tick(0);
        test.Inventory[0].Itemstack = test.Fixture.BackpackInventory[0].TakeOutWhole();
        test.Begin();
        test.Tick(1);
        Assert.Single(test.Sent);
        test.Release();
        test.Begin();
        test.Tick(0.5f);
        Assert.Equal(2, test.Sent.Count);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("pause")]
    [InlineData("attack")]
    [InlineData("notheld")]
    [InlineData("otherplayer")]
    public void IneligibleInteraction_DoesNotStart(string reason)
    {
        using var test = new AutoLootTestCase();
        if (reason == "disabled") VanillaExpandedModSystem.Config.EnableAutoLoot = false;
        if (reason == "pause") test.Paused = true;
        if (reason == "notheld") test.Mouse.Right = false;
        test.Controller.OnInteract(test.Behavior,
            reason == "otherplayer" ? new EntityPlayer() : test.PlayerEntity, new Vec3d(),
            reason == "attack" ? EnumInteractMode.Attack : EnumInteractMode.Interact);
        test.Tick(1);
        Assert.Empty(test.Sent);
        test.ProgressProvider.Verify(value => value.CreateProgressBar(It.IsAny<bool>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PredictionResult_IsOnePassPerHoldAndDoesNotClaimConfirmedSuccess(bool full)
    {
        using var test = new AutoLootTestCase();
        test.UseVanillaTransfers();
        if (full)
        {
            foreach (ItemSlot slot in test.Fixture.BackpackInventory.Concat(test.Fixture.HotbarInventory))
            {
                slot.Itemstack = test.Inventory[0].Itemstack!.Clone();
                slot.Itemstack.StackSize = 64;
            }
        }

        test.Begin();
        test.Tick(0.5f);
        test.Begin();
        test.Tick(1);
        Assert.Single(test.Attempts);
        Assert.Equal(full ? 0 : 1, test.Sent.Count);
        Assert.Equal(full ? 3 : 0, test.Inventory[0].StackSize);
        Assert.True(test.Dialog.IsOpened());
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        test.Fixture.ClientApiMock!.Verify(api => api.ShowChatMessage(It.IsAny<string>()), Times.Never);
        test.Fixture.ClientApiMock.Verify(api => api.TriggerIngameError(
            It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.DoesNotContain(test.Fixture.WorldMock.Invocations, invocation => invocation.Method.Name.StartsWith("PlaySound"));
        test.Player.Verify(player => player.TriggerFpAnimation(It.IsAny<EnumHandInteract>()), Times.Never);
        test.Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
    }

    [Theory]
    [InlineData("prediction")]
    [InlineData("transport")]
    public void TransferFailure_IsLoggedAndDoesNotRepeatOrClaimSuccess(string failure)
    {
        using var test = new AutoLootTestCase();
        if (failure == "prediction")
        {
            test.TransferOverride = (_, _) => throw new InvalidOperationException("prediction failed");
        }
        else
        {
            test.UseVanillaTransfers();
            test.Fixture.ClientNetworkMock!.Setup(network => network.SendEntityPacketWithOffset(
                It.IsAny<long>(), It.IsAny<int>(), It.IsAny<object>()))
                .Throws(new InvalidOperationException("transport failed"));
        }
        test.Begin();
        test.Tick(0.5f);
        test.Begin();
        test.Tick(1);
        Assert.Single(test.Attempts);
        Assert.Empty(test.Sent);
        test.Fixture.LoggerMock.Verify(logger => logger.Error(It.Is<string>(text => text.Contains("[AutoLoot]")),
            It.IsAny<object[]>()), Times.Once);
        Assert.DoesNotContain(test.Fixture.WorldMock.Invocations, invocation => invocation.Method.Name.StartsWith("PlaySound"));
        test.Player.Verify(value => value.TriggerFpAnimation(It.IsAny<EnumHandInteract>()), Times.Never);
        Assert.True(test.Dialog.IsOpened());
    }

    [Fact]
    public void Dispose_IsIdempotentAndIgnoresStaleTicksAndInteractions()
    {
        using var test = new AutoLootTestCase();
        test.Begin();
        test.Tick(0.25f);
        test.Controller.Dispose();
        test.Controller.Dispose();
        test.Begin();
        test.Tick(1);
        test.Events.Verify(events => events.UnregisterGameTickListener(71), Times.Once);
        test.Events.VerifyRemove(events => events.MouseUp -= It.IsAny<MouseEventDelegate>(), Times.Once);
        test.Events.VerifyRemove(events => events.LeaveWorld -= It.IsAny<Action>(), Times.Once);
        test.ProgressProvider.Verify(value => value.RemoveProgressBar(test.Progress.Object), Times.Once);
        Assert.Empty(test.Sent);
        Assert.True(test.Dialog.IsOpened());
    }

    [Fact]
    public void MissingRendering_DoesNotBlockCollection()
    {
        using var test = new AutoLootTestCase();
        test.ProgressProvider.Setup(provider => provider.CreateProgressBar(true))
            .Returns((VanillaExpanded.RadialProgress.IRadialProgressBar?)null);
        test.Begin();
        test.Tick(0.5f);
        Assert.Single(test.Sent);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(
            It.IsAny<VanillaExpanded.RadialProgress.IRadialProgressBar>()), Times.Never);
    }

    [Fact]
    public void OtherWorldInteraction_PreventsStartingAgainUntilRelease()
    {
        using var test = new AutoLootTestCase();
        test.Begin();
        test.Tick(0.25f);
        test.Controller.CancelUntilRelease();
        test.Begin();
        test.Tick(0.5f);
        Assert.Empty(test.Sent);
        test.Release();
        test.Begin();
        test.Tick(0.5f);
        Assert.Single(test.Sent);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    public void InvalidDelay_IsLoggedAndDoesNotDispatchOrRetry(float invalid)
    {
        using var test = new AutoLootTestCase();
        VanillaExpandedModSystem.Config.AutoLootDelay = invalid;
        test.Begin();
        test.Tick(0.5f);
        test.Begin();
        test.Tick(1);
        Assert.Empty(test.Sent);
        test.Fixture.LoggerMock.Verify(logger => logger.Error(It.Is<string>(text => text.Contains("[AutoLoot]")),
            It.IsAny<object[]>()), Times.Once);
    }
}
