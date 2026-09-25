<#
.SYNOPSIS
Verifies installed inventory primitives and an isolated complete-permutation boundary.
.DESCRIPTION
This is an API investigation fixture, not production movement code or multiplayer proof.
Uses real installed ItemSlot objects with detached inventories so gameplay callbacks,
server synchronization, and arbitrary mod overrides are deliberately outside the fixture.
#>
param([string] $GameDirectory = 'G:/Vintagestory')
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $GameDirectory 'VintagestoryAPI.dll'))
Write-Output ('Assembly: ' + $assembly.FullName)
Write-Output ('SHA256: ' + (Get-FileHash $assembly.Location -Algorithm SHA256).Hash)

#region Installed API evidence
# Print the actual assembly surface; adjacent source is explanatory evidence only.
foreach ($typeName in @('Vintagestory.API.Common.IInventoryNetworkUtil', 'Vintagestory.API.Client.GuiDialog', 'Vintagestory.API.Client.IInputAPI', 'Vintagestory.API.Client.IClientEventAPI', 'Vintagestory.API.Common.IEventAPI', 'Vintagestory.API.Server.IServerNetworkChannel', 'Vintagestory.API.Server.IServerPlayer', 'Vintagestory.API.Common.IPlayerInventoryManager', 'Vintagestory.API.Common.InventoryBase', 'Vintagestory.API.Server.IServerEventAPI')) {
    Write-Output $typeName
    $assembly.GetType($typeName).GetMembers() |
        Where-Object Name -Match 'Flip|Rollback|PauseInventory|Capture|PrefersUngrabbedMouse|DisableMouseGrab|KeyboardKeyState|GetHotKeyByCode|IsHotKeyPressed|ActiveSlotChanged|LevelFinalize|LeaveWorld|PlayerEntitySpawn|PauseResume|OnKeyDown|OnKeyUp|OnMouseDown|OnMouseUp|Focused|UnFocus|TryOpen|TryClose|EnqueueMainThreadTask|SetMessageHandler|SendPacket|BroadcastPlayerData|BroadcastHotbarSlot|PlayerDisconnect|PlayerRespawn|PlayerDeath' |
        ForEach-Object ToString
}
# The verified setter is nonvirtual and only assigns its backing reference.
$setter = $assembly.GetType('Vintagestory.API.Common.ItemSlot').GetProperty('Itemstack').SetMethod
$il = $setter.GetMethodBody().GetILAsByteArray()
if ($setter.IsVirtual -or $il.Length -ne 8 -or $il[0] -ne 2 -or $il[1] -ne 3 -or $il[2] -ne 0x7d -or $il[7] -ne 0x2a) {
    throw 'Installed Itemstack setter no longer has the verified plain-field assignment contract.'
}
Write-Output ('Itemstack setter IL: ' + [Convert]::ToHexString($il))
#endregion

#region Isolated movement evidence
# Snapshot references before any changes. Duplicate-looking stacks must remain distinct.
$slots = @([Vintagestory.API.Common.ItemSlot]::new($null), [Vintagestory.API.Common.ItemSlot]::new($null), [Vintagestory.API.Common.ItemSlot]::new($null))
$original = @([Vintagestory.API.Common.ItemStack]::new(), [Vintagestory.API.Common.ItemStack]::new(), [Vintagestory.API.Common.ItemStack]::new())
for ($index = 0; $index -lt 3; $index++) { $slots[$index].Itemstack = $original[$index] }
<#
.SYNOPSIS
Applies an isolated reference permutation after every supplied eligibility result passes.
.DESCRIPTION
Eligibility results are fixture inputs, not a substitute for production CanHold/CanTake
checks. The fixture verifies the rejection and assignment boundary against real slots.
#>
function Invoke-FixturePermutation {
    param($FixtureSlots, $Expected, $Final, [bool[]] $Allowed)
    # Rejection and stale identity checks complete before any reference is assigned.
    for ($position = 0; $position -lt $FixtureSlots.Length; $position++) {
        if (!$Allowed[$position] -or ![object]::ReferenceEquals($FixtureSlots[$position].Itemstack, $Expected[$position])) { return $false }
    }
    for ($position = 0; $position -lt $FixtureSlots.Length; $position++) { $FixtureSlots[$position].Itemstack = $Final[$position] }
    return $true
}
# A rejected complete plan returns before its first assignment.
$final = @($original[2], $original[0], $original[1])
if (Invoke-FixturePermutation $slots $original $final @($true, $true, $false)) { throw 'Rejected plan unexpectedly accepted.' }
for ($index = 0; $index -lt 3; $index++) {
    if (![object]::ReferenceEquals($slots[$index].Itemstack, $original[$index])) { throw 'Rejected plan changed a slot.' }
}
# Model B-to-C from [B,A,C] to [C,B,A]. All assignment work precedes notification.
$final = @($original[2], $original[0], $original[1])
$script:notifications = 0
$callback = [Vintagestory.API.Common.ActionConsumable] {
    for ($index = 0; $index -lt 3; $index++) {
        if (![object]::ReferenceEquals($slots[$index].Itemstack, $final[$index])) { throw 'Notification observed partial permutation.' }
    }
    $script:notifications++
    return $true
}
foreach ($slot in $slots) { $slot.add_MarkedDirty($callback) }
if (!(Invoke-FixturePermutation $slots $original $final @($true, $true, $true))) { throw 'Valid fixture permutation rejected.' }
foreach ($slot in $slots) { $slot.MarkDirty() }
if ($script:notifications -ne 3) { throw 'Missing notification.' }
for ($index = 0; $index -lt 3; $index++) {
    if (![object]::ReferenceEquals($slots[$index].Itemstack, $final[$index])) { throw 'Permutation lost identity.' }
}
# A callback exception is after the commit point: it cannot become a rejection.
$slots[0].remove_MarkedDirty($callback)
$slots[0].add_MarkedDirty([Vintagestory.API.Common.ActionConsumable] { throw 'Expected fixture notification failure' })
$observedFailure = $false
try { $slots[0].MarkDirty() } catch { $observedFailure = $true }
if (!$observedFailure) { throw 'Notification failure fixture did not run.' }
for ($index = 0; $index -lt 3; $index++) {
    if (![object]::ReferenceEquals($slots[$index].Itemstack, $final[$index])) { throw 'Postcommit failure unexpectedly rolled back.' }
}
Write-Output 'PASS: rejection unchanged; reference-conserving three-slot permutation; observers see complete state; notification failure is postcommit.'
<#
.SYNOPSIS
Asserts that one accepted fixture move reaches every exact expected reference.
#>
function Assert-FixtureStep {
    param($FixtureSlots, $Expected, $Final)
    $allowed = @($FixtureSlots | ForEach-Object { $true })
    if (!(Invoke-FixturePermutation $FixtureSlots $Expected $Final $allowed)) { throw 'Expected fixture move rejected.' }
    for ($position = 0; $position -lt $FixtureSlots.Length; $position++) {
        if (![object]::ReferenceEquals($FixtureSlots[$position].Itemstack, $Final[$position])) { throw 'Fixture move changed identity or location.' }
    }
}
# Verify complete sessions, using distinct references even though all blank stacks look equal.
$chainSlots = @([Vintagestory.API.Common.ItemSlot]::new($null), [Vintagestory.API.Common.ItemSlot]::new($null), [Vintagestory.API.Common.ItemSlot]::new($null))
$itemA = [Vintagestory.API.Common.ItemStack]::new()
$toolB = [Vintagestory.API.Common.ItemStack]::new()
$toolC = [Vintagestory.API.Common.ItemStack]::new()
$initial = @($itemA, $toolB, $toolC)
for ($position = 0; $position -lt 3; $position++) { $chainSlots[$position].Itemstack = $initial[$position] }
$twoSlots = @($chainSlots[0], $chainSlots[1])
Assert-FixtureStep $twoSlots @($itemA, $toolB) @($toolB, $itemA)
Assert-FixtureStep $twoSlots @($toolB, $itemA) @($itemA, $toolB)
Write-Output 'PASS: A-to-B-to-A two-slot session restores both exact references.'
Assert-FixtureStep $twoSlots @($itemA, $toolB) @($toolB, $itemA)
Assert-FixtureStep $chainSlots @($toolB, $itemA, $toolC) @($toolC, $toolB, $itemA)
Assert-FixtureStep @($chainSlots[0], $chainSlots[2]) @($toolC, $itemA) @($itemA, $toolC)
for ($position = 0; $position -lt 3; $position++) {
    if (![object]::ReferenceEquals($chainSlots[$position].Itemstack, $initial[$position])) { throw 'Chained session did not restore initial arrangement.' }
}
Write-Output 'PASS: A-to-B-to-C-to-A chained session restores all three exact references.'
# A replacement that looks identical cannot satisfy the original reference expectation.
$replacement = [Vintagestory.API.Common.ItemStack]::new()
$chainSlots[1].Itemstack = $replacement
if (Invoke-FixturePermutation $twoSlots @($itemA, $toolB) @($toolB, $itemA) @($true, $true)) { throw 'Stale source accepted.' }
if (![object]::ReferenceEquals($chainSlots[0].Itemstack, $itemA) -or ![object]::ReferenceEquals($chainSlots[1].Itemstack, $replacement)) { throw 'Stale rejection changed inventory.' }
Write-Output 'PASS: stale identical-looking source reference rejects without mutation.'
$chainSlots[0].Itemstack = $null
$chainSlots[1].Itemstack = $toolB
Assert-FixtureStep $twoSlots @($null, $toolB) @($toolB, $null)
Assert-FixtureStep $twoSlots @($toolB, $null) @($null, $toolB)
Write-Output 'PASS: initially empty hand equips and restores without inventing an item.'
Write-Output 'LIMIT: detached slots; no eligibility validator, world callbacks, live server, or network synchronization tested.'
#endregion

#region Installed implementation boundaries
# Cecil reads exact installed method bodies without loading graphical runtime dependencies.
[void][Reflection.Assembly]::LoadFrom((Join-Path $GameDirectory 'Lib/Mono.Cecil.dll'))
$library = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory 'VintagestoryLib.dll'))
try {
    Write-Output ('Library: ' + $library.Name.FullName)
    Write-Output ('Library SHA256: ' + (Get-FileHash (Join-Path $GameDirectory 'VintagestoryLib.dll') -Algorithm SHA256).Hash)
    foreach ($typeName in @('Vintagestory.Client.ScreenManager', 'Vintagestory.Client.NoObf.ClientPlatformAbstract', 'Vintagestory.Common.InventoryPlayerBackpacks', 'Vintagestory.Common.InventoryNetworkUtil', 'Vintagestory.Server.ServerPlayer', 'Vintagestory.Server.ServerMain', 'Vintagestory.Server.ServerWorldPlayerData', 'Vintagestory.Server.ServerPlayerInventoryManager', 'Vintagestory.Common.InventoryPlayerHotbar')) {
        $type = $library.MainModule.Types | Where-Object FullName -EQ $typeName
        Write-Output $typeName
        $type.Fields | Where-Object Name -EQ 'Platform' | ForEach-Object { Write-Output ('Public=' + $_.IsPublic + ' Static=' + $_.IsStatic + ' ' + $_.FullName) }
        $type.Methods | Where-Object Name -Match '^get_IsFocused$|^OnItemSlotModified$|^handleFlipItemstacksPacket$|^UpdateSlotStack$|^SendInventoryContents$|^SendDirtyInventoryContents$|^BroadcastPlayerData$|^BroadcastHotbarSlot$|^NewSlot$|^ToPacket$' | ForEach-Object {
            Write-Output ('Visibility=' + $_.Attributes + ' ' + $_.FullName)
            if ($_.Body) { $_.Body.Instructions | Where-Object { $_.OpCode.Name -Match 'call' } | ForEach-Object ToString }
        }
    }
    $assembly.GetType('Vintagestory.API.Common.CollectibleObject').GetMethods() |
        Where-Object Name -Match '^GetTool$|^GetToolTier$|^GetMaxDurability$|^GetRemainingDurability$' | ForEach-Object ToString
    Write-Output ('EnumTool: ' + ([Enum]::GetNames($assembly.GetType('Vintagestory.API.Common.EnumTool')) -join ', '))
    Write-Output ('GlKeys.Unknown=' + [int][Vintagestory.API.Client.GlKeys]::Unknown)
    $apiDefinition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory 'VintagestoryAPI.dll'))
    try {
        # Retain the notification arguments and dirty/persistence ordering, not signatures alone.
        $apiDefinition.MainModule.Types | Where-Object Name -in @('ItemSlot', 'InventoryBase') | ForEach-Object {
            $_.Methods | Where-Object Name -in @('OnItemSlotModified', 'DidModifyItemSlot', 'MarkSlotDirty', 'TryFlipWith') | ForEach-Object {
                Write-Output $_.FullName
                $_.Body.Instructions | ForEach-Object ToString
            }
        }
        # Preserve exact special-slot indices and the sendInventory boolean branch.
        $library.MainModule.Types | Where-Object Name -in @('InventoryPlayerHotbar', 'ServerWorldPlayerData') | ForEach-Object {
            $_.Methods | Where-Object { $_.Name -eq 'NewSlot' -or ($_.Name -eq 'ToPacket' -and $_.DeclaringType.Name -eq 'ServerWorldPlayerData') } | ForEach-Object {
                Write-Output $_.FullName
                $_.Body.Instructions | Where-Object { $_.Offset -ge 0x170 -or $_.Offset -lt 0x40 } | ForEach-Object ToString
            }
        }
    } finally { $apiDefinition.Dispose() }
} finally { $library.Dispose() }
#endregion





