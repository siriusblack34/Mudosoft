# ============================================================
# Orchestra Server Bootstrap -- 10.75.1.109
# ============================================================
# 109'da ADMIN PowerShell'de calistir:
#   Set-ExecutionPolicy -Scope Process Bypass -Force
#   .\bootstrap-109.ps1
#
# Idempotent: tekrar calistirilabilir.
# Hata olursa pencere acik kalir (sonda Read-Host var).
# ============================================================

#Requires -RunAsAdministrator

# Window'in kapanmamasi icin tum script try/finally icinde
try {

$ErrorActionPreference = 'Continue'   # Stop yerine Continue -- adim adim devam etsin
$ProgressPreference = 'SilentlyContinue'

$ORCHESTRA_ROOT = 'C:\Orchestra'
$BACKEND_DIR    = "$ORCHESTRA_ROOT\backend"
$FRONTEND_DIR   = "$ORCHESTRA_ROOT\frontend"
$LOG_DIR        = "$ORCHESTRA_ROOT\logs"
$DL_DIR         = "$ORCHESTRA_ROOT\_installers"
$BACKEND_PORT   = 5000
$BACKEND_HTTPS  = 5001

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-OK($msg)   { Write-Host "[OK] $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "[!!] $msg" -ForegroundColor Yellow }
function Write-Err2($msg) { Write-Host "[XX] $msg" -ForegroundColor Red }

function Download-File($url, $out) {
    if (Test-Path $out) { Write-OK "Zaten indirilmis: $(Split-Path $out -Leaf)"; return }
    Write-Host "    indiriliyor: $url"
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing
        Write-OK "indirildi: $(Split-Path $out -Leaf)"
    } catch {
        Write-Err2 "indirme basarisiz: $url  -- $_"
    }
}

function Install-Exe($path, $argList) {
    if (-not (Test-Path $path)) { Write-Err2 "kurulum dosyasi yok: $path"; return }
    Write-Host "    calistiriliyor: $path $argList"
    try {
        $p = Start-Process -FilePath $path -ArgumentList $argList -Wait -PassThru -NoNewWindow
        Write-OK "kurulum bitti (exit $($p.ExitCode))"
    } catch {
        Write-Err2 "kurulum hatasi: $_"
    }
}

# ------------------------------------------------------------
# 0) Klasor yapisi
# ------------------------------------------------------------
Write-Step "Klasor yapisi"
foreach ($d in @($ORCHESTRA_ROOT, $BACKEND_DIR, $FRONTEND_DIR, $LOG_DIR, $DL_DIR)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}
Write-OK "Klasorler: $ORCHESTRA_ROOT"

# ------------------------------------------------------------
# 1) TLS 1.2 (eski Server icin sart)
# ------------------------------------------------------------
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Test-DotnetRuntime {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    $out = & dotnet --list-runtimes 2>$null
    return [bool]($out | Select-String 'Microsoft.AspNetCore.App 8\.')
}
function Test-DotnetSdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    $out = & dotnet --list-sdks 2>$null
    return [bool]($out | Select-String '^8\.')
}

# ------------------------------------------------------------
# 2) .NET 8 ASP.NET Core Hosting Bundle
# ------------------------------------------------------------
Write-Step ".NET 8 ASP.NET Core Hosting Bundle"
if (Test-DotnetRuntime) {
    Write-OK ".NET 8 ASP.NET Core runtime zaten kurulu"
} else {
    $url = 'https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/8.0.11/dotnet-hosting-8.0.11-win.exe'
    $out = "$DL_DIR\dotnet-hosting-8.exe"
    Download-File $url $out
    Install-Exe $out "/install /quiet /norestart"
}

# ------------------------------------------------------------
# 3) .NET 8 SDK
# ------------------------------------------------------------
Write-Step ".NET 8 SDK"
if (Test-DotnetSdk) {
    Write-OK ".NET 8 SDK zaten kurulu"
} else {
    $url = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.404/dotnet-sdk-8.0.404-win-x64.exe'
    $out = "$DL_DIR\dotnet-sdk-8.exe"
    Download-File $url $out
    Install-Exe $out "/install /quiet /norestart"
}

# ------------------------------------------------------------
# 4) Node.js LTS
# ------------------------------------------------------------
Write-Step "Node.js LTS"
if (Get-Command node -ErrorAction SilentlyContinue) {
    Write-OK "Node.js zaten kurulu: $(node --version)"
} else {
    $url = 'https://nodejs.org/dist/v20.18.0/node-v20.18.0-x64.msi'
    $out = "$DL_DIR\node-lts.msi"
    Download-File $url $out
    Install-Exe 'msiexec.exe' "/i `"$out`" /qn /norestart"
}

# ------------------------------------------------------------
# 5) Git
# ------------------------------------------------------------
Write-Step "Git"
if (Get-Command git -ErrorAction SilentlyContinue) {
    Write-OK "Git zaten kurulu"
} else {
    $url = 'https://github.com/git-for-windows/git/releases/download/v2.47.0.windows.1/Git-2.47.0-64-bit.exe'
    $out = "$DL_DIR\git-installer.exe"
    Download-File $url $out
    Install-Exe $out "/VERYSILENT /NORESTART /NOCANCEL /SP- /SUPPRESSMSGBOXES"
}

# ------------------------------------------------------------
# 6) PostgreSQL 16
# ------------------------------------------------------------
Write-Step "PostgreSQL 16"
$pgSvc = Get-Service -Name 'postgresql*' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($pgSvc) {
    Write-OK "PostgreSQL servis zaten var: $($pgSvc.Name)"
} else {
    Write-Warn2 "PostgreSQL'i MANUEL kur (interaktif sifre ister):"
    Write-Host "  https://www.enterprisedb.com/downloads/postgres-postgresql-downloads" -ForegroundColor Gray
    Write-Host "  postgresql-16.x-x-windows-x64.exe indir ve calistir." -ForegroundColor Gray
    Write-Host "  Sifre belirle, port 5432, locale=Turkish_Turkey." -ForegroundColor Gray
}

# ------------------------------------------------------------
# 7) Docker Desktop
# ------------------------------------------------------------
Write-Step "Docker Desktop"
if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-OK "docker CLI zaten var"
} else {
    $url = 'https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe'
    $out = "$DL_DIR\docker-desktop.exe"
    Download-File $url $out
    Install-Exe $out "install --quiet --accept-license"
    Write-Warn2 "Docker Desktop: reboot sonrasi ilk acilisi MANUEL yap, WSL2 onayla"
}

# ------------------------------------------------------------
# 8) VC++ Redist 2015-2022 x64
# ------------------------------------------------------------
Write-Step "VC++ Redist 2015-2022 x64"
$vcKey = 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64'
if (Test-Path $vcKey) {
    Write-OK "VC++ Redist zaten kurulu"
} else {
    $url = 'https://aka.ms/vs/17/release/vc_redist.x64.exe'
    $out = "$DL_DIR\vc_redist.x64.exe"
    Download-File $url $out
    Install-Exe $out "/install /quiet /norestart"
}

# ------------------------------------------------------------
# 9) NSSM (nssm.cc + GitHub mirror fallback)
# ------------------------------------------------------------
Write-Step "NSSM"
$nssmPath = "C:\nssm\nssm.exe"
if (Test-Path $nssmPath) {
    Write-OK "NSSM zaten var"
} else {
    $nssmUrls = @(
        'https://nssm.cc/release/nssm-2.24.zip',
        'https://web.archive.org/web/2024/https://nssm.cc/release/nssm-2.24.zip'
    )
    $tmp = "$DL_DIR\nssm.zip"
    foreach ($u in $nssmUrls) {
        if (Test-Path $tmp) { break }
        Download-File $u $tmp
    }
    if (Test-Path $tmp) {
        try {
            Expand-Archive -Path $tmp -DestinationPath "$DL_DIR\nssm-extract" -Force
            New-Item -ItemType Directory -Path 'C:\nssm' -Force | Out-Null
            Copy-Item "$DL_DIR\nssm-extract\nssm-2.24\win64\nssm.exe" $nssmPath -Force
            Write-OK "NSSM kuruldu: $nssmPath"
        } catch {
            Write-Err2 "NSSM extract hatasi: $_"
        }
    } else {
        Write-Warn2 "NSSM indirilemedi. Manuel: https://nssm.cc/download (nssm-2.24.zip) -> win64\nssm.exe -> C:\nssm\nssm.exe"
    }
}

# PATH'e ekle
$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
if ($machinePath -notlike "*C:\nssm*") {
    [Environment]::SetEnvironmentVariable('Path', "$machinePath;C:\nssm", 'Machine')
    Write-OK "C:\nssm PATH'e eklendi (yeni shell'de aktif)"
}

# ------------------------------------------------------------
# 10) Firewall kurallari
# ------------------------------------------------------------
Write-Step "Firewall kurallari"
$rules = @(
    @{ Name='Orchestra-Backend-HTTP';  Port=$BACKEND_PORT  },
    @{ Name='Orchestra-Backend-HTTPS'; Port=$BACKEND_HTTPS },
    @{ Name='Orchestra-HTTP';          Port=80             },
    @{ Name='Orchestra-HTTPS';         Port=443            }
)
foreach ($r in $rules) {
    if (-not (Get-NetFirewallRule -DisplayName $r.Name -ErrorAction SilentlyContinue)) {
        try {
            New-NetFirewallRule -DisplayName $r.Name -Direction Inbound -Action Allow `
                -Protocol TCP -LocalPort $r.Port -ErrorAction Stop | Out-Null
            Write-OK "Firewall: $($r.Name) port $($r.Port)"
        } catch {
            Write-Err2 "Firewall kurali eklenemedi ($($r.Name)): $_"
        }
    } else {
        Write-OK "Firewall: $($r.Name) zaten var"
    }
}

# ------------------------------------------------------------
# 11) WinRM
# ------------------------------------------------------------
Write-Step "WinRM"
try {
    Enable-PSRemoting -Force -SkipNetworkProfileCheck -ErrorAction Stop | Out-Null
    Write-OK "WinRM aktif"
} catch {
    Write-Warn2 "WinRM: $_"
}

# ------------------------------------------------------------
# 12) Ozet
# ------------------------------------------------------------
Write-Step "OZET"
Write-Host "Kurulu kontrol:" -ForegroundColor White
$checks = @(
    @{ Exe='dotnet'; Args=@('--version');       Name='.NET SDK'     },
    @{ Exe='dotnet'; Args=@('--list-runtimes'); Name='.NET runtimes'},
    @{ Exe='node';   Args=@('--version');       Name='Node.js'      },
    @{ Exe='npm';    Args=@('--version');       Name='npm'          },
    @{ Exe='git';    Args=@('--version');       Name='Git'          },
    @{ Exe='docker'; Args=@('--version');       Name='Docker'       }
)
foreach ($c in $checks) {
    if (Get-Command $c.Exe -ErrorAction SilentlyContinue) {
        try {
            $out = & $c.Exe @($c.Args) 2>&1 | Out-String
            $first = ($out.Trim() -split "`r?`n" | Select-Object -First 1)
            Write-Host ("  {0,-18} {1}" -f $c.Name, $first) -ForegroundColor Green
        } catch {
            Write-Host ("  {0,-18} (HATA)" -f $c.Name) -ForegroundColor Yellow
        }
    } else {
        Write-Host ("  {0,-18} (yok -- yeni shell ac, PATH guncellenmis olabilir)" -f $c.Name) -ForegroundColor Red
    }
}

Write-Step "MANUEL ADIMLAR"
Write-Host @"
1) REBOOT (Hosting Bundle + Docker icin sart).
2) PostgreSQL manuel kur (yukaridaki link).
3) Docker Desktop'u ilk kez ac, WSL2 onayla.
4) Backend publish + frontend build deploy (ayri talimat).
5) NSSM ile servis kur (ayri talimat).
"@ -ForegroundColor Yellow

}
catch {
    Write-Host "`n[FATAL] $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
}
finally {
    Write-Host "`n==== Script bitti. Cikmak icin Enter'a bas ====" -ForegroundColor Cyan
    Read-Host | Out-Null
}
