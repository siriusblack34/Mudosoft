# Faz 2 — Ajan Kanalı Sertleştirme & Rollout Tasarımı

> Durum: TASARIM (2026-06-26). Henüz kod yok. Pentest hazırlık öz-denetiminin Faz 2'si.
> Faz 1 (backend-only, filo kırmadan) tamamlandı; bu belge filo-redeploy gerektiren ağır işi tarif eder.
> İlgili: pentest denetim bulguları K-2, K-3, K-4, K-5, Y-5 + HTTPS.

## 0. Çözülen tehditler

| Kod | Bulgu | Faz 2 çözümü |
|-----|-------|--------------|
| **K-2** | Ajan komut kanalı imzasız + HTTP → MITM/sahte backend = filo çapında SYSTEM RCE | Komut imzalama (backend imzalar, ajan doğrular) + cihaz kimliği + HTTPS |
| **K-3** | Self-update imzasız ZIP'i saldırgan-kontrollü URL'den çalıştırıyor | İmzalı update manifest + SHA-256 + Authenticode + pinli URL |
| **K-4** | Şifreleme katmanı opt-in/bypass, AES-CBC MAC opsiyonel | HTTPS zorunlu → ev-yapımı katmanı emekliye ayır; imzalama uçtan-uca bütünlük sağlar |
| **K-5** | Tek paylaşımlı AGENT_API_KEY → herhangi DeviceId için 30g token | Cihaz-başına anahtar çifti (enrollment) → paylaşımlı anahtar emekli |
| **Y-5** | Hub/ws-vnc cihaz-başına yetki yok | Hub'da ajan kimliği + operatör→cihaz scope kontrolü |

**Temel ilke:** Komut imzalama HTTP üzerinde bile sahteciliği engeller (backend özel anahtarı olmadan komut üretilemez). HTTPS gizlilik + sunucu kimliği ekler. İkisi birlikte = savunma derinliği.

---

## 1. Hedef mimari

### 1.1 Cihaz-başına kimlik (K-5)
- Ajan **ilk açılışta** cihaza özel bir anahtar çifti üretir (Ed25519 ya da RSA-2048).
- Özel anahtar cihazda **DPAPI (LocalMachine)** ile korunan bir dosyada saklanır (RsaKeyProvider'daki desenin aynısı).
- `DeviceId` = bugünkü gibi WMI UUID + BIOS seri SHA-256 (değişmez).

### 1.2 Enrollment (kayıt)
```
Ajan (ilk açılış, HTTPS):
  POST /api/agent/enroll
  Body: { deviceId, devicePublicKey, bootstrapApiKey }   // bootstrapApiKey = mevcut AGENT_API_KEY (TEK SEFERLİK)
Backend:
  - bootstrapApiKey doğrula (sabit-zaman)
  - deviceId için devicePublicKey'i DB'ye yaz (ilk kayıt = TOFU; sonraki değişiklik için admin onayı)
  - backend imzalama public key'ini + enrollment teyidini döndür
Ajan:
  - backend public key'i PİNLE (sonraki tüm komut doğrulamaları bununla)
```
Enrollment'tan sonra cihaz, isteklerini kendi özel anahtarıyla **imzalar**; paylaşımlı anahtar artık yeterli değildir.

### 1.3 İmzalı komut protokolü (K-2)
Komut DTO'su additive alanlarla genişler (eski ajanlar bilinmeyen alanları yok sayar → geriye uyumlu):
```jsonc
{
  "id": "guid",
  "deviceId": "abc123",          // hedef cihaz — ajan kendiyle eşleşmeli
  "type": "ExecuteScript",
  "payload": "...",
  "issuedAt": "2026-06-26T10:00:00Z",
  "exp":      "2026-06-26T10:05:00Z",   // kısa pencere
  "seq": 4821,                    // cihaz-başına monotonik artan
  "sig": "base64(Sign(backendPrivKey, canonical(id|deviceId|type|payload|issuedAt|exp|seq)))"
}
```
**Ajan doğrulaması (her komutta, sırayla):**
1. `sig`'i pinli backend public key ile doğrula → geçmezse **DÜŞÜR + logla**.
2. `deviceId == self` değilse düşür.
3. `exp` geçmişse / `issuedAt` gelecekte (saat kayması toleransı ±60sn) düşür.
4. `seq <= sonGörülenSeq` ise **replay** → düşür. Geçerse `sonGörülenSeq = seq` (DPAPI dosyasında kalıcı).
5. Hepsi geçerse çalıştır.

**Komut sonucu (ajan→backend):** ajan sonucu kendi özel anahtarıyla imzalar; backend cihaz public key'iyle doğrular → sonuç sahteciliği (anonim `command-result` POST) kapanır.

### 1.4 İmzalı update (K-3)
```jsonc
// GET /api/updates/manifest  → backend imzalı döndürür
{
  "version": "2.4.1",
  "sha256":  "hex...",
  "url":     "https://orchestra.mudo.com.tr/api/updates/download/2.4.1",  // SABİT/pinli host
  "sig":     "base64(Sign(backendPrivKey, version|sha256|url))"
}
```
**Ajan:** manifest imzasını pinli key ile doğrula → ZIP indir → **SHA-256 manifest ile eşleşmeli** → (opsiyonel) EXE Authenticode doğrula → uygula. `backendUrl`'i komut payload'undan ALMA; pinli host allowlist'i kullan. Bu, K-3'teki "saldırgan URL → SYSTEM" zincirini kırar.

### 1.5 HTTPS (K-4)
- IIS'te 443 + kurumsal/iç-CA sertifikası ([scripts/setup_https.ps1](../scripts/setup_https.ps1) hazır).
- **Kök CA tüm saha PC'lerinde GPO ile güvenilir olmalı** (yoksa ajan TLS doğrulaması patlar).
- Ajan `BackendUrl` → `https://...`; ajan sertifika zincirini **doğrular** (asla `ServerCertificateValidationCallback => true` koyma).
- HTTPS yerleşince opt-in AES middleware'i **kaldır** (TLS onu geçersiz kılar, hatalıydı). İmzalama kalır.

### 1.6 Hub & /ws/vnc yetkisi (Y-5)
- Ajan SignalR hub'larına kendi cihaz token'ı / imzasıyla bağlanır → `RemoteDesktopHub` + `DashboardHub` artık `[Authorize]` olabilir (Faz 1'deki [AllowAnonymous] geçici çözümü kaldırılır).
- Operatör→cihaz **scope kontrolü**: kullanıcının eriştiği cihaz, yetki/atama tablosuna göre doğrulanır (şu an herhangi operatör herhangi cihaza erişebiliyor).
- `consent-response` artık merkez ajan kimliğiyle gelir → anonim onay-onaylama kapanır.

---

## 2. Değişiklik listesi

**Backend:**
- `POST /api/agent/enroll` (yeni) + `DeviceCredential` tablosu (deviceId, publicKey, enrolledAt, lastSeq).
- Komut kuyruğuna imzalama: enqueue sırasında `sig/seq/exp` üret (yeni `CommandSigner` servisi, backend signing keypair).
- `GET /api/updates/manifest` (yeni, imzalı). `download/{version}` SHA-256 ile tutarlı.
- Cihaz-imza doğrulama middleware'i `/api/agent/*` için (enforce aşamasında [AllowAnonymous] yerine).
- Hub'lara `[Authorize]` + scope kontrolü; legacy issuer "MudoSoft" kaldırma (tüm ajanlar yeni token aldıktan sonra).
- Opt-in AES middleware kaldırma (HTTPS sonrası).

**Ajan:**
- Cihaz anahtar çifti üretimi + DPAPI saklama + enrollment çağrısı.
- Backend public key pinleme + komut imza/seq/exp doğrulama (CommandPoller).
- Sonuç imzalama (command-result).
- Update manifest imza + SHA-256 doğrulama (AgentUpdateCheckerService + CommandExecutor.ExecuteAgentUpdate); pinli URL.
- HTTPS BackendUrl + sertifika doğrulama.
- Hub bağlantısına cihaz token'ı.

---

## 3. Geriye-uyumlu rollout (tüm saha filosu — kırmadan)

> Kural: hiçbir aşama mevcut filoyu bricklemez. Backend **önce çift-mod** olur, ajanlar dalga dalga geçer, en sonda **enforce** edilir.

### Aşama 0 — Backend çift-mod (ÖNCE deploy, ajan değişikliği YOK)
- Backend tüm komutları **imzalamaya başlar** + `/enroll`, `/manifest` endpoint'lerini ekler.
- Ama imzalı/enrolled ajanı **ZORUNLU TUTMAZ**. Eski ajanlar yeni alanları yok sayar, çalışmaya devam eder.
- ✅ Risk: yok (additive). Rollback: backend'i geri al.

### Aşama 1 — Pilot (1-2 mağaza, ~1-2 hafta)
- Yeni ajan build'i: enroll + komut-imza doğrulama + imzalı update + HTTPS + key pinleme.
- **Pilot seçimi:** bir geçici/düşük-riskli PC + bir küçük mağaza (örn. bir GECICI cihaz + tek-kasalı bir mağaza). Canlı kritik mağaza (yoğun POS) SEÇME.
- İzlenecek: heartbeat sürekliliği, komut çalıştırma, **uzak masaüstü oturumu**, **bir update döngüsü**, enrollment dashboard'da görünüyor mu.
- ✅ Rollback: pilot ajanı eski sürüme döndür (tek mağaza).

### Aşama 2 — Dalga dalga filo (küçük gruplar halinde)
- `deploy_agent.ps1` / `UpdateController` push ile dalgalar. Her dalga enroll olur.
- Enrollment yüzdesini izle (kaç cihaz kayıtlı / toplam). Sorunlu cihazları ayıkla.
- HTTPS geçişi bu aşamada (kök CA GPO ile dağıtıldıktan SONRA). Önce 1-2 pilot mağazada https doğrula.
- ✅ Rollback: dalga başına; backend hâlâ çift-mod, eski ajan çalışır.

### Aşama 3 — ENFORCE (≈%100 enrolled olunca)
- Backend artık **ZORUNLU TUTAR:**
  - `/api/agent/*` cihaz-imzası ister (blanket `[AllowAnonymous]` → cihaz-imza middleware).
  - Enrolled olmayan / imzasız sonuç gönderen cihaz reddedilir.
  - Paylaşımlı `AGENT_API_KEY` emekli.
  - Hub'lara `[Authorize]` + scope; legacy "MudoSoft" issuer kaldırılır.
  - HTTPS-only (HTTP 80 → 443 redirect, ajan zaten https).
- ⚠️ Bu aşamadan önce enrollment %100 olmalı; eksik kalan cihaz **kesilir**. Enrollment dashboard'u şart.
- Rollback: enforce flag'ini kapat (çift-moda dön).

### Aşama 4 — Temizlik
- Opt-in AES middleware kaldır (HTTPS yerleşik).
- **Tüm sırları rotate et** (JWT/WMI/RDP/LDAP/DB — yandılar) + git geçmişini BFG/filter-repo ile temizle.
- MailKit vb. bağımlılık taraması.

---

## 4. Bağımlılık sırası
```
setup_https.ps1 + kök CA GPO dağıtımı   ─┐
Aşama 0 (backend çift-mod imzalama)       ├─→ Aşama 1 pilot ─→ Aşama 2 dalgalar ─→ Aşama 3 enforce ─→ Aşama 4 temizlik
yeni ajan build (enroll+verify+https)    ─┘
```
- Komut imzalama HTTPS'ten **bağımsız** ilerleyebilir (HTTP'de de sahteciliği durdurur) — istenirse imzalama önce, HTTPS sonra.
- HTTPS enforce'u, kök CA tüm PC'lerde güvenilir OLMADAN yapılMAZ.

## 5. Riskler & azaltma
| Risk | Azaltma |
|------|---------|
| Enforce öncesi eksik enrollment → cihaz kesilir | %100 enrollment dashboard'u; enforce'u manuel flag ile, kademeli |
| Kök CA dağıtılmadan https → ajan TLS patlar | Önce GPO doğrula; pilotta test; HTTPS'i enrollment'tan ayır |
| Cihaz özel anahtarı kaybı (disk/reimage) | Re-enroll akışı (admin onaylı); TOFU sonrası key değişimi admin onayı ister |
| Backend signing key sızması | Key'i DPAPI/HSM'de tut; rotasyon prosedürü + ajan re-pin akışı |
| Pilot sırasında uzak masaüstü kopar | Pilotu mesai dışı; eski hub yolu çift-modda açık kalır |

## 6. Tahmini etki
Faz 2 tamamlanınca açık kalan K-2/K-3/K-4/K-5/Y-5 kapanır; HTTPS + sır rotasyonu ile öz-değerlendirme **D+ → B+** bandına çıkar. Kalan artık-riskler (iş kararı bekleyen Y-1/2/3 SQL/komut yetki modeli) ayrı ele alınır.
