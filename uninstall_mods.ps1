param(
    [string]$GamePath = "E:\SteamLibrary\steamapps\common\Flashing Lights"
)

$ErrorActionPreference = "Continue"
$ModsDir = Join-Path $GamePath "Mods"

Write-Host "=== Flashing Lights Mods Uninstaller ===" -ForegroundColor Cyan
Write-Host ""

if (!(Test-Path $ModsDir)) {
    Write-Host "Mods folder not found at: $ModsDir" -ForegroundColor Red
    Write-Host "Specify the correct game path:" -ForegroundColor Yellow
    Write-Host "  .\uninstall_mods.ps1 -GamePath `"D:\SteamLibrary\steamapps\common\Flashing Lights`"" -ForegroundColor Gray
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "This will remove all FL mod files from:" -ForegroundColor Yellow
Write-Host "  $ModsDir" -ForegroundColor White
Write-Host ""

# Files to remove
$modFiles = @(
    "NPCAI.dll", "NPCAI.deps.json",
    "BackgroundRadioMod.dll", "BackgroundRadioMod.deps.json",
    "GrammarPoliceMod.dll", "GrammarPoliceMod.deps.json",
    "GrammarPoliceMod.pdb",
    "MDTMod.dll",
    "FlashingLights.ModKit.Core.dll",
    "GameEventLogger.dll",
    "EventLog.txt"
)

$configFiles = @(
    "mdt_data.json",
    "mdt_photos.json"
)

Write-Host "Mod DLLs to remove:" -ForegroundColor Cyan
$removed = 0
$notFound = 0
foreach ($f in $modFiles) {
    $path = Join-Path $ModsDir $f
    if (Test-Path $path) {
        Remove-Item -Path $path -Force
        Write-Host "  [DEL] $f" -ForegroundColor Red
        $removed++
    }
    else {
        Write-Host "  [--] $f (not found)" -ForegroundColor DarkGray
        $notFound++
    }
}

Write-Host ""
Write-Host "Config/data files (optional):" -ForegroundColor Cyan
foreach ($f in $configFiles) {
    $path = Join-Path $ModsDir $f
    if (Test-Path $path) {
        Remove-Item -Path $path -Force
        Write-Host "  [DEL] $f" -ForegroundColor Red
        $removed++
    }
    else {
        Write-Host "  [--] $f (not found)" -ForegroundColor DarkGray
        $notFound++
    }
}

Write-Host ""
Write-Host "=== Uninstall complete ===" -ForegroundColor Cyan
Write-Host "  $removed file(s) removed, $notFound not present"
Write-Host ""
Write-Host "Note: DispatchAudio\ folder (WAV files) was NOT removed." -ForegroundColor Gray
Write-Host "Remove manually if desired: $GamePath\DispatchAudio" -ForegroundColor Gray
Read-Host "Press Enter to exit"
