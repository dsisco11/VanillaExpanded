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
    [InlineData(0, 0)]
    [InlineData(32, 0)]
    [InlineData(0, 60)]
    [InlineData(32, 60)]
    [InlineData(1, 0)]
    [InlineData(128, 0)]
    public void CreatePreview_PrewarmingShowsExactSavingsForSameSmeltingFuel(int queuedAmount, float activeBurnTime)
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

        Assert.Equal(queuedAmount > 0 || activeBurnTime > 0, isPrewarming);
        Assert.Equal(isPrewarming, plan!.IsPrewarming);
        if (queuedAmount > 0)
        {
            Assert.NotNull(plan.PrewarmingStack);
            Assert.Equal(warmingFuel.Code, plan.PrewarmingStack.Collectible.Code);
            Assert.NotSame(context.Firepit.fuelSlot.Itemstack, plan.PrewarmingStack);
        }
        else
        {
            Assert.Null(plan.PrewarmingStack);
        }
        Assert.Equal(context.Fuel.Code, plan.DesiredStack.Collectible.Code);
        int baseline = AlloyCalculatorLogic.CalculateFuelRequired(
            1, 1000, 600, context.Firepit.furnaceTemperature, 20, 0, 0, 0, 40, 1300);
        int prewarmed = AlloyCalculatorLogic.CalculateFuelRequired(
            1, 1000, 600, context.Firepit.furnaceTemperature, 20, 0,
            activeBurnTime, context.Firepit.maxTemperature, 40, 1300,
            plan.PrewarmingStack?.StackSize ?? 0, warmingDuration, 700);
        Assert.Equal(prewarmed, plan.DesiredAmount);
        Assert.Equal(baseline, plan.FuelWithoutPrewarming);
        Assert.Equal(baseline - prewarmed, plan.SavedFuelAmount);
        if (queuedAmount > 0)
        {
            Assert.False(plan.CannotSaveFuel);
            Assert.True(plan.SavedFuelAmount >= 1);
            for (int amount = 0; amount < plan.PrewarmingStack!.StackSize; amount++)
                Assert.True(AlloyCalculatorLogic.CalculateFuelRequired(
                    1, 1000, 600, context.Firepit.furnaceTemperature, 20, 0,
                    activeBurnTime, context.Firepit.maxTemperature, 40, 1300, amount, warmingDuration, 700) >= baseline);
            if (queuedAmount == 1) Assert.True(plan.PrewarmingStack.StackSize > queuedAmount);
        }
        Assert.Equal(queuedAmount, context.Firepit.fuelSlot.StackSize);
        Assert.Equal(activeBurnTime, context.Firepit.fuelBurnTime);
        Assert.Equal(64, context.Fixture.BackpackInventory[0].StackSize);
        context.Fixture.ClientNetworkMock!.Verify(network => network.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public void CreatePreview_PrewarmingWithoutAvailableSmeltingFuel_StillReportsPrewarming()
    {
        TestContext context = CreateContext(currentFuelAmount: 32);
        Dictionary<int, ItemStack> batch = CreatePreviewBatch(context, 20);
        context.Fuel.CombustibleProps.BurnTemperature = 700;

        Assert.Equal(AlloyDepositResultCode.InsufficientItems, AlloyFuelDepositService.CreatePreview(
            context.Fixture.ClientApi, context.Firepit, batch, context.Fixture.Player,
            out AlloyFuelDepositPlan? plan, out bool isPrewarming));
        Assert.True(isPrewarming);
        Assert.Null(plan);
        Assert.Equal(32, context.Firepit.fuelSlot.StackSize);
    }

    [Fact]
    public void CreatePreview_NegligiblePrewarming_ReportsZeroSavedItems()
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
        Assert.True(isPrewarming);
        Assert.Equal(0, plan!.SavedFuelAmount);
        Assert.True(plan.CannotSaveFuel);
    }

    [Fact]
    public void MinimumPrewarming_SearchesBeyondOneStackAndStopsAtFirstSaving()
    {
        int baseline = AlloyCalculatorLogic.CalculateFuelRequired(1, 1000, 600, 20, 20, 0, 0, 0, 40, 1300);
        int? amount = AlloyCalculatorLogic.FindMinimumPrewarmingFuel(
            1, 1000, 600, 20, 0, 0, 40, 1300, 0.1f, 700, baseline, out int required);
        Assert.NotNull(amount);
        Assert.True(amount > 64);
        Assert.True(required < baseline);
        for (int count = 0; count < amount.Value; count++)
            Assert.True(AlloyCalculatorLogic.CalculateFuelRequired(
                1, 1000, 600, 20, 20, 0, 0, 0, 40, 1300, count, 0.1f, 700) >= baseline);
    }

    [Fact]
    public void MinimumPrewarming_ReachesHeatingLimitWithoutSavings()
    {
        int baseline = AlloyCalculatorLogic.CalculateFuelRequired(1, 1000, 600, 20, 20, 0, 0, 0, 40, 1300);
        Assert.Null(AlloyCalculatorLogic.FindMinimumPrewarmingFuel(
            1, 1000, 600, 20, 0, 0, 40, 1300, 24, 21, baseline, out _));
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
        Assert.Equal(expected, plan!.IsPrewarming);
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
        Assert.False(plan!.IsPrewarming);
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
