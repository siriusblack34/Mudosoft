# Orchestra - Claude Code Instructions

## Project Overview
Orchestra (eski adı: MudoSoft RMM) — kurumsal RMM platformu (mağaza, merkez, sunucu, network cihazları). Detaylar memory'de.

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
