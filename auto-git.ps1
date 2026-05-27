$repo = "C:\Users\joshy\OneDrive\Documents\FL MODS"
Set-Location $repo

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
