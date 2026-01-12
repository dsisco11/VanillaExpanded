using System;
using System.Linq;
using System.Reflection;

using VanillaExpanded;
using VanillaExpanded.ModSystems;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.ModSystems;

internal sealed class ConfigLibIntegrationModSystem : ModSystem
{
    private ICoreAPI? api;
    private bool registered;
    private object? configLibConfig;

    public override double ExecuteOrder() => 0.0;

    public override void StartPre(ICoreAPI api)
    {
        this.api = api;

        VanillaExpandedModSystem.EnsureConfigLoaded(api);
        TryRegisterConfigWithConfigLib();
    }

    public override void Start(ICoreAPI api)
    {
        this.api = api;

        // ConfigLib emits events on the VS event bus when settings change / config is saved.
        api.Event.RegisterEventBusListener(OnConfigLibConfigSaved, filterByEventName: string.Format("configlib:{0}:config-saved", Constants.ModId));
    }

    private void OnConfigLibConfigSaved(string eventName, ref EnumHandling handling, IAttribute data)
    {
        if (api is null) return;

        TrySyncConfigFromConfigLib();
        LiveConfigReload.NotifyAll(api);
        VanillaExpandedModSystem.PersistConfigAndNotifyReloadRequired(api, source: "ConfigLib");
    }

    private void TryRegisterConfigWithConfigLib()
    {
        if (registered) return;
        if (api is null) return;
        if (!api.ModLoader.IsModEnabled("configlib")) return;

        ModSystem? configLib = api.ModLoader.GetModSystem("ConfigLib.ConfigLibModSystem");
        if (configLib is null)
        {
            api.Logger.Debug("[VanillaExpanded] ConfigLib mod is enabled but ConfigLib.ConfigLibModSystem was not found.");
            return;
        }

        MethodInfo? method = configLib.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m =>
            {
                if (m.Name != "RegisterCustomManagedConfig") return false;
                var p = m.GetParameters();
                return p.Length >= 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(object);
            });

        if (method is null)
        {
            api.Logger.Debug("[VanillaExpanded] ConfigLib.ConfigLibModSystem.RegisterCustomManagedConfig(...) was not found (API mismatch?).");
            return;
        }

        try
        {
            Action onSyncedFromServer = () =>
            {
                if (api is null) return;
                TrySyncConfigFromConfigLib();
                LiveConfigReload.NotifyAll(api);
                VanillaExpandedModSystem.PersistConfigAndNotifyReloadRequired(api, source: "ConfigLib (server sync)");
            };

            Action onConfigSaved = () =>
            {
                if (api is null) return;
                TrySyncConfigFromConfigLib();
                LiveConfigReload.NotifyAll(api);
                VanillaExpandedModSystem.PersistConfigAndNotifyReloadRequired(api, source: "ConfigLib");
            };

            object?[] args = BuildArgs(method, onSyncedFromServer, onConfigSaved);
            method.Invoke(configLib, args);
            registered = true;

            // Capture the created/registered config instance and do an initial sync.
            configLibConfig = TryGetConfigInstance(configLib);
            TrySyncConfigFromConfigLib();
            LiveConfigReload.NotifyAll(api);
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[VanillaExpanded] Failed to register config with ConfigLib: {0}", ex);
        }
    }

    private void TrySyncConfigFromConfigLib()
    {
        if (api is null) return;
        if (!api.ModLoader.IsModEnabled("configlib")) return;

        // Prefer cached instance (if we captured it after registration).
        configLibConfig ??= TryGetConfigInstance(api.ModLoader.GetModSystem("ConfigLib.ConfigLibModSystem"));
        if (configLibConfig is null) return;

        try
        {
            MethodInfo? assignMethod = configLibConfig.GetType().GetMethod(
                "AssignSettingsValues",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(object) },
                modifiers: null);

            if (assignMethod is null)
            {
                api.Logger.Debug("[VanillaExpanded] ConfigLib config does not expose AssignSettingsValues(object). Cannot sync settings into mod config.");
                return;
            }

            assignMethod.Invoke(configLibConfig, new object?[] { VanillaExpandedModSystem.Config });
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[VanillaExpanded] Failed to sync settings from ConfigLib into mod config: {0}", ex);
        }
    }

    private object? TryGetConfigInstance(object? configLibModSystem)
    {
        if (api is null) return null;
        if (configLibModSystem is null) return null;

        try
        {
            MethodInfo? getConfig = configLibModSystem.GetType().GetMethod(
                "GetConfig",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);

            if (getConfig is null)
            {
                api.Logger.Debug("[VanillaExpanded] ConfigLib mod system does not expose GetConfig(string). Cannot locate config instance.");
                return null;
            }

            return getConfig.Invoke(configLibModSystem, new object?[] { Constants.ModId });
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[VanillaExpanded] Failed to get ConfigLib config instance: {0}", ex);
            return null;
        }
    }

    private static object?[] BuildArgs(MethodInfo method, Action onSyncedFromServer, Action onConfigSaved)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object?[] args = new object?[parameters.Length];

        // Required
        args[0] = Constants.ModId;
        args[1] = VanillaExpandedModSystem.Config;

        // Optional parameters (in current ConfigLib): path, onSyncedFromServer, onSettingChanged, onConfigSaved
        if (parameters.Length >= 3) args[2] = Constants.ConfigFileName;
        if (parameters.Length >= 4) args[3] = onSyncedFromServer;
        if (parameters.Length >= 5) args[4] = null;
        if (parameters.Length >= 6) args[5] = onConfigSaved;

        return args;
    }
}
