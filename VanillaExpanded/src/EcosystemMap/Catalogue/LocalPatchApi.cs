using System;
using System.Reflection;
using Vintagestory.API.Common;

namespace VanillaExpanded.EcosystemMap.Catalogue;

/// <summary>A fail-closed API projection exposing only the getters used by the vanilla JSON patcher.</summary>
internal class LocalPatchApi : DispatchProxy
{
    private ICoreAPI? source;
    private IAssetManager? assets;

    #region Public API
    /// <summary>Creates a server-target patch view over local data without constructing or invoking a server API.</summary>
    internal static ICoreAPI Create(ICoreAPI source, IAssetManager assets)
    {
        ICoreAPI proxy = Create<ICoreAPI, LocalPatchApi>();
        var implementation = (LocalPatchApi)(object)proxy;
        implementation.source = source;
        implementation.assets = assets;
        return proxy;
    }

    /// <summary>Allows the patcher's verified read-only context access and rejects all other API operations.</summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "get_Assets" => assets,
        "get_Side" => EnumAppSide.Server,
        "get_World" => source!.World,
        "get_ModLoader" => source!.ModLoader,
        "get_Logger" => source!.Logger,
        _ => throw new NotSupportedException("Unsupported operation in the isolated ecosystem patch context: " + targetMethod?.Name)
    };
    #endregion
}
