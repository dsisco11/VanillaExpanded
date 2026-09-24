param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Install', 'Uninstall')]
    [string]$Action
)

$archiveName = 'vanillaexpanded_999.99.9.zip'
$modsDirectory = Join-Path $env:APPDATA 'VintagestoryData\Mods'
$archivePath = Join-Path $modsDirectory $archiveName

if ($Action -eq 'Install') {
    $sourcePath = Join-Path $PSScriptRoot "..\Releases\$archiveName"
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "CakeBuild did not produce the development archive: $sourcePath"
    }

    New-Item -ItemType Directory -Force -Path $modsDirectory | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $archivePath -Force
    Write-Host "Installed $archiveName to $modsDirectory"
    return
}

Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
Write-Host "Uninstalled development mod from $archivePath"