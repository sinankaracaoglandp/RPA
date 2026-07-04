# RPA Platformu — Tasarım Spesifikasyonu v3

**Tarih:** 2026-07-04
**Durum:** Taslak — kullanıcı onayı bekliyor
**Kaynak:** `rpa-platform-spec-v2.md` üzerine, brainstorm oturumunda alınan kararlarla genişletilmiştir.

---

## 0. v2 → v3 Karar Özeti

| # | Karar | Etkilenen Bölüm |
|---|-------|-----------------|
| 1 | Hedef kitle: yazılımcılar + anahtar kullanıcılar → Basit/Gelişmiş mod, sihirbazlar, şablon galerisi | 8 |
| 2 | Robot dağıtımı: **Hibrit** (unattended VM havuzu + attended masaüstü) — v2'de tanımsızdı | 2 |
| 3 | Altyapı: **tamamen on-premise** — Azure AD → AD/LDAP, Azure Key Vault → HashiCorp Vault, Graph API → EWS/SMTP-IMAP | 3, 10, 12 |
| 4 | SAP programatik kanal: **doğrudan NCo** (MII/SAPBase kuralı bu platform için geçerli değildir; platform kendi bağlantı havuzunu yönetir) | 6 |
| 5 | Ölçek: **orta** (20-50 süreç, 5-10 robot, günde binlerce işlem) → kuyruk + robot havuzu + yük dağıtımı birinci sınıf tasarım | 2, 5 |
| 6 | Çoklu dil: TR varsayılan, EN hazır, genişletilebilir i18n | 8 |
| 7 | Tüm bileşenlerde güncel son stabil sürümler (.NET 10 LTS, Angular ≥20, Tailwind 4, EF Core 10, ES 9) | 3 |
| 8 | **OTP/2FA modülü** (yeni gereksinim): e-posta, TOTP, GSM modem, telefon yönlendirme ve insan onaylı kanalların tümü desteklenir | 7 |
| 9 | Proje planı, alt ajanlarla (subagent) paralel geliştirilebilecek **bağımsız iş paketleri** halinde yazılır | 15 |

Etiketleme: Her özellik **[MVP]** (Faz 1-6 kapsamı) veya **[S2]** (Sürüm 2+) olarak işaretlenmiştir.

---

## 1. Kavramsal Tasarım

Sistem; kurum içi operasyonlarda (SAP ERP, web portalları, dış API'ler, Excel, e-posta) tekrarlayan, kural tabanlı iş süreçlerini otomatize eden merkezi bir platformdur. Akışlar web tabanlı görsel bir tasarımcıda (Studio) sürükle-bırak ile çizilir, merkezi yönetimden (Orchestrator) zamanlanır/tetiklenir ve robot ajanları (Robot Agent) tarafından yürütülür.

### Ana Bileşenler

1. **Orchestrator** — Merkezi yönetim sunucusu (.NET 10, REST API + SignalR). Kuyruklar, zamanlayıcı, tetikleyiciler, robot kayıt/sağlık takibi, workflow/component deposu, credential/asset yönetimi, RBAC, Action Center, audit, alerting.
2. **RPA Studio** — Angular tabanlı, tarayıcıda çalışan görsel tasarımcı. Canvas (Rete.js 2), toolbox, properties paneli, debug/step-through, component yayınlama sihirbazı, UI Spy paneli.
3. **Robot Agent** — Tek codebase, iki mod:
   - **Unattended:** Dedike VM/sunucuda Windows Service. 7/24, zamanlanmış/kuyruklu işler.
   - **Attended:** Kullanıcı PC'sinde tray uygulaması. Kullanıcı kendine atanmış akışları tek tıkla tetikler.
4. **Component Library** — Sık kullanılan akış parçalarının ("SAP Login", "Portal OTP Girişi", "Excel'den Veri Oku") versiyonlanabilir, yeniden kullanılabilir modüller olarak saklandığı merkezi depo.
5. **UI Spy** — SAP GUI ve web elementlerini canlı algılayan modül; attended agent içine gömülüdür (ayrı kurulum yok), SignalR ile Studio'ya element ID'si iletir.

---

## 2. Robot Agent Mimarisi (v3'te yeni)

### 2.1 Ortak Çekirdek
- Agent, Orchestrator'a **kayıt olur** (makine adı, mod, etiketler, sürüm) ve **heartbeat** gönderir (varsayılan 30 sn).
- İş dağıtımı: SignalR push (birincil) + HTTP polling (fallback, 60 sn).
- **Etiket eşleme:** Her robot yetenek etiketleri taşır (`sap-gui`, `sap-nco`, `web`, `excel`, `gsm-modem`...). İş, yalnızca gerekli etiketleri taşıyan müsait robota atanır.
- **Kapasite:** Unattended robot aynı anda 1 GUI işi + N arka plan (API/NCo/Excel) işi çalıştırabilir; N konfigürasyonla belirlenir.
- Robot 2 ardışık heartbeat kaçırırsa işleri `Abandoned` işaretlenir; idempotency kontrolüyle başka robota yeniden atanır.
- Agent **otomatik güncelleme**: Orchestrator'dan yeni sürüm bildirimi alır, iş bitince kendini günceller. [MVP: manuel MSI; S2: otomatik]

### 2.2 Unattended Mod
- Windows Service olarak koşar; servis hesabı ile başlar.
- **SAP GUI / web GUI işleri için oturum yönetimi:** GUI otomasyonu etkileşimli bir Windows oturumu gerektirir. Agent, işi almadan önce hedef VM'de etkileşimli oturum açık mı kontrol eder; değilse yerel RDP loopback (`mstsc /v:localhost` + kayıtlı credential) veya otomatik logon (registry AutoAdminLogon, kilitli konsol) ile oturum açar. Oturum kilitliyken GUI Scripting'in çalışması için `tscon` ile konsola bağlama prosedürü uygulanır. Bu prosedür `SessionManager` sınıfında kapsüllenir ve kurulum dokümanında adım adım anlatılır. **[MVP]**
- İş bitiminde ekran görüntüsü + SAP GUI script log'u arşivlenir (hata analizi için).

### 2.3 Attended Mod
- Tray uygulaması; AD hesabıyla SSO.
- Kullanıcı panelinde: kendisine atanmış akış listesi, "Çalıştır" butonu, çalışan işin adım adım ilerlemesi, giriş formu gerektiren akışlarda parametre penceresi.
- **İnsan-onaylı adımlar:** Akış `UserPrompt` node'una gelince ekranda pencere açılır (örn. OTP kodu girme, onay). Zaman aşımı konfigüre edilir.
- UI Spy bu modda etkinleştirilir (Studio "Kayıt/Algılama" başlattığında).

---

## 3. Teknoloji Yığını (son stabil sürümler)

| Katman | Seçim | Not |
|---|---|---|
| Backend | .NET 10 (LTS), ASP.NET Core Web API, SignalR | Onion Architecture |
| ORM / DB | EF Core 10 + SQL Server | Mevcut Portal/MES altyapısıyla uyumlu |
| Frontend | Angular (kurulum anındaki son stable, ≥20) + Tailwind CSS 4 | Standalone components, signals |
| i18n | `@angular/localize` + backend'de .resx | TR varsayılan, EN hazır |
| Canvas | Rete.js 2 | Sürükle-bırak workflow editörü |
| Loglama | Serilog → Elasticsearch 9.x + Kibana | Yapılandırılmış log |
| SAP ekran | SAP GUI Scripting (`sapfewse.ocx`) | Fallback kanal |
| SAP veri | SAP .NET Connector (NCo 3.1, .NET uyumlu son sürüm) | Birincil kanal, bağlantı havuzu |
| Web | Playwright for .NET (son sürüm) | Chromium öncelikli |
| API | `HttpClient` + Polly (retry/circuit-breaker) | |
| Excel | ClosedXML (son sürüm) | .xlsx; CSV için CsvHelper |
| E-posta | MailKit (SMTP/IMAP) + Exchange EWS | On-premise Exchange |
| Vault | HashiCorp Vault; alternatif: DPAPI-şifreli yerel vault | `ICredentialVault` arkasında değiştirilebilir |
| Kimlik | AD/LDAP SSO (Negotiate/Kerberos) + JWT | |
| SMS (OTP) | USB GSM modem (AT komutları / SMPP) | Bkz. 7 |
| TOTP | Otp.NET | RFC 6238 |

**Mimari:** Onion Architecture — Domain (varlıklar, arayüzler, ExceptionType), Application (CQRS, servisler, retry policy, component invocation), Infrastructure (SAP GUI/NCo, Playwright, Vault client, Excel/e-posta/SMS connector'ları, EF Core), Presentation (REST API + SignalR hub).

---

## 4. Veri Modeli

Tüm tablolarda: `Id (GUID)`, `CreatedAt/By`, `UpdatedAt/By`, soft-delete (`IsDeleted`).

| Varlık | Ana Alanlar |
|---|---|
| **Project** | Ad, açıklama, klasör hiyerarşisi [S2: multi-tenancy] |
| **User** | AD kullanıcı adı, ad-soyad, e-posta, aktif ortamlar |
| **Role / UserRole / Permission** | Rol (Geliştirici, Onaylayan, İzleyici, Yönetici, Operatör); izinler proje × işlem (görüntüle/düzenle/yayınla/çalıştır/onayla) matrisi |
| **Workflow** | Proje, ad, açıklama, etiketler, aktif versiyon (ortam bazında) |
| **WorkflowVersion** | SemVer, JSON tanımı, durum (Draft/Test/Published/Deprecated), değişiklik notu, yayınlayan, onaylayan |
| **Component** | Ad, açıklama, etiketler, sahip |
| **ComponentVersion** | SemVer, JSON tanımı, Input/Output kontrat şeması, durum |
| **ComponentUsage** | Workflow versiyonu ↔ component versiyonu (etki analizi) |
| **Robot** | Makine adı, mod (attended/unattended), etiketler[], durum (Online/Offline/Busy/Maintenance), son heartbeat, agent sürümü, kapasite |
| **Queue** | Ad, proje, retry politikası (max deneme, backoff), SLA süresi, idempotency zorunlu mu |
| **QueueItem** | Kuyruk, referans anahtarı (idempotency), payload (JSON), durum (`New→InProgress→Successful/Failed/BusinessException/Abandoned`), deneme sayısı, atanmış robot, başlangıç/bitiş, hata detayı, checkpoint verisi |
| **Trigger** | Tip (Cron/API-webhook/Kuyruk-eşiği/E-posta/Manuel), hedef workflow+versiyon, ortam, parametreler, aktif mi |
| **Schedule** | Cron ifadesi, saat dilimi, çakışma politikası (atla/kuyrukla/paralel) |
| **Credential** | Ad, tip (SAP/Web/API/E-posta/TOTP-secret), vault key referansı, ortam, izinli projeler — **şifre asla DB'de tutulmaz** |
| **Asset** | Ad, tip (metin/sayı/bool/JSON), ortam bazlı değer — bağlantı adresleri, eşikler vb. |
| **Environment** | Dev/Test/Prod; her biri kendi Orchestrator konfigürasyonu, credential referansları |
| **JobRun** | Workflow versiyonu, tetikleyen (kim/ne), robot, ortam, durum, süre, log korelasyon ID (ES), ekran görüntüsü arşiv yolu |
| **ActionItem** | Action Center kaydı: tip (BusinessException/OTP-isteği/Onay), ilgili JobRun/QueueItem, atanan kullanıcı/rol, durum, çözüm notu, zaman aşımı |
| **OtpRequest** | JobRun, kanal (email/totp/gsm/forward/human), portal referansı, durum, kod (şifreli, kısa TTL), zaman aşımı |
| **AlertRule** | Koşul (SystemException tekrar eşiği / Business birikim / robot offline / kuyruk SLA), kanal (e-posta/Teams webhook), alıcılar |
| **AuditLog** | Kim, ne zaman, ne yaptı (oluştur/düzenle/yayınla/sil/manuel tetik/credential erişimi), eski-yeni değer özeti |

---

## 5. Core Engine

### 5.1 Workflow JSON Şeması **[MVP]**
```json
{
  "schemaVersion": "1.0",
  "id": "guid", "name": "...", "version": "1.2.0",
  "arguments": { "in": [{"name","type","required","default"}], "out": [...] },
  "variables": [{"name","type","scope","default"}],
  "nodes": [{ "id", "type": "activity|componentCall|if|forEach|tryCatch|userPrompt|...",
              "activity": "Sap.Nco.CallBapi", "properties": {...},
              "channel": "nco|gui" }],
  "connections": [{"from","fromPort","to","toPort"}],
  "errorHandling": { "defaultRetry": {...}, "screenshotOnError": true }
}
```
- Node tipleri ve her aktivitenin property şeması backend'de **aktivite kataloğu** olarak tanımlanır; Studio toolbox bu katalogdan dinamik beslenir (yeni aktivite = frontend değişikliği gerektirmez).
- Tip sistemi: string, int, decimal, bool, DateTime, DataTable, JSON, Credential (değeri asla loglanmaz/gösterilmez), SecureString.

### 5.2 Yürütme
- **Base Runner (state machine):** JSON'ı deserialize eder, node graph'ını topolojik sırayla yürütür; If/Else, ForEach (paralellik yok — [S2]), Try/Catch, değişken scope'ları.
- **Component Call:** Component JSON'ı Orchestrator'dan (veya imzalı yerel cache'den) çekilir, **izole değişken scope**'unda çalışır, Input eşlenir, Output döner; içerideki exception çağıranın Try/Catch'ine yayılır. Versiyon **pinlenir**; yeni versiyon çıkınca Studio'da "güncelleme mevcut" rozeti, geçiş manuel onayla.
- **Exception modeli:** `BusinessException` (iş kuralı — Action Center'a düşer, insan inceler) / `SystemException` (teknik — retry politikası devreye girer: üstel geri çekilme, kuyruk bazında max deneme). Aktivite bazında sınıflandırma kuralları (örn. SAP dönüş mesaj tipi E/A → Business, RFC_COMMUNICATION_FAILURE → System) katalogda tanımlıdır.
- **Idempotency/Checkpoint:** QueueItem referans anahtarı ile mükerrer işlem engellenir. Uzun akışlarda `Checkpoint` node'u durum kaydeder; yeniden çalıştırmada tamamlanmış adımlar atlanır. **[MVP: referans anahtarı; S2: adım bazlı checkpoint-resume]**
- **Loglama:** Her node giriş/çıkışı korelasyon ID'siyle ES'e yazılır; Credential tipli değerler maskelenir.

### 5.3 Aktivite Kataloğu (MVP listesi)
- **Mantık:** Assign, If/Else, ForEach, While, Try/Catch, Delay, Log, Checkpoint, UserPrompt (attended), Terminate (Business/System seçimiyle)
- **SAP GUI:** Connect/Login, ExecuteTransaction, Click, SetText, GetText, SelectTab, GridOku (ALV), Screenshot
- **SAP NCo:** CallBapi/Rfc (parametre/tablo eşleme, BAPI_TRANSACTION_COMMIT yönetimi), ReadTable (RFC_READ_TABLE)
- **Web (Playwright):** Aç/Git, Click, Fill, GetText, WaitFor, Download/Upload, Screenshot, Frame/Tab yönetimi
- **API:** HttpRequest (auth şablonları: Basic/Bearer/API-key), JSON parse/build
- **Excel/CSV:** Oku (aralık/tablo→DataTable), Yaz, Sayfa yönetimi, CSV oku/yaz
- **E-posta:** Gönder (SMTP/EWS), GelenKutusuOku/İzle (IMAP/EWS), Ek indir
- **OTP:** GetOtp (kanal parametreli — bkz. 7)
- **Dosya:** Kopyala/Taşı/Sil/Listele, Zip/Unzip

---

## 6. SAP Entegrasyon Stratejisi (Hibrit Kanal)

1. **Programatik kanal (birincil): NCo 3.1** ile doğrudan BAPI/RFC; mümkün yerlerde OData. Toplu/yüksek hacimli işler bu kanaldan. Bağlantı havuzu (`SapConnectionPool`) platform içinde yönetilir; sistem/client/kullanıcı bilgileri Credential Vault'tan gelir. *(Not: Kurumdaki "MII/SAPBase kullan" kuralı bu platform için geçerli değildir — platform kendi bağlantılarını kurar; bu istisna bilinçli bir karardır.)*
2. **GUI Scripting kanalı (fallback):** Programatik arayüzü olmayan ekranlar için. Studio'da her SAP aktivitesinde kanal açıkça seçilir; Engine `ISapDataChannel` / `ISapGuiChannel` implementasyonuna yönlenir.

**UI Spy:** Attended agent içindeki modül; `user32.dll` + SAP COM objeleriyle imlecin altındaki elementin hiyerarşik ID'sini (`wnd[0]/usr/...`) çıkarır, SignalR ile Studio'ya iletir. Web tarafında Playwright codegen benzeri selector çıkarımı **[MVP: SAP; S2: web spy]**.

---

## 7. OTP / 2FA Yönetim Modülü (v3'te yeni) **[MVP]**

Müşteri portallarına girişte SMS ve/veya e-posta ile gelen onay kodları için kanal-bağımsız tasarım. `GetOtp` aktivitesi, portal bazında konfigüre edilen kanaldan kodu getirir:

| Kanal | Mekanizma | Not |
|---|---|---|
| **E-posta** | IMAP/EWS ile belirlenen gelen kutusu izlenir; gönderen+konu filtresi ve regex ile kod çıkarılır; "işlendi" işaretlenir | Zaman penceresi: iş başlangıcından sonra gelen ilk eşleşme |
| **TOTP** | Portal authenticator destekliyorsa secret Vault'ta saklanır, kod platformca üretilir (Otp.NET) | En sağlam yol; portal başına bir kez kurulum |
| **GSM modem** | Sunucuya bağlı SIM'li USB modem; AT komutlarıyla SMS okunur, regex ile kod çıkarılır | Portal hesabının numarası bu SIM olmalı; `gsm-modem` etiketli robot gerekir |
| **Telefon yönlendirme** | Kodların geldiği telefona kurulan yönlendirme uygulaması (veya operatör SMS-forward servisi) SMS'i Orchestrator webhook'una POST eder | Numara değişmez; telefona bağımlılık var |
| **İnsan onaylı** | Robot `OtpRequest` açar → attended'da ekranda pencere; unattended'da Action Center kaydı + e-posta/Teams bildirimi → personel kodu girer → akış devam eder | Zaman aşımı (varsayılan 3 dk) sonunda BusinessException |

Ortak kurallar: kod DB'de şifreli ve kısa TTL ile tutulur, loglarda maskelenir; her `OtpRequest` audit'e yazılır; kanal sıralı fallback destekler (örn. önce GSM, 60 sn'de gelmezse insan onaylı).

---

## 8. Kullanıcı Deneyimi

### 8.1 Genel İlkeler
- **Çoklu dil:** TR varsayılan, EN hazır; tüm metinler kaynak dosyalarında, yeni dil = çeviri dosyası eklemek. Aktivite adları/açıklamaları katalogda çok dilli.
- **Basit mod / Gelişmiş mod:** Anahtar kullanıcı basit modda şablon galerisi + sihirbazlarla ilerler (örn. "Excel'den SAP'ye veri aktar" sihirbazı: dosya seç → alan eşle → BAPI seç → zamanla). Geliştirici gelişmiş modda tüm node'lara ve ifade editörüne erişir. Mod, rol bazında varsayılan atanır, kullanıcı değiştirebilir.
- Yayınlamadan önce **doğrulama**: bağlanmamış port, tanımsız değişken, eksik zorunlu property, kontratı değişmiş component → anlaşılır Türkçe hata listesi.
- Bağlam duyarlı yardım: her aktivitenin yanında "?" → kısa açıklama + örnek.

### 8.2 Ekran Envanteri

**Studio:** Canvas (zoom/pan, kopyala-yapıştır, undo/redo, mini-map), Toolbox (arama, kategori, Reusable Components sekmesi, etiket filtresi), Properties paneli (ifade editörü, credential/asset seçici), Değişkenler/Argümanlar paneli, **Debug/Step-Through** (breakpoint, değişken izleme, tek robot üzerinde test koşusu), Component Yayınlama Sihirbazı (bölüm seç → Input/Output kontratı tanımla → versiyon+not → onaya gönder), UI Spy paneli, Şablon Galerisi.

**Orchestrator:** Dashboard (bugünkü işler, başarı oranı, robot durumu, bekleyen Action Item'lar), İşler (JobRun listesi+detay+log+ekran görüntüsü), Kuyruklar (item listesi, durum filtreleri, yeniden kuyruğa alma), Robotlar (kayıt, etiket, sağlık, bakım modu), Zamanlama/Tetikleyiciler, **Action Center** (BusinessException inceleme, OTP istekleri, onay bekleyenler — atama, çözümleme, not), Credential & Asset yönetimi (vault referansı ekleme; değer görünmez), Kullanıcı/Rol yönetimi, Ortam yönetimi + Publish/Approve akışı, Audit görüntüleyici, Alert kuralları, Kibana'ya derin link.

**Attended Agent (tray):** Akış listem, Çalıştır, ilerleme bildirimi, OTP/onay pencereleri.

---

## 9. Ortam Yönetimi, Versiyonlama, Governance **[MVP]**

- Üç ortam: Dev / Test / Prod — her biri kendi bağlantı ve credential referanslarıyla.
- Workflow/Component durumları: Draft → Test → Published → Deprecated. **Published olmadan Prod'da çalışamaz.**
- Publish, "Onaylayan" rolünün onayını gerektirir (component'lerde zorunlu — birden çok projeyi etkiler). Her publish: versiyon + değişiklik notu; **rollback** desteklenir.
- Ortamlar arası taşıma: "Test'e al / Prod'a yayınla" tek tık; credential/asset referansları ortam karşılıklarıyla otomatik eşlenir.

## 10. Kimlik, Yetki, Güvenlik **[MVP]**

- AD/LDAP SSO (Kerberos/Negotiate) + JWT; attended agent aynı SSO'yu kullanır.
- RBAC: Geliştirici / Onaylayan / İzleyici / Yönetici / Operatör (Action Center çözümleyici). İzin matrisi proje × işlem.
- Credential'lar yalnızca Vault'ta; DB'de referans. Robot, credential'ı çalışma anında Vault'tan çeker, bellekte SecureString, loglarda maskeli.
- API: tüm endpoint'ler yetkili; webhook tetikleyiciler HMAC imzalı; robot-orchestrator kanalı TLS + robot API anahtarı.
- AuditLog: tüm yönetim aksiyonları + credential erişimleri.

## 11. İzleme, Loglama, Alerting **[MVP]**

- Serilog → ES: korelasyon ID (JobRun) ile uçtan uca iz; Kibana dashboard şablonları kurulumla gelir (iş hacmi, hata oranı, süre dağılımı, robot doluluk).
- AlertRule motoru (Orchestrator içinde arka plan servisi): SystemException tekrar eşiği, BusinessException birikimi, robot offline, kuyruk SLA aşımı → e-posta + Teams webhook.

## 12. Test Stratejisi

- **TDD zorunlu:** her iş paketi birim testleriyle teslim edilir (xUnit).
- Core Engine: workflow JSON senaryolarıyla golden-file testleri; exception/retry/idempotency senaryoları.
- SAP NCo/GUI: sahte (mock) kanal implementasyonlarıyla birim test; gerçek SAP DEP ortamına karşı entegrasyon test paketi (ayrı pipeline, elle tetikli).
- Playwright: yerel test sayfalarıyla deterministik testler.
- Studio: Angular component testleri + kritik akışlar için Playwright e2e.
- Pilot kabul: Faz 6'daki uçtan uca senaryo.

---

## 13. MVP Kapsam Sınırı

**[MVP]:** Yukarıda işaretli her şey — hibrit robot, kuyruk/zamanlayıcı/tetikleyiciler, aktivite kataloğu (5.3 listesi), component library, OTP modülü (5 kanal), Action Center, Dev/Test/Prod + onay akışı, RBAC/SSO, vault, audit, alerting, TR/EN.

**[S2] (Sürüm 2+):** Web UI Spy, adım bazlı checkpoint-resume, ForEach paralelliği, agent otomatik güncelleme, Docker/K8s ile robot ölçekleme, multi-tenancy/klasör hiyerarşisi, süreç madenciliği/analitik KPI'lar, mobil bildirim uygulaması.

---

## 14. Riskler ve Önlemler

| Risk | Önlem |
|---|---|
| SAP GUI Scripting kırılganlığı | Birincil kanal NCo; GUI yalnızca fallback; element ID'leri component'lerde merkezileşir |
| Unattended VM'de GUI oturumu düşmesi | SessionManager + oturum sağlık kontrolü + alert |
| GSM modem arızası | OTP kanal fallback zinciri (insan onaylı her zaman son basamak) |
| Anahtar kullanıcı hatalı akış yayınlaması | Publish onay akışı + doğrulama + Test ortamı zorunluluğu |
| Orta ölçekte kuyruk darboğazı | QueueItem atama SQL tarafında kilitli tek sorgu (`UPDLOCK/READPAST`); robot havuzu etiket bazlı |
| Credential sızıntısı | Vault + maskeleme + audit + credential tipinin ifadelerde kısıtlanması |

---

## 15. Proje Planı — Alt Ajan İş Paketleri

Her iş paketi (WP): **bağımsız teslim edilebilir**, girdi kontratı önceden sabit, birim testli, kabul kriterli. `⇉` işaretli paketler aynı faz içinde **paralel** yürütülebilir.

### Faz 1 — Temel Altyapı
- **WP-1.1** Onion solution iskeleti (Domain/Application/Infrastructure/WebAPI) + CI derleme. *Kabul: solution derlenir, katman bağımlılık testleri geçer.*
- **WP-1.2** ⇉ EF Core veri modeli (Bölüm 4'ün tamamı) + migration'lar. *Kabul: tüm varlıklar migrate olur, CRUD repository testleri geçer.*
- **WP-1.3** ⇉ AD/LDAP SSO + JWT + RBAC middleware. *Kabul: rol bazlı endpoint testleri.*
- **WP-1.4** ⇉ Serilog→ES altyapısı + korelasyon ID. 
- **WP-1.5** ⇉ `ICredentialVault` + HashiCorp Vault ve DPAPI implementasyonları.
- **WP-1.6** ⇉ AuditLog altyapısı (aksiyon interceptor'ı).
- **WP-1.7** ⇉ Angular iskelet: son stable Angular + Tailwind 4 + i18n altyapısı + SSO login + layout/navigasyon.

### Faz 2 — Core Engine
- **WP-2.1** Workflow JSON şeması + aktivite katalog altyapısı (şema doğrulama dahil).
- **WP-2.2** Base Runner: state machine, değişken/argüman, If/ForEach/TryCatch/Assign/Delay/Log. *Kabul: golden-file senaryo testleri.*
- **WP-2.3** Business/System Exception sınıflandırma + retry policy motoru.
- **WP-2.4** Component Invocation: izole scope, I/O eşleme, versiyon pinleme.
- **WP-2.5** Idempotency (referans anahtarı) + Checkpoint node.
- **WP-2.6** ⇉ API aktiviteleri (HttpRequest + Polly). 
- **WP-2.7** ⇉ Excel/CSV aktiviteleri. 
- **WP-2.8** ⇉ E-posta aktiviteleri (MailKit/EWS). 
- **WP-2.9** ⇉ Dosya aktiviteleri.

### Faz 3 — Robot Agent & Orchestrator Çekirdeği
- **WP-3.1** Orchestrator: robot kayıt/heartbeat/etiket + SignalR iş dağıtımı + polling fallback.
- **WP-3.2** Kuyruk motoru: QueueItem durum makinesi, kilitli atama sorgusu, Abandoned kurtarma.
- **WP-3.3** Zamanlayıcı (cron) + tetikleyiciler (API webhook, kuyruk eşiği, e-posta izleyici, manuel).
- **WP-3.4** Robot Agent çekirdeği (Windows Service + tray, tek codebase, iş alma/çalıştırma/raporlama).
- **WP-3.5** Unattended SessionManager (RDP/AutoLogon/tscon prosedürü) + kurulum dokümanı.
- **WP-3.6** ⇉ Attended UX: akış listem, UserPrompt pencereleri, bildirimler.

### Faz 4 — SAP & OTP (Kritik Yol)
- **WP-4.1** SAP GUI Scripting: bağlantı/oturum yöneticisi + aktiviteler (5.3 listesi).
- **WP-4.2** ⇉ SAP NCo kanalı: bağlantı havuzu + CallBapi/ReadTable + commit yönetimi.
- **WP-4.3** UI Spy modülü (agent içinde) + SignalR köprüsü.
- **WP-4.4** ⇉ OTP modülü: `IOtpChannel` + e-posta/TOTP/GSM/webhook-forward/insan-onaylı implementasyonları + `OtpRequest` akışı.
- **WP-4.5** "SAP Login" component'inin paketlenip yayınlanması (publish akışının uçtan uca ilk doğrulaması).

### Faz 5 — Studio UI
- **WP-5.1** Canvas (Rete.js 2): node render, bağlantı, zoom/undo, mini-map.
- **WP-5.2** Toolbox (katalogdan dinamik) + Properties paneli + ifade/credential/asset seçicileri.
- **WP-5.3** ⇉ Component Library paneli + "Component Olarak Yayınla" sihirbazı.
- **WP-5.4** ⇉ Debug/Step-Through (breakpoint, değişken izleme, test koşusu).
- **WP-5.5** ⇉ Basit mod + şablon galerisi + "Excel'den SAP'ye" sihirbazı.
- **WP-5.6** ⇉ Web aktiviteleri (Playwright) + Studio property'leri.

### Faz 6 — Orchestrator UI, Pilot ve Devreye Alma
- **WP-6.1** Orchestrator ekranları (8.2 envanteri: dashboard, işler, kuyruklar, robotlar, zamanlama, ortam/publish, kullanıcı/rol, audit, alert).
- **WP-6.2** ⇉ Action Center (BusinessException + OTP + onay kuyruğu).
- **WP-6.3** ⇉ Alerting motoru + Kibana dashboard şablonları.
- **WP-6.4** Dev/Test/Prod ayrımı + Publish/Approve uçtan uca test.
- **WP-6.5** **Pilot:** "Müşteri portalından (OTP'li giriş dahil) veri çekip SAP MM01 ile malzeme açma" — SAP Login component'i + OTP modülü + kuyruk + Action Center kullanılarak. *Kabul: 100 kayıtlık toplu koşuda hedef başarı ≥ %95, BusinessException'lar Action Center'da çözülür.*
- **WP-6.6** Kurulum/operasyon dokümantasyonu (VM hazırlığı, vault, GSM modem, Exchange, ES).

### Gelecek İterasyon (v4 adayları)
[S2] listesi + performans/throughput KPI'ları.
