using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Uses the engine player implementation to satisfy inaccessible interface members while observing chat delivery.</summary>
internal sealed class ObservedServerPlayer : ServerPlayer, IServerPlayer
{
    private IPlayerInventoryManager inventory = null!;
    public List<(int Group, string Message, EnumChatType Type)> Messages { get; private set; } = null!;
    public new IPlayerInventoryManager InventoryManager => inventory;

    #region Public API
    /// <summary>Creates a test-only engine subclass without starting server/player lifecycle infrastructure.</summary>
    public static ObservedServerPlayer Create(IPlayerInventoryManager inventory)
    {
        // Only the remapped inventory and chat members are exercised; initialize their entire state explicitly.
        var player = (ObservedServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ObservedServerPlayer));
        player.inventory = inventory;
        player.Messages = [];
        return player;
    }

    /// <summary>Records actual error-chat delivery without invoking network infrastructure.</summary>
    public new void SendMessage(int groupId, string message, EnumChatType chatType, string data = null!)
    {
        Messages.Add((groupId, message, chatType));
    }
    #endregion

    #region Private
    /// <summary>Declares the required engine constructor; the isolated fixture uses uninitialized allocation instead.</summary>
    private ObservedServerPlayer() : base(null!, null!) { }
    #endregion
}
