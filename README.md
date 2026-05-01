# Orchestra

**Mudo Bilgi Teknolojileri — Perakende POS Ortamları için Uzaktan Yönetim ve İzleme Sistemi**

Orchestra, 126 mağazada çalışan POS cihazlarını, sunucuları ve ağ ekipmanlarını merkezi olarak izlemek, yönetmek ve sorun gidermek için geliştirilmiş kurumsal bir RMM (Remote Management & Monitoring) sistemidir.

> **Not:** Sistemin önceki adı *MudoSoft RMM* idi. Sahadaki 60 cihazda kayıtlı teknik tanımlayıcılar (Windows servis ID'si `MudosoftAgentService`, kurulum klasörü `C:\Program Files\MudoSoft\Agent`, log path'leri vb.) geriye uyumluluk için olduğu gibi korunmaktadır — yalnızca marka ve kod tarafı yeniden adlandırılmıştır.

---

## İçindekiler

- [Sistem Mimarisi](#sistem-mimarisi)
- [Teknoloji Yığını](#teknoloji-yığını)
- [Bileşenler](#bileşenler)
  - [Backend](#backend-aspnet-core-8)
  - [Frontend](#frontend-react-18--typescript)
  - [Agent](#agent-net-8-windows-service)
  - [RD Helper](#rd-helper-webrtc-streaming)
  - [Tray Uygulaması](#tray-uygulaması)
  - [Shared Library](#shared-library)
- [Güvenlik](#güvenlik)
- [Gerçek Zamanlı İletişim](#gerçek-zamanlı-iletişim)
- [Şifreleme](#şifreleme)
- [Veritabanı](#veritabanı)
- [Kurulum & Çalıştırma](#kurulum--çalıştırma)

---

## Sistem Mimarisi

```
┌─────────────────────────────────────────────────────────────────────┐
│                            Orchestra                                │
│                                                                     │
│  ┌──────────────┐     HTTPS/WSS      ┌──────────────────────────┐  │
│  │   Frontend   │ ◄────────────────► │       Backend            │  │
│  │  React 18    │                    │  ASP.NET Core 8          │  │
│  │  TypeScript  │     SignalR        │  PostgreSQL              │  │
│  │  Vite/Twnd   │ ◄────────────────► │  SignalR Hubs            │  │
│  └──────────────┘                    └────────────┬─────────────┘  │
│                                                   │                 │
│                                           HTTPS + AES-256           │
│                                                   │                 │
│  ┌──────────────────────────────────────────────┐ │                 │
│  │              Mağaza (POS Ortamı)             │ │                 │
│  │                                              │ │                 │
│  │  ┌───────────────┐    ┌───────────────────┐  │ │                 │
│  │  │  Orchestra    │    │  RD Helper        │  │ │                 │
│  │  │  Agent        │    │  (WebRTC/VNC)     │  │ │                 │
│  │  │  .NET 8 Svc   │◄──►│  .NET 8 WinForms  │  ├─┘                 │
│  │  └───────┬───────┘    └───────────────────┘  │                  │
│  │          │                                    │                  │
│  │   ┌──────▼──────┐  ┌────────┐  ┌──────────┐  │                  │
│  │   │  PC/Server  │  │ Kasa-1 │  │ Router   │  │                  │
│  │   │  (POS Ana)  │  │ (POS)  │  │          │  │                  │
│  │   └─────────────┘  └────────┘  └──────────┘  │                  │
│  └──────────────────────────────────────────────┘                  │
└─────────────────────────────────────────────────────────────────────┘
```

### Veri Akışı

1. **Agent → Backend:** Her 15-45 saniyede bir heartbeat (CPU, RAM, disk, servis durumu)
2. **Backend → Frontend:** SignalR üzerinden gerçek zamanlı cihaz durum güncellemeleri
3. **Frontend → Backend → Agent:** Komut gönderimi (script çalıştırma, servis yönetimi, dosya transfer)
4. **Agent → RD Helper:** Uzak masaüstü oturumu için WebRTC peer bağlantısı

---

## Teknoloji Yığını

| Katman | Teknoloji | Versiyon |
|--------|-----------|----------|
| Backend Framework | ASP.NET Core | 8.0 |
| Veritabanı | PostgreSQL | 14+ |
| ORM | Entity Framework Core + Npgsql | 8.0 |
| Gerçek Zamanlı | SignalR | 8.0 |
| Kimlik Doğrulama | JWT Bearer | — |
| Şifreleme | AES-256 + RSA 2048 | — |
| Frontend Framework | React | 18.3 |
| Build Tool | Vite | 6.0 |
| Dil | TypeScript | 5.x |
| Stil | Tailwind CSS | 3.4 |
| Grafikler | Recharts | 3.4 |
| VNC İstemci | react-vnc + guacamole-common-js | — |
| WebRTC | SIPSorcery | 8.0 |
| Agent Runtime | .NET 8 Windows Service | 8.0 |
| Metrik Toplama | WMI + PerformanceCounter | — |
| VNC Proxy | Guacamole (Docker) | 1.5.5 |
| Parola Hash | BCrypt | 4.0 |
| Rate Limiting | AspNetCoreRateLimit | 5.0 |

---

## Bileşenler

### Backend (ASP.NET Core 8)

**Konum:** `backend/`

ASP.NET Core 8 ile yazılmış RESTful API. JWT kimlik doğrulama, SignalR hub'ları ve PostgreSQL veritabanı ile çalışır.

#### API Controller'ları (31 adet)

| Controller | Endpoint | Açıklama |
|-----------|----------|----------|
| `AuthController` | `/api/auth` | Giriş, JWT token üretimi, hesap kilitleme |
| `DashboardController` | `/api/dashboard` | Cihaz sağlık özeti, online/offline sayım |
| `DevicesController` | `/api/devices` | Cihaz listesi, online durumu, SQL erişim testi |
| `DeviceDetailsController` | `/api/device-details` | Cihaz metrikleri, donanım envanteri |
| `AgentController` | `/api/agent` | Agent indirme, versiyon yönetimi |
| `ActionsController` | `/api/actions` | Uzak komut yürütme geçmişi |
| `KasaLogController` | `/api/kasa-logs` | POS mali log analizi |
| `FilesController` | `/api/files` | Uzak dosya transferi |
| `ServicesController` | `/api/services` | Windows servis yönetimi |
| `RdpController` | `/api/rdp` | VNC/RDP oturum yönetimi |
| `DiskStatusController` | `/api/disk-status` | Disk kullanım izleme |
| `NetworkDiagnosticsController` | `/api/network-diagnostics` | Ağ kesinti tespiti |
| `RouterDiagnosticsController` | `/api/router-diagnostics` | Router gecikme, hat tipi sınıflandırma |
| `SqlQueryController` | `/api/sql-query` | Uzak POS veritabanında SQL sorgusu |
| `RemoteInstallController` | `/api/remote-install` | Uzaktan yazılım kurulum |
| `UsersController` | `/api/users` | Kullanıcı yönetimi |
| `ReportsController` | `/api/reports` | Sağlık, kesinti, arıza yoğunluğu raporları |
| `AlertsController` | `/api/alerts` | Cihaz uyarıları ve bildirimler |
| `StoreManagersController` | `/api/store-managers` | Mağaza personel yönetimi |
| `NotesController` | `/api/notes` | Paylaşılabilir cihaz notları |

#### Arka Plan Servisleri (5 Worker)

| Servis | Görev | Periyot |
|--------|-------|---------|
| `HeartbeatCheckerWorker` | Heartbeat izler, offline tespiti yapar, uyarı gönderir | 30 sn kontrol, 3 dk timeout |
| `DeviceStatusWorker` | ICMP/TCP ile cihaz online durumu taraması | 45 sn, 20 eşzamanlı |
| `NetworkOutageAlarmWorker` | Ağ kesinti tespiti, router flapping, kesinti e-postası | Periyodik |
| `SchedulerBackgroundService` | Zamanlanmış görev yürütmesi | Göreve göre |
| `RouterLatencyPurgeWorker` | Eski router gecikme örneklerini temizler | Periyodik |

#### Veri Modelleri (Ana Tablolar)

| Model | Açıklama |
|-------|----------|
| `Device` | Cihaz kaydı — online durumu, sağlık metrikleri, VNC ayarları |
| `DeviceMetric` | Zaman serisi — CPU, RAM, disk kullanımı (DeviceId + zaman damgası indeksli) |
| `StoreDevice` | POS cihaz envanteri — IP, SQL bağlantı dizisi |
| `User` | Kullanıcı hesapları — rol tabanlı erişim (Admin/User) |
| `RouterLatencySample` | Router ping geçmişi (zaman serisi) |
| `StoreNetworkInfo` | Mağaza ağ metadata — karasal Mbps, hat tipi |
| `CollectorReport` | Agent telemetri gönderimi |
| `VncSessionLog` | Uzak masaüstü oturum denetimi |
| `ActionRecord` | Komut yürütme geçmişi |
| `Alert` | Cihaz uyarıları ve bildirimleri |

#### Şifreli Payload Akışı

```
Agent ──[AES-256 şifreli]──► Middleware ──[Çözme]──► Controller
           │                      │
           │                 X-Encrypted: 1
           │                 X-ClientId: {deviceId}
           │
        AES anahtarı RSA 2048-bit ile şifreli (key exchange)
```

---

### Frontend (React 18 + TypeScript)

**Konum:** `frontend/`

Vite ile build edilen React 18 + TypeScript uygulaması. Tailwind CSS ile responsive arayüz.

#### Sayfa ve Ekranlar (41 sayfa)

**İzleme**
| Sayfa | Route | Açıklama |
|-------|-------|----------|
| Dashboard | `/` | Genel sağlık durumu, son kesintiler, cihaz özeti |
| Cihazlar | `/devices` | Filtrelenebilir cihaz listesi, online durumu |
| Cihaz Detay | `/devices/:id` | Metrikler, servisler, dosyalar, donanım bilgisi |
| Disk Durumu | `/disk-status` | Disk kullanım izleme |
| Ağ Diagnostik | `/network-diagnostics` | Kesinti tespiti ve analizi |
| Router | `/routers` | Router gecikme ve hat tipi analizi |

**Yönetim**
| Sayfa | Route | Açıklama |
|-------|-------|----------|
| Uzak Kurulum | `/remote-install` | Cihazlara yazılım/script dağıtımı |
| Servisler | `/devices/:id/services` | Windows servis kontrolü |
| Dosya Yönetici | `/devices/:id/files` | Uzak dosya yönetimi |
| SQL Sorgu | `/sql-query` | Uzak POS veritabanlarına SQL |
| Web RDP | `/rdp` | Web tabanlı uzak masaüstü (VNC) |
| Eylem Geçmişi | `/actions/history` | Komut yürütme geçmişi |

**Raporlar**
| Sayfa | Route | Açıklama |
|-------|-------|----------|
| Kasa Logları | `/kasa` | POS mali log analizi |
| Kesinti Raporu | `/reports/outage` | Mağaza bazlı kesinti raporları |
| Donanım Envanteri | `/reports/hardware` | Donanım envanter dışa aktarımı |
| Arıza Yoğunluğu | `/reports/fault-density` | Arıza yoğunluğu analizi |

**Organizasyon**
| Sayfa | Route | Açıklama |
|-------|-------|----------|
| Mağazalar | `/stores` | Mağaza rehberi ve yönetimi |
| Personel | `/staff` | Personel yönetimi |
| Gündem | `/agenda` | Mağaza takvimi/gündem |
| Kullanıcılar | `/team` | Sistem kullanıcı yönetimi |

#### Temel Paketler

```json
{
  "react": "18.3.1",
  "react-router-dom": "6.28.0",
  "@microsoft/signalr": "10.0.0",
  "recharts": "3.4.1",
  "guacamole-common-js": "1.5.0",
  "react-vnc": "3.2.0",
  "tailwindcss": "3.4.13",
  "lucide-react": "0.555.0",
  "date-fns": "4.1.0"
}
```

#### API İstemcisi (`src/lib/apiClient.ts`)

Tüm API çağrıları merkezi `apiClient.ts` üzerinden geçer:

- JWT token otomatik ekleme (`Authorization: Bearer`)
- 30 saniyelik istek zaman aşımı
- 401 hatası → otomatik login yönlendirmesi
- Şifreli payload desteği (`X-Encrypted` başlığı)
- Dosya yükleme helper fonksiyonları

#### Web RDP Özellikleri

- VNC tabanlı uzak masaüstü (noVNC / react-vnc)
- Oturum sayacı ve ping göstergesi
- Otomatik yeniden bağlanma (3 deneme)
- Ekran görüntüsü alma
- Klavye düzeni toggle (Win+Space)
- Kalite/sıkıştırma slider
- 15 dk boşta kalma kesme
- Dosya transfer paneli
- Çift monitör desteği
- Oturum geçmişi (kim, ne zaman, kaç dakika)

---

### Agent (.NET 8 Windows Service)

**Konum:** `agent/` | **Mevcut Versiyon:** 1.0.0.66

Mağazalardaki Windows cihazlara kurulan, arka planda Windows Service olarak çalışan izleme ve yönetim ajanı.

#### Temel Paketler

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.0" />
<PackageReference Include="System.Management" Version="8.0.0" />
<PackageReference Include="System.Diagnostics.PerformanceCounter" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" />
```

#### Veri Toplayıcılar (Collectors)

| Toplayıcı | Veri | Yöntem |
|-----------|------|--------|
| `DiskHealthCollector` | Disk alanı, SMART sağlık, bölüm durumu | WMI |
| `EventLogCollector` | Windows Event Log uyarı/hataları | EventLog API |
| `NetworkSpeedCollector` | Backend'e HTTP hız testi | HttpClient |
| `PortMonitorCollector` | Kritik port izleme (SQL:1433, MySQL:3306) | TCP probe |
| `ProcessUsageCollector` | En yüksek CPU/RAM kullanan süreçler | PerformanceCounter |
| `ServiceMonitorCollector` | Windows servis durumu (Genius3, MSSQL) | SCM API |
| `TemperatureCollector` | CPU/sistem sıcaklığı | WMI |
| `UpsStatusCollector` | UPS batarya durumu | WMI/USB |
| `UptimeReportCollector` | Sistem çalışma süresi | System.Environment |
| `WindowsUpdateCollector` | Bekleyen Windows güncellemeleri | WUA API |
| `ScheduledCleanupCollector` | Inbox/stok temizlik yürütmesi | Zamanlanmış |

#### Desteklenen Komutlar

| Komut | Açıklama |
|-------|----------|
| `ActionCommand` | PowerShell/batch script çalıştırma |
| `ServiceCommand` | Windows servis başlatma/durdurma |
| `FileWrite` | Uzak cihaza dosya yükleme (Base64) |
| `FileRead` | Uzak cihazdan dosya indirme (Base64) |
| `ScreenshotCommand` | Ekran görüntüsü alma |
| `ProcessCommand` | Süreç öldürme/listeleme |
| `UpdateCommand` | Kendi kendini güncelleme |

#### Agent İletişim Döngüsü

```
┌─────────────────────────────────────────────┐
│                 Agent Worker                 │
│                                             │
│  Her 15-45 sn:  Heartbeat gönder           │
│  Her 5-10 sn:   Komut kuyruğunu kontrol et │
│  Her toplama:   Metrik veri topla          │
│  Sürekli:       SignalR bağlantısı koru    │
└─────────────────────────────────────────────┘
          │
          │ HTTPS + AES-256
          ▼
     Backend API
```

#### Cihaz Kimliği

- MAC adresi + CPU seri numarasından türetilen kalıcı DeviceId
- Registry/config dosyasında saklanır
- Yeniden kurulum sonrası bile aynı kimlik korunur

---

### RD Helper (WebRTC Streaming)

**Konum:** `helper/` | **Mevcut Versiyon:** 1.0.0.31

Yüksek yetkiyle çalışan, WebRTC tabanlı uzak masaüstü streaming uygulaması.

#### Paketler

```xml
<PackageReference Include="SIPSorcery" Version="8.0.23" />
<PackageReference Include="SIPSorceryMedia.Encoders" Version="8.0.7" />
<PackageReference Include="SIPSorceryMedia.FFmpeg" Version="8.0.12" />
```

#### Çalışma Akışı

```
Frontend (Browser)
      │ WebRTC
      ▼
  Backend (SignalR - RemoteDesktopHub)
      │ VNC
      ▼
  Guacamole (Docker - guacd:4822)
      │ VNC Protocol
      ▼
  RD Helper (SIPSorcery WebRTC)
      │ Screen Capture + Input
      ▼
  Hedef Cihaz
```

---

### Tray Uygulaması

**Konum:** `tray/`

Windows sistem tepsisinde çalışan, agent durumunu gösteren WinForms uygulaması.

- Tepsi ikonu (her zaman görünür)
- Sağ tık bağlam menüsü
- Agent durum göstergesi
- Manuel güncelleme kontrolü
- Hızlı RDP/VNC başlatma

---

### Shared Library

**Konum:** `shared/`

Backend ve Agent arasında paylaşılan DTO ve modeller.

#### Temel DTO'lar

| DTO | Kullanım |
|-----|----------|
| `DeviceHeartbeatDto` | Agent → Backend: metrikler, uptime, servis durumu |
| `CollectorReportDto` | Agent → Backend: telemetri verileri |
| `CommandDto` | Backend → Agent: yürütülecek komut |
| `CommandResultDto` | Agent → Backend: komut sonucu |
| `DeviceDetailDto` | Tam cihaz donanım/yazılım envanteri |
| `DeviceMetricDto` | CPU/RAM/disk zaman serisi |
| `EncryptedPayloadDto` | RSA/AES şifreli mesaj sarmalayıcı |
| `InputEventDto` | Uzak masaüstü fare/klavye girdisi |

---

## Güvenlik

### JWT Kimlik Doğrulama

```
Kullanıcı token:  8 saat (HS256 imzalı)
Agent token:     30 gün  (HS256 imzalı)
Saat sapması:    0 (katı süre kontrolü)
```

### Hız Sınırlama (AspNetCoreRateLimit)

| Endpoint | Limit |
|----------|-------|
| Genel | 400 istek/dakika |
| Login/Auth | 10 istek/dakika |
| SQL sorgu | 400 istek/dakika |

### Hesap Kilitleme

- 5 başarısız giriş → 15 dakika kilitleme
- IP:kullanıcı adı bazlı takip
- ConcurrentDictionary ile bellek içi saklama

### Güvenlik Başlıkları

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
X-XSS-Protection: 1; mode=block
Referrer-Policy: strict-origin-when-cross-origin
```

### [AllowAnonymous] — Yalnızca

```
GET /api/agent/latest      ← Agent versiyon kontrolü
GET /api/agent/download    ← Agent binary indirme
GET /api/agent/updater-cmd ← Güncelleme komutu
```

**Diğer tüm endpoint'ler `[Authorize]` zorunludur.**

---

## Gerçek Zamanlı İletişim

### SignalR Hub'ları

**DashboardHub** (`/hubs/dashboard`)

```
Agent bağlantısı:  ?deviceId={id}  → "Agents" grubu
Admin bağlantısı:  JWT token       → "Admins" grubu

Yayınlanan olaylar:
  - DeviceStatusChanged
  - DeviceMetricUpdated
  - AlertTriggered
  - CommandResultReceived
```

**RemoteDesktopHub** (`/hubs/remote-desktop`)

```
WebRTC sinyal kanalı
VNC proxy koordinasyonu
Oturum yaşam döngüsü yönetimi
```

---

## Şifreleme

### Payload Şifreleme Akışı

```
1. Backend, RSA 2048-bit public key üretir ve yayınlar
2. Agent, rastgele AES-256 anahtarı üretir
3. Agent, AES anahtarını RSA public key ile şifreler
4. Agent, veriyi AES-256 ile şifreler
5. Paket:
   {
     "encryptedKey": "<RSA ile şifrelenmiş AES anahtarı>",
     "iv":           "<AES IV>",
     "ciphertext":  "<AES şifreli veri>"
   }
6. Header: X-Encrypted: 1
7. Backend Middleware çözer → Controller düz JSON alır
```

### Parola Güvenliği

- Kullanıcı parolaları: **BCrypt** (tuzlanmış, uyarlanabilir maliyet)
- VNC parolaları: Cihaz başına şifrelenmiş saklama

---

## Veritabanı

**PostgreSQL 14+** | **ORM:** Entity Framework Core 8 (Npgsql)

### Şema Özeti

```sql
-- Çekirdek tablolar
Devices              -- Cihaz kaydı (online, sağlık, VNC ayarları)
DeviceMetrics        -- Zaman serisi: CPU, RAM, disk
StoreDevices         -- POS cihaz envanteri (IP, SQL bağlantısı)
Users                -- Kullanıcılar (rol: Admin/User)
CollectorReports     -- Agent telemetri raporları

-- İzleme
RouterLatencySamples -- Router ping geçmişi
StoreNetworkInfo     -- Mağaza ağ metadata (Mbps, hat tipi)
DeviceStatusChanges  -- Cihaz durum geçiş geçmişi
StoreOfflineLogs     -- Çevrimdışı olay takibi

-- Komut & Eylem
CommandResultRecords -- Komut yürütme sonuçları
ActionRecords        -- Eylem geçmişi
ScheduledTasks       -- Windows zamanlanmış görevler

-- Denetim
LoginHistories       -- Kullanıcı giriş denetimi
VncSessionLogs       -- RDP/VNC oturum denetimi
Alerts               -- Cihaz uyarıları

-- Organizasyon
Notes                -- Paylaşılabilir cihaz notları
AgendaItems          -- Mağaza takvim öğeleri
StoreManagers        -- Mağaza personeli
AppSettings          -- Uygulama yapılandırma (anahtar-değer)
```

### Kritik İndeksler

```sql
-- Sık sorgulanan sütunlar için bileşik indeksler
CREATE INDEX idx_device_online_lastseen   ON "Devices" ("Online", "LastSeen");
CREATE INDEX idx_metric_device_time       ON "DeviceMetrics" ("DeviceId", "TimestampUtc");
CREATE INDEX idx_router_store_time        ON "RouterLatencySamples" ("StoreCode", "SampledAt");
CREATE INDEX idx_storedevice_storecode    ON "StoreDevices" ("StoreCode");
```

### Migrasyonlar

- 40+ migration dosyası (`backend/Migrations/`)
- Uygulama başlangıcında otomatik çalışır (Development modunda)
- Seed data: 126 mağaza, varsayılan admin kullanıcı, ağ yapılandırmaları

---

## Kurulum & Çalıştırma

### Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/) (v18+)
- [PostgreSQL](https://www.postgresql.org/download/) (v14+)
- [Docker](https://www.docker.com/) (VNC proxy için)

### 1. Depoyu Klonlayın

```bash
git clone https://github.com/siriusblack34/Mudosoft.git
cd Mudosoft
dotnet restore
cd frontend && npm install
```

> Repo klasör adı (`Mudosoft/`) ve `e:\Mudosoft\` yolu, sahadaki deploy script'leriyle uyum için korunmuştur — yalnızca solution/marka adı Orchestra olarak değişti.

### 2. Veritabanı Kurulumu

```sql
CREATE DATABASE mudosoft;
```

> **Not:** DB adı `mudosoft` olarak korunmuştur (connection string'lerde sahada-sabit tanımlayıcı).

### 3. Ortam Değişkenleri

`backend/.env` dosyasını oluşturun:

```env
DB_PASSWORD=postgres_sifreniz
JWT_SECRET_KEY=en_az_32_karakter_uzun_rastgele_key
ADMIN_USERNAME=admin
ADMIN_PASSWORD=admin_sifreniz
AGENT_API_KEY=rastgele_bir_api_key
```

### 4. Migration Uygulama

```bash
cd backend
dotnet ef database update
```

### 5. VNC Proxy (Docker)

```bash
docker-compose up -d
```

### 6. Çalıştırma

**Backend:**
```bash
cd backend
dotnet run
# → https://localhost:5001
```

**Frontend:**
```bash
cd frontend
npm run dev
# → http://localhost:5173
```

**Agent (Test):**
```bash
cd agent
dotnet run
```

**Agent (Windows Service Kurulumu):**
```bash
cd agent
dotnet publish -c Release -r win-x64 --self-contained

# Servis ID (MudosoftAgentService), install path ve EXE adı sahada-sabittir.
# DisplayName ise yeni marka ile "Orchestra Agent Service".
sc create "MudosoftAgentService" `
   binPath="C:\Program Files\MudoSoft\Agent\MudoSoft.Agent.exe" `
   DisplayName="Orchestra Agent Service"
sc start "MudosoftAgentService"
```

### Build Komutları

```bash
# Tüm çözüm
dotnet build orchestra.sln

# Frontend production build
cd frontend && npm run build

# Agent yayınlama (çıktı EXE adı: MudoSoft.Agent.exe — sahada-sabit korunur)
cd agent && dotnet publish -c Release -r win-x64 --self-contained
```

---

## Proje Yapısı

```
Mudosoft/
├── backend/                    # ASP.NET Core 8 API
│   ├── Controllers/            # 31 API controller
│   ├── Models/                 # Veritabanı entity'leri
│   ├── Services/               # İş mantığı servisleri
│   ├── Workers/                # 5 arka plan worker
│   ├── Middleware/             # Şifreli payload ara katmanı
│   ├── Crypto/                 # RSA/AES şifreleme
│   ├── Migrations/             # EF Core migrasyonları (40+)
│   └── Program.cs              # DI, middleware yapılandırma
│
├── frontend/                   # React 18 + TypeScript
│   └── src/
│       ├── pages/              # 41 sayfa bileşeni
│       ├── components/         # Yeniden kullanılabilir UI
│       ├── lib/apiClient.ts    # Merkezi API istemcisi
│       ├── contexts/           # Theme, Auth context
│       └── routes.tsx          # Route tanımları
│
├── agent/                      # .NET 8 Windows Service
│   └── Services/
│       ├── Collectors/         # 11 veri toplayıcı
│       ├── HeartbeatService.cs # Metrik gönderimi
│       └── CommandPoller.cs    # Komut alma
│
├── helper/                     # WebRTC streaming
│   └── Services/
│       └── WebRTCService.cs    # SIPSorcery WebRTC
│
├── tray/                       # Windows tray uygulaması
├── shared/                     # Ortak DTO'lar ve modeller
├── installer/                  # Kurulum scriptleri
├── docker-compose.yml          # Guacamole VNC proxy (orchestra-guacd)
└── orchestra.sln               # Visual Studio çözümü
```

### Namespace ve Çıktı Adlandırma

Rename operasyonu sırasında C# namespace'leri tamamen `Orchestra.*` olarak güncellendi, ancak sahadaki 60 cihazın bozulmaması için **assembly çıktı adları** csproj'larda manuel olarak korundu:

```xml
<!-- Orchestra.Agent.csproj -->
<PropertyGroup>
  <RootNamespace>Orchestra.Agent</RootNamespace>
  <AssemblyName>MudoSoft.Agent</AssemblyName>  <!-- Sahada-sabit -->
</PropertyGroup>
```

Bu sayede:
- **Kod tarafı:** `using Orchestra.Agent.Services;` ile yeni namespace
- **Çıktı tarafı:** `MudoSoft.Agent.exe` ile eski dosya adı (Windows servis binPath'i bu EXE'yi gösteriyor)

### JWT Geçiş Katmanı

Yeni token'lar Orchestra issuer/audience kullanır, ancak geçiş süresince **eski token'lar da kabul edilir** (60 cihaz yeni token alana dek):

```csharp
ValidIssuers   = ["Orchestra", "MudoSoft"]
ValidAudiences = ["OrchestraUsers", "OrchestraAgents",
                  "MudoSoftUsers",  "MudoSoftAgents"]
```

Tüm agent'lar (en geç 30 gün içinde, JWT yenilemesinde) yeni token'a geçtikten sonra eski liste temizlenebilir.

---

## Lisans

Telif hakkı © 2024-2026 Mudo Bilgi Teknolojileri. Tüm hakları saklıdır.
