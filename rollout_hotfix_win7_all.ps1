param(
    [string]$SourcePath = ""
)

$targets = @(
    9, 23, 24, 26, 32, 39, 43, 51, 52, 55,
    56, 57, 58, 59, 60, 76, 88, 91, 100, 102,
    104, 107, 110, 113, 114, 117, 121, 122, 125, 129,
    136, 139, 143, 147, 151, 152, 155, 158, 159, 161,
    173, 176, 181, 182, 191, 195, 202, 206, 210, 211,
    216, 217, 218, 219, 238, 239, 243, 247, 248, 249,
    251
)

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    & "$PSScriptRoot\rollout_hotfix.ps1" -Targets $targets
}
else {
    & "$PSScriptRoot\rollout_hotfix.ps1" -Targets $targets -SourcePath $SourcePath
}
