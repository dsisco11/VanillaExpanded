using Moq;
using System.Text.Json;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Network;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>Verifies client-side execution of planned fuel-slot corrections.</summary>
[Trait("Category", "Unit")]
public class AlloyFuelDepositServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreatePreview_UsefulTaggedPrewarmingFuels_ExcludeIncidentalBurnables(bool taggedInFirepit)
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        MockItem wood = CreateFuel(context.Fixture, 2, "firewood");
        wood.CombustibleProps.BurnTemperature = 700;
        MockItem peat = CreateFuel(context.Fixture, 3, "peat");
        peat.CombustibleProps.BurnTemperature = 800;
        MockItem incidental = CreateFuel(context.Fixture, 4, "incidental-burnable");
        incidental.CombustibleProps.BurnTemperature = 700;
        MarkFuelSource(context.Fixture, wood, peat);
        context.Firepit.fuelSlot.Itemstack = new ItemStack(taggedInFirepit ? wood : incidental, 1);
        context.Fixture.BackpackInventory[1].Itemstack = new ItemStack(taggedInFirepit ? incidental : wood, 1);
        context.Fixture.HotbarInventory[0].Itemstack = new ItemStack(peat, 1);

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player, out var plan));
        Assert.Equal([wood.Code, peat.Code], plan!.PrewarmingStacks.Select(stack => stack.Collectible.Code));
        Assert.All(plan.PrewarmingStacks, stack => Assert.True(stack.StackSize > 0));
    }

    [Theory]
    [InlineData(21)]
    [InlineData(1300)]
    public void CreatePreview_NoQualifyingTaggedPrewarmingFuel_FallsBackToBurnables(int taggedTemperature)
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        MockItem tagged = CreateFuel(context.Fixture, 2, "tagged");
        tagged.CombustibleProps.BurnTemperature = taggedTemperature;
        MockItem incidental = CreateFuel(context.Fixture, 3, "incidental-burnable");
        incidental.CombustibleProps.BurnTemperature = 700;
        MarkFuelSource(context.Fixture, tagged);
        context.Fixture.BackpackInventory[1].Itemstack = new ItemStack(tagged, 1);
        context.Fixture.HotbarInventory[0].Itemstack = new ItemStack(incidental, 1);

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player, out var plan));
        Assert.Equal(incidental.Code, Assert.Single(plan!.PrewarmingStacks).Collectible.Code);
    }

    [Fact]
    public void CreatePreview_CyclesDistinctUsefulPrewarmingTypesAcrossAccessibleInventories()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        MockItem wood = CreateFuel(context.Fixture, 2, "firewood");
        wood.CombustibleProps.BurnTemperature = 700;
        wood.CombustibleProps.BurnDuration = 0.1f;
        MockItem peat = CreateFuel(context.Fixture, 3, "peat");
        peat.CombustibleProps.BurnTemperature = 800;
        MockItem weak = CreateFuel(context.Fixture, 4, "weak");
        weak.CombustibleProps.BurnTemperature = 21;
        MockItem inert = context.Fixture.CreateNonLightSource(5);
        context.Fixture.BackpackInventory[1].Itemstack = new ItemStack(wood, 1);
        context.Fixture.HotbarInventory[0].Itemstack = new ItemStack(peat, 1);
        context.Fixture.HotbarInventory[1].Itemstack = new ItemStack(wood, 2);
        context.Fixture.HotbarInventory[2].Itemstack = new ItemStack(weak, 1);
        context.Fixture.HotbarInventory[3].Itemstack = new ItemStack(inert, 1);
        context.Firepit.fuelSlot.Itemstack = new ItemStack(wood, 3);

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player, out var plan));
        Assert.Equal([wood.Code, peat.Code], plan!.PrewarmingStacks.Select(stack => stack.Collectible.Code));
        Assert.True(plan.PrewarmingStacks[0].StackSize > 64);
        Assert.All(plan.PrewarmingStacks, stack => Assert.True(stack.StackSize > 1));
        Assert.Equal(1, context.Fixture.BackpackInventory[1].StackSize);
        Assert.Equal(3, context.Firepit.fuelSlot.StackSize);
        context.Fixture.ClientNetworkMock!.Verify(network => network.SendPacketClient(It.IsAny<object>()), Times.Never);

        context.Firepit.fuelSlot.Itemstack = null;
        context.Fixture.BackpackInventory.TakeLocked = true;
        context.Fixture.HotbarInventory[1].Itemstack = null;
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player, out _));
        context.Fixture.HotbarInventory[4].Itemstack = new ItemStack(context.Fuel, 64);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player, out plan));
        Assert.Equal(peat.Code, Assert.Single(plan!.PrewarmingStacks).Collectible.Code);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void CreatePreview_PrewarmingRespectsOpenedStorageAndWithdrawal(bool opened, bool locked, bool included)
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        MockItem wood = CreateFuel(context.Fixture, 2, "firewood");
        wood.CombustibleProps.BurnTemperature = 700;
        var chest = new InventoryGeneric(2, "chest", "prewarming", null!)
        {
            Api = context.Fixture.Api, Pos = new BlockPos(1, 0, 0),
            InvNetworkUtil = context.Fixture.InvNetworkUtilMock.Object,
            TakeLocked = locked
        };
        chest[0].Itemstack = new ItemStack(wood, 1);
        var owner = new Mock<BlockEntityGenericContainer>();
        owner.SetupGet(entity => entity.Inventory).Returns(chest);
        Mock.Get(context.Fixture.Api.World.BlockAccessor)
            .Setup(accessor => accessor.GetBlockEntity(chest.Pos)).Returns(owner.Object);
        context.Fixture.InventoryManagerMock.SetupGet(manager => manager.OpenedInventories)
            .Returns(opened ? [context.Firepit.Inventory, chest] : [context.Firepit.Inventory]);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player, out var plan));
        Assert.Equal(included, plan!.PrewarmingStacks.Length == 1);
        Assert.Equal(1, chest[0].StackSize);
    }

    [Theory]
    [InlineData(1, 20, 24, 700)]
    [InlineData(2, 20, 24, 700)]
    [InlineData(1, 500, 0.1f, 800)]
    public void PrewarmingToCeiling_UsesSmallestWholeItemCount(int stackSize, float initialHeat, float duration, int temperature)
    {
        int amount = AlloyCalculatorLogic.CalculatePrewarmingFuelRequired(
            stackSize, 1000, initialHeat, 0, 0, duration, temperature);
        AssertPrewarmingCeiling(amount, stackSize, initialHeat, duration, temperature);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(32, 0)]
    [InlineData(0, 60)]
    [InlineData(32, 60)]
    [InlineData(1, 0)]
    [InlineData(128, 0)]
    public void CreatePreview_PrewarmingDoesNotChangeNormalFuelEstimate(int queuedAmount, float activeBurnTime)
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        MockItem warmingFuel = CreateFuel(context.Fixture, 2, "firewood");
        warmingFuel.CombustibleProps.BurnTemperature = 700;
        float warmingDuration = queuedAmount == 1 ? 0.1f : 24;
        warmingFuel.CombustibleProps.BurnDuration = warmingDuration;
        if (queuedAmount > 0) context.Firepit.fuelSlot.Itemstack = new ItemStack(warmingFuel, queuedAmount);
        context.Firepit.fuelBurnTime = activeBurnTime;
        context.Firepit.maxTemperature = activeBurnTime > 0 ? 700 : 0;

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player,
            out AlloyFuelDepositPlan? plan, out bool isPrewarming));

        bool hasOption = queuedAmount > 0 && activeBurnTime == 0;
        Assert.Equal(hasOption, isPrewarming);
        if (hasOption)
        {
            ItemStack option = Assert.Single(plan!.PrewarmingStacks);
            Assert.Equal(warmingFuel.Code, option.Collectible.Code);
            Assert.NotSame(context.Firepit.fuelSlot.Itemstack, option);
            Assert.True(option.StackSize > 0);
            Assert.Equal(AlloyCalculatorLogic.CalculatePrewarmingFuelRequired(
                1, 1000, context.Firepit.furnaceTemperature, activeBurnTime,
                context.Firepit.maxTemperature, warmingDuration, 700), option.StackSize);
        }
        else
        {
            Assert.Empty(plan!.PrewarmingStacks);
        }
        Assert.Equal(context.Fuel.Code, plan.DesiredStack.Collectible.Code);
        int baseline = AlloyCalculatorLogic.CalculateFuelRequired(
            1, 1000, 600, context.Firepit.furnaceTemperature, 20, 0,
            activeBurnTime, context.Firepit.maxTemperature, 40, 1300);
        Assert.Equal(baseline, plan.DesiredAmount);
        if (hasOption)
        {
            int prewarmed = AlloyCalculatorLogic.CalculateFuelRequired(
                1, 1000, 600, context.Firepit.furnaceTemperature, 20, 0,
                activeBurnTime, context.Firepit.maxTemperature, 40, 1300,
                plan.PrewarmingStacks[0].StackSize, warmingDuration, 700);
            Assert.True(prewarmed < baseline);
            if (queuedAmount == 1) Assert.True(plan.PrewarmingStacks[0].StackSize > queuedAmount);
        }
        Assert.Equal(queuedAmount, context.Firepit.fuelSlot.StackSize);
        Assert.Equal(activeBurnTime, context.Firepit.fuelBurnTime);
        Assert.Equal(64, context.Fixture.BackpackInventory[0].StackSize);
        context.Fixture.ClientNetworkMock!.Verify(network => network.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void CreatePreview_WithoutAvailableSmeltingFuel_HasNoPrewarmingOptions()
    {
        TestContext context = CreateContext(currentFuelAmount: 32);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        context.Fuel.CombustibleProps.BurnTemperature = 700;

        Assert.Equal(AlloyDepositResultCode.InsufficientItems, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player,
            out AlloyFuelDepositPlan? plan, out bool isPrewarming));
        Assert.False(isPrewarming);
        Assert.Null(plan);
        Assert.Equal(32, context.Firepit.fuelSlot.StackSize);
    }

    [Fact]
    public void CreatePreview_NegligiblePrewarming_ExcludesOption()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        MockItem queued = CreateFuel(context.Fixture, 2, "short-burn");
        queued.CombustibleProps.BurnTemperature = 20;
        queued.CombustibleProps.BurnDuration = 0.1f;
        context.Firepit.fuelSlot.Itemstack = new ItemStack(queued, 1);

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player,
            out AlloyFuelDepositPlan? plan, out bool isPrewarming));
        Assert.False(isPrewarming);
        Assert.Empty(plan!.PrewarmingStacks);
    }

    [Fact]
    public void PrewarmingToCeiling_SearchesBeyondOneStack()
    {
        int baseline = AlloyCalculatorLogic.CalculateFuelRequired(1, 1000, 600, 20, 20, 0, 0, 0, 40, 1300);
        int amount = AlloyCalculatorLogic.CalculatePrewarmingFuelRequired(
            1, 1000, 20, 0, 0, 0.1f, 700);
        Assert.True(amount > 64);
        int required = AlloyCalculatorLogic.CalculateFuelRequired(
            1, 1000, 600, 20, 20, 0, 0, 0, 40, 1300, amount, 0.1f, 700);
        Assert.True(required < baseline);
        AssertPrewarmingCeiling(amount, 1, 20, 0.1f, 700);
    }

    [Fact]
    public void PrewarmingToCeiling_ReachesHeatingLimitWithoutSavings()
    {
        int baseline = AlloyCalculatorLogic.CalculateFuelRequired(1, 1000, 600, 20, 20, 0, 0, 0, 40, 1300);
        int amount = AlloyCalculatorLogic.CalculatePrewarmingFuelRequired(
            1, 1000, 20, 0, 0, 24, 21);
        AssertPrewarmingCeiling(amount, 1, 20, 24, 21);
        Assert.Equal(baseline, AlloyCalculatorLogic.CalculateFuelRequired(
            1, 1000, 600, 20, 20, 0, 0, 0, 40, 1300, amount, 24, 21));
    }

    [Theory]
    [InlineData(700, 1, true)]
    [InlineData(700, 1.5f, false)]
    [InlineData(1000, 1, false)]
    [InlineData(1300, 1, false)]
    public void CreatePreview_PrewarmingUsesEffectiveTemperature(int temperature, float heatModifier, bool expected)
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        MockItem queued = CreateFuel(context.Fixture, 2, "queued");
        queued.CombustibleProps.BurnTemperature = temperature;
        context.Firepit.fuelSlot.Itemstack = new ItemStack(queued, 10);
        Assert.IsType<TestFirepit>(context.Firepit).TestHeatModifier = heatModifier;

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player,
            out AlloyFuelDepositPlan? plan, out bool isPrewarming));
        Assert.Equal(expected, isPrewarming);
        Assert.Equal(expected, plan!.PrewarmingStacks.Length > 0);
    }

    [Fact]
    public void CreatePlan_QueuedPrewarmingDoesNotChangeDepositBehavior()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        Assert.IsType<InventorySmelting>(context.Firepit.Inventory).CookingSlots[0].Itemstack = batch[0];
        MockItem queued = CreateFuel(context.Fixture, 2, "firewood");
        queued.CombustibleProps.BurnTemperature = 700;
        context.Firepit.fuelSlot.Itemstack = new ItemStack(queued, 32);

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out AlloyFuelDepositPlan? plan));
        Assert.Empty(plan!.PrewarmingStacks);
        Assert.Equal(AlloyCalculatorLogic.CalculateFuelRequired(1, 1000, 600,
            context.Firepit.furnaceTemperature, context.Firepit.InputStackTemp, 0, 0, 0, 40, 1300),
            plan.DesiredAmount);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(20)]
    public void CreatePreview_UsesSelectedBatchWithEmptyFuelSlot_WithoutMovingItems(int amount)
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, amount);
        var inventory = Assert.IsType<InventorySmelting>(context.Firepit.Inventory);
        ItemStack crucible = context.Firepit.inputStack;
        inventory.CookingSlots[0].Itemstack = new ItemStack(batch[0].Collectible, 7);
        context.Firepit.inputStackCookingTime = 50;

        AlloyDepositResultCode result = AlloyFuelDepositService.CreatePreview(context.Fixture.ClientApi,
            context.Firepit, batch, context.Fixture.Player, out AlloyFuelDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(context.Fuel.Code, plan!.DesiredStack.Collectible.Code);
        Assert.Equal(AlloyCalculatorLogic.CalculateFuelRequired(1, 1000, 30 * amount,
            context.Firepit.furnaceTemperature, 20, 0, context.Firepit.fuelBurnTime,
            context.Firepit.maxTemperature, 40, 1300), plan.DesiredAmount);
        Assert.True(context.Firepit.fuelSlot.Empty);
        Assert.Equal(64, context.Fixture.BackpackInventory[0].StackSize);
        Assert.Equal(7, inventory.CookingSlots[0].StackSize);
        Assert.Same(crucible, context.Firepit.inputStack);
        Assert.Equal(50, context.Firepit.inputStackCookingTime);
        Assert.Equal(amount, batch[0].StackSize);
        context.Fixture.InventoryManagerMock.Verify(manager => manager.TryTransferTo(
            It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(), ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Never);
        context.Fixture.ClientNetworkMock!.Verify(network => network.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void CreatePreview_RecalculatesForTargetChanges_AndAgreesWithColdDepositPlan()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 2);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(context.Fixture.ClientApi,
            context.Firepit, batch, context.Fixture.Player, out AlloyFuelDepositPlan? small));
        batch[0].StackSize = 20;
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(context.Fixture.ClientApi,
            context.Firepit, batch, context.Fixture.Player, out AlloyFuelDepositPlan? large));
        Assert.True(large!.DesiredAmount > small!.DesiredAmount);

        var inventory = Assert.IsType<InventorySmelting>(context.Firepit.Inventory);
        inventory.CookingSlots[0].Itemstack = batch[0].Clone();
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(context.Fixture.ClientApi,
            context.Firepit, context.Fixture.Player, out AlloyFuelDepositPlan? deposit));
        Assert.Equal(large.DesiredAmount, deposit!.DesiredAmount);
        Assert.Equal(large.DesiredStack.Collectible.Code, deposit.DesiredStack.Collectible.Code);
    }

    [Fact]
    public void CreatePreview_NoSufficientFuel_ReportsUnavailable()
    {
        TestContext context = CreateContext();
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player, out AlloyFuelDepositPlan? plan));
        Assert.Null(plan);
    }

    [Fact]
    public void CreatePreview_EmptyBatch_ReportsInvalidRequest()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Assert.Equal(AlloyDepositResultCode.InvalidRequest, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, new Dictionary<int, ItemStack>(),
            context.Fixture.Player, out AlloyFuelDepositPlan? plan));
        Assert.Null(plan);
    }

    [Fact]
    public void CreatePreview_TagPreferenceAndLiveInventoryChanges_MatchDepositPolicy()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 2);
        MockItem cooler = CreateFuel(context.Fixture, 2, "cooler");
        cooler.CombustibleProps.BurnTemperature = 1100;
        context.Fixture.BackpackInventory[1].Itemstack = new ItemStack(cooler, 64);
        MarkFuelSource(context.Fixture, context.Fuel);

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(context.Fixture.ClientApi,
            context.Firepit, batch, context.Fixture.Player, out AlloyFuelDepositPlan? tagged));
        Assert.Equal(context.Fuel.Code, tagged!.DesiredStack.Collectible.Code);
        context.Fixture.BackpackInventory[0].Itemstack = null;
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(context.Fixture.ClientApi,
            context.Firepit, batch, context.Fixture.Player, out AlloyFuelDepositPlan? fallback));
        Assert.Equal(cooler.Code, fallback!.DesiredStack.Collectible.Code);
    }

    [Fact]
    public void CreatePreview_MultipleMetals_UsesHighestMeltingPointAndCombinedDuration()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 4);
        MockItem secondMetal = context.Fixture.CreateNonLightSource(11);
        secondMetal.Code = new AssetLocation("game", "nugget-other");
        secondMetal.CombustibleProps = new CombustibleProperties
        {
            MeltingPoint = 1200,
            MeltingDuration = 60,
            SmeltedRatio = 2
        };
        batch[1] = new ItemStack(secondMetal, 2);

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(context.Fixture.ClientApi,
            context.Firepit, batch, context.Fixture.Player, out AlloyFuelDepositPlan? plan));
        Assert.Equal(AlloyCalculatorLogic.CalculateFuelRequired(1, 1200, 180,
            context.Firepit.furnaceTemperature, 20, 0, 0, context.Firepit.maxTemperature, 40, 1300),
            plan!.DesiredAmount);
        Assert.All(Assert.IsType<InventorySmelting>(context.Firepit.Inventory).CookingSlots,
            slot => Assert.True(slot.Empty));
    }

    [Fact]
    public void CreatePreview_ActiveFuelCanFinishBatch_RequiresNoQueuedFuel()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 2);
        context.Firepit.furnaceTemperature = 1300;
        context.Firepit.maxTemperature = 1300;
        context.Firepit.fuelBurnTime = 1000;

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePreview(context.Fixture.ClientApi,
            context.Firepit, batch, context.Fixture.Player, out AlloyFuelDepositPlan? plan));
        Assert.Equal(0, plan!.DesiredAmount);
        Assert.Equal(1000, context.Firepit.fuelBurnTime);
    }

    [Fact]
    public void CreatePreview_ClosedFirepit_ReportsInventoryClosed()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 2);
        context.Fixture.InventoryManagerMock.SetupGet(manager => manager.OpenedInventories).Returns([]);

        Assert.Equal(AlloyDepositResultCode.InventoryClosed, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player, out AlloyFuelDepositPlan? plan));
        Assert.Null(plan);
    }

    [Fact]
    public void FuelSourcePatch_TagsDedicatedFuelsAndOnlyCoalOreVariants()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VanillaExpanded.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName,
            "VanillaExpanded", "assets", "vanillaexpanded", "patches", "fuelsources.json")));
        var patches = document.RootElement.EnumerateArray()
            .ToDictionary(patch => patch.GetProperty("file").GetString()!);
        string[] fuelAssets =
        [
            "game:itemtypes/resource/firewood.json",
            "game:itemtypes/resource/firewood-aged.json",
            "game:blocktypes/soil/peatbrick.json",
            "game:itemtypes/resource/charcoal.json",
            "game:itemtypes/resource/coke.json",
            "game:itemtypes/resource/coal-contaminated.json"
        ];
        Assert.Equal(fuelAssets.Length + 1, patches.Count);
        foreach (string asset in fuelAssets)
        {
            JsonElement patch = patches[asset];
            Assert.Equal("add", patch.GetProperty("op").GetString());
            Assert.Equal("/tags", patch.GetProperty("path").GetString());
            Assert.Equal(["is-fuel-source"], patch.GetProperty("value").EnumerateArray().Select(tag => tag.GetString()));
        }
        JsonElement orePatch = patches["game:itemtypes/resource/ore-ungraded.json"];
        Assert.Equal("add", orePatch.GetProperty("op").GetString());
        Assert.Equal("/tagsByType", orePatch.GetProperty("path").GetString());
        JsonProperty[] variants = orePatch.GetProperty("value").EnumerateObject().ToArray();
        Assert.Equal(["ore-anthracite", "ore-bituminouscoal", "ore-lignite"],
            variants.Select(variant => variant.Name).Order());
        Assert.All(variants, variant => Assert.Equal(["is-fuel-source"],
            variant.Value.EnumerateArray().Select(tag => tag.GetString())));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreatePlan_TaggedFuel_OutranksCoolerBurnableRegardlessOfLocation(bool taggedInFirepit)
    {
        TestContext context = CreateContext(currentFuelAmount: 64);
        MockItem external = CreateFuel(context.Fixture, 2, "external");
        context.Fixture.BackpackInventory[0].Itemstack = new ItemStack(external, 64);
        MockItem tagged = taggedInFirepit ? context.Fuel : external;
        MockItem untagged = taggedInFirepit ? external : context.Fuel;
        MarkFuelSource(context.Fixture, tagged);
        untagged.CombustibleProps.BurnTemperature = 1100;

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out AlloyFuelDepositPlan? plan));
        Assert.Equal(tagged.Code, plan!.DesiredStack.Collectible.Code);
    }

    [Theory]
    [InlineData(900, 40, 64)]
    [InlineData(1300, 0, 64)]
    [InlineData(1300, 1, 1)]
    public void CreatePlan_TaggedFuelUnsuitableOrInsufficient_FallsBackToUntaggedBurnable(
        int temperature, float duration, int amount)
    {
        TestContext context = CreateContext(currentFuelAmount: 64);
        MockItem tagged = CreateFuel(context.Fixture, 2, "tagged");
        MarkFuelSource(context.Fixture, tagged);
        tagged.CombustibleProps.BurnTemperature = temperature;
        tagged.CombustibleProps.BurnDuration = duration;
        context.Fixture.BackpackInventory[0].Itemstack = new ItemStack(tagged, amount);

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out AlloyFuelDepositPlan? plan));
        Assert.Equal(context.Fuel.Code, plan!.DesiredStack.Collectible.Code);
    }

    [Fact]
    public void CreatePlan_TaggedFuels_StillPrefersCoolestSufficientFuel()
    {
        TestContext context = CreateContext(currentFuelAmount: 64);
        MockItem cooler = CreateFuel(context.Fixture, 2, "cooler");
        cooler.CombustibleProps.BurnTemperature = 1100;
        MarkFuelSource(context.Fixture, context.Fuel, cooler);
        context.Fixture.BackpackInventory[0].Itemstack = new ItemStack(cooler, 64);

        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out AlloyFuelDepositPlan? plan));
        Assert.Equal(cooler.Code, plan!.DesiredStack.Collectible.Code);
    }

    /// <summary>Verifies cooler fuel wins despite needing more items, with fallback when it cannot supply the melt.</summary>
    [Theory]
    [InlineData(1100, 64, true)]
    [InlineData(1100, 1, false)]
    [InlineData(900, 64, false)]
    public void CreatePlan_CoolerFuel_PrefersSufficientSuitableSupply(int temperature, int amount, bool prefersCooler)
    {
        TestContext context = CreateContext(currentFuelAmount: 64);
        MockItem coolerFuel = CreateFuel(context.Fixture, 2, "cooler-fuel");
        coolerFuel.CombustibleProps.BurnTemperature = temperature;
        coolerFuel.CombustibleProps.BurnDuration = 10;
        context.Fixture.BackpackInventory[0].Itemstack = new ItemStack(coolerFuel, amount);

        AlloyDepositResultCode result = AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out AlloyFuelDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        AlloyFuelDepositPlan createdPlan = Assert.IsType<AlloyFuelDepositPlan>(plan);
        Assert.Equal(prefersCooler ? coolerFuel.Code : context.Fuel.Code, createdPlan.DesiredStack.Collectible.Code);
        if (prefersCooler)
        {
            // The preference must override the previous policy of minimizing item count across different fuels.
            context.Fixture.BackpackInventory[0].Itemstack = null;
            Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
                context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out AlloyFuelDepositPlan? hotterPlan));
            Assert.True(createdPlan.DesiredAmount > hotterPlan!.DesiredAmount);
        }
    }

    /// <summary>Verifies fuel eligibility against metal temperature, burn duration, and effective firepit heat.</summary>
    [Theory]
    [InlineData(900, 40, 1, false)]
    [InlineData(1000, 40, 1, true)]
    [InlineData(1300, 0, 1, false)]
    [InlineData(1300, 40, 0.5f, false)]
    [InlineData(900, 40, 1.5f, true)]
    public void CreatePlan_FuelProperties_DetermineSuitability(int temperature, float duration, float heatModifier, bool suitable)
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);
        context.Fuel.CombustibleProps.BurnTemperature = temperature;
        context.Fuel.CombustibleProps.BurnDuration = duration;
        Assert.IsType<TestFirepit>(context.Firepit).TestHeatModifier = heatModifier;

        AlloyDepositResultCode result = AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out AlloyFuelDepositPlan? plan);

        Assert.Equal(suitable ? AlloyDepositResultCode.Success : AlloyDepositResultCode.InsufficientItems, result);
        Assert.Equal(suitable, plan is not null);
    }

    /// <summary>Verifies that planning requires smeltable input in the firepit.</summary>
    [Fact]
    public void CreatePlan_NoInput_ReturnsInvalidRequest()
    {
        TestContext context = CreateContext(includeInput: false);

        AlloyDepositResultCode result = AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            context.Fixture.Player,
            out AlloyFuelDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.InvalidRequest, result);
        Assert.Null(plan);
    }

    /// <summary>Verifies that planning reports unavailable suitable fuel.</summary>
    [Fact]
    public void CreatePlan_NoSuitableFuel_ReturnsInsufficientItems()
    {
        TestContext context = CreateContext();

        AlloyDepositResultCode result = AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            context.Fixture.Player,
            out AlloyFuelDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.InsufficientItems, result);
        Assert.Null(plan);
    }

    /// <summary>Verifies that planning selects available fuel and records the expected current slot state.</summary>
    [Fact]
    public void CreatePlan_SuitableFuelAvailable_ReturnsPlan()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);

        AlloyDepositResultCode result = AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            context.Fixture.Player,
            out AlloyFuelDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        AlloyFuelDepositPlan createdPlan = Assert.IsType<AlloyFuelDepositPlan>(plan);
        Assert.Equal(context.Fuel.Code, createdPlan.DesiredStack.Collectible.Code);
        Assert.InRange(createdPlan.DesiredAmount, 1, 64);
        Assert.Null(createdPlan.ExpectedCurrentStack);
        Assert.Equal(0, createdPlan.ExpectedCurrentAmount);
    }

    /// <summary>Verifies that execution rejects a plan when the fuel slot changed after planning.</summary>
    [Fact]
    public void ExecutePlan_StaleCurrentState_ReturnsInvalidRequest()
    {
        TestContext context = CreateContext(currentFuelAmount: 4);
        var plan = new AlloyFuelDepositPlan(
            new ItemStack(context.Fuel),
            DesiredAmount: 5,
            ExpectedCurrentStack: new ItemStack(context.Fuel, 3),
            ExpectedCurrentAmount: 3);

        AlloyDepositResultCode result = AlloyFuelDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.InvalidRequest, result);
        Assert.Equal(4, context.Firepit.fuelSlot.StackSize);
    }

    /// <summary>Verifies that execution adds only a matching fuel deficit.</summary>
    [Fact]
    public void ExecutePlan_MatchingDeficit_AddsOnlyDeficit()
    {
        TestContext context = CreateContext(currentFuelAmount: 2, backpackFuelAmount: 8);
        var plan = new AlloyFuelDepositPlan(
            new ItemStack(context.Fuel),
            DesiredAmount: 5,
            ExpectedCurrentStack: new ItemStack(context.Fuel, 2),
            ExpectedCurrentAmount: 2);

        AlloyDepositResultCode result = AlloyFuelDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(5, context.Firepit.fuelSlot.StackSize);
        Assert.Equal(5, context.Fixture.BackpackInventory[0].StackSize);
    }

    /// <summary>Verifies that execution replaces a different fuel stack with the desired fuel.</summary>
    [Fact]
    public void ExecutePlan_DifferentFuel_ReplacesStack()
    {
        TestContext context = CreateContext(currentFuelAmount: 4, backpackFuelAmount: 8);
        MockItem otherFuel = CreateFuel(context.Fixture, 2, "other-fuel");
        context.Firepit.fuelSlot.Itemstack = new ItemStack(otherFuel, 4);
        var plan = new AlloyFuelDepositPlan(
            new ItemStack(context.Fuel),
            DesiredAmount: 5,
            ExpectedCurrentStack: new ItemStack(otherFuel, 4),
            ExpectedCurrentAmount: 4);

        AlloyDepositResultCode result = AlloyFuelDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(context.Fuel.Code, context.Firepit.fuelStack.Collectible.Code);
        Assert.Equal(5, context.Firepit.fuelSlot.StackSize);
        Assert.Contains(context.Fixture.BackpackInventory, slot => slot.Itemstack?.Collectible.Code == otherFuel.Code);
    }

    /// <summary>Regression test ensuring excess matching fuel is reduced rather than fully evacuated.</summary>
    [Fact]
    public void ExecutePlan_ExcessMatchingFuel_RemovesOnlyExcess()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem fuel = fixture.CreateNonLightSource(1);
        fuel.Code = new AssetLocation("game", "fuel");
        fuel.MaxStackSize = 64;
        var firepit = new BlockEntityFirepit
        {
            Api = fixture.Api,
            Pos = new BlockPos(0)
        };
        var inventory = Assert.IsType<InventorySmelting>(firepit.Inventory);
        inventory.Api = fixture.Api;
        inventory.InvNetworkUtil = fixture.InvNetworkUtilMock.Object;
        firepit.fuelSlot.Itemstack = new ItemStack(fuel, 8);
        fixture.InventoryManagerMock
            .SetupGet(manager => manager.OpenedInventories)
            .Returns([inventory]);
        var plan = new AlloyFuelDepositPlan(
            new ItemStack(fuel),
            DesiredAmount: 5,
            ExpectedCurrentStack: new ItemStack(fuel, 8),
            ExpectedCurrentAmount: 8);

        AlloyDepositResultCode result = AlloyFuelDepositService.ExecutePlan(
            fixture.ClientApi,
            firepit,
            plan,
            fixture.Player);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(5, firepit.fuelSlot.StackSize);
        Assert.Equal(3, fixture.BackpackInventory[0].StackSize);
        fixture.ClientNetworkMock!.Verify(
            network => network.SendPacketClient(It.IsAny<object>()),
            Times.Once);
    }

    private static void AssertPrewarmingCeiling(int amount, int stackSize, float initialHeat, float duration, int temperature)
    {
        float furnace = initialHeat;
        float input = 20;
        if (input < furnace)
        {
            float rate = (1 + Math.Clamp((furnace - input) / 30, 0, 1.6f)) * 0.1f;
            input = (StepTemperature(input, furnace, rate) + (stackSize - 1) * input) / stackSize;
        }
        bool reached = false;
        for (int count = 0; count <= amount; count++)
        {
            if (count > 0)
            {
                float remaining = duration;
                while (remaining > 0)
                {
                    remaining = Math.Max(0, remaining - 0.1f);
                    furnace = StepTemperature(furnace, temperature, 0.1f);
                    if (input < furnace)
                    {
                        float rate = (1 + Math.Clamp((furnace - input) / 30, 0, 1.6f)) * 0.1f;
                        input = (StepTemperature(input, furnace, rate) + (stackSize - 1) * input) / stackSize;
                    }
                }
            }
            reached = furnace >= temperature && input >= temperature - 1;
            if (count < amount) Assert.False(reached);
        }
        Assert.True(reached);
    }

    private static float StepTemperature(float current, float target, float delta)
    {
        float difference = Math.Abs(current - target);
        delta += delta * (difference / 28);
        return difference < delta || difference < 1
            ? target : current + (current > target ? -delta : delta);
    }

    /// <summary>Creates a deterministic firepit and fuel inventory context.</summary>
    private static TestContext CreateContext(
        bool includeInput = true,
        int currentFuelAmount = 0,
        int backpackFuelAmount = 0)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        var blockAccessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.SetupGet(world => world.BlockAccessor).Returns(blockAccessor.Object);
        MockItem fuel = CreateFuel(fixture, 1, "fuel");
        var firepit = new TestFirepit
        {
            Api = fixture.Api,
            Pos = new BlockPos(0)
        };
        var inventory = Assert.IsType<InventorySmelting>(firepit.Inventory);
        inventory.Api = fixture.Api;
        inventory.InvNetworkUtil = fixture.InvNetworkUtilMock.Object;
        if (includeInput)
        {
            var crucible = new TestSmeltingContainer
            {
                Code = new AssetLocation("game", "crucible"),
                Attributes = Vintagestory.API.Datastructures.JsonObject.FromJson(
                    "{\"cookingContainerSlots\":4,\"maxContainerSlotStackSize\":64}")
            };
            inventory[1].Itemstack = new ItemStack(crucible);
        }
        if (currentFuelAmount > 0) firepit.fuelSlot.Itemstack = new ItemStack(fuel, currentFuelAmount);
        if (backpackFuelAmount > 0) fixture.BackpackInventory[0].Itemstack = new ItemStack(fuel, backpackFuelAmount);
        fixture.InventoryManagerMock
            .SetupGet(manager => manager.OpenedInventories)
            .Returns([inventory]);
        return new TestContext(fixture, firepit, fuel);
    }

    /// <summary>Creates a suitable stackable fuel item.</summary>
    private static MockItem CreateFuel(VsTestFixture fixture, int id, string path)
    {
        MockItem fuel = fixture.CreateNonLightSource(id);
        fuel.Code = new AssetLocation("game", path);
        fuel.MaxStackSize = 64;
        fuel.CombustibleProps = new CombustibleProperties
        {
            BurnDuration = 40,
            BurnTemperature = 1_300
        };
        return fuel;
    }

    private static Dictionary<int, ItemStack> CreatePreviewBatch(TestContext context, int amount)
    {
        var crucible = new BlockSmeltingContainer
        {
            Code = new AssetLocation("game", "crucible"),
            Attributes = JsonObject.FromJson("{\"cookingContainerSlots\":4,\"maxContainerSlotStackSize\":64}")
        };
        context.Firepit.inputSlot.Itemstack = new ItemStack(crucible);
        MockItem metal = context.Fixture.CreateNonLightSource(10);
        metal.Code = new AssetLocation("game", "metalbit-copper");
        metal.CombustibleProps = new CombustibleProperties
        {
            MeltingPoint = 1000,
            MeltingDuration = 30,
            SmeltedRatio = 1
        };
        return new Dictionary<int, ItemStack> { [0] = new(metal, amount) };
    }

    private static void MarkFuelSource(VsTestFixture fixture, params MockItem[] fuels)
    {
        using var reader = new BinaryReader(new MemoryStream([1, 0, 1, 0]));
        TagSet fuelTag = TagSet.FromBytes(reader);
        foreach (MockItem fuel in fuels) fuel.Tags = fuelTag;
        var registry = new Mock<ITagRegistry<TagSet>>();
        registry.Setup(r => r.SlowEnumerateTagNames(It.IsAny<TagSet>()))
            .Returns((TagSet tags) => tags == fuelTag ? new[] { "is-fuel-source" } : []);
        fixture.ClientApiMock!.SetupGet(api => api.CollectibleTagRegistry).Returns(registry.Object);
    }

    private sealed record TestContext(
        VsTestFixture Fixture,
        BlockEntityFirepit Firepit,
        MockItem Fuel);

    /// <summary>Allows fuel planning to exercise the engine's firepit heat modifier contract.</summary>
    private sealed class TestFirepit : BlockEntityFirepit
    {
        public float TestHeatModifier { get; set; } = 1;
        public override float HeatModifier => TestHeatModifier;
    }

    /// <summary>Provides deterministic melting properties for fuel-plan tests.</summary>
    private sealed class TestSmeltingContainer : BlockSmeltingContainer
    {
        /// <summary>Returns a fixed melting point.</summary>
        public override float GetMeltingPoint(
            IWorldAccessor world,
            ISlotProvider cookingSlotsProvider,
            ItemSlot inputSlot) => 1_000;

        /// <summary>Returns a fixed melting duration.</summary>
        public override float GetMeltingDuration(
            IWorldAccessor world,
            ISlotProvider cookingSlotsProvider,
            ItemSlot inputSlot) => 30;
    }
}
