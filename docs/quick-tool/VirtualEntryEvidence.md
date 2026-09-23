# Virtual-entry offhand contract evidence

Verified against installed API/library 1.22.7.0 on 2026-09-23 using PowerShell 7.6.6. Run `pwsh -File docs/quick-tool/VerifyVirtualEntryContract.ps1`; [retained output](virtual-entry-probe.txt) includes assembly hashes, six passing scenario groups, and installed IL. This extends [the original inventory evidence](InventoryContractEvidence.md); it is an isolated contract fixture, not production equipment implementation or live multiplayer acceptance.

## Slot identity, restrictions, and authority

Installed `EntityPlayer.LeftHandItemSlot` resolves `player.InventoryManager.GetHotbarInventory()[11]`. Installed `InventoryPlayerHotbar.NewSlot` constructs index 11 as `ItemSlotOffhand`, index 10 as `ItemSlotSkill`, and ordinary slots as `ItemSlotSurvival`. Production code should obtain the player's actual `LeftHandItemSlot`, verify reference membership in that player's supported hotbar inventory and its supported runtime slot kind, and exclude it from ordinary tool discovery. Do not accept an arbitrary client-provided slot or assume every hotbar index is ordinary storage.

`ItemSlotOffhand` overrides `StorageType` to `EnumItemStorageFlags.Offhand` (256); `CanHold`, `CanTake`, and `OnItemSlotModified` come from `ItemSlot`. Actual installed `CanHold` rejects a General-only collectible and accepts General|Offhand, subject to inventory PutLocked, tags, and `CanContain`; `CanTake` enforces TakeLocked. The unmodified offhand slot's `MaxSlotStackSize` is 999999, so do not invent a one-item limit. Check each final stack against actual destination capacity separately: `CanHold` alone does not enforce that limit. The fixture lowers the capacity to one, supplies a two-item stack, and confirms whole-plan rejection despite passing storage flags.

Keep the approved offhand-first selector. If its selected offhand light cannot participate in the complete arrangement, disable/reject that selection with feedback and no inventory mutation. Do not silently select a lower-priority hotbar/backpack light, move original A to an invented alternate location, or relax offhand storage restrictions.

## Complete movement cases

The real-slot fixture uses `InventoryGeneric` ownership, ordinary `ItemSlotSurvival` slots, one `ItemSlotOffhand`, and real `ItemStack`/`Item` objects. Its representative light has positive `LightHsv[2]`. Each plan verifies exact expected references, actual `CanHold`/`CanTake`, and destination capacity before assigning the complete reference permutation. It rechecks references after virtual validation calls. No callbacks occur during assignments, following the setter boundary proved by the original probe.

For the table below, H is active hand, B0/C0 are tool homes, and O is offhand. Original A must be offhand-compatible whenever the final plan temporarily puts A in O; the fact that current tool B is incompatible with O does not prevent a valid three-slot permutation.

| Action | H | B0 | O | C0 |
| --- | --- | --- | --- | --- |
| Initial | A | B | L | C |
| Select B | B | A | L | C |
| Select offhand light L | L | B | A | C |
| Select C | C | B | L | A |
| Unequip | A | B | L | C |

Passing fixture groups:

- Direct offhand-light equip and restore preserve both exact original references.
- Tool B -> offhand light L -> tool C -> Unequip restores A and all three original item locations, with no merge, split, or loss.
- A lacking Offhand storage flags rejects both direct light equip and the chained B -> L selection unchanged. This tests original A, not merely the currently held tool.
- An initially empty hand equips the offhand light and returns it, leaving the hand empty.
- Destination capacity, PutLocked, and TakeLocked reject the whole move unchanged.
- Replacing the offhand stack with an identical-looking reference rejects a stale plan without substitution.

An already-held source remains a no-op under the existing common equipment contract. Server reference identity, session history, internal-notification guard, external replacement/quantity invalidation, and request deduplication remain shared with tool selections. Offhand being another slot in the same hotbar must not create a second inventory subscription; slot changes invalidate the virtual candidate cache and revalidate involved session state.

## Persistence and publication

The offhand slot has no separate bag serialization mechanism. It belongs to the normal player hotbar inventory. After committing all references, call `slot.OnItemSlotModified(previousStackAtThisSlot)` for every affected slot under the same notification guard as tool movements. Installed `InventoryPlayerHotbar.OnItemSlotModified` calls `updateSlotStatMods`; skipping normal notifications would omit those hand-related stat effects. `ItemSlot.OnItemSlotModified` calls inventory dirty/event/collectible notifications and transition processing as recorded in the original evidence. Backpack sources still require the previously verified bag persistence callbacks.

Use the same public `IServerPlayer.BroadcastPlayerData(true)` and `IPlayerInventoryManager.BroadcastHotbarSlot()` publication path. The former includes owned player inventories for the owner; installed `ServerMain.BroadcastHotbarSlot` explicitly serializes both active `Itemstack` and `Entity.LeftHandItemSlot` into `OffhandStack` for other players. These call paths are retained here and in the original inventory probe. No separate custom offhand synchronization protocol is required by the verified surface.

Precommit rejection leaves references unchanged. Postcommit callback failure remains committed-needs-reconciliation, not an unchanged rejection; callback mutation invalidates history. Arbitrary mod callback rollback is still outside the contract.

## Evidence limits

The fixture executes actual installed slot storage, lock, capacity, and identity checks. It does not instantiate a live player's hotbar inventory, execute world-dependent hand/stat/collectible callbacks, save/reload bags or worlds, run the shared selector extraction, or send/receive packets. Installed IL proves those integration hooks and ownership; later implementation tests and live acceptance must verify their execution. No performance test, broad build, packaging, or production code was added.
