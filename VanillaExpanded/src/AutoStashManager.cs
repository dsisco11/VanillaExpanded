using Vintagestory.API.Common;

namespace VanillaExpanded.src;
internal class AutoStashManager
{
    #region Fields
    protected readonly ModInfo mod;
    protected readonly ICoreAPI coreApi;
    #endregion

    #region Constructors
    public AutoStashManager(in ModInfo mod, ICoreAPI api)
    {
        this.mod = mod;
        coreApi = api;
    }
    #endregion

    #region Network Handlers
    #endregion
}
