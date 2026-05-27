$root = "C:\Users\joshy\OneDrive\Documents\FL MODS"
Set-Location $root

# Build both projects before committing
& "$root\build.ps1"

$date = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$message = "Auto-sync $date"

git add -A
$status = git status --porcelain
if ($status) {
    git commit -m $message
    git push
} else {
    git push
}
