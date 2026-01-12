using Vintagestory.API.Common;

namespace VanillaExpanded.ModSystems;

internal interface ILiveConfigurable
{
    void OnConfigReloaded(ICoreAPI api);
}
