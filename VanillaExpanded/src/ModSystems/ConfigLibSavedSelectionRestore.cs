using System;
using System.Reflection;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.ModSystems;

/// <summary>Restores saved dropdown selections through ConfigLib's public reload path after initialization.</summary>
internal sealed class ConfigLibSavedSelectionRestore : IDisposable
{
    private readonly object provider;
    private readonly EventInfo loadedEvent;
    private readonly MethodInfo getConfig;
    private readonly Action restore;
    private readonly ICoreAPI api;
    private readonly ICoreClientAPI? client;
    private bool clientReady;
    private bool pending;

    #region Public API
    /// <summary>Subscribes when ConfigLib is installed, without making it a required assembly dependency.</summary>
    internal static ConfigLibSavedSelectionRestore? Attach(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled("configlib")) return null;
        object? provider = api.ModLoader.GetModSystem("ConfigLib.ConfigLibModSystem");
        if (provider is null)
        {
            api.Logger.Warning("[VanillaExpanded] ConfigLib selection restoration could not find the loaded provider ({0}).", api.Side);
            return null;
        }
        Type type = provider.GetType();
        EventInfo? loaded = type.GetEvent("ConfigsLoaded");
        MethodInfo? getConfig = type.GetMethod("GetConfig", [typeof(string)]);
        if (loaded?.EventHandlerType != typeof(Action) || getConfig is null)
        {
            api.Logger.Warning("[VanillaExpanded] ConfigLib selection restoration could not find its public lifecycle API ({0}).", api.Side);
            return null;
        }
        return new(api, provider, loaded, getConfig);
    }

    /// <summary>Detaches the callback when the owning mod system is disposed.</summary>
    public void Dispose()
    {
        loadedEvent.RemoveEventHandler(provider, restore);
        if (client is not null) client.Event.LevelFinalize -= OnLevelFinalize;
        pending = false;
    }
    #endregion

    #region Private
    /// <summary>Retains the optional public API subscription for the current mod lifecycle.</summary>
    private ConfigLibSavedSelectionRestore(ICoreAPI api, object provider, EventInfo loadedEvent, MethodInfo getConfig)
    {
        this.api = api;
        this.provider = provider;
        this.loadedEvent = loadedEvent;
        this.getConfig = getConfig;
        client = api as ICoreClientAPI;
        clientReady = client?.PlayerReadyFired ?? true;
        restore = OnConfigsLoaded;
        loadedEvent.AddEventHandler(provider, restore);
        if (client is not null) client.Event.LevelFinalize += OnLevelFinalize;
    }

    /// <summary>Defers client restoration until networking is ready to broadcast setting changes.</summary>
    private void OnConfigsLoaded()
    {
        // ConfigLib broadcasts from its Value setter before it updates MappingKey. During asset
        // synchronization its channel is disconnected, so reloading here would leave the menu key stale.
        if (!clientReady)
        {
            pending = true;
            return;
        }
        Restore();
    }

    /// <summary>Restores the latest synchronized configuration once the client finishes loading the world.</summary>
    private void OnLevelFinalize()
    {
        clientReady = true;
        if (!pending) return;
        pending = false;
        Restore();
    }

    /// <summary>Reloads only our configuration after ConfigLib constructs or synchronizes its settings.</summary>
    private void Restore()
    {
        // ConfigLib 1.10.12's initial JSON parser leaves mapped settings at their default key.
        // Its public reload path resolves saved keys and emits corrected setting events.
        try
        {
            object? config = getConfig.Invoke(provider, [Constants.ModId]);
            config?.GetType().GetMethod("TryReadFromFile", Type.EmptyTypes)?.Invoke(config, null);
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[VanillaExpanded] Could not restore saved ConfigLib selections: {0}",
                exception.GetBaseException().Message);
        }
    }
    #endregion
}
