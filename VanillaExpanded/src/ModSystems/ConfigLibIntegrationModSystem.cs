using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.ModSystems;

internal sealed class ConfigLibIntegrationModSystem : ModSystem
{
    internal const string ConfigSavedEvent = "configlib:{0}:config-saved";
    internal const string ConfigChangedEvent = "configlib:{0}:setting-changed";
    internal const string ConfigLoadedEvent = "configlib:{0}:setting-loaded";
    internal const string ConfigReloadEvent = "configlib:config-reload";

    private ICoreAPI? api;

    public override double ExecuteOrder() => 0.0;

    public override void StartPre(ICoreAPI api)
    {
        this.api = api;
        VanillaExpandedModSystem.EnsureConfigLoaded(api);
    }

    public override void Start(ICoreAPI api)
    {
        this.api = api;

        api.Event.RegisterEventBusListener(OnConfigLibEvent, filterByEventName: string.Format(ConfigSavedEvent, Constants.ModId));
        api.Event.RegisterEventBusListener(OnConfigLibEvent, filterByEventName: string.Format(ConfigChangedEvent, Constants.ModId));
        api.Event.RegisterEventBusListener(OnConfigLibEvent, filterByEventName: string.Format(ConfigLoadedEvent, Constants.ModId));
        api.Event.RegisterEventBusListener(OnConfigLibEvent, filterByEventName: ConfigReloadEvent);
    }

    private void OnConfigLibEvent(string eventName, ref EnumHandling handling, IAttribute data)
    {
        if (api is null) return;
        if (ApplyConfigLibSettings(VanillaExpandedModSystem.Config, data) == 0) return;

        LiveConfigReload.NotifyAll(api);
    }

    internal static int ApplyConfigLibSettings(VanillaExpandedConfig config, IAttribute data)
    {
        int applied = 0;

        foreach (ConfigLibSettingUpdate update in ExtractSettingUpdates(data))
        {
            PropertyInfo? property = typeof(VanillaExpandedConfig).GetProperty(
                update.PropertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property?.CanWrite != true) continue;
            if (!TryConvertValue(update.Value, property.PropertyType, out object? value)) continue;

            property.SetValue(config, value);
            applied++;
        }

        return applied;
    }

    private static ConfigLibSettingUpdate[] ExtractSettingUpdates(IAttribute data)
    {
        if (data is TreeAttribute tree)
        {
            var updates = new List<ConfigLibSettingUpdate>();

            if (TryExtractSingleSetting(tree, out ConfigLibSettingUpdate single))
            {
                updates.Add(single);
            }

            if (TryGetTree(tree, "Settings", out TreeAttribute? settings))
            {
                foreach (string key in settings!.Keys)
                {
                    if (settings[key] is TreeAttribute entry && TryExtractSingleSetting(entry, out ConfigLibSettingUpdate update))
                    {
                        updates.Add(update);
                    }
                }
            }

            return updates.ToArray();
        }

        if (data is not StringAttribute attribute || string.IsNullOrWhiteSpace(attribute.value))
        {
            return Array.Empty<ConfigLibSettingUpdate>();
        }

        try
        {
            Dictionary<string, string>? values = JsonConvert.DeserializeObject<Dictionary<string, string>>(attribute.value);
            return values?.Select(pair => new ConfigLibSettingUpdate(pair.Key, pair.Value)).ToArray()
                ?? Array.Empty<ConfigLibSettingUpdate>();
        }
        catch
        {
            return Array.Empty<ConfigLibSettingUpdate>();
        }
    }

    private static bool TryExtractSingleSetting(TreeAttribute tree, out ConfigLibSettingUpdate update)
    {
        string? propertyName = TryGetString(tree, "MappingKey")
            ?? TryGetString(tree, "Setting")
            ?? TryGetString(tree, "Code")
            ?? TryGetString(tree, "Key");

        string? value = TryGetString(tree, "Value")
            ?? TryGetString(tree, "value")
            ?? TryGetString(tree, "NewValue");

        if (string.IsNullOrWhiteSpace(propertyName) || value is null)
        {
            update = default;
            return false;
        }

        update = new ConfigLibSettingUpdate(propertyName, value);
        return true;
    }

    private static bool TryGetTree(TreeAttribute tree, string key, out TreeAttribute? value)
    {
        foreach (string candidate in tree.Keys)
        {
            if (!string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)) continue;

            value = tree[candidate] as TreeAttribute;
            return value is not null;
        }

        value = null;
        return false;
    }

    private static string? TryGetString(TreeAttribute tree, string key)
    {
        foreach (string candidate in tree.Keys)
        {
            if (!string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)) continue;

            return tree[candidate] switch
            {
                StringAttribute value => value.value,
                IntAttribute value => value.value.ToString(CultureInfo.InvariantCulture),
                LongAttribute value => value.value.ToString(CultureInfo.InvariantCulture),
                FloatAttribute value => value.value.ToString("R", CultureInfo.InvariantCulture),
                DoubleAttribute value => value.value.ToString("R", CultureInfo.InvariantCulture),
                BoolAttribute value => value.value ? "true" : "false",
                _ => tree[candidate].ToString()
            };
        }

        return null;
    }

    private static bool TryConvertValue(string rawValue, Type targetType, out object? value)
    {
        try
        {
            value = JsonConvert.DeserializeObject(rawValue, targetType);
            return value is not null;
        }
        catch
        {
            try
            {
                value = Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }
    }

    private readonly record struct ConfigLibSettingUpdate(string PropertyName, string Value);
}
