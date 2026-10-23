using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

[Trait("Category", "Unit")]
public class AlloyDepositPlanTests
{
    [Theory]
    [InlineData(-1, 6)]
    [InlineData(4, 6)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    public void Resolve_InvalidTargetIndexOrQuantity_ReturnsNull(int index, int amount)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem metal = CreateItem(1, "ingot-metal-a");
        MockItem bits = CreateSmeltableItem(2, "metalbit-metal-a", metal);
        var ingredient = new MetalDepositIngredient(metal.Code, new ItemStack(metal), 1, 1);
        var supplies = new InventoryGeneric(1, "supply", "invalid-target", null!) { Api = fixture.Api };
        supplies[0].Itemstack = new ItemStack(bits, 12);
        var cooking = new InventoryGeneric(4, "cooking", "invalid-target", null!) { Api = fixture.Api };
        var plan = new AlloyDepositPlan([new(index, ingredient, amount, new ItemStack(bits))]);

        Assert.Null(AlloyDepositStackPlanner.Resolve(
            fixture.World, plan, cooking.ToArray(), supplies, MatchesIngredient));
        Assert.Equal(12, supplies[0].StackSize);
        Assert.All(cooking, slot => Assert.True(slot.Empty));
    }

    [Theory]
    [InlineData("aggregate-shortage")]
    [InlineData("unavailable-form")]
    [InlineData("wrong-metal")]
    [InlineData("duplicate-slot")]
    [InlineData("missing-desired-stack")]
    public void Resolve_InfeasibleConcreteLayout_ReselectsFromEligibleSupplies(string scenario)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem metal = CreateItem(1, "ingot-metal-a");
        MockItem bits = CreateSmeltableItem(2, "metalbit-metal-a", metal);
        MockItem nuggets = CreateSmeltableItem(3, "nugget-metal-a", metal);
        MockItem otherMetal = CreateItem(4, "ingot-metal-b");
        MockItem otherBits = CreateSmeltableItem(5, "metalbit-metal-b", otherMetal);
        var ingredient = new MetalDepositIngredient(metal.Code, new ItemStack(metal), 1, 1);
        var supplies = new InventoryGeneric(3, "supply", "reselection", null!) { Api = fixture.Api };
        int bitsAvailable = scenario == "aggregate-shortage" ? 8 : scenario == "unavailable-form" ? 0 : 12;
        if (bitsAvailable > 0) supplies[0].Itemstack = new ItemStack(bits, bitsAvailable);
        supplies[1].Itemstack = new ItemStack(nuggets, 20);
        supplies[2].Itemstack = new ItemStack(otherBits, 1);
        var cooking = new InventoryGeneric(4, "cooking", "reselection", null!) { Api = fixture.Api };
        MockItem desired = scenario == "wrong-metal" ? otherBits : bits;
        var original = new AlloyDepositPlan(
        [
            new(0, ingredient, 6, new ItemStack(desired)),
            new(scenario == "duplicate-slot" ? 0 : 1, ingredient, 6,
                scenario == "missing-desired-stack" ? null : new ItemStack(desired))
        ]);

        AlloyDepositPlan result = Assert.IsType<AlloyDepositPlan>(AlloyDepositStackPlanner.Resolve(
            fixture.World, original, cooking.ToArray(), supplies, MatchesIngredient));

        Assert.NotSame(original, result);
        Assert.Equal(12, result.Targets.Sum(target => target.Amount));
        Assert.Equal(result.Targets.Length, result.Targets.Select(target => target.SlotIndex).Distinct().Count());
        Assert.All(result.Targets, target =>
        {
            Assert.Contains(target.SlotIndex, original.Targets.Select(item => item.SlotIndex));
            Assert.True(MatchesIngredient(target.DesiredStack!, ingredient));
        });
        Assert.True(result.Targets.Where(target => target.DesiredStack!.Collectible == bits)
            .Sum(target => target.Amount) <= bitsAvailable);
        Assert.Equal(bitsAvailable, supplies[0].StackSize);
        Assert.Equal(20, supplies[1].StackSize);
        Assert.Equal(1, supplies[2].StackSize);
        Assert.All(cooking, slot => Assert.True(slot.Empty));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_ConcreteTargetsExceedCapacity_RejectsLayout(bool itemLimit)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem metal = CreateItem(1, "ingot-metal-a");
        MockItem bits = CreateSmeltableItem(2, "metalbit-metal-a", metal);
        if (itemLimit) bits.MaxStackSize = 4;
        var ingredient = new MetalDepositIngredient(metal.Code, new ItemStack(metal), 1, 1);
        var supplies = new InventoryGeneric(3, "supply", "capacity-validation", null!) { Api = fixture.Api };
        foreach (ItemSlot slot in supplies) slot.Itemstack = new ItemStack(bits, 4);
        var cooking = new InventoryGeneric(4, "cooking", "capacity-validation", null!) { Api = fixture.Api };
        if (!itemLimit)
            foreach (ItemSlot slot in cooking) slot.MaxSlotStackSize = 4;
        var original = new AlloyDepositPlan(
        [
            new(0, ingredient, 6, new ItemStack(bits)),
            new(1, ingredient, 6, new ItemStack(bits))
        ]);

        Assert.Null(AlloyDepositStackPlanner.Resolve(
            fixture.World, original, cooking.ToArray(), supplies, MatchesIngredient));
        Assert.Equal([4, 4, 4], supplies.Select(slot => slot.StackSize));
        Assert.All(cooking, slot => Assert.True(slot.Empty));
    }

    [Fact]
    public void Resolve_EqualSizeSources_GroupsCompatibleStacksAndRetainsTieOrder()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem metal = CreateItem(1, "ingot-metal-a");
        MockItem bits = CreateSmeltableItem(2, "metalbit-metal-a", metal);
        MockItem nuggets = CreateSmeltableItem(3, "nugget-metal-a", metal);
        var supplies = new InventoryGeneric(3, "supply", "stable-grouping", null!) { Api = fixture.Api };
        supplies[0].Itemstack = new ItemStack(bits, 3);
        supplies[1].Itemstack = new ItemStack(nuggets, 3);
        supplies[2].Itemstack = new ItemStack(bits, 3);
        var cooking = new InventoryGeneric(4, "cooking", "stable-grouping", null!) { Api = fixture.Api };
        var ingredient = new MetalDepositIngredient(metal.Code, new ItemStack(metal), 1, 1);
        var request = new AlloyDepositPlan([new(0, ingredient, 8)]);

        AlloyDepositPlan plan = Assert.IsType<AlloyDepositPlan>(AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true));

        Assert.Equal(6, plan.Targets.Where(target => target.DesiredStack!.Collectible == bits)
            .Sum(target => target.Amount));
        Assert.Equal(2, plan.Targets.Where(target => target.DesiredStack!.Collectible == nuggets)
            .Sum(target => target.Amount));
        Assert.Same(bits, plan.Targets[0].DesiredStack!.Collectible);
        AssertSameLayout(plan, Assert.IsType<AlloyDepositPlan>(AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true)));
        plan.Targets[0].DesiredStack!.StackSize = 99;
        Assert.Equal([3, 3, 3], supplies.Select(slot => slot.StackSize));
        Assert.All(cooking, slot => Assert.True(slot.Empty));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void Resolve_UnusableFormCapacity_UsesAlternativeOrRejects(int capacity, bool hasAlternative)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem metal = CreateItem(1, "ingot-metal-a");
        MockItem unusable = CreateSmeltableItem(2, "chunk-metal-a", metal);
        MockItem usable = CreateSmeltableItem(3, "nugget-metal-a", metal);
        unusable.MaxStackSize = capacity;
        var supplies = new InventoryGeneric(2, "supply", "unusable-capacity", null!) { Api = fixture.Api };
        supplies[0].Itemstack = new ItemStack(unusable, 1);
        if (hasAlternative) supplies[1].Itemstack = new ItemStack(usable, 10);
        var cooking = new InventoryGeneric(4, "cooking", "unusable-capacity", null!) { Api = fixture.Api };
        var ingredient = new MetalDepositIngredient(metal.Code, new ItemStack(metal), 1, 1);
        var request = new AlloyDepositPlan([new(0, ingredient, 10)]);

        AlloyDepositPlan? result = AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true);

        Assert.Equal(1, supplies[0].StackSize);
        Assert.Equal(hasAlternative ? 10 : 0, supplies[1].StackSize);
        Assert.All(cooking, slot => Assert.True(slot.Empty));
        if (!hasAlternative)
        {
            Assert.Null(result);
            return;
        }
        AlloyDepositPlan plan = Assert.IsType<AlloyDepositPlan>(result);
        Assert.Equal(10, plan.Targets.Sum(target => target.Amount));
        Assert.All(plan.Targets, target => Assert.Same(usable, target.DesiredStack!.Collectible));
        Assert.All(plan.Targets, target => Assert.NotSame(supplies[1].Itemstack, target.DesiredStack));
    }

    [Fact]
    public void Resolve_ThreeMetalAlloyWithOptionalForms_FindsFourSlotPlan()
    {
        const int targetUnits = 100;
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem metalA = CreateItem(1, "ingot-metal-a");
        MockItem metalB = CreateItem(2, "ingot-metal-b");
        MockItem metalC = CreateItem(3, "ingot-metal-c");
        MetalDepositIngredient[] ingredients =
        [
            new(metalA.Code, new ItemStack(metalA), 0.5f, 0.5f),
            new(metalB.Code, new ItemStack(metalB), 1f / 3, 1f / 3),
            new(metalC.Code, new ItemStack(metalC), 1f / 6, 1f / 6)
        ];
        // 75:50:25 normalized to the integer percentages accepted by the calculator UI.
        var percentages = new Dictionary<int, int> { [0] = 50, [1] = 33, [2] = 17 };
        Dictionary<int, int> amounts = AlloyCalculatorLogic.CalculateAllNuggetsRequired(
            targetUnits, percentages, ingredients);
        // Exact thirds cannot be represented in a 100-unit whole-nugget batch; rounding yields 105 units.
        Assert.Equal([10, 7, 4], Enumerable.Range(0, 3).Select(index => amounts[index]));

        MockItem aBits = CreateSmeltableItem(4, "metalbit-metal-a", metalA);
        MockItem aNuggets = CreateSmeltableItem(5, "nugget-metal-a", metalA);
        MockItem aChunks = CreateSmeltableItem(6, "chunk-metal-a", metalA);
        MockItem bBits = CreateSmeltableItem(7, "metalbit-metal-b", metalB);
        MockItem bNuggets = CreateSmeltableItem(8, "nugget-metal-b", metalB);
        MockItem cBits = CreateSmeltableItem(9, "metalbit-metal-c", metalC);
        MockItem[] forms = [aBits, aNuggets, aChunks, bBits, bNuggets, cBits];
        int[] quantities = [15, 10, 5, 3, 4, 10];
        var supplies = new InventoryGeneric(6, "supply", "three-metal", null!) { Api = fixture.Api };
        for (int index = 0; index < forms.Length; index++)
        {
            forms[index].CombustibleProps.SmeltedRatio = 20;
            supplies[index].Itemstack = new ItemStack(forms[index], quantities[index]);
        }
        var cooking = new InventoryGeneric(4, "cooking", "three-metal", null!) { Api = fixture.Api };
        AlloyDepositPlan request = Assert.IsType<AlloyDepositPlan>(AlloyDepositPlan.CreateRequest(
            ingredients,
            amounts.ToDictionary(pair => pair.Key, pair => new ItemStack(
                pair.Key == 0 ? aBits : pair.Key == 1 ? bBits : cBits, pair.Value)),
            cooking.Count));

        // A feasible layout is 10 A nuggets, 3 B bits, 4 B nuggets, and 4 C bits.
        // Greedy smallest-stack selection instead splits A across chunks and nuggets, requiring five slots.
        AlloyDepositPlan? plan = AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true);

        Assert.All(cooking, slot => Assert.True(slot.Empty));
        Assert.Equal(quantities, supplies.Select(slot => slot.StackSize));
        Assert.NotNull(plan);
        Assert.InRange(plan.Targets.Length, 3, 4);
        for (int index = 0; index < ingredients.Length; index++)
        {
            Assert.Equal(amounts[index], plan.Targets
                .Where(target => target.Ingredient == ingredients[index]).Sum(target => target.Amount));
        }
        AssertSameLayout(plan, Assert.IsType<AlloyDepositPlan>(AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true)));
    }

    [Fact]
    public void Resolve_ValidConcreteLayoutWithNewSmallerForm_RetainsExactLayout()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem metal = CreateItem(1, "ingot-metal-a");
        MockItem nuggets = CreateSmeltableItem(2, "nugget-metal-a", metal);
        MockItem bits = CreateSmeltableItem(3, "metalbit-metal-a", metal);
        var ingredient = new MetalDepositIngredient(metal.Code, new ItemStack(metal), 1, 1);
        var supplies = new InventoryGeneric(1, "supply", "preservation", null!) { Api = fixture.Api };
        supplies[0].Itemstack = new ItemStack(bits, 1);
        var cooking = new InventoryGeneric(4, "cooking", "preservation", null!) { Api = fixture.Api };
        int[] amounts = [7, 5, 4, 4];
        for (int index = 0; index < cooking.Count; index++)
            cooking[index].Itemstack = new ItemStack(nuggets, amounts[index]);
        var original = new AlloyDepositPlan([.. amounts.Select((amount, index) =>
            new AlloyDepositSlotTarget(index, ingredient, amount, new ItemStack(nuggets)))]);

        AlloyDepositPlan? resolved = AlloyDepositStackPlanner.Resolve(
            fixture.World, original, cooking.ToArray(), supplies.Concat(cooking), MatchesIngredient);

        Assert.Equal(amounts, cooking.Select(slot => slot.StackSize));
        Assert.Equal(1, supplies[0].StackSize);
        Assert.Same(original, resolved);
        Assert.Same(original, AlloyDepositStackPlanner.Resolve(
            fixture.World, original, cooking.ToArray(), supplies.Concat(cooking), MatchesIngredient));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_FallbackWithCapacityLimits_FindsFourSlotPlan(bool reducePreferredForm)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem metalA = CreateItem(1, "ingot-metal-a");
        MockItem metalB = CreateItem(2, "ingot-metal-b");
        MockItem preferredA = CreateSmeltableItem(3, "chunk-metal-a", metalA);
        MockItem alternativeA = CreateSmeltableItem(4, "nugget-metal-a", metalA);
        MockItem bBits = CreateSmeltableItem(5, "metalbit-metal-b", metalB);
        MockItem bNuggets = CreateSmeltableItem(6, "nugget-metal-b", metalB);
        var a = new MetalDepositIngredient(metalA.Code, new ItemStack(metalA), 0, 1);
        var b = new MetalDepositIngredient(metalB.Code, new ItemStack(metalB), 0, 1);
        preferredA.MaxStackSize = reducePreferredForm ? 2 : 64;
        alternativeA.MaxStackSize = reducePreferredForm ? 6 : 4;
        // In the reduced-contribution case, five preferred items span three source stacks.
        var supplies = new InventoryGeneric(6, "supply", "capacity", null!) { Api = fixture.Api };
        if (reducePreferredForm)
        {
            supplies[0].Itemstack = new ItemStack(preferredA, 1);
            supplies[1].Itemstack = new ItemStack(preferredA, 2);
            supplies[2].Itemstack = new ItemStack(preferredA, 2);
        }
        else supplies[0].Itemstack = new ItemStack(preferredA, 1);
        // Eight alternative items must also be represented by legal source stacks.
        supplies[3].Itemstack = new ItemStack(alternativeA, reducePreferredForm ? 6 : 4);
        supplies[4].Itemstack = new ItemStack(reducePreferredForm ? bBits : alternativeA, reducePreferredForm ? 1 : 4);
        supplies[5].Itemstack = new ItemStack(reducePreferredForm ? bBits : bNuggets, 1);
        if (!reducePreferredForm)
        {
            // B needs both nonmergeable forms, leaving two slots for A.
            supplies[1].Itemstack = new ItemStack(bBits, 1);
        }
        int requestedA = reducePreferredForm ? 10 : 8;
        int requestedB = reducePreferredForm ? 1 : 2;
        var request = new AlloyDepositPlan(
        [
            new(0, a, requestedA),
            new(1, b, requestedB)
        ]);
        var cooking = new InventoryGeneric(4, "cooking", "capacity", null!) { Api = fixture.Api };
        var before = supplies.Select(slot => (slot.Itemstack?.Collectible.Code, slot.StackSize)).ToArray();
        Assert.True(supplies.Where(slot => !slot.Empty && MatchesIngredient(slot.Itemstack, a))
            .Sum(slot => slot.StackSize) >= requestedA);
        Assert.True(supplies.Where(slot => !slot.Empty && MatchesIngredient(slot.Itemstack, b))
            .Sum(slot => slot.StackSize) >= requestedB);

        AlloyDepositPlan? result = AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true);

        Assert.Equal(before, supplies.Select(slot => (slot.Itemstack?.Collectible.Code, slot.StackSize)));
        Assert.All(cooking, slot => Assert.True(slot.Empty));
        AlloyDepositPlan plan = Assert.IsType<AlloyDepositPlan>(result);
        Assert.Equal(4, plan.Targets.Length);
        Assert.Equal(4, plan.Targets.Select(target => target.SlotIndex).Distinct().Count());
        Assert.Equal(requestedA, plan.Targets.Where(target => target.Ingredient == a).Sum(target => target.Amount));
        Assert.Equal(requestedB, plan.Targets.Where(target => target.Ingredient == b).Sum(target => target.Amount));
        Assert.All(plan.Targets, target => Assert.InRange(target.Amount, 1,
            Math.Min(cooking[target.SlotIndex].MaxSlotStackSize, target.DesiredStack!.Collectible.MaxStackSize)));
        if (reducePreferredForm)
        {
            // Skipping the preferred form is impossible: the alternative supplies only six of ten items.
            Assert.Equal(4, plan.Targets.Where(target => target.DesiredStack!.Collectible == preferredA)
                .Sum(target => target.Amount));
            Assert.Equal(6, plan.Targets.Where(target => target.DesiredStack!.Collectible == alternativeA)
                .Sum(target => target.Amount));
        }
        else
        {
            Assert.DoesNotContain(plan.Targets, target => target.DesiredStack!.Collectible == preferredA);
            Assert.Equal(2, plan.Targets.Count(target => target.DesiredStack!.Collectible == alternativeA));
        }
        AssertSameLayout(plan, Assert.IsType<AlloyDepositPlan>(AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true)));
    }

    [Fact]
    public void CreateRequest_CapturesTotalsWithoutSpreadingAcrossCookingSlots()
    {
        MockItem copper = CreateItem(1, "ingot-copper");
        MockItem tin = CreateItem(2, "ingot-tin");
        MetalDepositIngredient[] ingredients =
        [
            new(copper.Code, new ItemStack(copper), 0.88f, 0.92f),
            new(tin.Code, new ItemStack(tin), 0.08f, 0.12f)
        ];
        Dictionary<int, ItemStack> calculatedStacks = new()
        {
            [0] = new ItemStack(copper, 9),
            [1] = new ItemStack(tin, 1)
        };

        AlloyDepositPlan? plan = AlloyDepositPlan.CreateRequest(ingredients, calculatedStacks, cookingSlotCount: 4);

        Assert.NotNull(plan);
        Assert.Equal([9, 1], plan.Targets.Select(static target => target.Amount));
        Assert.All(plan.Targets, static target => Assert.Null(target.DesiredStack));
        Assert.Same(tin.Code, plan.Targets[^1].Ingredient.Code);
    }

    [Fact]
    public void CreateRequest_MissingCalculatedStack_ReturnsNull()
    {
        MockItem copper = CreateItem(1, "ingot-copper");
        MetalDepositIngredient[] ingredients =
        [
            new(copper.Code, new ItemStack(copper), 1, 1)
        ];

        AlloyDepositPlan? plan = AlloyDepositPlan.CreateRequest(
            ingredients,
            new Dictionary<int, ItemStack>(),
            cookingSlotCount: 4);

        Assert.Null(plan);
    }

    [Theory]
    [InlineData(18, 64, 9)]
    [InlineData(120, 64, 60)]
    [InlineData(18, 10, 9)]
    public void Resolve_ReservesAllFormsBeforeBalancing(int copperAmount, int copperStackLimit, int expectedCopperPerSlot)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem copper = CreateItem(1, "ingot-copper");
        MockItem tin = CreateItem(2, "ingot-tin");
        MockItem copperNugget = CreateSmeltableItem(3, "nugget-copper", copper);
        copperNugget.MaxStackSize = copperStackLimit;
        MockItem tinBit = CreateSmeltableItem(4, "metalbit-tin", tin);
        MockItem tinNugget = CreateSmeltableItem(5, "nugget-tin", tin);
        var supplies = new InventoryGeneric(20, "supply", "planner", null!) { Api = fixture.Api };
        int remaining = copperAmount;
        int slotIndex = 0;
        while (remaining > 0)
        {
            int amount = Math.Min(remaining, copperStackLimit);
            supplies[slotIndex++].Itemstack = new ItemStack(copperNugget, amount);
            remaining -= amount;
        }
        supplies[slotIndex++].Itemstack = new ItemStack(tinBit, 1);
        supplies[slotIndex].Itemstack = new ItemStack(tinNugget, 1);
        var cooking = new InventoryGeneric(4, "cooking", "planner", null!) { Api = fixture.Api };
        MetalDepositIngredient[] ingredients =
        [
            new(copper.Code, new ItemStack(copper), 0.9f, 0.9f),
            new(tin.Code, new ItemStack(tin), 0.1f, 0.1f)
        ];
        AlloyDepositPlan request = Assert.IsType<AlloyDepositPlan>(AlloyDepositPlan.CreateRequest(
            ingredients, new Dictionary<int, ItemStack>
            {
                [0] = new(copper, copperAmount),
                [1] = new(tin, 2)
            }, cooking.Count));

        AlloyDepositPlan plan = Assert.IsType<AlloyDepositPlan>(AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true));

        Assert.Equal([0, 1, 2, 3], plan.Targets.Select(target => target.SlotIndex));
        Assert.Equal([expectedCopperPerSlot, expectedCopperPerSlot, 1, 1], plan.Targets.Select(target => target.Amount));
        Assert.Equal([copperNugget.Code, copperNugget.Code, tinBit.Code, tinNugget.Code],
            plan.Targets.Select(target => target.DesiredStack!.Collectible.Code));
        Assert.All(cooking, slot => Assert.True(slot.Empty));
        Assert.Equal(copperAmount + 2, supplies.Sum(slot => slot.StackSize));
        Assert.Equal(plan, AlloyDepositStackPlanner.Resolve(
            fixture.World, plan, cooking.ToArray(), supplies, MatchesIngredient));
        AssertSameLayout(plan, Assert.IsType<AlloyDepositPlan>(AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true)));

        var previousLayout = new AlloyDepositPlan(
        [
            new(0, ingredients[0], copperAmount / 3, new ItemStack(copperNugget)),
            new(1, ingredients[0], copperAmount / 3, new ItemStack(copperNugget)),
            new(2, ingredients[0], copperAmount / 3, new ItemStack(copperNugget)),
            new(3, ingredients[1], 2, new ItemStack(tinNugget))
        ]);
        AlloyDepositPlan replanned = Assert.IsType<AlloyDepositPlan>(AlloyDepositStackPlanner.Resolve(
            fixture.World, previousLayout, cooking.ToArray(), supplies, MatchesIngredient));
        Assert.Equal(plan.Targets.Select(target => (target.SlotIndex, target.Amount, target.DesiredStack!.Collectible.Code)),
            replanned.Targets.Select(target => (target.SlotIndex, target.Amount, target.DesiredStack!.Collectible.Code)));
    }

    [Fact]
    public void Resolve_RequiredFormsExceedTotalSlots_ReturnsNullWithoutChangingSupply()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem metal = CreateItem(1, "ingot-copper");
        var ingredient = new MetalDepositIngredient(metal.Code, new ItemStack(metal), 1, 1);
        var supplies = new InventoryGeneric(5, "supply", "planner", null!) { Api = fixture.Api };
        for (int index = 0; index < supplies.Count; index++)
            supplies[index].Itemstack = new ItemStack(CreateSmeltableItem(index + 2, $"copper-form-{index}", metal), 1);
        var cooking = new InventoryGeneric(4, "cooking", "planner", null!) { Api = fixture.Api };
        var request = new AlloyDepositPlan([new AlloyDepositSlotTarget(0, ingredient, 5)]);

        Assert.Null(AlloyDepositStackPlanner.Resolve(
            fixture.World, request, cooking.ToArray(), supplies, MatchesIngredient, spreadAcrossCookingSlots: true));
        Assert.All(cooking, slot => Assert.True(slot.Empty));
        Assert.All(supplies, slot => Assert.Equal(1, slot.StackSize));
    }

    private static void AssertSameLayout(AlloyDepositPlan expected, AlloyDepositPlan actual)
    {
        Assert.Equal(
            expected.Targets.Select(target => (target.SlotIndex, target.Ingredient, target.Amount, target.DesiredStack!.Collectible.Code)),
            actual.Targets.Select(target => (target.SlotIndex, target.Ingredient, target.Amount, target.DesiredStack!.Collectible.Code)));
    }

    private static bool MatchesIngredient(ItemStack stack, MetalDepositIngredient ingredient)
    {
        return stack.Collectible.CombustibleProps?.SmeltedStack?.Code == ingredient.Code;
    }

    private static MockItem CreateSmeltableItem(int id, string path, MockItem ingot)
    {
        MockItem item = CreateItem(id, path);
        item.MaxStackSize = 64;
        item.CombustibleProps = new CombustibleProperties
        {
            SmeltedRatio = 1,
            SmeltedStack = new JsonItemStack { Code = ingot.Code, ResolvedItemstack = new ItemStack(ingot) }
        };
        return item;
    }

    private static MockItem CreateItem(int id, string path)
    {
        MockItem item = MockItem.CreateNonLightSource(id);
        item.Code = new AssetLocation("game", path);
        return item;
    }
}