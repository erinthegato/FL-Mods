param(
    [string]$GameRoot = ""
)

# ── Auto-detect game root ──
function Find-GameRoot {
    # Check env var first
    $env = [Environment]::GetEnvironmentVariable("FL_GAME_ROOT", "User")
    if ($env -and (Test-Path "$env\flashinglights.exe")) {
        return $env
    }

    # Common Steam paths
    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Flashing Lights",
        "C:\Program Files\Steam\steamapps\common\Flashing Lights",
        "D:\SteamLibrary\steamapps\common\Flashing Lights",
        "E:\SteamLibrary\steamapps\common\Flashing Lights",
        "F:\SteamLibrary\steamapps\common\Flashing Lights",
        "$env:LOCALAPPDATA\Steam\steamapps\common\Flashing Lights"
    )

    # Try to find via Steam registry
    try {
        $steamPath = (Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -Name InstallPath -ErrorAction Stop).InstallPath
        $libs = "$steamPath\steamapps\libraryfolders.vdf"
        if (Test-Path $libs) {
            $content = Get-Content $libs -Raw
            $matches = [regex]::Matches($content, '"path"\s*"([^"]+)"')
            foreach ($m in $matches) {
                $path = $m.Groups[1].Value -replace '\\\\', '\'
                $candidates += "$path\steamapps\common\Flashing Lights"
            }
        }
    } catch {}

    foreach ($c in $candidates) {
        if (Test-Path "$c\flashinglights.exe") {
            return $c
        }
    }

    return ""
}

if (-not $GameRoot) {
    $GameRoot = Find-GameRoot
}

if (-not $GameRoot) {
    Write-Host "ERROR: Could not find Flashing Lights game root." -ForegroundColor Red
    Write-Host "Set the FL_GAME_ROOT environment variable or pass -GameRoot <path>." -ForegroundColor Yellow
    exit 1
}

Write-Host "Game root: $GameRoot" -ForegroundColor Cyan

$root = Split-Path -Parent $PSCommandPath

# ── Build GameEventLogger ──
Write-Host "Building GameEventLogger..." -ForegroundColor Cyan
$proj1 = Join-Path $root "GameEventLogger\GameEventLogger.csproj"
dotnet build $proj1 -c Release --nologo -v q -p:GameRoot=$GameRoot
if ($LASTEXITCODE -ne 0) {
    Write-Host "GameEventLogger build FAILED" -ForegroundColor Red
    exit 1
}

# ── Build FLWatchdog ──
Write-Host "Building FLWatchdog..." -ForegroundColor Cyan
$proj2 = Join-Path $root "FLWatchdog\FLWatchdog.csproj"
dotnet build $proj2 -c Release --nologo -v q -p:GameRoot=$GameRoot
if ($LASTEXITCODE -ne 0) {
    Write-Host "FLWatchdog build FAILED" -ForegroundColor Red
    exit 1
}

Write-Host "Build complete." -ForegroundColor Green
