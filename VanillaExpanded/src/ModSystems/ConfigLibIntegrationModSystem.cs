using System;
using System.Linq;
using System.Reflection;

using VanillaExpanded;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.ModSystems;

internal sealed class ConfigLibIntegrationModSystem : ModSystem
{
    private ICoreAPI? api;
    private bool registered;

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

        // For managed configs, ConfigLib already updated our config object instance.
        // Apply any changes that can be applied live (recipes, (re)patching, etc.).
        VanillaExpandedModSystem.ApplyLiveConfig(api);
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
                VanillaExpandedModSystem.ApplyLiveConfig(api);
            };

            object?[] args = BuildArgs(method, onSyncedFromServer);
            method.Invoke(configLib, args);
            registered = true;
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[VanillaExpanded] Failed to register config with ConfigLib: {0}", ex);
        }
    }

    private static object?[] BuildArgs(MethodInfo method, Action onSyncedFromServer)
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
        if (parameters.Length >= 6) args[5] = null;

        return args;
    }
}
