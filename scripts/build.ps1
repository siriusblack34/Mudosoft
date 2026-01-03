# MudoSoft Build Script with Auto-Versioning
# Usage: .\build.ps1 [-Major] [-Minor] [-Patch] [-Build]
# Default: Increments Build number (fourth number)

param(
    [switch]$Major,   # Increment major version (1.0.0.0 -> 2.0.0.0)
    [switch]$Minor,   # Increment minor version (1.0.0.0 -> 1.1.0.0)
    [switch]$Patch,   # Increment patch version (1.0.0.0 -> 1.0.1.0)
    [switch]$Build    # Increment build version (1.0.0.0 -> 1.0.0.1) - DEFAULT
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not $ProjectRoot) { $ProjectRoot = "c:\Projects\mudosoft" }

$VersionFile = "$ProjectRoot\VERSION"
$AgentCsproj = "$ProjectRoot\agent\MudoSoft.Agent.csproj"
$TrayCsproj = "$ProjectRoot\tray\MudoSoft.Tray.csproj"
$InstallerScript = "$ProjectRoot\agent\MudoSoftAgentSetup.iss"

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  MudoSoft Auto-Build Script" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan

# 1. Read or create version file
if (Test-Path $VersionFile) {
    $currentVersion = Get-Content $VersionFile -Raw
    $currentVersion = $currentVersion.Trim()
}
else {
    $currentVersion = "1.0.0.0"
}

Write-Host "`nCurrent Version: $currentVersion" -ForegroundColor Yellow

# 2. Parse and increment version
$versionParts = $currentVersion.Split('.')
$majorV = [int]$versionParts[0]
$minorV = [int]$versionParts[1]
$patchV = [int]$versionParts[2]
$buildV = [int]$versionParts[3]

if ($Major) {
    $majorV++
    $minorV = 0
    $patchV = 0
    $buildV = 0
}
elseif ($Minor) {
    $minorV++
    $patchV = 0
    $buildV = 0
}
elseif ($Patch) {
    $patchV++
    $buildV = 0
}
else {
    # Default: increment build
    $buildV++
}

$newVersion = "$majorV.$minorV.$patchV.$buildV"
Write-Host "New Version: $newVersion" -ForegroundColor Green

# 3. Save new version
Set-Content -Path $VersionFile -Value $newVersion -NoNewline

# 4. Update Agent csproj
Write-Host "`nUpdating Agent version..." -ForegroundColor Cyan
$agentContent = Get-Content $AgentCsproj -Raw
$agentContent = $agentContent -replace '<Version>[\d\.]+</Version>', "<Version>$newVersion</Version>"
$agentContent = $agentContent -replace '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$newVersion</AssemblyVersion>"
$agentContent = $agentContent -replace '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$newVersion</FileVersion>"
Set-Content -Path $AgentCsproj -Value $agentContent -NoNewline

# 5. Update Tray csproj
Write-Host "Updating Tray version..." -ForegroundColor Cyan
$trayContent = Get-Content $TrayCsproj -Raw
$trayContent = $trayContent -replace '<Version>[\d\.]+</Version>', "<Version>$newVersion</Version>"
$trayContent = $trayContent -replace '<AssemblyVersion>[\d\.]+</AssemblyVersion>', "<AssemblyVersion>$newVersion</AssemblyVersion>"
$trayContent = $trayContent -replace '<FileVersion>[\d\.]+</FileVersion>', "<FileVersion>$newVersion</FileVersion>"
Set-Content -Path $TrayCsproj -Value $trayContent -NoNewline

# 6. Build Agent
Write-Host "`nBuilding Agent..." -ForegroundColor Cyan
Push-Location "$ProjectRoot\agent"
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\publish_single_exe
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "Agent build failed!" }
Pop-Location

# 7. Build Tray
Write-Host "`nBuilding Tray..." -ForegroundColor Cyan
Push-Location "$ProjectRoot\tray"
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\publish_single_exe
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "Tray build failed!" }
Pop-Location

# 8. Compile Installer
Write-Host "`nCompiling Installer..." -ForegroundColor Cyan
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" $InstallerScript
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed!" }

# 9. Create update package (ZIP)
$updateDir = "$ProjectRoot\agent\update_package"
$zipPath = "$ProjectRoot\agent\installer_output\MudoSoft_Update_$newVersion.zip"

Write-Host "`nCreating update package..." -ForegroundColor Cyan
if (Test-Path $updateDir) { Remove-Item $updateDir -Recurse -Force }
New-Item -ItemType Directory -Path $updateDir | Out-Null

Copy-Item "$ProjectRoot\agent\publish_single_exe\MudoSoft.Agent.exe" $updateDir
Copy-Item "$ProjectRoot\agent\publish_single_exe\appsettings.json" $updateDir
Copy-Item "$ProjectRoot\tray\publish_single_exe\MudoSoft.Tray.exe" $updateDir

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$updateDir\*" -DestinationPath $zipPath

Write-Host "`n======================================" -ForegroundColor Green
Write-Host "  BUILD COMPLETE!" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Green
Write-Host "Version: $newVersion" -ForegroundColor Yellow
Write-Host "Installer: $ProjectRoot\agent\installer_output\MudoSoftAgentSetup_1.0.0.exe" -ForegroundColor Cyan
Write-Host "Update ZIP: $zipPath" -ForegroundColor Cyan
Write-Host "`nUpload the ZIP to Dashboard > Agent Update" -ForegroundColor Magenta
