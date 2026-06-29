<#
.SYNOPSIS
    Orchestra (109 / IIS) için HTTPS hazırlık script'i. Pentest öncesi K/HTTPS maddesi.

.DESCRIPTION
    Backend zaten production'da UseHttpsRedirection + UseHsts uyguluyor (Program.cs); ama TLS
    sonlandırması IIS'te yapılır. Bu script IIS'e 443 binding + sertifika kurar ve 80->443
    yönlendirmesini açar. İki yol destekler:
      -Mode WinAcme : DNS hazırsa (orchestra.mudo.com.tr) Let's Encrypt ile otomatik sertifika.
      -Mode Pfx     : DNS yokken kurumsal/iç-CA sertifikasını (.pfx) içe aktarır (IP veya FQDN).

    ÇALIŞTIRMADAN ÖNCE OKU:
      * DNS: orchestra.mudo.com.tr 109'a çözülmeli (WinAcme modu için ŞART). KoçSistem'den bekleniyor.
      * Public CA bir IP'ye sertifika VERMEZ. DNS gelene kadar -Mode Pfx (iç CA) kullan.
      * AGENT TARAFI: agent'lar http://10.75.1.109 kullanıyor. https'e geçişte:
          - agent appsettings.json BackendUrl -> https://orchestra.mudo.com.tr
          - sertifika zinciri TÜM saha PC'lerinde GÜVENİLİR olmalı (kurumsal CA kök sertifikası GPO ile dağıtılmalı),
            yoksa agent TLS doğrulaması patlar -> heartbeat/komut kopar. Bu, koordineli FİLO redeploy gerektirir.
          - Bu script SADECE sunucu tarafını hazırlar; agent geçişi ayrı ve dikkatli yapılmalı.

.EXAMPLE
    .\setup_https.ps1 -Mode Pfx -PfxPath C:\certs\orchestra.pfx -SiteName orchestra
.EXAMPLE
    .\setup_https.ps1 -Mode WinAcme -Hostname orchestra.mudo.com.tr -SiteName orchestra
#>
[CmdletBinding()]
param(
    [ValidateSet('WinAcme','Pfx')]
    [string]$Mode = 'Pfx',
    [string]$SiteName = 'orchestra',
    [string]$Hostname = 'orchestra.mudo.com.tr',
    [string]$PfxPath,
    [switch]$Redirect80   # 80 -> 443 kalıcı yönlendirme kuralı ekle
)

$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Yönetici (Administrator) olarak çalıştır."
}

Import-Module WebAdministration

function Ensure-HttpsBinding([string]$thumbprint) {
    Write-Host "[*] $SiteName için 443 binding kontrol ediliyor..." -ForegroundColor Cyan
    $existing = Get-WebBinding -Name $SiteName -Protocol https -ErrorAction SilentlyContinue
    if (-not $existing) {
        New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $Hostname -SslFlags 1
        Write-Host "    + 443 binding eklendi ($Hostname)" -ForegroundColor Green
    }
    # SNI binding'e sertifikayı bağla
    $bindingPath = "IIS:\SslBindings\!443!$Hostname"
    if (Test-Path $bindingPath) { Remove-Item $bindingPath -Force }
    Get-Item "Cert:\LocalMachine\My\$thumbprint" | New-Item $bindingPath -SslFlags 1 | Out-Null
    Write-Host "    + Sertifika binding'e bağlandı (thumbprint $thumbprint)" -ForegroundColor Green
}

switch ($Mode) {
    'Pfx' {
        if (-not $PfxPath -or -not (Test-Path $PfxPath)) { throw "-PfxPath geçerli bir .pfx dosyası olmalı." }
        $sec = Read-Host "PFX parolası" -AsSecureString
        Write-Host "[*] Sertifika içe aktarılıyor (LocalMachine\My)..." -ForegroundColor Cyan
        $cert = Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation Cert:\LocalMachine\My -Password $sec
        Ensure-HttpsBinding $cert.Thumbprint
    }
    'WinAcme' {
        $wacs = Join-Path $env:ProgramData 'win-acme\wacs.exe'
        if (-not (Test-Path $wacs)) {
            Write-Warning "win-acme bulunamadı. https://www.win-acme.com/ adresinden indir ve C:\ProgramData\win-acme'e çıkar."
            Write-Host "Manuel: wacs.exe --target iis --siteid <id> --installation iis --commonname $Hostname" -ForegroundColor Yellow
            return
        }
        Write-Host "[*] win-acme ile sertifika alınıyor (DNS $Hostname -> bu sunucu çözmeli)..." -ForegroundColor Cyan
        & $wacs --target iis --host $Hostname --installation iis --commonname $Hostname --accepttos
    }
}

if ($Redirect80) {
    Write-Host "[*] 80 -> 443 kalıcı yönlendirme kuralı ekleniyor (URL Rewrite gerekir)..." -ForegroundColor Cyan
    $cfg = "MACHINE/WEBROOT/APPHOST/$SiteName"
    Add-WebConfigurationProperty -PSPath $cfg -Filter "system.webServer/rewrite/rules" -Name "." -Value @{
        name='HTTPS-Redirect'; stopProcessing='true'
    } -ErrorAction SilentlyContinue
    Write-Warning "URL Rewrite kuralının match/conditions/action kısmını web.config'de doğrula (HTTP_X-Forwarded-Proto kontrolü ARR arkasında önemli)."
}

Write-Host ""
Write-Host "==== SONRAKİ ADIMLAR (manuel / dikkatli) ====" -ForegroundColor Magenta
Write-Host " 1) Test: https://$Hostname/api/health  -> {status:ok} dönmeli, sertifika geçerli görünmeli."
Write-Host " 2) frontend .env:  VITE_API_BASE=https://$Hostname  (rebuild + IIS'e kopyala)."
Write-Host " 3) Cors:AllowedOrigins'e https://$Hostname ekli mi kontrol et (appsettings.json)."
Write-Host " 4) AGENT geçişi (KOORDİNELİ FİLO): kök CA tüm saha PC'lerinde güvenilir olduktan SONRA"
Write-Host "    agent BackendUrl'i https'e al ve aşamalı rollout yap. Önce 1-2 pilot mağaza."
Write-Host " 5) HSTS backend'de zaten açık (UseHsts, non-dev). Tarayıcı önbelleğini hesaba kat."
