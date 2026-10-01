using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Supplies the owning API chain used by the engine's otherwise inaccessible player reachability check.</summary>
internal sealed class ServerPermissionContext
{
    public ServerMain Server { get; }

    #region Public API
    /// <summary>Allocates inert API holders without executing server, world-map, or API constructors or lifecycle methods.</summary>
    /// <remarks>Wires only ServerMain.api, ServerCoreAPI.server, ServerMain.WorldMap, and ServerWorldMap.RelaxedBlockAccess.</remarks>
    public ServerPermissionContext(IBlockAccessor accessor)
    {
        // These objects are only the field chain read by ServerPlayer's real range check; no server infrastructure starts.
        Server = (ServerMain)RuntimeHelpers.GetUninitializedObject(typeof(ServerMain));
        var api = (ServerCoreAPI)RuntimeHelpers.GetUninitializedObject(typeof(ServerCoreAPI));
        var map = (ServerWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(ServerWorldMap));
        SetField(Server, "api", api);
        SetField(api, "server", Server);
        SetField(Server, "WorldMap", map);
        SetField(map, "RelaxedBlockAccess", accessor);
    }
    #endregion

    #region Private
    /// <summary>Checks the exact engine field and its expected value type before installing fixture state.</summary>
    private static void SetField(object owner, string name, object value)
    {
        var field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.True(field.FieldType.IsInstanceOfType(value));
        Assert.False(field.IsInitOnly);
        field.SetValue(owner, value);
        Assert.Same(value, field.GetValue(owner));
    }
    #endregion
}
