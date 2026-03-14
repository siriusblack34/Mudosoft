# MudoSoft - Claude Code Instructions

## Project Overview
RMM (Remote Management & Monitoring) system for retail POS environments.

## Tech Stack
- **Backend:** ASP.NET Core 8, PostgreSQL, SignalR — `backend/`
- **Frontend:** React 18, TypeScript, Vite, Tailwind CSS — `frontend/`
- **Agent:** .NET 8 Windows Service — `agent/`
- **Shared DTOs:** Class Library — `shared/`
- **RD Helper:** Windows app for WebRTC streaming — `helper/`
- **Solution:** `mudosoft.sln`

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
dotnet build mudosoft.sln
```

## Key Conventions
- Language: Turkish comments and UI text are acceptable
- Auth: JWT tokens (8h user, 30d agent). Use `[Authorize]` on all endpoints except agent download.
- SignalR hubs: DashboardHub, RemoteDesktopHub
- Encryption: AES-256 + RSA key exchange for payloads
- DB access: Direct DbContext in controllers (repository pattern only for DeviceRepository)
- API client: `frontend/src/lib/apiClient.ts` — includes JWT header, 30s timeout, 401 redirect

## Rules
- Do NOT commit `.env` files or secrets
- Stop backend process before rebuilding (DLL lock issue)
- Keep `[AllowAnonymous]` only on agent download endpoints (latest, download, updater-cmd)
- Frontend API calls must go through `apiClient.ts`, not raw fetch/axios
- Avoid adding unnecessary abstractions — keep it simple
- Do not create README or documentation files unless explicitly asked
