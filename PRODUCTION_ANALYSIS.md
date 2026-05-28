# Orchestra — Production Readiness Analysis

**Tarih:** 2026-05-28  
**Kapsam:** Backend (.NET 8), Agent (Windows Service), Frontend (React 18 + Vite)

---

## 1. KRİTİK BUGLAR (Hemen Düzeltilmeli)

### 1.1 CollectorOrchestrator Devre Dışı
**Dosya:** `agent/Services/CollectorOrchestrator.cs`  
Açıklama: Collector döngüsü "emergency hotfix" yorumuyla kapatılmış. GeniusPosCollector dahil hiçbir collector çalışmıyor.  
**Risk:** Agent heartbeat atıyor ama collector verisi gelmiyor. Dashboard metrikleri bayatlar.  
**Aksiyon:** Kök neden bulunup orchestrator yeniden etkinleştirilmeli.

### 1.2 Çift Lock File (npm + yarn)
**Dosya:** `frontend/package-lock.json` + `frontend/yarn.lock`  
Açıklama: İki lock dosyası CI ortamında farklı bağımlılık sürümlerine yol açabilir.  
**Aksiyon:** Biri silinmeli; tüm devlerin aynı paket yöneticisini kullanması zorunlu kılınmalı.

### 1.3 SHA-256 Hash Browser API Kısıtlaması
**Dosya:** `frontend/src/pages/FileManagerPage.tsx`  
Açıklama: `crypto.subtle.digest` HTTPS veya localhost olmayan HTTP bağlantılarda çalışmaz. Hash hesaplanamadığında upload başarısız olabilir.  
**Aksiyon:** HTTP ortamında hash doğrulamayı atla veya HTTPS zorunlu kıl.

### 1.4 ChunkedUpload State Sunucu Bellekte Tutuluyor
**Dosya:** `backend/Controllers/FilesController.cs`  
Açıklama: `ConcurrentDictionary<string, ChunkedUploadState>` uygulama restart'ta sıfırlanır; yük dengelemede (load balancer) farklı instance'a düşen chunk kaybolur.  
**Aksiyon:** Redis veya veritabanına taşı; ya da sticky session zorunlu kıl.

### 1.5 ReceivedChunks Thread Güvenliği Eksik
**Dosya:** `backend/Controllers/FilesController.cs:146`  
Açıklama: `state.ReceivedChunks.Count` lock dışı okunuyor; lock sadece Add'de var.  
**Aksiyon:** Count okumalarını da `lock(state.ReceivedChunks)` içine al.

### 1.6 VNC WebSocket Token URL'de Açık
**Dosya:** `frontend/src/pages/WebRdpPage.tsx:139`  
Açıklama: JWT token query string olarak WebSocket URL'ine ekleniyor. HTTP access log'larında görünür.  
**Aksiyon:** Kısa ömürlü (30s) one-time token endpoint'i ekle; token query string'den çıkar.

---

## 2. GÜVENLİK AÇIKLARI

### 2.1 SQL Query Endpoint — Serbest SQL Yürütme
**Dosya:** `backend/Controllers/SQLQueryPage` (veya SQLQueryController)  
Açıklama: `/api/sql-query` endpoint'i herhangi bir authenticate kullanıcıya keyfi SQL çalıştırma izni veriyor.  
**Aksiyon:** Sadece admin rolüne kısıtla, sadece SELECT'e izin ver, zaman aşımı ve satır limiti ekle.

### 2.2 Komut Enjeksiyonu Riski (Agent Komutları)
**Dosya:** `agent/Services/CommandExecutor.cs`  
Açıklama: Uzaktan gönderilen komutlar `cmd.exe /c` veya PowerShell ile çalıştırılıyorsa, parametreler yeterli sanitize edilmeden geçilebilir.  
**Aksiyon:** Whitelist-based komut doğrulaması; izin verilen komut listesi.

### 2.3 LDAP Kimlik Bilgileri Konfigürasyonda
**Dosya:** `backend/appsettings.json` veya environment  
Açıklama: AD bind kullanıcı adı/şifresi appsettings'te düz metin tutuluyorsa commit'e girebilir.  
**Aksiyon:** Azure Key Vault, AWS Secrets Manager veya en azından environment variable kullan.

### 2.4 Agent Token 30 Gün Geçerli
**Dosya:** `backend/Controllers/AuthController.cs`  
Açıklama: Agent JWT token'ları 30 gün geçerli. Ele geçirilirse 30 gün boyunca geçerli.  
**Aksiyon:** Agent token yenileme mekanizması ekle veya süreyi kısalt (7 gün).

### 2.5 RemoteDesktopHelper Flag Dosyası Güvenliği
**Dosya:** `agent/Services/RemoteDesktopService.cs`  
Açıklama: Helper canlılığı `%TEMP%` klasöründeki flag dosyasıyla ölçülüyor. Herhangi bir süreç bu dosyayı oluşturabilir.  
**Aksiyon:** Flag dosyasına process ID yaz, gerçek process'i doğrula.

---

## 3. MİMARİ BORÇ

### 3.1 Sıfır Test Kapsamı
Tüm projede hiç unit/integration test yok. Regresyon riski çok yüksek.  
**Öncelik:** Önce kritik servisleri kapsayan testler: AuthService, HealthScoreService, CollectorOrchestrator, FilesController.

### 3.2 Monolitik Backend
Tüm domain (auth, devices, collectors, files, AD, reports, VNC, SQL...) tek ASP.NET projesinde.  
**Öneri:** Kısa vadede modül bazında namespace/proje ayrımı; uzun vadede servis ayrımı (en azından agent komut servisi ayrılabilir).

### 3.3 In-Memory State
`FilesController._uploads`, `ActiveSessionsController` gibi birçok yer sunucu belleğinde state tutuyor. Uygulama restart'ta kaybolur.  
**Aksiyon:** IDistributedCache (Redis) veya veritabanı ile kalıcı hale getir.

### 3.4 SignalR Hub ve VNC Mimari Çakışması
Remote desktop için hem SignalR JPEG streaming (RemoteDesktopHub) hem react-vnc WebSocket proxy (VNC over WS) var. İkisi arasında net bir kullanım senaryosu ayrımı yok.  
**Aksiyon:** Hangisinin ne zaman kullanılacağını dokümante et; ikisi arasında frontend'de açık seçim mantığı ekle.

### 3.5 CollectorReport JSON Serbest Format
`CollectorReport.Data` JSON olarak düz metin tutuluyor. Şema yok, versiyon yok. Breaking change'ler sessizce veri bozabilir.  
**Aksiyon:** Her collector tipi için şema sürümü ekle, deserialization'da versiyon kontrolü yap.

---

## 4. PERFORMANS DARBOĞAZLARI

### 4.1 HealthScore Her İstekte Tüm Cihazları Yeniden Hesaplıyor
**Dosya:** `backend/Services/HealthScoreService.cs`  
Açıklama: `/api/health-score/summary` çağrısında tüm cihazlar için collector raporları DB'den çekilip hesaplanıyor.  
**Aksiyon:** Background service'te periyodik önhesaplama (örn: 5 dakikada bir); sonuçları cache'le.

### 4.2 N+1 Sorgu Riski
Birçok controller `foreach` içinde ayrı DB sorgusu yapıyor. EF Core lazy loading açıksa bu otomatik oluşur.  
**Aksiyon:** EF Core lazy loading'i kapat; Include() ile eager loading kullan; kritik endpoint'leri SQL Profiler ile izle.

### 4.3 Büyük JSON Yanıtları Kompresyonsuz
Cihaz listesi, collector raporları gibi büyük yanıtlarda response compression yok.  
**Aksiyon:** `builder.Services.AddResponseCompression()` + Brotli/Gzip middleware ekle.

### 4.4 Frontend Bundle Analizi Yapılmamış
Vite build'de chunk splitting ve tree-shaking ayarları varsayılan. İlk yükleme süresi optimize edilmemiş.  
**Aksiyon:** `vite-plugin-visualizer` ile bundle analizi yap; büyük sayfaları lazy-load et.

### 4.5 SQLite → PostgreSQL Eksik Migration Riski
`app.db` / `mudosoft.db` SQLite dosyaları var. Production'da PostgreSQL kullanılıyorsa SQLite dosyaları test kalıntısı mı? Yoksa hâlâ mi kullanılıyor?  
**Aksiyon:** Kullanım durumunu netleştir; tek veritabanına geç.

---

## 5. ÜRETİM ÖNCESİ KONTROL LİSTESİ

### Güvenlik
- [ ] Tüm endpoint'lerde `[Authorize]` veya `[AllowAnonymous]` açık; yanlış bırakılmış endpoint yok
- [ ] HTTPS zorunlu (`UseHttpsRedirection`, `HstsOptions`)
- [ ] Rate limiting production değerleriyle ayarlı
- [ ] CORS politikası sadece gerçek domain'lere izin veriyor (wildcard yok)
- [ ] Serbest SQL endpoint sadece admin rolünde
- [ ] Tüm gizli bilgiler (DB şifresi, LDAP, JWT secret) environment variable'da
- [ ] Agent JWT token süresi gözden geçirildi

### Kararlılık
- [ ] CollectorOrchestrator yeniden etkin ve test edildi
- [ ] In-memory state Redis'e taşındı (veya sticky session zorunlu kılındı)
- [ ] Chunk upload state kalıcı hale getirildi
- [ ] Helper flag dosyası process ID doğrulamasıyla güçlendirildi
- [ ] Exponential backoff limitleri production ortamında test edildi

### Gözlemlenebilirlik
- [ ] Structured logging (Serilog/NLog) yapılandırıldı; log seviyeleri doğru
- [ ] Health check endpoint (`/health`) eklendi
- [ ] Temel metrikler (request latency, error rate, DB connection count) Prometheus/Grafana veya benzeri araçla izleniyor
- [ ] Agent heartbeat'inin kaybolması için alarm kuruldu

### Deployment
- [ ] Dual lock file sorunu giderildi (npm veya yarn, ikisi birden değil)
- [ ] Frontend .env.production doğrulandı (API URL, vs.)
- [ ] Backend appsettings.Production.json doğrulandı
- [ ] Veritabanı migration'ları production'a uygulandı
- [ ] Rollback planı hazır (önceki sürüm çalışır halde bekliyor)

### Test
- [ ] En az smoke test seti mevcut (auth, device list, collector report)
- [ ] File upload (chunked) uçtan uca test edildi
- [ ] Remote desktop bağlantısı farklı ağ koşullarında test edildi
- [ ] HealthScore değerleri gerçek cihaz verileriyle doğrulandı

---

## 6. 30 GÜNLÜK YOL HARİTASI

### Hafta 1 — Kritik Düzeltmeler
| Öncelik | Görev | Süre |
|---------|-------|------|
| P0 | CollectorOrchestrator'ı yeniden etkinleştir ve test et | 1 gün |
| P0 | SQL Query endpoint'ini admin-only ve SELECT-only yap | 0.5 gün |
| P0 | ChunkedUpload state'i kalıcı hale getir (Redis veya DB) | 2 gün |
| P1 | VNC token URL'den çıkar, one-time token endpoint ekle | 1 gün |
| P1 | HTTPS zorunlu kıl, CORS düzelt | 0.5 gün |

### Hafta 2 — Kararlılık ve Gözlemlenebilirlik
| Öncelik | Görev | Süre |
|---------|-------|------|
| P1 | Structured logging (Serilog) ekle; agent heartbeat alarm | 1 gün |
| P1 | `/health` endpoint ekle; basit uptime monitörü kur | 0.5 gün |
| P1 | HealthScore arka plan önhesaplama servisi | 1.5 gün |
| P2 | Response compression middleware | 0.5 gün |
| P2 | EF Core N+1 sorgu audit + Include düzeltmeleri | 2 gün |

### Hafta 3 — Test Altyapısı
| Öncelik | Görev | Süre |
|---------|-------|------|
| P1 | xUnit + testcontainers ile backend smoke testleri | 3 gün |
| P1 | Frontend kritik sayfa render testleri (Vitest) | 2 gün |
| P2 | Agent collector unit testleri (GeniusPos, Disk, Network) | 2 gün |

### Hafta 4 — Deployment ve Olgunlaştırma
| Öncelik | Görev | Süre |
|---------|-------|------|
| P1 | CI/CD pipeline kur (GitHub Actions veya Azure Pipelines) | 2 gün |
| P1 | Docker compose üretim profili tamamla | 1 gün |
| P2 | Bundle analizi ve lazy loading iyileştirmeleri | 1 gün |
| P2 | API dokümantasyonu (Swagger/OpenAPI) üretim için gözden geçir | 1 gün |

---

## 7. ÖNCELİKLENDİRİLMİŞ GÖREV LİSTESİ

```
ACIL (Bu Hafta)
  [P0] CollectorOrchestrator'ı yeniden etkin kıl
  [P0] SQL endpoint admin-only + SELECT-only
  [P0] ChunkedUpload state → Redis/DB

ÖNEMLİ (2 Hafta İçinde)
  [P1] VNC WebSocket one-time token
  [P1] HTTPS + CORS düzeltmesi
  [P1] Structured logging + alarm
  [P1] HealthScore önbelleğe alma
  [P1] /health endpoint
  [P1] Backend smoke testleri

UZUN VADELİ (1 Ay)
  [P2] Response compression
  [P2] N+1 sorgu düzeltmeleri
  [P2] Frontend bundle optimizasyonu
  [P2] CI/CD pipeline
  [P2] Docker compose production profile
  [P2] Agent token yenileme
```

---

*Bu belge kod değişikliği içermez. Gözlem ve öneri niteliğindedir.*
