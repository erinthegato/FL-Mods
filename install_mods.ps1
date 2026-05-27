param(
    [string]$GamePath = "E:\SteamLibrary\steamapps\common\Flashing Lights"
)

$ErrorActionPreference = "Stop"
$ModsDir = Join-Path $GamePath "Mods"
$ScriptDir = Split-Path $PSCommandPath -Parent

Write-Host "=== Flashing Lights Mods Installer ===" -ForegroundColor Cyan
Write-Host ""

# Find game
if (!(Test-Path $GamePath)) {
    Write-Host "Game path not found: $GamePath" -ForegroundColor Yellow
    $choices = @()

    # Common Steam paths
    $common = @(
        "E:\SteamLibrary\steamapps\common\Flashing Lights",
        "C:\Program Files (x86)\Steam\steamapps\common\Flashing Lights",
        "D:\SteamLibrary\steamapps\common\Flashing Lights",
        "$env:LOCALAPPDATA\Programs\Steam\steamapps\common\Flashing Lights"
    )
    foreach ($p in $common) {
        if (Test-Path $p) { $choices += $p }
    }

    if ($choices.Count -eq 0) {
        $GamePath = Read-Host "Enter the full path to Flashing Lights installation"
        if (!(Test-Path $GamePath)) { Write-Host "Path not found." -ForegroundColor Red; exit 1 }
    }
    elseif ($choices.Count -eq 1) {
        $GamePath = $choices[0]
        Write-Host "Found game at: $GamePath" -ForegroundColor Green
    }
    else {
        Write-Host "Multiple installations found:"
        for ($i = 0; $i -lt $choices.Count; $i++) {
            Write-Host "  $($i+1). $($choices[$i])"
        }
        $sel = Read-Host "Select (1-$($choices.Count))"
        $GamePath = $choices[[int]$sel - 1]
    }
}

$ModsDir = Join-Path $GamePath "Mods"
Write-Host "Installing to: $GamePath" -ForegroundColor Cyan

# Create Mods directory
if (!(Test-Path $ModsDir)) { New-Item -ItemType Directory -Path $ModsDir -Force | Out-Null }

# Files to install (from same folder as this script)
$files = @(
    "NPCAI.dll", "NPCAI.deps.json",
    "BackgroundRadioMod.dll", "BackgroundRadioMod.deps.json",
    "GrammarPoliceMod.dll", "GrammarPoliceMod.deps.json",
    "MDTMod.dll",
    "FlashingLights.ModKit.Core.dll",
    "GameEventLogger.dll"
)

$dispatchDir = Join-Path $GamePath "DispatchAudio"
$panicDir = Join-Path $dispatchDir "Panic Button"

$zipDir = Join-Path $ScriptDir "Mods"
$srcDir = if (Test-Path $zipDir) { $zipDir } else { $ScriptDir }

$copied = 0
$skipped = 0
foreach ($f in $files) {
    $src = Join-Path $srcDir $f
    $dst = Join-Path $ModsDir $f
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $dst -Force
        Write-Host "  [OK] $f" -ForegroundColor Green
        $copied++
    }
    else {
        Write-Host "  [--] $f not found (skipped)" -ForegroundColor DarkGray
        $skipped++
    }
}

# DispatchAudio folder
$dispatchSrc = Join-Path $ScriptDir "DispatchAudio"
if (Test-Path $dispatchSrc) {
    if (!(Test-Path $dispatchDir)) {
        Copy-Item -Path $dispatchSrc -Destination $GamePath -Recurse -Force
        Write-Host "  [OK] DispatchAudio\ folder" -ForegroundColor Green
    }
    else {
        Write-Host "  [--] DispatchAudio\ already exists (skipped)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "=== Install complete ===" -ForegroundColor Cyan
Write-Host "  $copied file(s) copied, $skipped skipped"
Write-Host "  Mods folder: $ModsDir"
Write-Host ""
Write-Host "NPCAI needs an API key configured in-game via ModKit settings." -ForegroundColor Yellow
Write-Host "See FL_Mods_Installation_Guide.txt for details." -ForegroundColor Yellow
Read-Host "Press Enter to exit"
