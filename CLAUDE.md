# Orchestra - Claude Code Instructions

## Project Overview
Orchestra (eski adı: MudoSoft RMM) — kurumsal RMM platformu (mağaza, merkez, sunucu, network cihazları). Detaylar memory'de.

## Production Sunucu (109) — Migration Tamamlandı 2026-05-25
- **Eski:** 10.0.210.99 — hâlâ aktif, agent'lar buraya heartbeat atıyor (rollout bekliyor)
- **Yeni:** 10.75.1.109 (Windows Server 2019 Standard) — DB taşındı, backend+frontend çalışır durumda
- Migration kullanıcı tarafından manuel yapıldı; `bootstrap-109.ps1` repo köke konuldu (sadece referans, bir kez kullanıldı)

### 109'da kurulu yapı
```
C:\projects\orchestra\
├── repo\          ← git clone (https://github.com/siriusblack34/Mudosoft.git)
├── backend\       ← dotnet publish ciktisi (NSSM ile servis)
├── frontend\      ← (kullanilmiyor, dist C:\inetpub\orchestra'ya kopyalanir)
└── logs\          ← backend.out.log + backend.err.log
C:\inetpub\orchestra\    ← IIS site root (frontend dist + web.config)
C:\inetpub\icmimarlar\   ← arkadasin Next.js projesi icin reverse proxy site
C:\xampp\                ← arkadasin XAMPP (Apache 127.0.0.1:8080'e kilitlendi)
C:\nssm\nssm.exe         ← Windows service manager
```

### Servisler
- `OrchestraBackend` (NSSM Windows service) — `C:\projects\orchestra\backend\MudoSoft.Backend.exe`, `MUDODMN\mudoadmtd` altında çalışır, http://localhost:5000
- `PostgreSQL 18` — DB adı **`orchestra`** (eskiden mudosoft idi, kod `appsettings.json`'da değişti). `postgres` superuser bağlanıyor, şifre `.env`'de `DB_PASSWORD`
- `IIS Sites` — orchestra (`*:80:orchestra.mudo.com.tr` + `*:80:` IP fallback), icmimarlar (`*:80:icmimarlar.mudo.com.tr` → reverse proxy localhost:8080)
- IIS ARR proxy enabled, URL Rewrite + ARR modules kurulu
- Apache (XAMPP): sadece `127.0.0.1:8080`, SSL kapalı, dış dünyaya kapalı (güvenlik için)

### IIS web.config (Orchestra, C:\inetpub\orchestra\web.config)
- `/api`, `/hubs`, `/swagger` → `http://localhost:5000/{R:0}` proxy
- SPA fallback → `/index.html`
- WebSocket enabled (SignalR için)

### Frontend `.env` (C:\projects\orchestra\repo\frontend\.env)
- `VITE_API_BASE=http://10.75.1.109` (DNS gelene kadar IP, sonra orchestra.mudo.com.tr olacak)
- Boş string yazma — `||` fallback bug'ı 5102'ye düşürür

### Backend `.env` (C:\projects\orchestra\repo\backend\.env)
- Manuel kopyalandı, git'te yok
- `DB_PASSWORD`, `JWT_SECRET_KEY`, `ADMIN_USERNAME/PASSWORD`, `AGENT_API_KEY`, `GENIUS_DB_USER/PASSWORD`, `WMI_PASSWORD`, `RDP_PASSWORD`, `LDAP_BIND_USER/PASSWORD`
- `RDP_PASSWORD=${RDP_PASSWORD}` placeholder bug'ı düzeltildi (gerçek değer yazılı)

### DB connection string (appsettings.json)
`Host=localhost;Port=5432;Database=orchestra;Username=postgres;Password=${DB_PASSWORD};Pooling=true;`

### Bilinen sorunlar / yapılacaklar
- Agent rollout: 60 sahanın config'i hâlâ 99'a heartbeat atıyor — `deploy_agent.ps1` veya UpdateController üzerinden 109 URL'iyle push gerekli
- DNS: `orchestra.mudo.com.tr` KoçSistem'den istenecek (`icmimarlar.mudo.com.tr` zaten 109'a yönlendiriliyor)
- SSL: HTTPS yok, agent'lar HTTP üzerinden konuşacak (kurumsal CA veya win-acme sonra)
- Docker/Guacamole: Server 2019 Docker Desktop desteklemiyor, WSL2 yolu denenmedi — Guacamole feature şu an çalışmıyor (VNC üzerinden RDP çalışmaya devam ediyor)

### Migration sonrası kontrol komutları
```powershell
# DB row sayilari
$env:PGPASSWORD = '<postgres-sifresi>'
& "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -h localhost -d orchestra -c "SELECT count(*) FROM \"Devices\";"

# Backend servis durumu
Get-Service OrchestraBackend
Get-Content C:\projects\orchestra\logs\backend.out.log -Tail 30

# IIS site'lar
Import-Module WebAdministration; Get-Website

# Apache 8080 sadece localhost
Invoke-WebRequest http://localhost:8080 -UseBasicParsing
```

## Tech Stack
- **Backend:** ASP.NET Core 8, PostgreSQL, SignalR — `backend/`
- **Frontend:** React 18, TypeScript, Vite, Tailwind CSS — `frontend/`
- **Agent:** .NET 8 Windows Service — `agent/`
- **Shared DTOs:** Class Library — `shared/`
- **RD Helper:** Windows app for WebRTC streaming — `helper/`
- **Solution:** `orchestra.sln` (eski adı: `mudosoft.sln`)

## Build & Run Commands
```bash
# Backend
cd backend && dotnet build
cd backend && dotnet run

# Frontend
cd frontend && npm install
cd frontend && npm run dev

# Agent
cd agent && dotnet build

# Full solution
dotnet build orchestra.sln
```

## Key Conventions
- Language: Turkish comments and UI text are acceptable
- Auth: JWT tokens (8h user, 30d agent). Use `[Authorize]` on all endpoints except agent download.
- SignalR hubs: DashboardHub, RemoteDesktopHub
- Encryption: AES-256 + RSA key exchange for payloads
- DB access: Direct DbContext in controllers (repository pattern only for DeviceRepository)
- API client: `frontend/src/lib/apiClient.ts` — includes JWT header, 30s timeout, 401 redirect

## İki Cihaz Tablosu (önemli — karıştırma)
- **Devices** — agent çalıştıran cihazlar. PK string hex hash. AgentService.HandleHeartbeatAsync besler.
- **StoreDevices** — tüm envanter (PC + Kasa-1/2/3 + ROUTER), agent olsun olmasın. PK "{store}-{slot}" (örn. "5-K1"). DeviceType: "PC", "Kasa-1/2/3", "ROUTER", "GECICI".
- İki tablo IP üzerinden eşleşir (StoreDevice.CalculatedIpAddress = Device.IpAddress).
- `DevicesController` → Devices, `SqlQueryController` → StoreDevices.

## Background Services (Program.cs'de register'lı)
- `HeartbeatCheckerWorker`, `NetworkOutageAlarmWorker`, `CriticalServiceMonitorWorker`
- `SchedulerBackgroundService` + `ScheduledTaskSeeder` — kullanıcı task'ları
- `DeviceStatusWorker` — dashboard cache
- `RouterLatencyPurgeWorker`
- `SerialNumberSyncService` — ayda 1 wmic /node: ile BIOS seri (Devices + StoreDevices)
- `PrinterSerialSyncService` — ayda 1 GENIUS DB'den OKC yazıcı sicil (StoreDevices)
- `UserInstallWatcherService` (Windows-only)

## Endpoint Aileleri
- `/api/agent/*` — agent heartbeat, command queue, file transfer
- `/api/devices/*` — agent'lı cihazlar (Devices tablosu)
- `/api/sqlquery/devices/{id}/*` — kasa/PC envanter (StoreDevices), system-info, manual-info partial update, printer-serial refresh
- `/api/rdp/*` — VNC proxy, oturum logları
- `/api/inventory/*`, `/api/activity-log/*`, `/api/store-openings/*`, `/api/ad-directory/*`, `/api/batch-execution/*`, `/api/service-monitor/*` — modül endpoint'leri

## Rules
- Do NOT commit `.env` files or secrets
- Stop backend process before rebuilding (DLL lock issue)
- Keep `[AllowAnonymous]` only on agent download endpoints (latest, download, updater-cmd)
- Frontend API calls must go through `apiClient.ts`, not raw fetch/axios
- Avoid adding unnecessary abstractions — keep it simple
- Do not create README or documentation files unless explicitly asked
- EF migration eklerken: `Database.Migrate()` startup'ta otomatik çalışır ama bazen `__EFMigrationsHistory`'ye yazmadan kolonu eklediği görüldü. Yeni migration eklerken manuel doğrula (psql).
- Saha PC'lerinde WSMan (port 5985) **kapalı**, RPC/DCOM (port 135) **açık** — remote WMI için `wmic /node:` veya `Get-CimInstance -Protocol DCOM` kullan, `Get-CimInstance -ComputerName` (varsayılan WSMan) çalışmaz.
