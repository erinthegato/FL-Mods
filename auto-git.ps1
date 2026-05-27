param(
    [int]$DebounceSeconds = 5,
    [switch]$Once
)

$ErrorActionPreference = 'SilentlyContinue'
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$LogPath = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads\FL_MODS_auto_git.log'

function Write-AutoGitLog {
    param([string]$Message)
    $timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
    Add-Content -LiteralPath $LogPath -Value "[$timestamp] $Message" -Encoding UTF8
}

function Invoke-AutoCommitPush {
    $status = & git -C $RepoRoot status --porcelain 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-AutoGitLog "git status failed: $status"
        return
    }

    if ([string]::IsNullOrWhiteSpace(($status | Out-String))) {
        return
    }

    & git -C $RepoRoot add -A -- . 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-AutoGitLog 'git add failed.'
        return
    }

    $staged = & git -C $RepoRoot diff --cached --name-only 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($staged | Out-String))) {
        return
    }

    $message = "Auto sync $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    $commitOutput = & git -C $RepoRoot commit -m $message 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-AutoGitLog "git commit failed: $($commitOutput | Out-String)"
        return
    }

    $branch = (& git -C $RepoRoot branch --show-current 2>&1 | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($branch)) {
        $branch = 'main'
    }

    $pushOutput = & git -C $RepoRoot push origin $branch 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-AutoGitLog "git push failed: $($pushOutput | Out-String)"
        return
    }

    $commitLine = (& git -C $RepoRoot log -1 --oneline 2>&1 | Out-String).Trim()
    Write-AutoGitLog "Pushed $commitLine to origin/$branch"
}

Invoke-AutoCommitPush
if ($Once) {
    exit 0
}

$script:PendingSync = $false
$script:LastChange = Get-Date
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $RepoRoot
$watcher.IncludeSubdirectories = $true
$watcher.NotifyFilter = [System.IO.NotifyFilters]'FileName, DirectoryName, LastWrite, Size'
$watcher.EnableRaisingEvents = $true

$action = {
    $path = $Event.SourceEventArgs.FullPath
    if ($path -match '\\\.git\\|\\bin\\|\\obj\\|\\\.vs\\') {
        return
    }
    $script:PendingSync = $true
    $script:LastChange = Get-Date
}

$subscriptions = @(
    Register-ObjectEvent -InputObject $watcher -EventName Changed -Action $action,
    Register-ObjectEvent -InputObject $watcher -EventName Created -Action $action,
    Register-ObjectEvent -InputObject $watcher -EventName Deleted -Action $action,
    Register-ObjectEvent -InputObject $watcher -EventName Renamed -Action $action
)

Write-AutoGitLog "Auto git watcher started for $RepoRoot"

try {
    while ($true) {
        Start-Sleep -Seconds 1
        if ($script:PendingSync -and ((Get-Date) - $script:LastChange).TotalSeconds -ge $DebounceSeconds) {
            $script:PendingSync = $false
            Invoke-AutoCommitPush
        }
    }
}
finally {
    foreach ($subscription in $subscriptions) {
        Unregister-Event -SubscriptionId $subscription.Id
    }
    $watcher.Dispose()
    Write-AutoGitLog "Auto git watcher stopped for $RepoRoot"
}
