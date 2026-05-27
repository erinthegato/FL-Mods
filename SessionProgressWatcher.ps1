param(
    [switch]$Once
)

$ErrorActionPreference = 'SilentlyContinue'
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputPath = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads\FL_MODS_session_progress.txt'

function Invoke-GitText {
    param([string[]]$GitArgs)
    $text = & git -C $RepoRoot @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        return "(git unavailable)"
    }
    return ($text | Out-String).Trim()
}

function Write-SessionProgress {
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
    $branch = Invoke-GitText @('branch', '--show-current')
    $status = Invoke-GitText @('status', '--short')
    if ([string]::IsNullOrWhiteSpace($status)) {
        $status = 'Working tree clean.'
    }
    $recentCommits = Invoke-GitText @('log', '-5', '--oneline')

    $content = @"
FL MODS Session Progress
Updated: $timestamp
Repository: $RepoRoot
Branch: $branch

Session progress:
- Removed MDT/NPC sync and automatic NPC record creation from GameEventLogger.
- Added manual MDT record creation with split first/last name inputs, registration status, wanted status, driver license status, license plate formatting, and weapon license.
- Collapsed citation and charge filing into the Filing tab with searchable charge selection.
- Replaced generic report/citation location entry with split ZIP and street fields.
- Updated panic behavior to play a random Panic Button tone, then a random Code99 WAV, without the red screen overlay.
- Updated NPCAI so the UI only opens near NPC/AI, closes when no NPC is nearby, and preserves the NPC name when reopening from the same player position.
- Built and deployed GameEventLogger, MDTMod, and NPCAI successfully after the main feature work.
- Committed and pushed the main feature work to GitHub as 17d032a.
- Removed GameEventLogger background/crash logging hooks and CrashLog.txt writes, leaving only essential panic/dispatch logging.
- Added press-to-rebind hotkey controls across BackgroundRadio, GrammarPolice, MDT, and NPCAI, with Escape cancelling binding.
- Stabilized NPCAI proximity checks and reduced FPS stutter by replacing broad object scans with throttled scene caches.
- Optimized GameEventLogger panic detection and audio lookup, including cached WAV lists and throttled weapon discovery.
- Added BodyCamOverlay with manual toggle, weapon-draw activation, rapid key-2 activation, generated Axon-style signal audio, and configurable repeat interval.
- Added AssetLoader for Unity AssetBundles from E:\SteamLibrary\steamapps\common\Flashing Lights\UserData\FLMods\Assets.
- Fixed BodyCamOverlay and AssetLoader MelonLoader registration so they load as mods.
- Fixed GrammarPolice speech-recognition dependency failure so it no longer floods the log every frame.
- Built, deployed, committed, and pushed the latest bodycam, asset-loader, panic-trigger, and performance fixes to GitHub as 3eb7419.

Current working tree:
$status

Recent commits:
$recentCommits
"@

    Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8 -Force
}

Write-SessionProgress
if ($Once) {
    exit 0
}

$script:PendingWrite = $false
$script:LastWriteRequest = Get-Date
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $RepoRoot
$watcher.IncludeSubdirectories = $true
$watcher.NotifyFilter = [System.IO.NotifyFilters]'FileName, DirectoryName, LastWrite, Size'
$watcher.EnableRaisingEvents = $true

$action = {
    $path = $Event.SourceEventArgs.FullPath
    if ($path -match '\\\.git\\|\\bin\\|\\obj\\') {
        return
    }
    $script:PendingWrite = $true
    $script:LastWriteRequest = Get-Date
}

$subscriptions = @(
    Register-ObjectEvent -InputObject $watcher -EventName Changed -Action $action,
    Register-ObjectEvent -InputObject $watcher -EventName Created -Action $action,
    Register-ObjectEvent -InputObject $watcher -EventName Deleted -Action $action,
    Register-ObjectEvent -InputObject $watcher -EventName Renamed -Action $action
)

try {
    while ($true) {
        Start-Sleep -Milliseconds 750
        if ($script:PendingWrite -and ((Get-Date) - $script:LastWriteRequest).TotalMilliseconds -gt 1000) {
            Write-SessionProgress
            $script:PendingWrite = $false
        }
    }
}
finally {
    foreach ($subscription in $subscriptions) {
        Unregister-Event -SubscriptionId $subscription.Id
    }
    $watcher.Dispose()
}
