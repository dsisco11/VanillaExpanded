using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Moq;
using VanillaExpanded.Tests.Unit.AutoLoot.Support;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoLoot;

[Collection("AutoLoot")]
[Trait("Category", "Unit")]
public sealed class AutoLootSystemTests
{
    [Fact]
    public void ShouldLoad_IsClientOnlyRegardlessOfFeatureToggle()
    {
        using var test = new AutoLootTestCase();
        var system = new AutoLootSystem_Client();
        VanillaExpandedModSystem.Config.EnableAutoLoot = false;
        Assert.True(system.ShouldLoad(EnumAppSide.Client));
        Assert.False(system.ShouldLoad(EnumAppSide.Server));
    }

    [Fact]
    public void RealPostfix_PreservesNativeOpeningThenStartsClientHoldAndUnpatchesOnDispose()
    {
        using var test = new AutoLootTestCase();
        test.Controller.Dispose();
        var system = new AutoLootSystem_Client(test.ProgressProvider.Object);
        var original = AccessTools.Method(typeof(EntityBehaviorHarvestable), nameof(EntityBehaviorHarvestable.OnInteract));
        var dialog = (GuiDialogCreatureContents)RuntimeHelpers.GetUninitializedObject(typeof(GuiDialogCreatureContents));
        typeof(GuiDialog).GetField("opened", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, true);
        typeof(EntityBehaviorHarvestable).GetField("dlg", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(test.Behavior, dialog);
        typeof(EntityBehaviorContainer).GetField("Api", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(test.Behavior, test.Fixture.Api);
        test.PlayerEntity.WatchedAttributes.SetString("playerUID", test.Player.Object.PlayerUID);
        try
        {
            system.StartClientSide(test.Fixture.ClientApi);
            Assert.Same(system, AutoLootSystem_Client.Active);
            Assert.Contains(Constants.ModId + ".autoloot.client", Harmony.GetPatchInfo(original).Owners);
            EnumHandling handling = EnumHandling.PassThrough;

            test.Behavior.OnInteract(test.PlayerEntity, test.Fixture.HotbarInventory[0],
                new Vec3d(), EnumInteractMode.Interact, ref handling);

            Assert.Equal(EnumHandling.PassThrough, handling);
            test.Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(test.Inventory), Times.Once);
            test.Tick(0.5f);
            Assert.Single(test.Sent);
            Assert.True(dialog.IsOpened());
        }
        finally
        {
            system.Dispose();
        }
        Assert.Null(AutoLootSystem_Client.Active);
        Assert.DoesNotContain(Constants.ModId + ".autoloot.client",
            Harmony.GetPatchInfo(original)?.Owners ?? []);
        test.Fixture.ClientNetworkMock!.Verify(network => network.RegisterChannel(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void LiveDisable_CancelsImmediatelyAndCanEnableWithoutRepatching()
    {
        using var test = new AutoLootTestCase();
        test.Controller.Dispose();
        var system = new AutoLootSystem_Client(test.ProgressProvider.Object);
        try
        {
            VanillaExpandedModSystem.Config.EnableAutoLoot = false;
            system.StartClientSide(test.Fixture.ClientApi);
            VanillaExpandedModSystem.Config.EnableAutoLoot = true;
            system.OnConfigReloaded(test.Fixture.Api);
            system.Client!.OnInteract(test.Behavior, test.Dialog, test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
            test.Tick(0.25f);
            VanillaExpandedModSystem.Config.EnableAutoLoot = false;
            system.OnConfigReloaded(test.Fixture.Api);
            test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
            test.Tick(1);
            Assert.Empty(test.Sent);
            VanillaExpandedModSystem.Config.EnableAutoLoot = true;
            test.Release();
            system.Client.OnInteract(test.Behavior, test.Dialog, test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
            test.Tick(0.5f);
            Assert.Single(test.Sent);
        }
        finally
        {
            system.Dispose();
        }
    }

    [Fact]
    public void AttachedAutoStash_TakesPrecedenceOverCorpseLootForTheSameHold()
    {
        using var test = new AutoLootTestCase();
        using var attached = new AttachedGestureCase();
        test.Controller.Dispose();
        var system = new AutoLootSystem_Client(test.ProgressProvider.Object);
        try
        {
            system.StartClientSide(test.Fixture.ClientApi);
            system.Client!.OnInteract(test.Behavior, test.Dialog, test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
            test.Tick(0.25f);
            attached.Controller.Begin(attached.Attachable, 1);
            test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
            system.Client.OnInteract(test.Behavior, test.Dialog, test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
            test.Tick(1);
            Assert.Empty(test.Sent);
            attached.Tick(0.5f);
            Assert.Single(attached.Channel.SentPackets);
        }
        finally
        {
            system.Dispose();
        }
    }
}
