# VanillaExpanded - Copilot Instructions

## Project Overview
VintageStory mod adding QoL enhancements. Multi-project workspace includes:
- `VanillaExpanded/` - Main mod code
- `VanillaExpanded.Tests/` - Unit and E2E tests
- `vsapi/`, `vssurvivalmod/`, `vscreativemod/` - Game source references (read-only, explore for API patterns)

## Architecture Patterns

### Mod System Registration
Entry point is `VanillaExpandedModSystem`. Register behaviors in `Start()`:
```csharp
api.RegisterBlockBehaviorClass(BlockBehaviorAutoStashable.RegistryId, typeof(BlockBehaviorAutoStashable));
api.RegisterCollectibleBehaviorClass(BehaviorIgnitionTool.RegistryId, typeof(BehaviorIgnitionTool));
```

### Harmony Patching
Use `[HarmonyPatch]` attributes for game modifications. Patches are applied in `VanillaExpandedModSystem.Start()` and cleaned up in `Dispose()`. See `SpawnDecal/ServerPlayer.patch.cs` or `AutoStashing/AutoStashPatch.cs` for examples.

### Client-Server Communication
Network packets in `src/Network/` use naming convention `{Feature}.packet.cs`. Register in `VanillaExpandedModSystem.Start()`:
```csharp
api.Network.RegisterChannel(Mod.Info.ModID)
    .RegisterMessageType<Packet_RequestAutoStash>()
```

### Block Behaviors
New block behaviors extend `BlockBehavior` with static `RegistryId` property. JSON patches in `assets/vanillaexpanded/patches/` inject behaviors into vanilla blocks.

## Testing Conventions

### Test Organization
- **Unit tests**: `VanillaExpanded.Tests/Unit/{Feature}/` - Mark with `[Trait("Category", "Unit")]`
- **E2E tests**: `VanillaExpanded.Tests/E2E/{Feature}/` - Mark with `[Trait("Category", "E2E")]`

### Mock Infrastructure (`Mocks/`)
Use existing mocks rather than creating new ones:
- `MockItem.CreateLightSource(id, brightness)` / `MockItem.CreateNonLightSource(id)`
- `InventoryGeneric` with `null!` API, then set `Api` and `InvNetworkUtil` after construction
- `MockClientNetworkChannel` / `MockServerNetworkChannel` for packet verification

### Inventory Testing Pattern
```csharp
var inventory = new InventoryGeneric(10, "className", "id-1", null!);
inventory.Api = apiMock.Object;
inventory.InvNetworkUtil = invNetworkUtilMock.Object;
inventory[0].Itemstack = new ItemStack(MockItem.CreateLightSource(id: 1, brightness: 20));
```

## Build & Test Commands

```bash
# Run tests
dotnet test                                          # All tests
dotnet test --filter "Category=Unit"                 # Fast unit tests
dotnet test --filter "FullyQualifiedName~AutoStashing"  # Feature-specific

# Build mod release (uses Cake)
./build.ps1    # or ./build.sh on Linux
```

The Cake build (`CakeBuild/Program.cs`) validates JSON, compiles, and packages to `Releases/`.

## Key Directories
- `VanillaExpanded/src/` - Feature modules (AutoStashing, AlloyCalculator, etc.)
- `VanillaExpanded/assets/vanillaexpanded/` - JSON patches and recipes
- `VanillaExpanded.Tests/Mocks/` - Reusable test infrastructure

## Conventions
- Use `internal` visibility for mod-internal classes
- Static `RegistryId` properties for registerable behaviors
- Feature-based folder organization in both `src/` and tests
- Prefer real `InventoryGeneric` over mocking `ItemSlot` (non-virtual members)
