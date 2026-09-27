using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Applies one tool mode locally and mirrors the base dialog's authoritative server packet.</summary>
internal static class ToolModeSelection
{
    private const int ToolModePacketId = 27;

    /// <summary>Performs the same client, network, and inventory steps as GuiDialogToolMode.OnSlotClick.</summary>
    internal static void Apply(ICoreClientAPI api, CollectibleObject collectible, ItemSlot slot,
        IClientPlayer player, BlockSelection? selection, int mode)
    {
        collectible.SetToolMode(slot, player, selection!, mode);
        api.Network.SendPacketClient(CreatePacket(mode, selection));
        slot.MarkDirty();
    }

    /// <summary>Builds the generated game packet while tolerating tool modes that do not target a block.</summary>
    internal static Packet_Client CreatePacket(int mode, BlockSelection? selection)
    {
        var toolMode = new Packet_ToolMode { Mode = mode };
        if (selection is not null)
        {
            toolMode.X = selection.Position.X;
            toolMode.Y = selection.Position.InternalY;
            toolMode.Z = selection.Position.Z;
            toolMode.SelectionBoxIndex = selection.SelectionBoxIndex;
            toolMode.Face = selection.Face.Index;
            toolMode.HitX = CollectibleNet.SerializeDouble(selection.HitPosition.X);
            toolMode.HitY = CollectibleNet.SerializeDouble(selection.HitPosition.Y);
            toolMode.HitZ = CollectibleNet.SerializeDouble(selection.HitPosition.Z);
        }
        return new Packet_Client { Id = ToolModePacketId, ToolMode = toolMode };
    }
}