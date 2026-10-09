using System.Reflection;
using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.RadialProgress;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Drives registered attached-container ticks and real client request dispatch without a client runtime.</summary>
internal sealed class AttachedGestureCase : IDisposable
{
    private readonly AutoStashTestScope scope = new();
    private InventorySnapshot contents;
    private readonly Mock<IHeldBag> bag;
    private readonly string ownerAttributes;
    private readonly int[] attachmentDirtySlots;
    public VsTestFixture Fixture { get; } = VsTestFixture.Client();
    public Mock<IClientEventAPI> Events { get; } = new();
    public Mock<IInputAPI> Input { get; } = new();
    public Mock<IModLoader> Loader { get; } = new();
    public Mock<IClientPlayer> Player { get; }
    public EntityPlayer PlayerEntity { get; }
    public Entity Host { get; }
    public EntityBehaviorAttachable Attachable { get; }
    public WearableSlotConfig[] SlotConfigurations { get; } =
        [new() { AttachmentPointCode = "other" }, new() { AttachmentPointCode = "bag" }];
    public AutoStashSystem_Client System { get; }
    public Mock<IRadialProgressBar> Progress { get; } = new();
    public Mock<IProgressSystemProvider> ProgressProvider { get; } = new();

    public MockClientNetworkChannel Channel { get; } = new();
    public MouseButtonState Mouse { get; } = new() { Right = true };
    public EntitySelection Selection { get; }
    public Action<float> Tick { get; private set; } = null!;
    public EntityAttachedContainerAutoStashClient Controller => System.EntityAttachedContainers!;

    #region Public API
    /// <summary>Wires a real mapping from selection box zero to attachment slot one and captures the registered tick.</summary>
    public AttachedGestureCase()
    {
        Progress.SetupAllProperties();
        ProgressProvider.Setup(provider => provider.CreateProgressBar()).Returns(Progress.Object);
        System = new AutoStashSystem_Client(ProgressProvider.Object);
        Fixture.ClientApiMock!.Setup(api => api.Event).Returns(Events.Object);
        Fixture.ClientApiMock.Setup(api => api.Input).Returns(Input.Object);
        Fixture.ClientApiMock.Setup(api => api.ModLoader).Returns(Loader.Object);
        Fixture.WorldMock.Setup(world => world.Api).Returns(Fixture.Api);
        Input.Setup(api => api.InWorldMouseButton).Returns(Mouse);
        Events.Setup(api => api.RegisterGameTickListener(It.IsAny<Action<float>>(), 20, It.IsAny<int>()))
            .Callback<Action<float>, int, int>((callback, _, _) => Tick = callback).Returns(71);
        Fixture.ClientNetworkMock!.Setup(network => network.GetChannel(Constants.ModId)).Returns(Channel);
        Player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
        PlayerEntity = new EntityPlayer { Api = Fixture.Api, World = Fixture.World };
        PlayerEntity.Controls.CtrlKey = true;
        PlayerEntity.Controls.ShiftKey = true;
        Player.Setup(player => player.Entity).Returns(PlayerEntity);
        Player.Setup(player => player.InventoryManager).Returns(Fixture.Player);
        Fixture.ClientWorldMock!.Setup(world => world.Player).Returns(Player.Object);
        Fixture.WorldMock.Setup(world => world.PlayerByUid(It.IsAny<string>())).Returns(Player.Object);
        Host = new EntityAgent { Api = Fixture.Api, World = Fixture.World, EntityId = 42 };
        typeof(Entity).GetProperty(nameof(Entity.Properties))!.SetValue(Host,
            new EntityProperties { Client = new EntityClientProperties([], new Dictionary<string, Vintagestory.API.Datastructures.JsonObject>()) });
        Attachable = new EntityBehaviorAttachable(Host);
        var inventory = new InventoryGeneric(2, "wearablesInv", "gesture", null!);
        typeof(EntityBehaviorAttachable).GetField("inv", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Attachable, inventory);
        typeof(EntityBehaviorAttachable).GetField("wearableSlots", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Attachable,
            SlotConfigurations);
        // Seed engine selection data, then let the actual mapping method resolve the attachment.
        var boxes = new EntityBehaviorSelectionBoxes(Host);
        var boxField = typeof(EntityBehaviorSelectionBoxes).GetField("selectionBoxes")!;
        var boxType = boxField.FieldType.GetElementType()!;
        var box = Activator.CreateInstance(boxType)!;
        var pointField = boxType.GetField("AttachPoint")!;
        var point = Activator.CreateInstance(pointField.FieldType)!;
        pointField.FieldType.GetField("Code")!.SetValue(point, "bag");
        pointField.SetValue(box, point);
        var secondBox = Activator.CreateInstance(boxType)!;
        var secondPoint = Activator.CreateInstance(pointField.FieldType)!;
        pointField.FieldType.GetField("Code")!.SetValue(secondPoint, "other");
        pointField.SetValue(secondBox, secondPoint);
        var boxArray = Array.CreateInstance(boxType, 2);
        boxArray.SetValue(box, 0);
        boxArray.SetValue(secondBox, 1);
        boxField.SetValue(boxes, boxArray);
        Host.SidedProperties.Behaviors.Add(Attachable);
        Host.SidedProperties.Behaviors.Add(boxes);
        var item = MockItem.CreateNonLightSource(1);
        var bagItem = MockItem.CreateNonLightSource(2);
        var bagBehavior = new Mock<CollectibleBehavior>(bagItem);
        bag = bagBehavior.As<IHeldBag>();
        bag.Setup(value => value.GetContents(It.IsAny<ItemStack>(), It.IsAny<IWorldAccessor>())).Returns(new[] { new ItemStack(item) });
        bagItem.CollectibleBehaviors = [bagBehavior.Object];
        inventory[1].Itemstack = new ItemStack(bagItem);
        inventory[0].Itemstack = inventory[1].Itemstack!.Clone();
        Fixture.BackpackInventory[0].Itemstack = new ItemStack(item);
        Selection = new EntitySelection { Entity = Host, SelectionBoxIndex = 1 };
        PlayerEntity.EntitySelection = Selection;
        Player.Setup(player => player.CurrentEntitySelection).Returns(() => PlayerEntity.EntitySelection);
        System.StartClientSide(Fixture.ClientApi);
        Loader.Setup(loader => loader.GetModSystem<AutoStashSystem_Client>(It.IsAny<bool>())).Returns(System);
        contents = new InventorySnapshot(Fixture.BackpackInventory, Fixture.HotbarInventory, inventory);
        ownerAttributes = Host.WatchedAttributes.ToJsonToken().ToString();
        attachmentDirtySlots = inventory.DirtySlots.OrderBy(index => index).ToArray();
    }

    /// <summary>Invokes the actual interaction entry rather than preloading controller state.</summary>
    public bool Start(ref EnumHandling handling, EnumInteractMode mode = EnumInteractMode.Interact)
    {
        contents = new InventorySnapshot(Fixture.BackpackInventory, Fixture.HotbarInventory, Attachable.Inventory);
        return EntityAttachedContainerAutoStash.HandleInteract(Attachable, PlayerEntity, mode, ref handling);
    }

    /// <summary>Restores configuration and unregisters the actual captured listener even after assertion failure.</summary>
    public void Dispose()
    {
        try
        {
            System.Dispose();
            // A client gesture is advisory; requests must not mutate or persist inventory state.
            contents.AssertUnchangedExcept();
            contents.AssertConserved();
            Assert.Equal(ownerAttributes, Host.WatchedAttributes.ToJsonToken().ToString());
            bag.Verify(value => value.Store(It.IsAny<ItemStack>(), It.IsAny<ItemSlotBagContent>()), Times.Never);
            Fixture.InventoryManagerMock.Verify(value => value.OpenInventory(It.IsAny<IInventory>()), Times.Never);
            Fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
            Fixture.InventoryManagerMock.Verify(value => value.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(),
                ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Never);
            Assert.Equal(attachmentDirtySlots, Attachable.Inventory.DirtySlots.OrderBy(index => index).ToArray());
            Player.Verify(value => value.TriggerFpAnimation(EnumHandInteract.HeldItemInteract), Times.Exactly(Channel.SentPackets.Count));
            var sounds = Fixture.WorldMock.Invocations.Where(call => call.Method.Name == "PlaySoundAt").ToArray();
            Assert.Equal(Channel.SentPackets.Count, sounds.Length);
            Assert.All(sounds, call =>
            {
                Assert.Equal(new AssetLocation("game:sounds/player/poultice-applied"), call.Arguments[0]);
                Assert.Same(PlayerEntity, call.Arguments[1]);
                Assert.Null(call.Arguments[2]);
                Assert.Equal(false, call.Arguments[3]);
                Assert.Equal(16f, call.Arguments[4]);
                Assert.Equal(1f, call.Arguments[5]);
            });
        }
        finally { scope.Dispose(); }
    }
    #endregion
}





