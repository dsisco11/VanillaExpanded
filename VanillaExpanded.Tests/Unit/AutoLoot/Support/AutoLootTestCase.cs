using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.AutoLoot;
using VanillaExpanded.RadialProgress;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.AutoLoot.Support;

internal sealed class AutoLootTestCase : IDisposable
{
    private readonly bool originalEnabled = VanillaExpandedModSystem.Config.EnableAutoLoot;
    private readonly float originalDelay = VanillaExpandedModSystem.Config.AutoLootDelay;
    private readonly bool originalStashEnabled = VanillaExpandedModSystem.Config.EnableAutoStash;
    private readonly bool originalStashTopmost = VanillaExpandedModSystem.Config.AutoStashGuiRendersTopmost;
    private readonly string originalLocale = Lang.CurrentLocale;
    private readonly ITranslationService? originalEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
    public VsTestFixture Fixture { get; } = VsTestFixture.Client();
    public Mock<IClientEventAPI> Events { get; } = new();
    public Mock<IInputAPI> Input { get; } = new();
    public Mock<IClientPlayer> Player { get; }
    public EntityPlayer PlayerEntity { get; }
    public EntityAgent Corpse { get; }
    public Entity? WorldEntity { get; set; }
    public EntityBehaviorHarvestable Behavior { get; }
    public InventoryGeneric Inventory { get; }
    public TestLootDialog Dialog { get; }
    public AutoLootClient Controller { get; }
    public AutoLootSystem_Client System { get; }
    public Mock<IClassRegistryAPI> ClassRegistry { get; } = new();
    public List<EntityProperties> EntityTypes { get; } = [];
    public EntityBehaviorAutoLootable LootBehavior => Assert.IsType<EntityBehaviorAutoLootable>(
        Corpse.GetBehavior<EntityBehaviorAutoLootable>());
    public MouseButtonState Mouse { get; } = new() { Right = true };
    public Mock<IRadialProgressBar> Progress { get; } = new();
    public Mock<IProgressSystemProvider> ProgressProvider { get; } = new();
    public Action<float> Tick { get; private set; } = null!;
    public bool MouseGrabbed { get; set; }
    public bool Paused { get; set; }
    public List<(long EntityId, int Offset, object Packet)> Sent { get; } = [];
    public List<ItemSlot> Attempts { get; } = [];
    public System.Func<ItemSlot, ItemStackMoveOperation, object[]?>? TransferOverride { get; set; }

    public AutoLootTestCase()
    {
        var translations = new Mock<ITranslationService>();
        translations.Setup(service => service.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string _, object[] _) => "Auto Looting");
        Lang.AvailableLanguages["en"] = translations.Object;
        Lang.ChangeLanguage("en");
        VanillaExpandedModSystem.Config.EnableAutoLoot = true;
        VanillaExpandedModSystem.Config.AutoLootDelay = 0.5f;
        Fixture.ClientApiMock!.Setup(api => api.Event).Returns(Events.Object);
        Fixture.ClientApiMock.Setup(api => api.Input).Returns(Input.Object);
        Fixture.ClientApiMock.Setup(api => api.Gui).Returns(Mock.Of<IGuiAPI>());
        Fixture.ClientApiMock.Setup(api => api.IsGamePaused).Returns(() => Paused);
        Input.Setup(api => api.MouseButton).Returns(Mouse);
        Input.Setup(api => api.InWorldMouseButton).Returns(new MouseButtonState());
        Input.Setup(api => api.MouseGrabbed).Returns(() => MouseGrabbed);
        Events.Setup(api => api.RegisterGameTickListener(It.IsAny<Action<float>>(), 20, It.IsAny<int>()))
            .Callback<Action<float>, int, int>((callback, _, _) => Tick = callback).Returns(71);
        Player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
        PlayerEntity = new EntityPlayer { Api = Fixture.Api, World = Fixture.World };
        Player.Setup(player => player.Entity).Returns(PlayerEntity);
        Player.Setup(player => player.PlayerUID).Returns("autoloot-player");
        Player.Setup(player => player.InventoryManager).Returns(Fixture.Player);
        Player.Setup(player => player.CurrentEntitySelection).Returns(() => PlayerEntity.EntitySelection);
        Fixture.ClientWorldMock!.Setup(world => world.Player).Returns(Player.Object);
        Fixture.WorldMock.Setup(world => world.PlayerByUid(It.IsAny<string>())).Returns(Player.Object);
        Fixture.WorldMock.Setup(world => world.Config).Returns(new TreeAttribute());
        Fixture.WorldMock.Setup(world => world.EntityTypes).Returns(EntityTypes);
        Fixture.ApiMock.Setup(api => api.ClassRegistry).Returns(ClassRegistry.Object);
        ClassRegistry.Setup(registry => registry.GetEntityBehaviorClass("harvestable"))
            .Returns(typeof(EntityBehaviorHarvestable));
        EntityTypes.Add(new EntityProperties
        {
            Client = new EntityClientProperties([JsonObject.FromJson("{\"code\":\"harvestable\"}")], null)
        });
        Corpse = new EntityAgent { EntityId = 42, Api = Fixture.Api, World = Fixture.World, Alive = false };
        typeof(Entity).GetProperty(nameof(Entity.Properties))!.SetValue(Corpse,
            new EntityProperties { Client = new EntityClientProperties([], new Dictionary<string, JsonObject>()) });
        Behavior = new EntityBehaviorHarvestable(Corpse);
        Corpse.SidedProperties.Behaviors.Add(Behavior);
        Corpse.WatchedAttributes.SetBool("harvested", true);
        Inventory = new InventoryGeneric(3, "harvestableContents", "42", null!);
        Inventory.Api = Fixture.Api;
        Inventory.InvNetworkUtil = Fixture.InvNetworkUtilMock.Object;
        typeof(EntityBehaviorHarvestable).GetField("inv", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Behavior, Inventory);
        WorldEntity = Corpse;
        Fixture.WorldMock.Setup(world => world.GetEntityById(42)).Returns(() => WorldEntity);
        Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories).Returns([Inventory]);
        Inventory.Open(Player.Object);
        PlayerEntity.EntitySelection = new EntitySelection { Entity = Corpse };
        var item = Fixture.CreateNonLightSource(1);
        item.MaxStackSize = 64;
        Inventory[0].Itemstack = new ItemStack(item, 3);
        Dialog = TestLootDialog.Create(() => Inventory.Close(Player.Object));
        typeof(EntityBehaviorHarvestable).GetField("dlg", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Behavior, Dialog);
        Progress.SetupAllProperties();
        ProgressProvider.Setup(provider => provider.CreateProgressBar(true)).Returns(Progress.Object);
        Fixture.ClientNetworkMock!.Setup(network => network.SendEntityPacketWithOffset(
            It.IsAny<long>(), It.IsAny<int>(), It.IsAny<object>()))
            .Callback<long, int, object>((id, offset, packet) => Sent.Add((id, offset, packet)));
        Fixture.InventoryManagerMock.Setup(manager => manager.TryTransferAway(It.IsAny<ItemSlot>(),
                ref It.Ref<ItemStackMoveOperation>.IsAny, true, false))
            .Returns((ItemSlot source, ref ItemStackMoveOperation operation, bool _, bool _) =>
            {
                Attempts.Add(source);
                Assert.Same(Player.Object, operation.ActingPlayer);
                Assert.Equal(EnumModifierKey.SHIFT, operation.Modifiers);
                Assert.Equal(EnumMouseButton.Left, operation.MouseButton);
                Assert.Equal(EnumMergePriority.AutoMerge, operation.CurrentPriority);
                Assert.Equal(source.StackSize, operation.RequestedQuantity);
                return TransferOverride is not null
                    ? TransferOverride(source, operation) : TransferToPlayer(source, operation);
            });
        System = new AutoLootSystem_Client(ProgressProvider.Object);
        System.Start(Fixture.ClientApi);
        System.StartClientSide(Fixture.ClientApi);
        Controller = System.Client!;
        Events.Raise(events => events.BlockTexturesLoaded += null);
        Corpse.AddBehavior(new EntityBehaviorAutoLootable(Corpse));
    }

    public void Begin() => Controller.OnInteract(Behavior, PlayerEntity, new Vec3d(), EnumInteractMode.Interact);

    public AutoLootTarget ResolveTarget()
    {
        Assert.True(AutoLootTarget.TryCreate(Fixture.ClientApi, Player.Object, Behavior,
            new Vec3d(), out AutoLootTarget? target));
        return target;
    }

    public object[]? TransferToPlayer(ItemSlot source, ItemStackMoveOperation operation)
    {
        var packets = new List<object>();
        foreach (ItemSlot destination in Fixture.BackpackInventory.Concat(Fixture.HotbarInventory))
        {
            if (source.Empty) break;
            int before = source.StackSize;
            object? packet = Fixture.Player.TryTransferTo(source, destination, ref operation);
            if (source.StackSize < before && packet is not null) packets.Add(packet);
        }
        return packets.Count == 0 ? null : packets.ToArray();
    }

    public PlayerInventoryManager UseVanillaTransfers(params InventoryBase[] otherInventories)
    {
        var inventories = new Vintagestory.API.Datastructures.OrderedDictionary<string, InventoryBase>
        {
            [Inventory.InventoryID] = Inventory
        };
        foreach (InventoryGeneric inventory in new[] { Fixture.BackpackInventory, Fixture.HotbarInventory })
        {
            // Expose the existing real slots as owned inventories for vanilla's player-only filter.
            var owned = new Mock<InventoryBasePlayer>(inventory.ClassName, Player.Object.PlayerUID, null!)
            {
                CallBase = true
            };
            owned.Object.Api = Fixture.Api;
            owned.SetupGet(value => value.Count).Returns(inventory.Count);
            owned.Setup(value => value[It.IsAny<int>()]).Returns((int index) => inventory[index]);
            inventories.Add(owned.Object.InventoryID, owned.Object);
        }
        foreach (InventoryBase inventory in otherInventories)
        {
            inventory.Open(Player.Object);
            inventories.Add(inventory.InventoryID, inventory);
        }
        var manager = new Mock<PlayerInventoryManager>(inventories, Player.Object) { CallBase = true }.Object;
        TransferOverride = (ItemSlot source, ItemStackMoveOperation operation) =>
            manager.TryTransferAway(source, ref operation, onlyPlayerInventory: true, slotNotifyEffect: false);
        return manager;
    }

    public MouseEvent Release(EnumMouseButton button = EnumMouseButton.Right)
    {
        var args = new MouseEvent(0, 0, button, 0);
        Events.Raise(events => events.MouseUp += null, args);
        return args;
    }

    public void Dispose()
    {
        try
        {
            System.Dispose();
        }
        finally
        {
            VanillaExpandedModSystem.Config.EnableAutoLoot = originalEnabled;
            VanillaExpandedModSystem.Config.AutoLootDelay = originalDelay;
            VanillaExpandedModSystem.Config.EnableAutoStash = originalStashEnabled;
            VanillaExpandedModSystem.Config.AutoStashGuiRendersTopmost = originalStashTopmost;
            if (originalEnglish is null) Lang.AvailableLanguages.Remove("en");
            else Lang.AvailableLanguages["en"] = originalEnglish;
            Lang.ChangeLanguage(originalLocale);
        }
    }

    internal sealed class TestLootDialog : GuiDialogCreatureContents
    {
        public override string ToggleKeyCombinationCode => "test-autoloot";
        private Action closeInventory = null!;
        private TestLootDialog() : base(null!, null!, null!, "")
        {
        }

        public static TestLootDialog Create(Action closeInventory)
        {
            var dialog = (TestLootDialog)RuntimeHelpers.GetUninitializedObject(typeof(TestLootDialog));
            dialog.closeInventory = closeInventory;
            dialog.opened = true;
            return dialog;
        }

        public override bool TryClose()
        {
            if (!opened) return false;
            opened = false;
            closeInventory();
            return true;
        }
    }
}
