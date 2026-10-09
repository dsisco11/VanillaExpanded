using System;
using System.Collections.Immutable;

using HarmonyLib;

using VanillaExpanded.ClothingIndicators;
using VanillaExpanded.CrucibleIndicators;
using VanillaExpanded.FoodContainerIndicators;
using VanillaExpanded.ItemSlotIndicators.Animation;
using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;
using VanillaExpanded.ItemSlotIndicators.Effects.FoodGrains;
using VanillaExpanded.ItemSlotIndicators.Rendering;
using VanillaExpanded.LiquidContainerIndicators;
using VanillaExpanded.NightVisionIndicators;
using VanillaExpanded.PerishableItemSlots;
using VanillaExpanded.PreparationIndicators;
using VanillaExpanded.WateringCanIndicators;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Owns the client indicator lifecycle and selects primary and optional overlay indicators by independent provider priority.</summary>
internal sealed class ItemSlotIndicatorSystem : ModSystem
{
    private ImmutableArray<ItemSlotIndicatorRegistration> providers = [];
    private Harmony? harmony;
    private ItemSlotIndicatorFrameUpdater? frameUpdater;
    private LiquidSloshSimulation? liquidSimulation;
    private LiquidSloshSimulation? metalSimulation;
    private FoodGrainSimulation? foodSimulation;
    private FoodGrainSimulation? metalChunksSimulation;

    /// <summary>Gets the client-owned indicator renderer used by the GUI hook.</summary>
    internal ItemSlotIndicatorRenderer? Renderer { get; private set; }

    /// <summary>Gets shared frame inputs without advancing animation or provider sampling.</summary>
    internal ItemSlotIndicatorFrameSnapshot FrameSnapshot => frameUpdater?.State.Snapshot ?? default;

    /// <summary>Gets whether registered effects require authoritative camera input.</summary>
    internal bool NeedsCameraMotion { get; private set; }

    /// <summary>Gets the client-owned prepared resources without performing graphics work during selection.</summary>
    internal ItemSlotIndicatorResources? Resources { get; private set; }

    /// <summary>Gets the initialized client system used by the shared GUI render hook.</summary>
    internal static ItemSlotIndicatorSystem? Active { get; private set; }

    /// <summary>Supplies monotonic time for refresh scheduling, replaceable for deterministic tests.</summary>
    internal Func<long> Clock { get; set; } = static () => Environment.TickCount64;

    #region Public API
    #region Lifecycle
    /// <summary>Loads indicators only on the client, independently of any individual feature setting.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>Registers built-in providers, initializes rendering, and installs the indicator hook.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        Resources = new ItemSlotIndicatorResources(api.Event, new ItemSlotIndicatorResourceBackend(api));
        foreach (var registration in providers)
            if (registration.Effect is not null) Resources.Register(registration.Effect);
        Register(new FreshnessIndicatorProvider(),
            contextKey: static () => (VanillaExpandedModSystem.Config.EnablePerishableItemFreshnessIndicators,
                VanillaExpandedModSystem.Config.PerishableItemFreshnessIndicatorIntensity),
            supportedStyles: [ItemSlotIndicatorRenderingStyle.SlotBackground, ItemSlotIndicatorRenderingStyle.SlotOutline,
                ItemSlotIndicatorRenderingStyle.HorizontalBar],
            styleSelector: static () => VanillaExpandedModSystem.Config.FreshnessRenderingStyle);
        Register(new FoodContainerIndicatorProvider(),
            contextKey: static () => (VanillaExpandedModSystem.Config.EnablePerishableItemFreshnessIndicators,
                VanillaExpandedModSystem.Config.PerishableItemFreshnessIndicatorIntensity,
                VanillaExpandedModSystem.Config.FoodLevelIndicatorStyle),
            effect: FoodGrainIndicatorEffect.Definition, overlay: true, adaptiveSampling: new AdaptiveSamplingOptions(),
            defaultStyle: ItemSlotIndicatorRenderingStyle.HorizontalBar,
            supportedStyles: [ItemSlotIndicatorRenderingStyle.HorizontalBar, ItemSlotIndicatorRenderingStyle.SlotBackground],
            styleSelector: static () => VanillaExpandedModSystem.Config.FoodLevelRenderingStyle);
        Register(new PreparationIndicatorProvider(), priority: -10,
            contextKey: static () => VanillaExpandedModSystem.Config.EnablePreparationIndicators);
        Register(new ClothingIndicatorProvider(), priority: -10,
            contextKey: static () => VanillaExpandedModSystem.Config.EnableClothingIndicators);
        // Preserve freshness when applicable; otherwise show the container's liquid volume.
        var adaptiveSampling = new AdaptiveSamplingOptions();
        Register(new LiquidContainerIndicatorProvider(), priority: -10, adaptiveSampling: adaptiveSampling,
            contextKey: static () => VanillaExpandedModSystem.Config.EnableLiquidContainerIndicators,
            effect: LiquidSloshIndicatorEffect.Definition);
        Register(new WateringCanIndicatorProvider(), priority: 10, adaptiveSampling: adaptiveSampling,
            contextKey: static () => VanillaExpandedModSystem.Config.EnableLiquidContainerIndicators,
            effect: LiquidSloshIndicatorEffect.Definition);
        Register(new NightVisionFuelIndicatorProvider(), priority: 10, adaptiveSampling: adaptiveSampling,
            contextKey: static () => VanillaExpandedModSystem.Config.EnableNightVisionFuelIndicators);
        Register(new CrucibleIndicatorProvider(false), priority: 10, adaptiveSampling: adaptiveSampling,
            contextKey: static () => (VanillaExpandedModSystem.Config.EnableCrucibleIndicators,
                VanillaExpandedModSystem.Config.CrucibleIndicatorCapacityUnits), effect: CrucibleIndicatorEffect.Solid);
        Register(new CrucibleIndicatorProvider(true), priority: 10, adaptiveSampling: adaptiveSampling,
            contextKey: static () => (VanillaExpandedModSystem.Config.EnableCrucibleIndicators,
                VanillaExpandedModSystem.Config.CrucibleIndicatorCapacityUnits), effect: CrucibleIndicatorEffect.Molten);
        frameUpdater = new ItemSlotIndicatorFrameUpdater(api.Event, new ItemSlotIndicatorCameraSource(api), () => NeedsCameraMotion);
        liquidSimulation = new LiquidSloshSimulation(api, () => frameUpdater.CameraSample);
        metalSimulation = new LiquidSloshSimulation(api, () => frameUpdater.CameraSample, LiquidSloshSimulationProfile.Metal);
        foodSimulation = new FoodGrainSimulation(api, () => frameUpdater.CameraSample);
        metalChunksSimulation = new FoodGrainSimulation(api, () => frameUpdater.CameraSample, GrainSimulationProfile.SolidMetal);
        Resources.Initialize();
        Renderer = new ItemSlotIndicatorRenderer(Resources,
            new ItemSlotIndicatorDrawBackend(api, () => liquidSimulation.SurfaceTexture, () => foodSimulation.StateTexture,
                () => metalSimulation.SurfaceTexture, () => metalChunksSimulation.StateTexture));
        Active = this;
        harmony = new Harmony(Constants.ModId + ".itemslotindicators");
        new PatchClassProcessor(harmony, typeof(ItemSlotIndicatorPatch)).Patch();
    }

    /// <summary>Removes this system's hook, providers, and shared rendering resources.</summary>
    public override void Dispose()
    {
        harmony?.UnpatchAll(harmony.Id);
        harmony = null;
        liquidSimulation?.Dispose();
        liquidSimulation = null;
        metalSimulation?.Dispose();
        metalSimulation = null;
        foodSimulation?.Dispose();
        foodSimulation = null;
        metalChunksSimulation?.Dispose();
        metalChunksSimulation = null;
        frameUpdater?.Dispose();
        frameUpdater = null;
        Renderer?.Dispose();
        Renderer = null;
        Resources?.Dispose();
        Resources = null;
        Clear();
        if (Active == this)
        {
            Active = null;
        }
        base.Dispose();
    }
    #endregion

    #region Providers
    /// <summary>Registers a provider with one-second caching by default; zero interval stays immediate, and equal priorities retain order.</summary>
    /// <param name="provider">Calculates an indicator when its cached result is absent or expired.</param>
    /// <param name="priority">Higher priorities are queried first.</param>
    /// <param name="refreshIntervalMilliseconds">Minimum time between samples of an unchanged stack context; defaults to 1,000 milliseconds, and zero disables caching.</param>
    /// <param name="contextKey">Optional immutable configuration value compared by equality on each query to invalidate cached results.</param>
    /// <param name="adaptiveSampling">Optional fast sampling policy for changing indicators; ignored when caching is disabled.</param>
    /// <param name="effect">Optional immutable rendering description; omitted registrations use the ordinary rectangle.</param>
    /// <param name="overlay">Whether the provider selects an additional layer after the primary indicator.</param>
    /// <param name="defaultStyle">Fallback presentation for omitted, invalid, or unsupported selections.</param>
    /// <param name="supportedStyles">Supported presentations, including the default; omitted metadata supports only the default.</param>
    /// <param name="styleSelector">Optional live presentation selector independent of provider sampling.</param>
    internal void Register(IItemSlotIndicatorProvider provider, int priority = 0,
        long refreshIntervalMilliseconds = 1_000, Func<object?>? contextKey = null,
        AdaptiveSamplingOptions? adaptiveSampling = null, ItemSlotIndicatorEffectDefinition? effect = null, bool overlay = false,
        ItemSlotIndicatorRenderingStyle defaultStyle = ItemSlotIndicatorRenderingStyle.SlotBackground,
        ItemSlotIndicatorRenderingStyle[]? supportedStyles = null, Func<ItemSlotIndicatorRenderingStyle>? styleSelector = null)
    {
        var registration = new ItemSlotIndicatorRegistration(provider, priority, refreshIntervalMilliseconds, contextKey, adaptiveSampling,
            effect, overlay, defaultStyle, supportedStyles, styleSelector);
        // Validate metadata before insertion; a rejected registration cannot change selection or cached samples.
        if (effect is not null)
        {
            foreach (var existing in providers)
                existing.Effect?.ValidateCompatibility(effect);
            Resources?.Register(effect);
        }
        int index = 0;
        while (index < providers.Length && providers[index].Priority >= priority) index++;
        providers = providers.Insert(index, registration);
        NeedsCameraMotion |= effect?.NeedsCameraMotion == true;
    }

    /// <summary>Returns the first applicable primary and overlay presentations, including zero fill.</summary>
    internal bool TryGetRenderSelection(ItemSlot slot, out ItemSlotIndicatorRenderSelection selection)
    {
        long now = Clock();
        selection = default;
        bool primaryFound = false;
        foreach (var entry in providers)
        {
            if (entry.Overlay) continue;
            if (!entry.TryGetIndicator(slot, now, out ItemSlotIndicator indicator)) continue;
            // Attach the winning registration's effect after sampling, so animation never affects cache validity.
            selection = new ItemSlotIndicatorRenderSelection(indicator, SelectEffect(entry.Effect), entry.ResolveStyle());
            primaryFound = true;
            break;
        }
        // Additional indicators have their own priority race and sampling; they cannot hide freshness.
        foreach (var entry in providers)
        {
            if (!entry.Overlay || !entry.TryGetIndicator(slot, now, out var indicator)) continue;
            selection = primaryFound
                ? selection with { OverlayIndicator = indicator, OverlayEffect = SelectEffect(entry.Effect), OverlayStyle = entry.ResolveStyle() }
                : new ItemSlotIndicatorRenderSelection(indicator, SelectEffect(entry.Effect), entry.ResolveStyle());
            return true;
        }
        return primaryFound;
    }

    /// <summary>Releases registrations and all system-owned provider samples.</summary>
    internal void Clear()
    {
        providers = [];
        NeedsCameraMotion = false;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Applies live effect configuration without invalidating resource-volume samples.</summary>
    private static ItemSlotIndicatorEffectDefinition? SelectEffect(ItemSlotIndicatorEffectDefinition? effect) =>
        (effect?.ShaderName == LiquidSloshIndicatorEffect.Definition.ShaderName
            && !VanillaExpandedModSystem.Config.EnableLiquidSloshEffect)
        || ((effect == CrucibleIndicatorEffect.Solid || effect == CrucibleIndicatorEffect.Molten)
            && !VanillaExpandedModSystem.Config.EnableCrucibleEffect) ? null : effect;
    #endregion
}
