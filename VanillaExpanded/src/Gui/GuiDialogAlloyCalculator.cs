using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Gui;

/// <summary>
/// A GUI dialog for calculating alloy metal ratios and required units.
/// Displays as a floaty dialog attached to the firepit block.
/// </summary>
public sealed class GuiDialogAlloyCalculator : GuiDialogBlockEntity
{
    #region Constants
    private const string ModId = "vanillaexpanded";
    private const string DialogKey = "alloycalculator";
    private const double SliderWidth = 150;
    private const double LabelWidth = 80;
    private const double RowHeight = 35;
    private const double DropdownWidth = 150;
    private const double InputWidth = 70;
    private const int DefaultTargetUnits = 100;
    private const double TitlebarHeight = 20;
    private const double SlotSize = 40;
    private const double ButtonHeight = 25;
    #endregion

    #region Saved State
    /// <summary>
    /// Stores saved dialog state per block entity position.
    /// </summary>
    private static readonly Dictionary<BlockPos, SavedDialogState> savedStates = [];

    private sealed class SavedDialogState
    {
        public int SelectedAlloyIndex { get; set; }
        public int TargetUnits { get; set; } = DefaultTargetUnits;
        public Dictionary<int, int> SliderValues { get; set; } = [];
    }
    #endregion

    #region Fields
    private readonly GuiDialog? firepitDialog;
    private List<AlloyRecipe> alloys = [];
    private AlloyRecipe? selectedAlloy;
    
    /// <summary> Currently selected ingredients for the chosen alloy. </summary>
    private ImmutableArray<MetalAlloyIngredient> selectedIngredients = [];
    private readonly Dictionary<int, int> sliderValues = [];
    private readonly Dictionary<int, ItemStack> calculatedStacks = [];
    private readonly List<SlideshowItemstackTextComponent> slideshowComponents = [];
    private int targetUnits = DefaultTargetUnits;
    private bool isAdjustingSliders;
    
    // Cached handbook data for filtering
    private ItemStack[]? handbookStacks;
    private List<ItemStack>? smeltingContainers;
    private List<ItemStack>? smeltingFuels;
    private int maxFuelTemperature;
    #endregion

    #region Properties
    public override double DrawOrder => 0.2;
    public override string ToggleKeyCombinationCode => string.Empty;
    protected override double FloatyDialogPosition => 0.5;
    protected override double FloatyDialogAlign => 1.0;

    /// <summary>
    /// Gets the calculated ingredient stacks for the current alloy configuration.
    /// Key is the ingredient index, value is the ItemStack with the calculated amount.
    /// </summary>
    public IReadOnlyDictionary<int, ItemStack> CalculatedIngredientStacks => calculatedStacks;
    #endregion

    #region Accessors
    public double FirepitDialogWidth => firepitDialog?.SingleComposer?.Bounds.OuterWidth ?? 0;
    #endregion

    #region Constructor
    public GuiDialogAlloyCalculator(ICoreClientAPI capi, BlockPos blockPos, GuiDialog firepitDialog) 
        : base(Lang.Get($"{ModId}:gui-alloycalculator-title"), blockPos, capi)
    {
        if (IsDuplicate) return;
        this.firepitDialog = firepitDialog;
        LoadAlloys();
    }
    #endregion

    #region Initialization
    private void LoadAlloys()
    {
        alloys = [.. capi.GetMetalAlloys()
            .Where(static a => a.Enabled && a.Ingredients.Length > 0)
            .OrderBy(static a => GetAlloyDisplayName(a))];

        // Build handbook stacks cache for filtering
        BuildHandbookStacksCache();
    }

    // TODO: There has to be a better way to calculate/cache these item-stack variants, ideally we should be capable of leveraging the cache that the handbook already has internally.
    private void BuildHandbookStacksCache()
    {
        var stacks = new List<ItemStack>();
        smeltingContainers = [];
        smeltingFuels = [];

        foreach (var obj in capi.World.Collectibles)
        {
            var objStacks = obj.GetHandBookStacks(capi);
            if (objStacks is null) continue;

            foreach (var stack in objStacks)
            {
                stacks.Add(stack);

                // Collect smelting containers (crucibles, etc.)
                if (stack.ItemAttributes?["cookingContainerSlots"].Exists == true)
                {
                    smeltingContainers.Add(stack);
                }

                // Collect fuels
                var combustProps = stack.Collectible.CombustibleProps;
                if (combustProps?.BurnDuration is not null || combustProps?.BurnTemperature is not null)
                {
                    smeltingFuels.Add(stack);
                }
            }
        }

        handbookStacks = stacks.ToArray();

        // Calculate max fuel temperature
        maxFuelTemperature = smeltingFuels
            .Where(static f => f.Collectible.CombustibleProps?.BurnTemperature is not null)
            .Select(static f => f.Collectible.CombustibleProps!.BurnTemperature)
            .DefaultIfEmpty(0)
            .Max();
    }
    #endregion

    #region Dialog Composition
    private void ComposeDialog()
    {
        // Calculate number of ingredient rows
        var ingredientCount = selectedIngredients.Length;

        // Define content bounds - this establishes the size of our dialog content
        // Width: either slider row or slot row, whichever is wider
        var sliderRowWidth = LabelWidth + SliderWidth;
        var slotRowWidth = ingredientCount * SlotSize;
        var contentWidth = Math.Max(sliderRowWidth, slotRowWidth);
        
        // Height: titlebar + dropdown row + sliders + slot row + button row
        var contentHeight = TitlebarHeight + 30;
        if (ingredientCount > 0)
        {
            contentHeight += ingredientCount * RowHeight; // sliders
            contentHeight += 15 + SlotSize; // gap + slot row
            contentHeight += 10 + ButtonHeight; // gap + button
        }
        var contentBounds = ElementBounds.Fixed(0, 0, contentWidth, contentHeight);

        // Background bounds with padding, sized to fit children
        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(contentBounds);

        // Calculate offset to position right of firepit dialog
        var firepitWidth = FirepitDialogWidth;
        var dialogXOffset = (firepitWidth / 2);

        // Dialog bounds - positioned to the right of the firepit
        var dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedAlignmentOffset(dialogXOffset, 0);

        // Build the UI - start below title bar
        var yOffset = TitlebarHeight;

        // Define element bounds
        var dropdownBounds = ElementBounds.Fixed(0, yOffset, DropdownWidth, 25);
        var inputBounds = ElementBounds.Fixed(DropdownWidth + 10, yOffset, InputWidth, 25);
        yOffset += 30;

        var alloyValues = alloys.Select(static (_, i) => i.ToString());
        var alloyNames = alloys.Select(static (recipe, _) => GetAlloyDisplayName(recipe));
        var selectedIndex = selectedAlloy is not null ? alloys.IndexOf(selectedAlloy) : 0;
        if (selectedIndex < 0) selectedIndex = 0;

        var composer = capi.Gui
            .CreateCompo($"{DialogKey}{BlockEntityPosition}", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(Lang.Get($"{ModId}:gui-alloycalculator-title"), OnTitleBarClose)
            .BeginChildElements(bgBounds)
            .AddDropDown([.. alloyValues], [.. alloyNames], selectedIndex, OnAlloySelected, dropdownBounds, "alloyDropdown")
            .AddHoverText(Lang.Get($"{ModId}:gui-alloycalculator-dropdown-tooltip"), CairoFont.WhiteDetailText(), 250, dropdownBounds.FlatCopy(), "dropdownTooltip")
            .AddNumberInput(inputBounds, OnTargetUnitsChanged, CairoFont.WhiteDetailText(), "targetUnits")
            .AddHoverText(Lang.Get($"{ModId}:gui-alloycalculator-targetunits-tooltip"), CairoFont.WhiteDetailText(), 250, inputBounds.FlatCopy(), "targetUnitsTooltip");

        // Add ingredient sliders if an alloy is selected
        if (selectedAlloy is not null && ingredientCount > 0)
        {
            // Add sliders
            for (var idx = 0; idx < ingredientCount; idx++)
            {
                var ingredient = selectedIngredients[idx];
                var ingredientIndex = idx;
                var ingredientName = GetIngredientDisplayName(ingredient);

                var labelBounds = ElementBounds.Fixed(0, yOffset, LabelWidth, RowHeight);
                var sliderBounds = ElementBounds.Fixed(LabelWidth, yOffset + 4, SliderWidth, 20);

                var sliderKey = $"slider_{ingredientIndex}";
                var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
                var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
                var sliderTooltip = Lang.Get($"{ModId}:gui-alloycalculator-slider-tooltip", ingredientName, minPercent, maxPercent);

                composer
                    .AddStaticText(ingredientName, CairoFont.WhiteSmallText(), labelBounds)
                    .AddSlider(value => OnSliderChanged(ingredientIndex, value), sliderBounds, sliderKey)
                    .AddHoverText(sliderTooltip, CairoFont.WhiteDetailText(), 250, sliderBounds.FlatCopy(), $"sliderTooltip_{ingredientIndex}");

                yOffset += RowHeight;
            }

            // Add second divider before slots
            yOffset += 5;
            var divider2Bounds = ElementBounds.Fixed(0, yOffset, contentWidth, 1);
            composer.AddInset(divider2Bounds, 1, 0.5f);
            yOffset += 10;

            // Create slideshow components for each ingredient
            slideshowComponents.Clear();
            var richTextComponents = new List<RichTextComponentBase>();
            const int slotPadding = 5;

            for (var i = 0; i < ingredientCount; i++)
            {
                var ingredient = selectedAlloy.Ingredients[i];
                var stacks = GetAllMetalVariantStacks(ingredient, 1);
                
                if (stacks.Length > 0)
                {
                    var slideshow = new SlideshowItemstackTextComponent(capi, stacks, SlotSize, EnumFloat.Inline)
                    {
                        ShowStackSize = true,
                        Background = true,
                        PaddingRight = slotPadding,
                        ExtraTooltipText = "\n" + Lang.Get($"{ModId}:gui-alloycalculator-itemstack-tooltip")
                    };
                    slideshowComponents.Add(slideshow);
                    richTextComponents.Add(slideshow);
                }
            }

            // Create centered slot bounds - parent to contentBounds for proper alignment
            var slotsWidth = ingredientCount * (SlotSize + slotPadding);
            var slotBounds = ElementBounds
                .Fixed(0, yOffset, slotsWidth, SlotSize + 8)
                .WithParent(contentBounds)
                .WithAlignment(EnumDialogArea.CenterFixed);
            composer.AddRichtext(richTextComponents.ToArray(), slotBounds, "ingredientSlots");

            // Add deposit button
            yOffset += (int)SlotSize + 18;
            var buttonBounds = ElementBounds
                .Fixed(0, yOffset, 80, ButtonHeight)
                .WithParent(contentBounds)
                .WithAlignment(EnumDialogArea.CenterFixed);
            composer
                .AddSmallButton(Lang.Get($"{ModId}:gui-alloycalculator-deposit"), OnDepositButtonClicked, buttonBounds, EnumButtonStyle.Normal, "depositButton")
                .AddHoverText(Lang.Get($"{ModId}:gui-alloycalculator-deposit-tooltip"), CairoFont.WhiteDetailText(), 250, buttonBounds.FlatCopy(), "depositTooltip");
        }

        SingleComposer = composer.EndChildElements().Compose();

        // Set target units value
        var targetInput = SingleComposer?.GetNumberInput("targetUnits");
        targetInput?.SetValue(targetUnits.ToString());

        // Initialize slider values after composition
        if (selectedAlloy is not null)
        {
            InitializeSliderValues();
            UpdateResultsDisplay();
        }
    }
    #endregion

    #region Slider Logic
    private void InitializeSliderValues()
    {
        if (selectedAlloy is null || SingleComposer is null) return;

        sliderValues.Clear();

        // Initialize each slider to the midpoint of its valid range
        for (var i = 0; i < selectedIngredients.Length; i++)
        {
            var ingredient = selectedIngredients[i];
            var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
            var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
            var midPoint = (minPercent + maxPercent) / 2;

            sliderValues[i] = midPoint;

            var slider = SingleComposer.GetSlider($"slider_{i}");
            slider?.SetValues(midPoint, minPercent, maxPercent, 1, "%");
        }

        // Normalize to ensure sum is 100%
        NormalizeSliderValues(-1);
    }

    private bool OnSliderChanged(int changedIndex, int newValue)
    {
        if (isAdjustingSliders || selectedAlloy is null) return true;

        sliderValues[changedIndex] = newValue;
        NormalizeSliderValues(changedIndex);
        SaveSliderValues();
        UpdateResultsDisplay();

        return true;
    }

    /// <summary>
    /// Saves current slider values to the saved state.
    /// </summary>
    private void SaveSliderValues()
    {
        var state = GetOrCreateSavedState();
        state.SliderValues.Clear();
        foreach (var (idx, value) in sliderValues)
        {
            state.SliderValues[idx] = value;
        }
    }

    private void NormalizeSliderValues(int changedIndex)
    {
        if (selectedAlloy is null || SingleComposer is null) return;

        isAdjustingSliders = true;

        try
        {
            var totalPercent = sliderValues.Values.Sum();
            var difference = totalPercent - 100;

            if (Math.Abs(difference) < 1) return; // Already at 100%

            // Distribute the difference among other sliders proportionally
            var otherIndices = sliderValues.Keys.Where(i => i != changedIndex).ToList();
            if (otherIndices.Count == 0) return;

            // Calculate how much each other slider can absorb
            var adjustments = new Dictionary<int, int>();
            var totalAdjustable = 0.0;

            foreach (var idx in otherIndices)
            {
                var ingredient = selectedIngredients[idx];
                var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
                var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
                var currentValue = sliderValues[idx];

                // If we need to decrease (difference > 0), check how much we can decrease
                // If we need to increase (difference < 0), check how much we can increase
                var adjustable = difference > 0
                    ? currentValue - minPercent
                    : maxPercent - currentValue;

                adjustments[idx] = adjustable;
                totalAdjustable += adjustable;
            }

            if (totalAdjustable <= 0) return;

            // Apply proportional adjustments
            var remaining = Math.Abs(difference);
            foreach (var idx in otherIndices)
            {
                if (remaining <= 0) break;

                var proportion = adjustments[idx] / totalAdjustable;
                var adjustment = (int)Math.Round(Math.Abs(difference) * proportion);
                adjustment = Math.Min(adjustment, adjustments[idx]);
                adjustment = Math.Min(adjustment, remaining);

                if (difference > 0)
                {
                    sliderValues[idx] -= adjustment;
                }
                else
                {
                    sliderValues[idx] += adjustment;
                }

                remaining -= adjustment;

                // Update the slider UI
                var slider = SingleComposer.GetSlider($"slider_{idx}");
                if (slider is not null)
                {
                    var ingredient = selectedIngredients[idx];
                    var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
                    var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
                    slider.SetValues(sliderValues[idx], minPercent, maxPercent, 1, "%");
                }
            }
        }
        finally
        {
            isAdjustingSliders = false;
        }
    }
    #endregion

    #region Results Calculation
    private void UpdateResultsDisplay()
    {
        if (selectedAlloy is null || SingleComposer is null) return;

        calculatedStacks.Clear();

        for (var i = 0; i < selectedIngredients.Length; i++)
        {
            var ingredient = selectedIngredients[i];
            var percent = sliderValues.TryGetValue(i, out var val) ? val : 0;
            var units = targetUnits * percent / 100.0;
            var nuggets = (int)Math.Ceiling(units / 5.0); // 1 nugget = 5 units, round up

            // Update slideshow component with new stack size
            if (i < slideshowComponents.Count)
            {
                var stacks = GetAllMetalVariantStacks(ingredient, nuggets);
                slideshowComponents[i].Itemstacks = stacks;
            }

            // Store the primary stack (metal bit) for external use
            if (nuggets > 0)
            {
                var stack = GetMetalBitStack(ingredient, nuggets);
                if (stack is not null)
                {
                    calculatedStacks[i] = stack;
                }
            }
        }
    }

    /// <summary>
    /// Gets all metal variant stacks (nuggets, ore chunks, etc.) that smelt into the given metal.
    /// Filters by handbook visibility and smeltability.
    /// </summary>
    private ItemStack[] GetAllMetalVariantStacks(MetalAlloyIngredient ingredient, int stackSize)
    {
        // The ingredient's ResolvedItemstack is the ingot - we need items that smelt into this
        var targetIngot = ingredient.ResolvedItemstack;
        if (targetIngot is null || handbookStacks is null) return [];

        // Filter handbook stacks to find items that smelt into this metal and can be smelted
        var stacks = handbookStacks
            .Where(stack => 
                targetIngot.Equals(capi.World, stack.Collectible.CombustibleProps?.SmeltedStack?.ResolvedItemstack, GlobalConstants.IgnoredStackAttributes)
                && CanSmelt(stack))
            .Select(stack => 
            {
                var clone = stack.Clone();
                clone.StackSize = stackSize;
                return clone;
            });

        var filtered = stacks.Where(static stack => stack.Collectible?.FirstCodePart() == "metalbit" || stack.Collectible?.FirstCodePart() == "nugget");
        return [.. filtered];
    }

    /// <summary>
    /// Checks if an item can be smelted based on fuel temperature and melting point.
    /// </summary>
    private bool CanSmelt(ItemStack stack)
    {
        var combustProps = stack.Collectible.CombustibleProps;
        if (combustProps is null) return false;
        
        // Check if fuel temperature is high enough to melt this item
        if (combustProps.MeltingPoint > maxFuelTemperature) return false;

        return true;
    }

    /// <summary>
    /// Gets a metal bit ItemStack for the given ingredient.
    /// </summary>
    private ItemStack? GetMetalBitStack(MetalAlloyIngredient ingredient, int stackSize)
    {
        // Extract metal name from ingredient code (e.g., "ingot-copper" -> "copper")
        var code = ingredient.Code?.Path;
        if (code is null) return null;

        var metalName = code.Contains('-')
            ? code[(code.LastIndexOf('-') + 1)..]
            : code;

        // Get the metal bit item
        var bitCode = new AssetLocation("game", $"metalbit-{metalName}");
        var bitItem = capi.World.GetItem(bitCode);
        
        if (bitItem is null) return null;

        return new ItemStack(bitItem, stackSize);
    }
    #endregion

    #region Event Handlers
    private void OnAlloySelected(string code, bool selected)
    {
        if (!int.TryParse(code, out var index) || index < 0 || index >= alloys.Count)
        {
            return;
        }

        selectedAlloy = alloys[index];
        selectedIngredients = selectedAlloy.Ingredients.OrderBy(static ing => GetIngredientDisplayName(ing)).ToImmutableArray();
        
        // Save selected alloy index
        GetOrCreateSavedState().SelectedAlloyIndex = index;
        
        ComposeDialog();
    }

    private void OnTargetUnitsChanged(string value)
    {
        if (int.TryParse(value, out var units) && units > 0)
        {
            targetUnits = units;
            GetOrCreateSavedState().TargetUnits = units;
            UpdateResultsDisplay();
        }
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private bool OnDepositButtonClicked()
    {
        DepositIngredientsIntoCrucible();
        return true;
    }
    #endregion

    #region Deposit Logic
    /// <summary>
    /// Deposits the calculated ingredients from player inventory into the crucible.
    /// First clears the crucible, then deposits ingredients spread evenly across slots.
    /// </summary>
    private void DepositIngredientsIntoCrucible()
    {
        var firepit = capi.World.BlockAccessor.GetBlockEntity<BlockEntityFirepit>(BlockEntityPosition);
        if (firepit?.Inventory is not InventorySmelting crucibleInventory) return;

        var cookingSlots = crucibleInventory.CookingSlots;
        if (cookingSlots.Length == 0) return;

        var player = capi.World.Player;

        // Open the crucible inventory and notify the server - this is required for TryTransferTo to work
        // The firepit dialog already opens the inventory, but we need to ensure the server knows we're
        // interacting with it for the transfer operations to be authorized
        var openPacket = player.InventoryManager.OpenInventory(crucibleInventory);
        if (openPacket is not null)
        {
            capi.Network.SendPacketClient(openPacket);
        }

        try
        {
            // First: Clear all items from crucible back to player inventory
            ClearCrucible(player.InventoryManager, cookingSlots);

            // Build ingredient info: valid codes and target amounts
            var ingredients = new List<(HashSet<AssetLocation> validCodes, int targetAmount)>();
            
            foreach (var (ingredientIndex, targetStack) in calculatedStacks.OrderByDescending(static kvp => kvp.Value?.StackSize ?? 0))
            {
                if (targetStack is null || targetStack.StackSize <= 0) continue;

                var ingredient = selectedIngredients[ingredientIndex];
                var validStacks = GetAllMetalVariantStacks(ingredient, 1);
                var validCodes = validStacks.Select(static s => s.Collectible.Code).ToHashSet();
                
                ingredients.Add((validCodes, targetStack.StackSize));
            }

            if (ingredients.Count == 0) return;

            // Calculate slot allocation: distribute slots proportionally by ingredient amount
            var totalItems = ingredients.Sum(static i => i.targetAmount);
            var slotAllocations = AllocateSlotsProportionally(ingredients, cookingSlots.Length);

            // Deposit each ingredient into its allocated slots, spread evenly
            var slotIndex = 0;
            for (var i = 0; i < ingredients.Count; i++)
            {
                var (validCodes, targetAmount) = ingredients[i];
                var slotsForIngredient = slotAllocations[i];
                
                if (slotsForIngredient == 0) continue;

                // Calculate how to spread items across allocated slots
                var itemsPerSlot = targetAmount / slotsForIngredient;
                var remainder = targetAmount % slotsForIngredient;

                var slotTargets = new int[slotsForIngredient];
                for (var s = 0; s < slotsForIngredient; s++)
                {
                    slotTargets[s] = itemsPerSlot + (s < remainder ? 1 : 0);
                }

                // Deposit into each allocated slot
                for (var s = 0; s < slotsForIngredient && slotIndex < cookingSlots.Length; s++, slotIndex++)
                {
                    var targetSlot = cookingSlots[slotIndex];
                    var targetForThisSlot = slotTargets[s];
                    
                    DepositFromPlayerInventory(player.InventoryManager, targetSlot, validCodes, targetForThisSlot);
                }
            }
        }
        finally
        {
            // Close the crucible inventory and sync with server
            player.InventoryManager.CloseInventoryAndSync(crucibleInventory);
        }
    }

    /// <summary>
    /// Allocates cooking slots proportionally based on ingredient amounts.
    /// Larger amounts get more slots, with at least 1 slot per ingredient.
    /// </summary>
    private static int[] AllocateSlotsProportionally(List<(HashSet<AssetLocation> validCodes, int targetAmount)> ingredients, int totalSlots)
    {
        var allocations = new int[ingredients.Count];
        
        if (ingredients.Count == 0) return allocations;
        
        // If more ingredients than slots, give 1 slot each until we run out
        if (ingredients.Count >= totalSlots)
        {
            for (var i = 0; i < Math.Min(ingredients.Count, totalSlots); i++)
            {
                allocations[i] = 1;
            }
            return allocations;
        }

        // First: guarantee each ingredient gets at least 1 slot
        for (var i = 0; i < ingredients.Count; i++)
        {
            allocations[i] = 1;
        }
        
        var remainingSlots = totalSlots - ingredients.Count;
        if (remainingSlots <= 0) return allocations;

        // Second: distribute remaining slots proportionally to larger amounts
        var totalItems = ingredients.Sum(static i => i.targetAmount);
        
        for (var i = 0; i < ingredients.Count && remainingSlots > 0; i++)
        {
            var proportion = (double)ingredients[i].targetAmount / totalItems;
            var extraSlots = (int)Math.Round(proportion * remainingSlots);
            allocations[i] += extraSlots;
        }

        // Ensure we don't exceed total slots
        var totalAllocated = allocations.Sum();
        while (totalAllocated > totalSlots)
        {
            for (var i = ingredients.Count - 1; i >= 0 && totalAllocated > totalSlots; i--)
            {
                if (allocations[i] > 1)
                {
                    allocations[i]--;
                    totalAllocated--;
                }
            }
        }

        // Distribute any unused slots to largest ingredients
        while (totalAllocated < totalSlots)
        {
            for (var i = 0; i < ingredients.Count && totalAllocated < totalSlots; i++)
            {
                allocations[i]++;
                totalAllocated++;
            }
        }

        return allocations;
    }

    /// <summary>
    /// Clears all items from crucible cooking slots back to player inventory.
    /// </summary>
    private void ClearCrucible(IPlayerInventoryManager playerInventory, ItemSlot[] cookingSlots)
    {
        foreach (var slot in cookingSlots)
        {
            if (slot?.Itemstack is null) continue;
            WithdrawFromCrucible(playerInventory, slot, slot.Itemstack.StackSize);
        }
    }

    /// <summary>
    /// Withdraws items from a crucible slot back to player inventory.
    /// </summary>
    private void WithdrawFromCrucible(
        IPlayerInventoryManager playerInventory,
        ItemSlot sourceSlot,
        int amount)
    {
        if (sourceSlot?.Itemstack is null || amount <= 0) return;

        var remaining = Math.Min(amount, sourceSlot.Itemstack.StackSize);

        // Get player's own inventories (backpack and hotbar) - these are the inventories we can withdraw to
        var backpackInventory = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        var hotbarInventory = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);

        // Try to place in backpack first, then hotbar
        remaining = WithdrawToInventory(playerInventory, sourceSlot, backpackInventory, remaining);
        if (remaining > 0)
        {
            remaining = WithdrawToInventory(playerInventory, sourceSlot, hotbarInventory, remaining);
        }
    }

    /// <summary>
    /// Helper method to withdraw items from a source slot to a target inventory.
    /// </summary>
    private int WithdrawToInventory(
        IPlayerInventoryManager playerInventory,
        ItemSlot sourceSlot,
        IInventory? targetInventory,
        int remaining)
    {
        if (targetInventory is null || remaining <= 0) return remaining;

        foreach (var targetSlot in targetInventory)
        {
            if (remaining <= 0) break;
            if (targetSlot is null) continue;

            // Check if slot can accept this item
            if (!targetSlot.Empty && !targetSlot.Itemstack.Equals(capi.World, sourceSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
            {
                continue;
            }

            var canFit = targetSlot.Empty
                ? Math.Min(remaining, sourceSlot.Itemstack.Collectible.MaxStackSize)
                : Math.Min(remaining, targetSlot.Itemstack.Collectible.MaxStackSize - targetSlot.Itemstack.StackSize);

            if (canFit <= 0) continue;

            // Use TryTransferTo which handles networking
            var op = new ItemStackMoveOperation(capi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, canFit);
            op.ActingPlayer = capi.World.Player;

            var packet = playerInventory.TryTransferTo(sourceSlot, targetSlot, ref op);

            if (packet is not null)
            {
                capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, packet);
            }

            remaining -= op.MovedQuantity;
        }

        return remaining;
    }

    /// <summary>
    /// Finds and deposits matching items from player inventory into a specific crucible slot.
    /// </summary>
    private void DepositFromPlayerInventory(
        IPlayerInventoryManager playerInventory,
        ItemSlot targetSlot,
        HashSet<AssetLocation> validItemCodes,
        int itemsToDeposit)
    {
        var remaining = itemsToDeposit;

        // Get player's own inventories (backpack and hotbar) - these are the inventories we can deposit from
        var backpackInventory = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        var hotbarInventory = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);

        // Try to deposit from backpack first, then hotbar
        remaining = DepositFromInventory(playerInventory, backpackInventory, targetSlot, validItemCodes, remaining);
        if (remaining > 0)
        {
            remaining = DepositFromInventory(playerInventory, hotbarInventory, targetSlot, validItemCodes, remaining);
        }
    }

    /// <summary>
    /// Helper method to deposit matching items from a source inventory into a target slot.
    /// </summary>
    private int DepositFromInventory(
        IPlayerInventoryManager playerInventory,
        IInventory? sourceInventory,
        ItemSlot targetSlot,
        HashSet<AssetLocation> validItemCodes,
        int remaining)
    {
        if (sourceInventory is null || remaining <= 0) return remaining;

        foreach (var slot in sourceInventory)
        {
            if (remaining <= 0) break;
            if (slot?.Itemstack is null) continue;

            // Check if this item is one of our valid ingredient variants
            if (!validItemCodes.Contains(slot.Itemstack.Collectible.Code)) continue;

            var itemsToTake = Math.Min(remaining, slot.Itemstack.StackSize);
            if (itemsToTake <= 0) continue;

            var deposited = TryDepositIntoSlot(playerInventory, slot, targetSlot, itemsToTake);
            remaining -= deposited;
        }

        return remaining;
    }

    /// <summary>
    /// Tries to move items from a source slot into a specific target slot.
    /// </summary>
    private int TryDepositIntoSlot(
        IPlayerInventoryManager playerInventory,
        ItemSlot sourceSlot,
        ItemSlot targetSlot,
        int maxItems)
    {
        if (targetSlot is null) return 0;

        // Check if slot can accept this item
        if (!targetSlot.Empty && !targetSlot.Itemstack.Equals(capi.World, sourceSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
        {
            return 0;
        }

        var canFit = targetSlot.Empty
            ? Math.Min(maxItems, sourceSlot.Itemstack.Collectible.MaxStackSize)
            : Math.Min(maxItems, targetSlot.MaxSlotStackSize - targetSlot.Itemstack.StackSize);

        if (canFit <= 0) return 0;

        // Use TryTransferTo which handles networking
        var op = new ItemStackMoveOperation(capi.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, canFit);
        op.ActingPlayer = capi.World.Player;

        var packet = playerInventory.TryTransferTo(sourceSlot, targetSlot, ref op);

        if (packet is not null)
        {
            capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, packet);
        }

        return op.MovedQuantity;
    }
    #endregion

    #region Dialog Lifecycle
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();

        // Restore saved state or use defaults
        if (savedStates.TryGetValue(BlockEntityPosition, out var state))
        {
            targetUnits = state.TargetUnits;
            OnAlloySelected(state.SelectedAlloyIndex.ToString(), true);
            
            // Restore slider values after dialog is composed
            RestoreSliderValues(state);
        }
        else
        {
            OnAlloySelected("0", true);
        }
    }

    /// <summary>
    /// Restores slider values from saved state.
    /// </summary>
    private void RestoreSliderValues(SavedDialogState state)
    {
        if (SingleComposer is null || selectedAlloy is null) return;

        isAdjustingSliders = true;
        try
        {
            foreach (var (idx, value) in state.SliderValues)
            {
                if (idx >= selectedIngredients.Length) continue;

                var ingredient = selectedIngredients[idx];
                var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
                var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
                var clampedValue = Math.Clamp(value, minPercent, maxPercent);

                sliderValues[idx] = clampedValue;
                var slider = SingleComposer.GetSlider($"slider_{idx}");
                slider?.SetValues(clampedValue, minPercent, maxPercent, 1, "%");
            }
        }
        finally
        {
            isAdjustingSliders = false;
        }

        UpdateResultsDisplay();
    }

    /// <summary>
    /// Gets or creates a saved state for the current block entity.
    /// </summary>
    private SavedDialogState GetOrCreateSavedState()
    {
        if (!savedStates.TryGetValue(BlockEntityPosition, out var state))
        {
            state = new SavedDialogState();
            savedStates[BlockEntityPosition] = state;
        }
        return state;
    }

    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);
        if (capi.Settings.Bool["immersiveMouseMode"])
        {
            // Adjust position to account for firepit dialog width in immersive mode (positions the calculator dialog to the right of the firepit dialog)
            SingleComposer.Bounds.absOffsetX = (SingleComposer.Bounds.OuterWidth / 2) + (FirepitDialogWidth / 2) - 5;
            SingleComposer.Bounds.absOffsetY = 0;
        }
    }

    public override bool TryOpen()
    {
        if (alloys.Count == 0)
        {
            LoadAlloys();
            if (alloys.Count == 0)
            {
                return false; // No alloys available
            }
        }

        return base.TryOpen();
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Gets the display name for a material based on its asset location.
    /// </summary>
    private static string GetMaterialDisplayName(in AssetLocation assetLocation)
    {
        var materialCode = assetLocation.EndVariant();
        return Lang.GetMatching($"material-{materialCode}") ?? assetLocation.Path;
    }

    private static string GetAlloyDisplayName(in AlloyRecipe alloy)
    {
        return GetMaterialDisplayName(alloy.Output?.Code ?? "unknown");
    }

    private static string GetIngredientDisplayName(in MetalAlloyIngredient ingredient)
    {
        return GetMaterialDisplayName(ingredient?.Code ?? "unknown");
    }
    #endregion
}