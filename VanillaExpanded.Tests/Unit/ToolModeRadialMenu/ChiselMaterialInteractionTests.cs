using Moq;
using VanillaExpanded.RadialMenu;
using VanillaExpanded.ToolModeRadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.ToolModeRadialMenu;

/// <summary>Exercises picker callbacks and locally predicted native packet sequences without claiming server acceptance.</summary>
[Collection("ToolModeRadialMenuConfig")]
[Trait("Category", "Unit")]
public sealed class ChiselMaterialInteractionTests : IDisposable
{
    private readonly string locale = Lang.CurrentLocale;
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    /// <summary>Initializes the translation boundary normally supplied by the game.</summary>
    public ChiselMaterialInteractionTests()
    {
        var service = new Mock<ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] args) => key);
        Lang.AvailableLanguages["en"] = service.Object;
        Lang.ChangeLanguage("en");
    }
    /// <summary>Restores shared language state after each check.</summary>
    public void Dispose()
    {
        if (english is null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english;
        Lang.ChangeLanguage(locale);
    }

    #region Operation
    /// <summary>Stages one item before the existing tool-mode packet and uses freshly resolved addmat.</summary>
    [Fact]
    public void Operation_ConsumesOneAndSendsExistingPacketAfterStage()
    {
        var fixture = new Fixture();
        fixture.Chisel.Modes = [Mode("carve"), Mode("material"), Mode("addmat")];
        Assert.True(fixture.Apply());
        Assert.Equal(2, fixture.Source.StackSize);
        Assert.True(fixture.Cursor.Empty);
        Assert.Equal(2, fixture.Packets.Count);
        Assert.Same(fixture.TransferPackets[0], fixture.Packets[0]);
        var packet = Assert.IsType<Packet_Client>(fixture.Packets[1]);
        Assert.Equal(27, packet.Id);
        Assert.Equal(2, packet.ToolMode.Mode);
        Assert.Equal(10, packet.ToolMode.X);
        Assert.Equal(20, packet.ToolMode.Y);
        Assert.Equal(30, packet.ToolMode.Z);
        Assert.Equal(7, fixture.Tool.Itemstack!.Attributes.GetInt("toolMode"));
        Assert.Equal([(1, EnumMouseButton.Left, EnumMergePriority.DirectMerge)], fixture.Transfers);
    }

    /// <summary>Stages exactly one mouse-held item regardless of the chosen source stack size.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(64)]
    public void Operation_StagesExactlyOneFromAnySourceStack(int count)
    {
        var fixture = new Fixture();
        fixture.Source.Itemstack!.StackSize = count;
        fixture.Candidate.Stack.StackSize = count;
        bool stageObserved = false;
        fixture.OnSend = packet =>
        {
            if (!ReferenceEquals(packet, fixture.TransferPackets[0])) return;
            stageObserved = true;
            Assert.Equal(1, fixture.Cursor.StackSize);
            Assert.Equal(count - 1, fixture.Source.StackSize);
        };
        Assert.True(fixture.Apply());
        Assert.True(stageObserved);
        Assert.True(fixture.Cursor.Empty);
        Assert.Equal(count - 1, fixture.Source.StackSize);
    }

    /// <summary>Returns locally unconsumed creative/rejected material with its own native packet.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Operation_ReturnsKnownRemainder(bool creative)
    {
        var fixture = new Fixture();
        fixture.Chisel.Consume = false;
        fixture.GameMode = creative ? EnumGameMode.Creative : EnumGameMode.Survival;
        Assert.True(fixture.Apply());
        Assert.Equal(3, fixture.Source.StackSize);
        Assert.True(fixture.Cursor.Empty);
        Assert.Equal(3, fixture.Packets.Count);
        Assert.Same(fixture.TransferPackets[0], fixture.Packets[0]);
        Assert.IsType<Packet_Client>(fixture.Packets[1]);
        Assert.Same(fixture.TransferPackets[1], fixture.Packets[2]);
        fixture.Manager.Verify(value => value.DropMouseSlotItems(It.IsAny<bool>()), Times.Never);
    }

    /// <summary>Declines any replacement, count change or attribute change at the recorded address.</summary>
    [Theory]
    [InlineData("identity")]
    [InlineData("count")]
    [InlineData("attributes")]
    [InlineData("context")]
    [InlineData("cursor")]
    public void Operation_StaleInputHasNoPackets(string change)
    {
        var fixture = new Fixture();
        if (change == "identity") fixture.Source.Itemstack = fixture.Source.Itemstack!.Clone();
        if (change == "count") fixture.Source.Itemstack!.StackSize++;
        if (change == "attributes") fixture.Source.Itemstack!.Attributes.SetString("marker", "changed");
        if (change == "context") fixture.ContextCurrent = false;
        if (change == "cursor") fixture.Cursor.Itemstack = fixture.Candidate.Stack.Clone();
        Assert.False(fixture.Apply());
        Assert.Empty(fixture.Packets);
        Assert.Equal(0, fixture.Chisel.Applied);
    }

    /// <summary>A refused stage never executes the empty-cursor native mode branch.</summary>
    [Fact]
    public void Operation_StageFailureDoesNotApplyToolMode()
    {
        var fixture = new Fixture { FailStage = true };
        Assert.False(fixture.Apply());
        Assert.Equal(0, fixture.Chisel.Applied);
        Assert.True(fixture.Cursor.Empty);
        Assert.Equal(3, fixture.Source.StackSize);
        Assert.Single(fixture.Packets);
        Assert.DoesNotContain(fixture.Packets, packet => packet is Packet_Client);
    }

    /// <summary>Local cancellation still returns a staged item, while disconnect leaves native correction authoritative.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Operation_ContextChangeDuringStaging(bool disconnect)
    {
        var fixture = new Fixture();
        fixture.AfterTransfer = () => { fixture.ContextCurrent = false; fixture.WorldCurrent = !disconnect; };
        Assert.False(fixture.Apply());
        Assert.Equal(0, fixture.Chisel.Applied);
        Assert.Equal(disconnect ? 2 : 3, fixture.Source.StackSize);
        Assert.Equal(disconnect ? 1 : 0, fixture.Cursor.StackSize);
        Assert.DoesNotContain(fixture.Packets, packet => packet is Packet_Client);
        fixture.Manager.Verify(value => value.DropMouseSlotItems(It.IsAny<bool>()), Times.Never);
    }

    /// <summary>A predicted return without its native packet is not reported as a completed operation.</summary>
    [Fact]
    public void Operation_MissingReturnPacketDeclinesLocalCompletion()
    {
        var fixture = new Fixture { NullReturnPacket = true };
        fixture.Chisel.Consume = false;
        Assert.False(fixture.Apply());
        Assert.True(fixture.Cursor.Empty);
        Assert.Equal(3, fixture.Source.StackSize);
        Assert.Equal(2, fixture.Packets.Count);
    }
    /// <summary>Return failure drops exactly the known single item through the native mouse-slot operation.</summary>
    [Fact]
    public void Operation_ReturnFailureDropsKnownCursorRemainder()
    {
        var fixture = new Fixture { FailReturn = true };
        fixture.Chisel.Consume = false;
        Assert.False(fixture.Apply());
        Assert.Equal(2, fixture.Source.StackSize);
        Assert.True(fixture.Cursor.Empty);
        Assert.Equal(3, fixture.Packets.Count);
        Assert.Equal(1, fixture.DroppedQuantity);
        fixture.Manager.Verify(value => value.DropMouseSlotItems(false), Times.Once);
        fixture.Manager.Verify(value => value.DropMouseSlotItems(true), Times.Never);
        fixture.Api.Verify(value => value.TriggerIngameError(It.IsAny<object>(), "chisel-material-dropped", It.IsAny<string>()), Times.Once);
    }
    /// <summary>A replacement source is preserved while the known staged remainder is natively dropped.</summary>
    [Fact]
    public void Operation_ReplacedSourceDropsRemainderWithoutOverwriting()
    {
        var fixture = new Fixture();
        ItemStack? replacement = null;
        fixture.AfterTransfer = () =>
        {
            fixture.AfterTransfer = null;
            replacement = fixture.Source.Itemstack!.Clone();
            replacement.StackSize = 10;
            fixture.Source.Itemstack = replacement;
        };
        Assert.False(fixture.Apply());
        Assert.Same(replacement, fixture.Source.Itemstack);
        Assert.Equal(10, fixture.Source.StackSize);
        Assert.True(fixture.Cursor.Empty);
        Assert.Equal(1, fixture.DroppedQuantity);
        Assert.Single(fixture.Transfers);
        fixture.Manager.Verify(value => value.DropMouseSlotItems(false), Times.Once);
    }
    /// <summary>A refused native drop leaves the remainder intact rather than deleting it.</summary>
    [Fact]
    public void Operation_DropFailureRetainsKnownCursorRemainder()
    {
        var fixture = new Fixture { FailReturn = true, FailDrop = true };
        fixture.Chisel.Consume = false;
        Assert.False(fixture.Apply());
        Assert.Equal(2, fixture.Source.StackSize);
        Assert.Equal(1, fixture.Cursor.StackSize);
        Assert.Equal(0, fixture.DroppedQuantity);
        fixture.Manager.Verify(value => value.DropMouseSlotItems(false), Times.Once);
        fixture.Api.Verify(value => value.TriggerIngameError(It.IsAny<object>(), "chisel-material-dropped", It.IsAny<string>()), Times.Never);
    }
    #endregion
    #region Picker callbacks
    /// <summary>Transitions into pages and Back without applying a tool mode.</summary>
    [Fact]
    public void Menu_EntryPagesAndBackKeepSameDialog()
    {
        var fixture = new Fixture();
        for (int index = 2; index < 16; index++) fixture.Hotbar[index].Itemstack = fixture.Source.Itemstack!.Clone();
        var menu = fixture.CreateMenu();
        Assert.Equal(RadialMenuSelectionResult.KeepOpen, menu.Select("1"));
        Assert.Contains(fixture.LastEntries, entry => entry.Id == ChiselMaterialMenuContentFactory.NextId);
        Assert.Equal(RadialMenuSelectionResult.KeepOpen, menu.Select(ChiselMaterialMenuContentFactory.NextId));
        Assert.Contains(fixture.LastEntries, entry => entry.Id == "chisel-material:stack:12");
        Assert.Equal(RadialMenuSelectionResult.KeepOpen, menu.Select(ChiselMaterialMenuContentFactory.BackId));
        Assert.Contains(fixture.LastEntries, entry => entry.Id == "1");
        Assert.Empty(fixture.Packets);
    }
    /// <summary>An occupied cursor declines entry and preserves unrelated held material.</summary>
    [Fact]
    public void Menu_OccupiedCursorDoesNotEnterPicker()
    {
        var fixture = new Fixture();
        fixture.Cursor.Itemstack = fixture.Candidate.Stack.Clone();
        var held = fixture.Cursor.Itemstack;
        Assert.Equal(RadialMenuSelectionResult.KeepOpen, fixture.CreateMenu().Select("1"));
        Assert.Empty(fixture.LastEntries);
        Assert.Same(held, fixture.Cursor.Itemstack);
        Assert.Empty(fixture.Packets);
    }
    /// <summary>Repeated additions refresh stack snapshots and preserve the original target position.</summary>
    [Fact]
    public void Menu_RepeatedSelectionRefreshesParentModesAndCapturedTarget()
    {
        var fixture = new Fixture();
        var menu = fixture.CreateMenu();
        fixture.Selection.Position.X = 99;
        Assert.Equal(RadialMenuSelectionResult.KeepOpen, menu.Select("1"));
        fixture.Chisel.Modes = [Mode("carve"), Mode("material"), Mode("addmat")];
        Assert.Equal(RadialMenuSelectionResult.KeepOpen, menu.Select("chisel-material:stack:0"));
        Assert.Contains(fixture.LastEntries, entry => entry.Id == "2");
        Assert.Equal(RadialMenuSelectionResult.KeepOpen, menu.Select("2"));
        Assert.Equal(RadialMenuSelectionResult.KeepOpen, menu.Select("chisel-material:stack:0"));
        Assert.Equal(1, fixture.Source.StackSize);
        Assert.All(fixture.Packets.OfType<Packet_Client>(), packet => Assert.Equal(10, packet.ToolMode.X));
        Assert.Equal(7, fixture.Tool.Itemstack!.Attributes.GetInt("toolMode"));
    }
    /// <summary>Reentrant selection is serialized and cancellation still returns a staged local item.</summary>
    [Fact]
    public void Menu_ReentrantCancelReturnsKnownStageWithoutApplying()
    {
        var fixture = new Fixture();
        var menu = fixture.CreateMenu();
        menu.Select("1");
        fixture.AfterTransfer = () =>
        {
            Assert.Equal(RadialMenuSelectionResult.KeepOpen, menu.Select("chisel-material:stack:0"));
            menu.Cancel();
        };
        Assert.Equal(RadialMenuSelectionResult.Close, menu.Select("chisel-material:stack:0"));
        Assert.Equal(3, fixture.Source.StackSize);
        Assert.True(fixture.Cursor.Empty);
        Assert.Equal(0, fixture.Chisel.Applied);
    }
    /// <summary>A replaced captured tool or target closes without touching inventory.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Menu_RejectsReplacedToolOrTarget(bool tool)
    {
        var fixture = new Fixture();
        var menu = fixture.CreateMenu();
        if (tool) fixture.Tool.Itemstack = fixture.Tool.Itemstack!.Clone();
        else fixture.Blocks.Setup(value => value.GetBlockEntity(It.IsAny<BlockPos>())).Returns(new BlockEntityChisel());
        Assert.Equal(RadialMenuSelectionResult.Close, menu.Select("1"));
        Assert.Empty(fixture.Packets);
    }
    /// <summary>Numeric carving modes retain the existing direct tool-mode selection behavior.</summary>
    [Fact]
    public void Menu_OrdinarySelectionUsesExistingApply()
    {
        var fixture = new Fixture();
        Assert.Equal(RadialMenuSelectionResult.Close, fixture.CreateMenu().Select("0"));
        Assert.Equal(1, fixture.Chisel.Applied);
        Assert.Equal(27, Assert.IsType<Packet_Client>(Assert.Single(fixture.Packets)).Id);
        Assert.Empty(fixture.Transfers);
    }
    /// <summary>The actual opening owner routes addmat into the picker and cancels safely on world departure.</summary>
    [Fact]
    public void Owner_OpenRoutesPickerAndLeaveWorldStopsSelections()
    {
        var fixture = new Fixture();
        System.Func<string, RadialMenuSelectionResult>? selected = null;
        Action? cancelled = null;
        fixture.Player.Setup(value => value.CurrentBlockSelection).Returns(fixture.Selection);
        fixture.Radial.Setup(value => value.Open(It.IsAny<RadialMenuLayout>(), It.IsAny<IEnumerable<RadialMenuEntry>>(),
            It.IsAny<System.Func<string, RadialMenuSelectionResult>>(), It.IsAny<Action>()))
            .Callback<RadialMenuLayout, IEnumerable<RadialMenuEntry>, System.Func<string, RadialMenuSelectionResult>, Action>(
                (layout, entries, selection, cancel) => { selected = selection; cancelled = cancel; }).Returns(true);
        fixture.Radial.Setup(value => value.Cancel()).Callback(() => cancelled?.Invoke());
        var owner = new ToolModeRadialMenuSystem();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(ToolModeRadialMenuSystem).GetField("api", flags)!.SetValue(owner, fixture.Api.Object);
        typeof(ToolModeRadialMenuSystem).GetField("menu", flags)!.SetValue(owner, fixture.Radial.Object);
        bool enabled = VanillaExpandedModSystem.Config.EnableToolModeRadialMenu;
        try
        {
            VanillaExpandedModSystem.Config.EnableToolModeRadialMenu = true;
            Assert.True(owner.TryOpen());
            Assert.Equal(RadialMenuSelectionResult.KeepOpen, selected!("1"));
            Assert.Contains(fixture.LastEntries, entry => entry.Id == "chisel-material:stack:0");
            typeof(ToolModeRadialMenuSystem).GetMethod("OnLeaveWorld", flags)!.Invoke(owner, null);
            Assert.Equal(RadialMenuSelectionResult.Close, selected("chisel-material:stack:0"));
            Assert.Empty(fixture.Packets);
            fixture.Radial.Verify(value => value.Cancel(), Times.Once);
        }
        finally { VanillaExpandedModSystem.Config.EnableToolModeRadialMenu = enabled; }
    }
    /// <summary>Cursor-driven additions on ordinary displayed modes retain the native legacy path.</summary>
    [Fact]
    public void Menu_ManualCursorMaterialPreservesOrdinaryPath()
    {
        var fixture = new Fixture();
        fixture.Cursor.Itemstack = fixture.Candidate.Stack.Clone();
        Assert.Equal(RadialMenuSelectionResult.KeepOpen, fixture.CreateMenu().Select("0"));
        Assert.Equal(1, fixture.Chisel.Applied);
        Assert.Empty(fixture.Transfers);
        Assert.Equal(2, fixture.Cursor.StackSize);
        Assert.Single(fixture.Packets);
    }
    /// <summary>Changed staged attributes are not applied or overwritten with the original snapshot.</summary>
    [Fact]
    public void Operation_ChangedCursorAttributesPreventApplyAndReturn()
    {
        var fixture = new Fixture();
        fixture.OnSend = packet => fixture.Cursor.Itemstack!.Attributes.SetString("changed", "yes");
        Assert.False(fixture.Apply());
        Assert.Equal(0, fixture.Chisel.Applied);
        Assert.Equal("yes", fixture.Cursor.Itemstack!.Attributes.GetString("changed"));
        Assert.Equal(2, fixture.Source.StackSize);
        Assert.Single(fixture.Transfers);
        fixture.Manager.Verify(value => value.DropMouseSlotItems(It.IsAny<bool>()), Times.Never);
    }
    /// <summary>Failure sending the stage packet still returns the locally known remainder through native movement.</summary>
    [Fact]
    public void Operation_StagePacketSendThrowsReturnsKnownLocalRemainder()
    {
        var fixture = new Fixture();
        fixture.OnSend = packet => { fixture.OnSend = null; throw new InvalidOperationException("controlled send failure"); };
        Assert.False(fixture.Apply());
        Assert.Equal(3, fixture.Source.StackSize);
        Assert.True(fixture.Cursor.Empty);
        Assert.Equal(0, fixture.Chisel.Applied);
        Assert.Equal(2, fixture.Transfers.Count);
    }
    #endregion

    #endregion
    #region Private
    /// <summary>Creates an identifiable vanilla mode entry.</summary>
    private static SkillItem Mode(string code) => new() { Code = new AssetLocation(code), Name = code };
    /// <summary>Matches the native ref-operation transfer signature for a controlled prediction boundary.</summary>
    private delegate object Transfer(ItemSlot source, ItemSlot target, ref ItemStackMoveOperation operation);

    /// <summary>Uses real inventory slots with controllable local tool behavior and native-shaped packet calls.</summary>
    private sealed class Fixture
    {
        public Mock<ICoreClientAPI> Api { get; } = new();
        public Mock<IClientWorldAccessor> World { get; } = new();
        public Mock<IClientPlayer> Player { get; } = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
        public Mock<IPlayerInventoryManager> Manager { get; } = new();
        public Mock<IRadialMenu> Radial { get; } = new();
        public Mock<IBlockAccessor> Blocks { get; } = new();
        public List<RadialMenuEntry> LastEntries { get; private set; } = [];
        public Action<object>? OnSend { get; set; }
        public InventoryGeneric Hotbar { get; } = new(20, "hotbar", "interaction", null!, (index, inventory) => new ItemSlotSurvival(inventory));
        public InventoryGeneric Mouse { get; } = new(1, "mouse", "interaction", null!);
        public TestChisel Chisel { get; } = new();
        public BlockSelection Selection { get; } = new() { Position = new BlockPos(10, 20, 30), Face = BlockFacing.UP, HitPosition = new Vec3d(.2, .3, .4) };
        public BlockEntityChisel Target { get; } = new() { BlockIds = [], AvailMaterialQuantities = [] };
        public ItemSlot Tool => Hotbar[0];
        public ItemSlot Source => Hotbar[1];
        public ItemSlot Cursor => Mouse[0];
        public ChiselMaterialCandidate Candidate { get; }
        public List<object> Packets { get; } = [];
        public List<object> TransferPackets { get; } = [];
        public List<(int Quantity, EnumMouseButton Button, EnumMergePriority Priority)> Transfers { get; } = [];
        public bool ContextCurrent { get; set; } = true;
        public bool WorldCurrent { get; set; } = true;
        public bool FailStage { get; set; }
        public bool FailReturn { get; set; }
        public bool FailDrop { get; set; }
        public int DroppedQuantity { get; private set; }
        public bool NullReturnPacket { get; set; }
        public EnumGameMode GameMode { get; set; }
        public Action? AfterTransfer { get; set; }

        /// <summary>Wires owned slots and a ref-operation mock that performs native local movement.</summary>
        public Fixture()
        {
            var config = new TreeAttribute();
            config.SetString("microblockChiseling", "all");
            World.Setup(value => value.Config).Returns(config);
            Blocks.Setup(value => value.GetBlockEntity(It.IsAny<BlockPos>())).Returns(Target);
            World.Setup(value => value.BlockAccessor).Returns(Blocks.Object);
            World.Setup(value => value.Player).Returns(Player.Object);
            Api.Setup(value => value.World).Returns(World.Object);
            Api.As<ICoreAPI>().Setup(value => value.World).Returns(World.Object);
            Api.Setup(value => value.Logger).Returns(new Mock<ILogger>().Object);
            var network = new Mock<IClientNetworkAPI>();
            network.Setup(value => value.SendPacketClient(It.IsAny<object>())).Callback<object>(packet => { Packets.Add(packet); OnSend?.Invoke(packet); });
            Api.Setup(value => value.Network).Returns(network.Object);
            var data = new Mock<IWorldPlayerData>();
            data.Setup(value => value.CurrentGameMode).Returns(() => GameMode);
            Player.Setup(value => value.WorldData).Returns(data.Object);
            Player.Setup(value => value.InventoryManager).Returns(Manager.Object);
            Manager.Setup(value => value.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns(Hotbar);
            Manager.Setup(value => value.ActiveHotbarSlot).Returns(Tool);
            Manager.Setup(value => value.MouseItemSlot).Returns(Cursor);
            Manager.Setup(value => value.DropMouseSlotItems(false)).Returns(() =>
            {
                if (FailDrop) return false;
                DroppedQuantity += Cursor.TakeOut(1)?.StackSize ?? 0;
                return Cursor.Empty;
            });
            Manager.Setup(value => value.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(), ref It.Ref<ItemStackMoveOperation>.IsAny))
                .Returns(new Transfer((ItemSlot source, ItemSlot target, ref ItemStackMoveOperation operation) =>
                {
                    Transfers.Add((operation.RequestedQuantity, operation.MouseButton, operation.CurrentPriority));
                    if (!(ReferenceEquals(source, Source) ? FailStage : FailReturn)) source.TryPutInto(target, ref operation);
                    var packet = new object();
                    TransferPackets.Add(packet);
                    AfterTransfer?.Invoke();
                    return ReferenceEquals(source, Cursor) && NullReturnPacket ? null! : packet;
                }));
            Radial.Setup(value => value.UpdateLayout(It.IsAny<RadialMenuLayout>(), It.IsAny<IEnumerable<RadialMenuEntry>>()))
                .Callback<RadialMenuLayout, IEnumerable<RadialMenuEntry>>((layout, entries) => LastEntries = entries.ToList());
            Hotbar.Api = Api.Object;
            Mouse.Api = Api.Object;
            Tool.Itemstack = new ItemStack(Chisel);
            Tool.Itemstack.Attributes.SetInt("toolMode", 7);
            Source.Itemstack = new ItemStack(new MaterialBlock(Api.Object), 3);
            Candidate = new ChiselMaterialCandidate(Hotbar, 1, Source.Itemstack, Source.Itemstack.Clone());
        }
        /// <summary>Captures the current tool and target in the actual picker controller.</summary>
        public ChiselMaterialMenu CreateMenu() => new(Api.Object, Radial.Object, Player.Object, Chisel, Tool, Selection, Chisel.Modes, () => WorldCurrent);
        /// <summary>Invokes only the operation under its explicit local context guards.</summary>
        public bool Apply() => ChiselMaterialOperation.TryApply(Api.Object, Chisel, Tool, Player.Object, Selection, Target, Candidate,
            () => ContextCurrent, () => WorldCurrent);
    }
    /// <summary>Models predictable chisel local consumption while observing the exact existing application call.</summary>
    private sealed class TestChisel : ItemChisel
    {
        public bool Consume { get; set; } = true;
        public int Applied { get; private set; }
        public SkillItem[] Modes { get; set; } = [Mode("carve"), Mode("addmat")];
        /// <summary>Initializes the held collectible identity without engine startup.</summary>
        public TestChisel() { ItemId = 100; Code = new AssetLocation("chisel"); }
        /// <summary>Supplies modes that can change independently of the original dialog.</summary>
        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer player, BlockSelection selection) => Modes;
        /// <summary>Predicts one material consumption and preserves the selected carving mode.</summary>
        public override void SetToolMode(ItemSlot slot, IPlayer player, BlockSelection selection, int mode)
        {
            Applied++;
            if (Consume && !player.InventoryManager.MouseItemSlot.Empty) player.InventoryManager.MouseItemSlot.TakeOut(1);
        }
    }
    /// <summary>Supplies deterministic eligible material geometry and native stack metadata.</summary>
    private sealed class MaterialBlock : Block
    {
        /// <summary>Creates a cubic carried stone block with general storage capacity.</summary>
        public MaterialBlock(ICoreAPI coreApi) { api = coreApi; BlockId = 1; Code = new AssetLocation("material"); BlockMaterial = EnumBlockMaterial.Stone; DrawType = EnumDrawType.Cube; MaxStackSize = 64; }
        /// <summary>Returns the standard voxel contribution volume.</summary>
        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor accessor, BlockPos position) => [Cuboidf.Default()];
        /// <summary>Provides a deterministic label without game language assets.</summary>
        public override string GetHeldItemName(ItemStack stack) => "Material";
    }
    #endregion
}
