# Mudosoft

Mudo Bilgi Teknolojileri - Mudosoft RMM System

## Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/) (v18+)
- SQL Server (LocalDB veya Express)

## Kurulum

```bash
git clone https://github.com/siriusblack34/Mudosoft.git
cd Mudosoft
dotnet restore
cd frontend && npm install
```

## Çalıştırma

**Backend:**
```bash
cd backend
dotnet run
```

**Frontend:**
```bash
cd frontend
npm run dev
```

## Proje Yapısı

| Klasör | Açıklama |
|--------|----------|
| `backend/` | ASP.NET Core API sunucusu |
| `frontend/` | React + Vite web arayüzü |
| `agent/` | Windows cihazlarda çalışan RMM agent servisi |
| `helper/` | Uzak masaüstü yardımcı uygulaması |
| `tray/` | Agent tray uygulaması |
| `shared/` | Ortak model ve DTO'lar |
| `installer/` | Kurulum scriptleri |
