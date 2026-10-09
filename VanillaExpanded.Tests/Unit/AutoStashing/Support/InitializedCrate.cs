using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Uses vanilla crate inventory initialization while isolating rendering and world synchronization.</summary>
internal sealed class InitializedCrate : BlockEntityCrate
{
    public int DirtyCalls { get; private set; }

    #region Public API
    /// <summary>Initializes the actual crate inventory, including its suitability delegates and retrieve-only lock.</summary>
    public InitializedCrate(ICoreAPI api, bool retrieveOnly = false)
    {
        Api = api;
        Pos = new BlockPos(0);
        Block = new BlockCrate
        {
            Attributes = JsonObject.FromJson($"{{\"properties\":{{\"*\":{{\"quantitySlots\":2,\"retrieveOnly\":{retrieveOnly.ToString().ToLowerInvariant()}}}}}}}")
        };
        InitInventory(Block, api);
        Inventory.Api = api;
        Inventory.InvNetworkUtil = Mock.Of<IInventoryNetworkUtil>();
    }

    /// <summary>Records synchronization requests without requiring a loaded world or renderer.</summary>
    public override void MarkDirty(bool redrawOnClient = false, IPlayer? skipPlayer = null)
    {
        DirtyCalls++;
    }
    #endregion
}
