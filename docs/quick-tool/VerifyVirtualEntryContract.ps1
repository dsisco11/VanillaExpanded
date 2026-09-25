<#
.SYNOPSIS
Verifies installed offhand restrictions and isolated virtual-entry movement plans.
.DESCRIPTION
Uses real installed inventory/slot/collectible objects and CanHold/CanTake checks.
This fixture does not load a world, execute gameplay notifications, or send packets.
#>
param([string] $GameDirectory = 'G:/Vintagestory')
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $GameDirectory 'VintagestoryAPI.dll'))
Write-Output ('Assembly: ' + $assembly.FullName)
Write-Output ('SHA256: ' + (Get-FileHash $assembly.Location -Algorithm SHA256).Hash)

#region Fixture helpers
<#
.SYNOPSIS
Creates a real installed item stack with controlled storage flags.
#>
function New-FixtureStack {
    param([bool] $OffhandAllowed)
    $item = [Vintagestory.API.Common.Item]::new()
    $item.StorageFlags = [Vintagestory.API.Common.EnumItemStorageFlags]::General
    if ($OffhandAllowed) { $item.StorageFlags = $item.StorageFlags -bor [Vintagestory.API.Common.EnumItemStorageFlags]::Offhand }
    return [Vintagestory.API.Common.ItemStack]::new($item, 1)
}
<#
.SYNOPSIS
Checks all real-slot restrictions before a complete reference permutation.
.DESCRIPTION
Indices describe source positions for each destination. No notifications occur in the
assignment interval; the existing inventory probe separately covers that boundary.
#>
function Invoke-FixturePlan {
    param($Slots, $Expected, [int[]] $SourceIndices)
    # Use complete permutations only; never merge, split, or discard any source stack.
    if (($SourceIndices | Sort-Object -Unique).Count -ne $Slots.Count) { throw 'Fixture requires a complete permutation.' }
    for ($index = 0; $index -lt $Slots.Count; $index++) {
        if (![object]::ReferenceEquals($Slots[$index].Itemstack, $Expected[$index])) { return $false }
        if ($SourceIndices[$index] -eq $index) { continue }
        $source = $Slots[$SourceIndices[$index]]
        if (!$source.Empty -and (!$source.CanTake() -or !$Slots[$index].CanHold($source) -or $source.StackSize -gt $Slots[$index].MaxSlotStackSize)) { return $false }
    }
    # Virtual restriction checks can invoke external code; recheck identities afterwards.
    for ($index = 0; $index -lt $Slots.Count; $index++) {
        if (![object]::ReferenceEquals($Slots[$index].Itemstack, $Expected[$index])) { return $false }
    }
    for ($index = 0; $index -lt $Slots.Count; $index++) { $Slots[$index].Itemstack = $Expected[$SourceIndices[$index]] }
    return $true
}
<#
.SYNOPSIS
Asserts exact reference locations including empty slots.
#>
function Assert-FixtureContents {
    param($Slots, $Expected)
    for ($index = 0; $index -lt $Slots.Count; $index++) {
        if (![object]::ReferenceEquals($Slots[$index].Itemstack, $Expected[$index])) { throw 'Fixture reference or location mismatch.' }
    }
}
#endregion

#region Actual offhand validation
$inventory = [Vintagestory.API.Common.InventoryGeneric]::new(4, 'virtualprobe', 'local', $null)
$offhand = [Vintagestory.API.Common.ItemSlotOffhand]::new($inventory)
$inventory[2] = $offhand
foreach ($ordinaryIndex in @(0,1,3)) { $inventory[$ordinaryIndex] = [Vintagestory.API.Common.ItemSlotSurvival]::new($inventory) }
$slots = @($inventory[0], $inventory[1], $offhand, $inventory[3])
$itemA = New-FixtureStack $true
$toolB = New-FixtureStack $false
$lightL = New-FixtureStack $true
$lightL.Collectible.LightHsv = [byte[]] @(0,0,20)
$toolC = New-FixtureStack $false
$initial = @($itemA, $toolB, $lightL, $toolC)
for ($index = 0; $index -lt 4; $index++) { $slots[$index].Itemstack = $initial[$index] }
Write-Output ('Offhand StorageType=' + $offhand.StorageType + ' MaxSlotStackSize=' + $offhand.MaxSlotStackSize)
Write-Output ('CanHold owner=' + $offhand.GetType().GetMethod('CanHold').DeclaringType.FullName)
Write-Output ('OnItemSlotModified owner=' + $offhand.GetType().GetMethod('OnItemSlotModified').DeclaringType.FullName)
if (!$offhand.CanHold($slots[0]) -or $offhand.CanHold($slots[1])) { throw 'Actual offhand storage flag contract failed.' }
# Equip light directly and restore original A and original offhand light exactly.
if (!(Invoke-FixturePlan $slots $initial @(2,1,0,3))) { throw 'Valid offhand light equip rejected.' }
Assert-FixtureContents $slots @($lightL,$toolB,$itemA,$toolC)
if (!(Invoke-FixturePlan $slots @($lightL,$toolB,$itemA,$toolC) @(2,1,0,3))) { throw 'Valid offhand light restore rejected.' }
Assert-FixtureContents $slots $initial
Write-Output 'PASS: offhand light equip/restore preserves original A and light references.'
# The displaced original A travels through offhand; current B does not stay there.
if (!(Invoke-FixturePlan $slots $initial @(1,0,2,3))) { throw 'Tool equip failed.' }
if (!(Invoke-FixturePlan $slots @($toolB,$itemA,$lightL,$toolC) @(2,0,1,3))) { throw 'Tool-to-light failed.' }
Assert-FixtureContents $slots @($lightL,$toolB,$itemA,$toolC)
if (!(Invoke-FixturePlan $slots @($lightL,$toolB,$itemA,$toolC) @(3,1,0,2))) { throw 'Light-to-tool failed.' }
Assert-FixtureContents $slots @($toolC,$toolB,$lightL,$itemA)
if (!(Invoke-FixturePlan $slots @($toolC,$toolB,$lightL,$itemA) @(3,1,2,0))) { throw 'Chained restore failed.' }
Assert-FixtureContents $slots $initial
Write-Output 'PASS: A-to-tool-B-to-offhand-light-to-tool-C-to-A restores every exact original reference.'
# Selecting the offhand-first candidate must reject, not pick another light, when A cannot enter offhand.
$unsupportedA = New-FixtureStack $false
$slots[0].Itemstack = $unsupportedA
$unsupported = @($unsupportedA,$toolB,$lightL,$toolC)
if (Invoke-FixturePlan $slots $unsupported @(2,1,0,3)) { throw 'Offhand accepted unsupported original A.' }
Assert-FixtureContents $slots $unsupported
if (!(Invoke-FixturePlan $slots $unsupported @(1,0,2,3))) { throw 'Tool selection unexpectedly failed.' }
$unsupportedChain = @($toolB,$unsupportedA,$lightL,$toolC)
if (Invoke-FixturePlan $slots $unsupportedChain @(2,0,1,3)) { throw 'Chained offhand accepted unsupported original A.' }
Assert-FixtureContents $slots $unsupportedChain
Write-Output 'PASS: incompatible original A rejects direct and chained offhand light selection without mutation.'
# Empty original hand is legal; no stack must be inserted into the offhand destination.
$slots[0].Itemstack = $null
$slots[1].Itemstack = $toolB
$empty = @($null,$toolB,$lightL,$toolC)
if (!(Invoke-FixturePlan $slots $empty @(2,1,0,3))) { throw 'Empty-hand light equip failed.' }
if (!(Invoke-FixturePlan $slots @($lightL,$toolB,$null,$toolC) @(2,1,0,3))) { throw 'Empty-hand restore failed.' }
Assert-FixtureContents $slots $empty
Write-Output 'PASS: initially empty hand restores light to offhand and remains empty.'
# CanHold handles locks/flags; the complete planner must separately validate slot capacity.
$slots[0].Itemstack = $itemA
$offhand.MaxSlotStackSize = 1
$itemA.StackSize = 2
if (!$offhand.CanHold($slots[0])) { throw 'Fixture expected flags-only CanHold to pass.' }
if (Invoke-FixturePlan $slots $initial @(2,1,0,3)) { throw 'Capacity violation accepted.' }
Assert-FixtureContents $slots $initial
$itemA.StackSize = 1
[Vintagestory.API.Common.InventoryBase].GetProperty('PutLocked').SetValue($inventory, $true)
if (Invoke-FixturePlan $slots $initial @(2,1,0,3)) { throw 'PutLocked accepted.' }
[Vintagestory.API.Common.InventoryBase].GetProperty('PutLocked').SetValue($inventory, $false)
[Vintagestory.API.Common.InventoryBase].GetProperty('TakeLocked').SetValue($inventory, $true)
if (Invoke-FixturePlan $slots $initial @(2,1,0,3)) { throw 'TakeLocked accepted.' }
[Vintagestory.API.Common.InventoryBase].GetProperty('TakeLocked').SetValue($inventory, $false)
Assert-FixtureContents $slots $initial
Write-Output 'PASS: capacity, PutLocked and TakeLocked reject complete movement unchanged.'
$replacement = New-FixtureStack $true
$offhand.Itemstack = $replacement
if (Invoke-FixturePlan $slots $initial @(2,1,0,3)) { throw 'Stale offhand identity accepted.' }
Assert-FixtureContents $slots @($itemA,$toolB,$replacement,$toolC)
Write-Output 'PASS: replaced offhand reference rejects without identical-item substitution.'
Write-Output 'LIMIT: no live world, actual hotbar callbacks, bag persistence, selector extraction, or packet delivery execution.'
#endregion

#region Installed identity and publication evidence
[void][Reflection.Assembly]::LoadFrom((Join-Path $GameDirectory 'Lib/Mono.Cecil.dll'))
foreach ($filename in @('VintagestoryAPI.dll','VintagestoryLib.dll')) {
    $definition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory $filename))
    try {
        Write-Output ('Assembly IL: ' + $definition.Name.FullName)
        Write-Output ('SHA256: ' + (Get-FileHash (Join-Path $GameDirectory $filename) -Algorithm SHA256).Hash)
        $definition.MainModule.Types | Where-Object Name -in @('ItemSlotOffhand','ItemSlot','EntityPlayer','InventoryPlayerHotbar','ServerPlayerInventoryManager','ServerPlayer','ServerMain','ServerWorldPlayerData') | ForEach-Object {
            $type = $_
            $type.Methods | Where-Object {
                ($type.Name -eq 'ItemSlotOffhand') -or
                ($type.Name -eq 'ItemSlot' -and $_.Name -in @('CanHold','CanTake','OnItemSlotModified')) -or
                ($type.Name -eq 'EntityPlayer' -and $_.Name -eq 'get_LeftHandItemSlot') -or
                ($type.Name -eq 'InventoryPlayerHotbar' -and $_.Name -in @('NewSlot','OnItemSlotModified')) -or
                ($_.Name -in @('BroadcastPlayerData','BroadcastHotbarSlot')) -or
                ($type.Name -eq 'ServerWorldPlayerData' -and $_.Name -eq 'ToPacket')
            } | ForEach-Object {
                Write-Output ('Visibility=' + $_.Attributes + ' ' + $_.FullName)
                if ($type.Name -in @('ItemSlotOffhand','ItemSlot','EntityPlayer','InventoryPlayerHotbar')) { $_.Body.Instructions | ForEach-Object ToString }
                else { $_.Body.Instructions | Where-Object { $_.OpCode.Name -match 'call' -or $_.Operand -match 'OffhandStack|InventoryContents' } | ForEach-Object ToString }
            }
        }
    } finally { $definition.Dispose() }
}
#endregion



