# Mudosoft

Mudo Bilgi Teknolojileri - Mudosoft RMM System

## Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/) (v18+)
- [PostgreSQL](https://www.postgresql.org/download/) (v14+)

## Kurulum

```bash
git clone https://github.com/siriusblack34/Mudosoft.git
cd Mudosoft
dotnet restore
cd frontend && npm install
```

## Veritabanı Kurulumu

1. PostgreSQL kurduktan sonra boş bir veritabanı oluşturun:

```sql
CREATE DATABASE mudosoft;
```

2. `backend/` klasöründe `.env.example` dosyasını kopyalayıp `.env` olarak kaydedin ve bilgileri doldurun:

```env
DB_PASSWORD=postgres_sifreniz
JWT_SECRET_KEY=uzun_rastgele_bir_key
ADMIN_USERNAME=admin
ADMIN_PASSWORD=admin_sifreniz
AGENT_API_KEY=rastgele_bir_api_key
```

3. Migration'ları uygulayarak tabloları otomatik oluşturun:

```bash
cd backend
dotnet ef database update
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
