using System;

using Vintagestory.API.Common;

namespace VanillaExpanded.ModSystems;

internal static class LiveConfigReload
{
    public static void NotifyAll(ICoreAPI api)
    {
        foreach (ModSystem system in api.ModLoader.Systems)
        {
            if (system is ILiveConfigurable live)
            {
                try
                {
                    live.OnConfigReloaded(api);
                }
                catch (Exception ex)
                {
                    api.Logger.Warning("[VanillaExpanded] Live config reload failed in {0}: {1}", system.GetType().FullName, ex);
                }
            }
        }
    }
}
