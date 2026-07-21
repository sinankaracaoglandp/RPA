# RPA Platform v3 — Proje Kuralları

**Spec:** `docs/specs/2026-07-04-rpa-platform-v3-design.md`
**Plan:** `docs/plans/2026-07-04-implementation.md`
**Kontrat Paketi:** Aşağıda belirtilen arayüzler ve şemalar — **değişmez referans.**

---

## Kontrat Paketi (Değiştirilemez Referans)

Bu dosyalar **tüm alt ajanlar tarafından bağlayıcıdır.** Değişiklik ancak **Kontrat Değişiklik Prosedürü** ile yapılır (bkz. Kısım 4).

### C# Arayüzleri (Domain Katmanı)

1. **`src/RPA.Domain/Interfaces/IActivity.cs`** — Tüm aktivite implementasyonları bu arayüzü implements eder. `ActivityMetadata` katalogda tutulur.
2. **`src/RPA.Domain/Interfaces/IWorkflowRunner.cs`** — BaseRunner public sözleşmesi. `ExecuteAsync(workflowVersion, arguments, jobRunId) → result`.
3. **`src/RPA.Domain/Interfaces/ICredentialVault.cs`** — Credential yönetimi (HashiCorp Vault / DPAPI). `GetSecretAsync(key) → SecureString` (plaintext asla).
4. **`src/RPA.Domain/Interfaces/IOtpChannel.cs`** — OTP/2FA kanal arayüzü. 5 implementasyon (Email, TOTP, GsmModem, PhoneForward, HumanApproval).
5. **`src/RPA.Domain/Interfaces/ISapChannel.cs`** — SAP hibrit kanal (`ISapDataChannel` NCo için, `ISapGuiChannel` GUI için).

### Veri Modeli (Domain Katmanı)

- **`src/RPA.Domain/Entities/*.cs`** — 21 varlık sınıfı (spec Bölüm 4 tablo birebir). BaseEntity: GUID, CreatedAt/By, UpdatedAt/By, IsDeleted.
- **`src/RPA.Domain/Enums/*.cs`** — ExceptionType (Business/System), QueueItemStatus, RobotMode, ComponentStatus, TriggerType, OtpChannel, RobotStatus.

### Workflow JSON Şeması

- **`src/RPA.Domain/WorkflowSchema.json`** — Node tiplerinin, parametrelerin, bağlantıların tam özellikleri. Version: "1.0" (migration'lar için).

---

## Geliştirme Prensipleri

### 1. TDD Zorunlu

Her görev (task) **failing test → minimal impl → pass → commit** döngüsünü takip eder:

```bash
# 1. Test yaz (FAIL)
# 2. Çalıştır — hatayı gözlemle
# 3. Implementation yazma — minimal (YAGNI)
# 4. Çalıştır — PASS
# 5. Commit
```

Test dosya yolu: `tests/RPA.[Layer].Tests/[Feature]Tests.cs`

### 2. Onion Architecture Katman Bağımlılığı

- **Domain →** arayüzler, varlıklar, enum'lar (harici bağımlılık YOK)
- **Application →** servisler, CQRS, DTO'lar; Domain'e bağlı
- **Infrastructure →** EF Core, SAP client'ları, Vault; Application+Domain'e bağlı
- **WebAPI →** Controller'lar, SignalR hub; Application+Infrastructure'ye bağlı

**Dairesel bağımlılık engellendi:** WebAPI → Domain doğru, Domain → WebAPI yanlış.

### 3. Credential Güvenliği (Spec Bölüm 5.5, 10)

- **Asla plaintext:** Database'de credential değeri saklanmaz. Yalnızca Vault key referansı tutulur.
- **SecureString:** Vault'tan getirilen değer SecureString olarak ellenir. Bellekte şifrelenmiş.
- **Log maskeleme:** Türü `Credential` olan değişkenler loglarda hiçbir zaman açılmaz.
- **Aktivite tasarımı:** Credential parametresi `ICredentialVault` arayüzü kullanır; direkt property taşımaz.

### 4. Logging Standart (Spec Bölüm 11)

Serilog → Elasticsearch:

```csharp
ILogger<T> _logger; // Dependency injection

// Her node girişi/çıkışı
_logger.LogInformation("Node {NodeId} başlatıldı", nodeId);
_logger.LogInformation("Node {NodeId} tamamlandı, output: {@Output}", nodeId, output);

// Korelasyon ID otomatik (JobRun GUID)
// Elasticsearch query: correlation_id: "<JobRunId>"
```

Credential tipli değerler: `_logger.LogInformation("Credential: [MASKED]")` — impl'de mask edilir.

### 5. Naming Convention

- **C# sınıflar:** PascalCase (`ProjectService`, `QueueItem`)
- **Interfaces:** `I` prefix (`IActivity`, `IWorkflowRunner`)
- **Activity ID:** Dot notation (`Sap.Nco.CallBapi`, `Web.Click`, `Excel.Read`)
- **Enum değerleri:** PascalCase (`BusinessException`, `InProgress`)
- **Database:** snake_case tablo / kolon adları (Entity name'den EF tarafından otomatik)

### 6. Exception Handling (Spec Bölüm 5.2, 6)

- **`BusinessException`** — İş kuralı hlal (expected). Örn. "Malzeme zaten mevcut", "Geçersiz SAP dönüş". Action Center'a düşer.
- **`SystemException`** — Teknik hata (unexpected). Örn. "Bağlantı timeout", "RFC_COMMUNICATION_FAILURE". Retry politikası uygulanır.

Sınıflandırma: ActivityMetadata'da `ExceptionClassificationRule` tanımlanır. Örn.:
```
SAP dönüş Type == 'E' → Business
SAP RFC bağlantı hatası → System
HTTP 5xx → System
HTTP 400-499 → Business (input hatası)
```

### 7. Code Review Efor Seçimi

- **Opus paketleri** (`high`): Fen kritikal yol (motor, SAP, OTP, kuyruk, Canvas)
- **Sonnet paketleri** (`medium`): Standart implementasyon (aktiviteler, ekranlar, CRUD)
- **Haiku paketleri** (`low`): Boilerplate, doküman

Her teslim sonrası `/code-review [efor]`; güvenlik dokunan paketlerde ek `/security-review`.

---

## Kontrat Değişikliği — 2026-07-05

`ISapGuiChannel` kendi dosyasına taşındı (`src/RPA.Domain/Interfaces/ISapGuiChannel.cs`);
daha önce `ISapChannel.cs` içindeydi. `ISapDataChannel` ve `SapCallResult` `ISapChannel.cs`'te kaldı.

Metot adları netleştirildi ve bir metot eklendi:
- `LoginAsync` → `ConnectAsync`, `LogoutAsync` → `DisconnectAsync` (spec Bölüm 5.3 "Connect" terminolojisi).
- **Yeni:** `SelectTabAsync(elementId)` — SAP tab-strip desteği (aktivite: `Sap.Gui.SelectTab`).

Etkilenen paketler: WP-4.1 (bu paket — implementasyon + aktiviteler), WP-4.2 (NCo — yalnızca
`ISapDataChannel` kullanır, etkilenmez), WP-4.5 (SAP Login component — yeni ad/metotları kullanacak).
Gerekçe: SelectTab aktivitesi (Spec 5.3 SAP GUI listesi) kanalda karşılık gerektiriyordu; mevcut
`ISapGuiChannel` bunu içermiyordu. Kanalın hiçbir tüketicisi henüz yoktu (bu ilk SAP paketidir).

---

## Kontrat Değişikliği — 2026-07-05 (WP-4.2 NCo)

`ISapDataChannel` + `SapCallResult` `ISapChannel.cs`'ten kendi dosyasına taşındı
(`src/RPA.Domain/Interfaces/ISapDataChannel.cs`) — `ISapGuiChannel` ile aynı desen.
`ISapChannel.cs` yalnızca geriye dönük referans yorumu içerir. **İmzalar değişmedi**
(CallBapiAsync, CallRfcAsync, ReadTableAsync, CommitAsync, RollbackAsync, IsHealthyAsync).
Etkilenen paket yok (arayüzün henüz tüketicisi yoktu; bu ilk NCo paketidir).

---

## Kontrat Değişikliği — 2026-07-05 (WP-4.3 OTP)

`IOtpChannel` sözleşmesi OTP modülü implementasyonu sırasında netleştirildi:
- **Metot:** `GetCodeAsync(OtpRequest, CancellationToken)` → `GetOtpAsync(OtpRequest request, TimeSpan timeout, CancellationToken)`.
  Timeout artık explicit parametredir (fallback orkestrasyonu `GetOtpActivity` içinde kanal-başına timeout uygular).
- **`IsHealthyAsync()` kaldırıldı** (tüketicisi yoktu; sağlık kontrolü kanal-dışı ele alınır).
- **`OtpRequest`** artık `IOtpChannel.cs` içindeki request-DTO değil, `RPA.Domain.Entities.OtpRequest`
  entity'sidir (JobRunId, Channel, PortalReference, EncryptedCode, Status, ExpiresAt, ProvidedAt).
  Kanala özgü teknik parametreler (email hesabı, TOTP secret, GSM numarası, webhook ref, kod deseni)
  `RPA.Infrastructure.OTP.OtpChannelSettings` içine taşındı (kanal constructor'ına DI ile verilir).
- **Yeni:** `OtpRequestStatus` enum (Pending, Provided, Expired, Failed).

Etkilenen paket yok — `IOtpChannel`'in henüz hiçbir tüketicisi yoktu (bu ilk OTP paketidir).
Gerekçe: audit entity'si ile runtime request-DTO'sunun tek isim altında çakışması giderildi; timeout
tabanlı sıralı fallback sözleşmede explicit hale getirildi.

---

## Kontrat Değişikliği — 2026-07-06 (WP-Faz5-Backend)

**Backend Component APIs generalization** — Components controller & publish service expanded from hardcoded SAP Login to generic component support.

**Before (WP-4.5):** Hardcoded routes (`POST /api/components/sap-login/publish`, `POST /api/components/sap-login/{version}/approve`), single-component-only.

**After:** Generalized routes:
- `GET /api/components` → list all published components
- `GET /api/components/{componentId}/{version}` → fetch specific version
- `POST /api/components/{componentId}/publish` → publish any component (body: { version, jsonDefinition, inputOutputSchema, ... })
- `POST /api/components/{componentId}/{version}/approve` → approve any version
- `ComponentVersion` fields extended: add `displayName`, `description`, `author`, `category` (optional, for library metadata)

**Etkilenen paketler:** WP-4.5 (SAP Login publish path — now uses generalized routes instead of hardcoded), WP-5.3 (Component Library UI — consumes generalized API).

**Gerekçe:** Faz 5 Studio requires a generic component library with publish/approve UX. Faz 4 Task 4.5 only implemented SAP Login component as a single-component proof-of-concept. Generalizing the API enables Task 5.3 without TODOs or stubs.

---

## Kontrat Degisikligi - 2026-07-07 (Paket C SAP Hedef Goster)

UI Spy tek-secim oturumu ve Studio picker metadata'si icin kontrat genisletildi.

- `SpyElementMessage`: `SessionId` (Guid), `Kind` (`sap|web|desktop`) eklendi. Paket D/E icin web/desktop'a ozgu nullable alanlar simdiden eklendi.
- `ActivityParameter`: opsiyonel `PickerKind` eklendi. `null`/empty picker yok, `sap` SAP GUI picker demektir.
- `StudioHub`: `StartSpy(sessionId, kind)` ve `StopSpy(sessionId)` metotlari eklenecek; `ReceiveDetectedElement` sessionId ile caller-only yayina gececek.

Etkilenen paketler: Paket C (SAP picker), Paket D (Web picker), Paket E (Desktop picker), Studio activity metadata tuketicileri, Agent UI Spy transport.
Gerekce: Studio'da selector/element alanlarinin hedef goster dugmesiyle tek seferlik ve kullaniciya ozel secim yapabilmesi icin mevcut surekli `Clients.All` yayin kontrati yeterli degildir.

---

## Kontrat Değişikliği — 2026-07-10 (Paket E — Windows Masaüstü Otomasyonu, runtime motoru)

**Yeni arayüz:** `src/RPA.Domain/Interfaces/IDesktopAutomationChannel.cs` — herhangi bir Windows
masaüstü uygulamasında UIA tabanlı otomasyon kanalı (tıklama, metin yazma, öğe seçme, tuş gönderme,
bekleme, ekran görüntüsü). SAP (`ISapGuiChannel`) ve Web (Playwright) kanallarından bağımsızdır.
Metotlar: `AttachAsync`, `LaunchAsync`, `ClickAsync`, `SetTextAsync`, `GetTextAsync`,
`SelectItemAsync`, `SendKeysAsync`, `WaitForAsync`, `ScreenshotAsync`.

**Selector formatı:** UIA yolu — `/` ile ayrılmış segmentler, her segment `ControlType` adı +
`[Key='Value']` (tam) / `[Key~'regex']` (regex) koşulları (AutomationId/Name/Title/ClassName).
`WorkflowSchema.json`'a dokunulmadı (selector düz string).

**Yeni aktivite ailesi:** `Desktop.*` (kategori "Masaüstü", capability `desktop`) —
`Desktop.Attach/Launch/Click/SetText/GetText/SelectItem/SendKeys/WaitFor/Screenshot`.
Katalog `ActivityRegistry.RegisterDesktop`; keyed DI kaydı `WorkflowServiceCollectionExtensions`.
Selector/element alanları `PickerKind="desktop"` (mevcut 🎯 picker altyapısını kullanır).

**Implementasyon konumu:** `IDesktopAutomationChannel`'in FlaUI (UIA3) implementasyonu
`RPA.Agent/Desktop/FlaUiDesktopAutomationChannel.cs` — FlaUI.UIA3 NuGet Windows-only olduğundan
Agent (net10.0-windows) sürecinde yaşar ve `AddAgentCore` içinde Windows koşuluyla kaydedilir.
Aktiviteler platform-nötrdür (yalnız arayüze bağlı) → Infrastructure.Tests'te mock'lanabilir.

**Exception sınıflandırması:** element bulunamadı/timeout → `SystemException`; Business reddi yok.

Etkilenen paketler yok (yeni arayüzün tüketicisi yalnız yeni `Desktop.*` aktiviteleridir; ilk
masaüstü paketidir). **Kalan iş:** DesktopSpy (Agent) — 🎯 ile `kind:"desktop"` element seçimi,
`SpySessionCoordinator`'a takılacak (henüz yapılmadı).

---

## Kontrat Değişikliği — 2026-07-10 (DesktopSpy iptal bildirimi + tek-ekran UX)

**`StudioHub`**: yeni `NotifySpyCancelled(Guid sessionId)` metodu + `SpyCancelled` event'i.
Ajan tek-seçim iptal/boş bittiğinde (Esc veya seçim yapmadan) çağırır; hub oturum sahibi Studio
bağlantısına `SpyCancelled` yayınlar. Studio picker'ı 60 sn timeout beklemeden hemen kapatır
ve `pending` temizlenir (tekrar 🎯'e basınca "already active" hatası olmaz).

**`ISpyElementTransport`** (RPA.Infrastructure.UISpy): yeni `NotifyCancelledAsync(sessionId, ct)`.
Implementasyon `SignalRSpyElementTransport` → `NotifySpyCancelled` hub metodunu çağırır.
Tüketici: `SpySessionCoordinator` (iptal/boş/timeout durumunda çağırır).

**Studio `SpyService`** (`spy.service.ts`): `SpyCancelled` handler'ı eklendi.

**Tek-ekran UX:** `FlaUiDesktopSinglePicker` seçim süresince öndeki pencereyi (tasarımcı
tarayıcısı) küçültür, seçim/iptal bitince geri getirir (`ShowWindow` P/Invoke) — hedef uygulama
görünür olsun diye.

Etkilenen paketler: Paket C (SAP picker aynı iptal yolunu kullanır), Paket E (Desktop picker).
Gerekçe: iptal sinyali Agent→Studio iletilmiyordu; Esc sonrası Studio 60 sn askıda kalıp sonraki
denemede "already active" veriyordu.

---

## Kontrat Degisikligi - 2026-07-11 (Credential Vault Management)

`ICredentialVault` sozlesmesine plaintext icermeyen guvenli listeleme eklendi:
- **Yeni:** `ListSecretsAsync(string? tag = null) -> IEnumerable<VaultSecretReference>`
- **Yeni DTO:** `VaultSecretReference { Key, Metadata }`

Mevcut `GetSecretAsync`, `StoreSecretAsync`, `DeleteSecretAsync`, `ExistsAsync`, `ListSecretsByTagAsync` imzalari degismedi.
Etkilenen paketler: WebAPI Credentials endpoint, DPAPI Vault, HashiCorp Vault, Studio Orchestrator Credentials ekrani.
Gerekce: Kullanicinin credential degerini UI uzerinden Vault'a yazabilmesi ve listede yalnizca key/metadata gorebilmesi icin secret degerini dondurmeyen listeleme sozlesmesi gerekliydi.

---

## Kontrat Değişikliği — 2026-07-11 (Paket D — Web picker "hedef göster")

WebSpy tek-seçim picker'ı eklendi (UI Spy `kind:"web"`), böylece Web.* aktivitelerinin
selector alanlarında 🎯 düğmesi çalışır.

- **Yeni arayüz:** `IWebSinglePicker` (`RPA.Agent/UISpy/SpySessionCoordinator.cs`) — SAP/Desktop
  picker deseniyle aynı. `SpySessionCoordinator` opsiyonel `IWebSinglePicker? webPicker` parametresi
  aldı ve `kind:"web"` dalını işliyor.
- **Implementasyon:** `PlaywrightWebSinglePicker` (`RPA.Agent/UISpy/`) — başlıklı Chromium açar,
  sayfaya enjekte script ile hover-vurgu + `CTRL+Tık` seçim + `Esc` iptal; kararlı CSS selector
  üretir (id → data-testid → nth-of-type yolu). `AddAgentCore` içinde kayıtlı.
- **Yeni value object:** `RPA.Domain/ValueObjects/WebUiElement.cs` (Selector, TagName,
  InnerTextPreview, PageUrl). `SpyElementMessage.FromWeb(element, sessionId)` eklendi (`Kind="web"`).
- **Katalog:** `ActivityRegistry` içindeki Web.* selector girişlerine `pickerKind:"web"` eklendi
  (Web.Click/Fill/GetText/WaitFor/Download/Upload/Screenshot).

Etkilenen paketler: Paket D (bu paket). Studio tarafı değişmedi — `SpyKind` zaten `web` içeriyordu;
`selector-picker-button` metadata `PickerKind`'i `spy.pick(kind)`'e geçiriyor.
Gerekçe: Web selector alanları için "hedef göster" tek-seçim akışı eksikti (yalnız SAP/Desktop vardı).

---

## Kontrat Değişikliği — 2026-07-11 (Paket C — SAP GUI gerçek sürüş)

SAP GUI Scripting artık gerçek COM ile sürülüyor (önceden yalnız `StubSapGuiSession` vardı ve
kanal/aktiviteler hiçbir yerde DI'a bağlı değildi).

- **Yeni soyutlama:** `ISapGuiSessionFactory` (`RPA.Infrastructure/SAP/`) — oturum üreticisi.
  - `ComSapGuiSessionFactory`: çalışan SAP Logon'a (ProgID "SAPGUI", ROT üzerinden `GetActiveObject`)
    bağlanır, `OpenConnection(systemId)` + giriş ekranı doldurup `sendVKey(0)` ile logon yapar.
  - `StubSapGuiSessionFactory`: SAP olmayan ortam / birim testleri (deterministik).
- **`ComSapGuiSession`**: `GuiSession` COM'unu sarar; tüm işlemler `findById` ile bir **STA thread**'de
  (`SapStaThread`) marshallanır (SAP scripting STA gerektirir). Element bulunamama/COM hatası →
  `SystemException`. ReadGrid = GuiGridView `RowCount/ColumnOrder/GetCellValue`; Screenshot = `HardCopy`.
- **`SapGuiSessionManager`**: opsiyonel `ISapGuiSessionFactory` ctor parametresi (null → stub;
  mevcut birim testleri değişmeden geçer). Gerçek modda fabrikayı kullanır.
- **Wiring (yeni):** `AddSapGuiChannel` artık `ISapGuiSessionFactory`'yi (Windows → COM) ve
  `Sap.Gui.*` aktivitelerini **keyed `IActivity`** olarak kaydeder; Agent `Program.cs` bunu çağırır
  (önceden hiç çağrılmıyordu → SAP GUI aktiviteleri çalıştırılamıyordu).

**Ön koşul (gerçek sürüş):** SAP GUI kurulu + SAP Logon açık + GUI Scripting etkin
(SAP Logon > Options > Accessibility & Scripting > Scripting) ve `systemId` SAP Logon'da tanımlı.
Aksi halde açık `SystemException` mesajları döner. **Kalan:** SAP UI Spy element çözücüsü hâlâ
`NullSapGuiElementResolver` (SAP "hedef göster" gerçek COM çözücüsü ayrı iş).

Etkilenen paketler: Paket C (SAP GUI). Birim testleri etkilenmez (stub varsayılanı).

---

## Kontrat Değişikliği — 2026-07-11 (Kod & Veri — DataTable + C# kod aktivitesi)

Yeni aktivite ailesi **"Kod & Veri"** (`CatCode`) — kategori "Kod & Veri", capability `code`.

- **`Data.ToDataTable`** / **`Data.FromDataTable`** — platformun satır-listesi gösterimi
  (`List<Dictionary<string,object?>>` — SAP GridRead/ReadTable/BAPI, Excel) ile gerçek
  `System.Data.DataTable` arasında dönüşüm. Dönüştürücü: `Activities/Code/DataTableConverter`.
- **`System.InvokeCode`** — Roslyn (`Microsoft.CodeAnalysis.CSharp.Scripting`) ile C# kodu çalıştırır.
  Script global API'si `CodeGlobals`: `Get("ad")` / `Set("ad", deger)` (workflow değişkenleri),
  `ToDataTable(rows)` / `ToRows(dt)`, `Log(...)`. Çıktılar `Outputs` sözlüğünden döner (node-local
  scope kaybı olmaz). Import: System, Linq, Collections.Generic, Data, Text, Globalization.
  - Derleme hatası → `BusinessException`; çalışma-anı hatası → System (runner sınıflandırır).
  - **GÜVENLİK:** kod robot süreci yetkileriyle **sandbox'sız** çalışır — yalnız güvenilir
    tasarımcılara açılmalı (yetki/rol kontrolü çağıran katmanda düşünülmeli).
- **Kayıt:** `WorkflowServiceCollectionExtensions` keyed `IActivity` (System.InvokeCode,
  Data.ToDataTable, Data.FromDataTable); katalog `ActivityRegistry.RegisterCode`.
- **Paket:** Infrastructure'a `Microsoft.CodeAnalysis.CSharp.Scripting` 4.14.0 eklendi.

Etkilenen paket yok (yeni aile). Not: SAP/Excel satır çıktıları artık DataTable olarak da işlenebilir.

---

## Kontrat Değişikliği — 2026-07-12 (Paket F — Görüntü/OCR Fallback Otomasyonu)

Erişilebilirlik ağacı olmayan uygulamalar için piksel + metin tabanlı otomasyon kanalı.

- **Yeni arayüz:** `IVisionAutomationChannel` (`src/RPA.Domain/Interfaces/`) — template matching +
  OCR. `IDesktopAutomationChannel` kardeşi. Metotlar: ClickImageAsync, WaitForImageAsync,
  ImageExistsAsync, GetTextAsync, ClickTextAsync, TextExistsAsync. Yeni value object `VisionMatch`.
- **Yeni aktivite ailesi:** `Vision.*` (kategori "Görüntü", capability `vision`) —
  Click/WaitFor/Exists/GetText/ClickText/TextExists. Katalog `ActivityRegistry.RegisterVision`;
  keyed DI `WorkflowServiceCollectionExtensions`. OCR çok dilli (`tur+eng+deu`).
- **İmplementasyon:** `TesseractOpenCvVisionChannel` (`RPA.Agent/Vision/`) — OpenCvSharp4 (template)
  + Tesseract (OCR) + GDI ekran yakalama + gerçek fare. Windows-only, `AddAgentCore`'da kayıtlı.
  Non-agent süreçlerde `UnavailableVisionAutomationChannel` (TryAddSingleton).
- **🎯 image picker:** `SpyElementMessage`'a `Kind="image"`, `ImageBase64`, `Region` + `FromImage`.
  `ActivityParameter.PickerKind` yeni değer `"image"`. `StudioHub.StartSpy` `kind:"image"` kabul eder.
  Yeni arayüz `IImageRegionPicker` / `GdiImageRegionPicker` (bölge seç → base64 PNG göm).
  `SpySessionCoordinator` opsiyonel `IImageRegionPicker? imagePicker` parametresi aldı.
- **Anchor Faz 2'ye ertelendi:** `TemplateMatcher.FindAll` çok-eşleşme döndürecek şekilde hazır.

Etkilenen paketler: Studio picker metadata tüketicileri (yeni `image` kind), Agent UI Spy transport.
SAP/Web/Desktop picker'lar etkilenmez (additive).
Gerekçe: UIA/DOM sunmayan uygulamalar için (eski Win32, custom-render) otomasyon boşluğu.

### Ek — 2026-07-13 (Paket F izleme düzeltmeleri)

- **StudioHub whitelist:** `StudioHub.SupportedKinds`'e `"image"` eklendi (eksikti; picker
  "Desteklenmeyen spy tipi: image" alıyordu).
- **Çoklu monitör:** `ScreenCapture` tam-ekran yakalama `SystemInformation.VirtualScreen` (tüm
  monitörler) + `VirtualScreenOrigin`; `TesseractOpenCvVisionChannel.DoClick` tıklamayı sanal-ekran
  orijiniyle kaydırır; `GdiImageRegionPicker` overlay tüm monitörleri kaplar.
- **Freeze/dondurma (geçici menü/pencere yakalama) — kontrat genişledi:**
  - `IImageRegionPicker.DetectOnceAsync(ImagePickerOptions options, CancellationToken)` — yeni
    `ImagePickerOptions(CaptureMode "f2"|"timer", DelaySeconds)` (JSON parse; varsayılan F2).
  - `ISpySessionCoordinator.StartAsync(sessionId, kind, string? optionsJson, ct)` — image için
    picker seçeneklerini taşır; image timeout ≥300 sn (manuel UI hazırlığı).
  - `ISpyCommandConnection.OnStartSpy(Func<Guid,string,string?,Task>)` + `StudioHub.StartSpy(sessionId,
    kind, string? optionsJson)` — **SignalR istemcileri artık StartSpy'ı 3 argümanla çağırmalı**
    (non-image için `null`).
  - `GdiImageRegionPicker`: iki aşamalı — arm (yapılandırılabilir global hotkey / geri sayım) →
    ekranı **dondur** (snapshot) → donmuş görüntü üzerinde seçim; kırpma canlı ekrandan değil
    snapshot'tan yapılır.
  - **Dondurma kısayolu yapılandırılabilir:** `ImagePickerOptions` `HotKey` (F1–F12) + `Ctrl/Shift/Alt`
    alanları; `VirtualKey`/`Modifiers`/`DisplayCombo`. Manuel modda `RegisterHotKey` bu kombinasyonu
    kullanır (hedef uygulamada boş bir tuş seçilebilsin diye). Varsayılan F2.
  - Studio: `SpyService.pick(kind, options?)` + picker düğmesinde mod/saniye + tuş/Ctrl/Shift/Alt
    kontrolleri (yalnız `image`), i18n `picker.captureMode/modeF2/modeTimer/delaySeconds/seconds/
    freezeKey/ctrl/shift/alt`.
  Etkilenen: Studio spy tüketicileri, Agent UI Spy transport, WebAPI StudioHub. Gerekçe: OCR/görüntü
  ile açılan geçici SAP menüsü/pencere, picker overlay hemen açılınca yakalanamıyordu.

---

## Kontrat Değişikliği — 2026-07-13 (Menü gezinme: sıralı vision + SAP menü)

Açılır menülerin node'lar arası odak kaybında kapanması sorununa iki çözüm eklendi.

**1) `Vision.ClickSequence` (yeni aktivite, arayüz değişmedi):** Tek node içinde sırayla N
görüntüye tıklar (iç içe menüler). Her adım `{image, clickType, waitMs}`; adımlar aynı node'da
art arda çalıştığından node'lar arası odak kaybı olmaz → açılır menü zincir boyunca açık kalır.
Ortak `confidence`/`timeoutMs`. `IVisionAutomationChannel` **değişmedi** (mevcut `ClickImageAsync`
+ `Task.Delay` ile). Katalog + keyed DI (`Vision.ClickSequence`). Yeni parametre `PickerKind`
değeri **`"image-sequence"`** — Studio'da özel sıralı adım editörü (`VisionSequenceEditorComponent`)
render eder; her adım mevcut `image` 🎯 picker'ını kullanır. `ActivityPort.pickerKind` (Studio
model) `'image-sequence'` değerini de kabul eder (spy türü DEĞİL, yalnız editör ipucu →
`selector-picker-button`'a null geçilir).

**2) `Sap.Gui.SelectMenu` (yeni aktivite + kontrat genişledi):** Menü çubuğunda **metin yoluyla**
gezinip öğe seçer (örn. `Sistem/Liste/Yazdır`). Element ID gerekmez, odak/görünürlükten bağımsız
(COM scripting). **`ISapGuiChannel.SelectMenuAsync(string menuPath)`** eklendi ("/"-ayrık metin
yolu). İç soyutlama `ISapGuiSession.SelectMenuAsync(IReadOnlyList<string> menuTexts)`;
`ComSapGuiSession` `wnd[0]/mbar` ağacını `Text` ile yürür (normalize: '&', sondaki '...', boşluk,
küçük harf), `StubSapGuiSession` `LastSelectedMenu`'ye kaydeder. Katalog + keyed DI.

Etkilenen paketler: Paket F (Vision), SAP GUI (Paket C), Studio activity metadata tüketicileri.
Gerekçe: `Vision.Click`'i iki ayrı node'a bölmek açılır menülerde çalışmıyordu — menü, node 1
onu açıp node 2 başlamadan odak kaybıyla kapanıyordu (SAP + genel Win32/Electron uygulamalarında
doğrulandı). Ayrıca ekran yakalama `CAPTUREBLT` ile layered pencereleri (açık menü/popup) de alır
hale getirildi (`ScreenCapture` BitBlt).

---

## Kontrat Değişikliği — 2026-07-14 (Job → Ajan Dispatch)

Studio workflow'larının hangi ajanda koşacağı `Trigger` katmanında tanımlanır hale geldi.
- **`Trigger`** entity: `TargetRobotTags` (virgülle ayrık tag havuzu) + `Priority` eklendi. Migration `AddTriggerRobotTargeting`.
- **Yeni arayüz:** `IRobotDispatcher.SelectRobotAsync(trigger, ct)` — Online + kapasitesi müsait +
  tag'leri kapsayan robotu seçer (en boş kapasite → heartbeat). Impl `RobotDispatcher` (Infrastructure).
- **`TriggerService`** ctor'a `IRobotDispatcher` aldı; JobRun'a `AssignedRobotId` yazar, aday yoksa `Status="Pending"`.
- **`ITriggerRepository`**: `ListTriggersAsync(projectId?, environmentId?, isActive?)` + `GetActiveJobCountsByRobotAsync()`.
- **API:** `GET /api/triggers` (job listesi); `CreateTriggerRequest`/`UpdateTriggerRequest`/`TriggerDto` `TargetRobotTags`+`Priority` içerir.
- **Studio:** `orchestrator/schedules` ekranı (job oluştur/liste/fire, hedef ajan tag seçimi).

Kapsam dışı: Agent handoff/poll protokolü (JobRun'ın ajana gerçekten teslim edilip çalıştırılması) — ayrı spec.
Etkilenen paketler yok (yeni özellik; mevcut in-process çalıştırma placeholder'ı korunur).

---

## Kontrat Değişikliği — 2026-07-15 (Common Loop Nodes)

Workflow node tiplerine `for` eklendi. `Logic.For`, dahil bitişli sayaç döngüsüdür ve
`start`, `end`, `step`, `indexVariable` alanlarını kullanır. `step` sıfır olamaz.
Etkilenen paketler: Domain WorkflowSchema, Infrastructure workflow modeli/runner/katalog,
Studio workflow modeli/canvas/property paneli.
Gerekçe: While, For ve ForEach döngülerini ortak designer bağlantı semantiğine taşırken
sayaç tabanlı döngüyü ayrı bir `Logic.For` node'u olarak sunmak.

## Kontrat Değişikliği — 2026-07-15 (E-Fatura Standart UBL Alanları ve Hassas Girdiler)

E-fatura çalışma zamanı modelleri onaylı tasarımdaki standart UBL-TR kapsamına tamamlandı:
`InvoiceData` düzenleme saati, vergi/tevkifat listeleri ve toplam iskonto alanlarını;
`InvoicePartyData` adres/iletişim alanlarını; `InvoiceLineData` açıklama, iskonto ve vergi
alanlarını taşır. `InvoiceParseOptions` yapılandırılabilir `MaxDepth` içerir. Mevcut alanlar
ve constructor kullanımları geriye uyumludur. E-fatura kaynak girdileri aktivite metadata'sında
`Sensitive` işaretlenir; genel observer maskeleme politikası `Credential` yanında bu tipi de maskeler.

Etkilenen paketler: Infrastructure EInvoice parser/aktiviteleri, BaseRunner observer maskelemesi,
Infrastructure testleri. Workflow JSON şeması ve Domain public arayüz imzaları değişmedi.
Gerekçe: Onaylı e-fatura tasarımındaki standart alan, XML derinlik sınırı ve gerçek fatura
verisinin log/observer olaylarına sızmaması kabul kriterlerini eksiksiz uygulamak.

## Kontrat Değişikliği — 2026-07-15 (E-Invoice UBL Activities)

Workflow aktivite kontratına `EInvoice.ReadUbl` ve `EInvoice.ReadUblBatch` kimlikleri eklenmiştir.
`EInvoice.ReadUbl` girdileri `filePath`, `xmlContent`, `mappings`, `outputBindings`; çıktıları
`invoice`, `lines`, `customFields` olarak tanımlanmıştır. `EInvoice.ReadUblBatch` girdileri
`filePaths`, `xmlContents`, `errorMode`, `mappings`, `outputBindings`; çıktısı `results` olarak
tanımlanmıştır. Tekli ve batch kaynak çiftleri karşılıklı dışlayıcıdır.

Etkilenen paketler: Domain `WorkflowSchema.json`, Infrastructure UBL parser/aktiviteleri/katalog/DI,
Studio aktivite modeli, mapping editörü ve property paneli.
Gerekçe: UBL-TR e-faturalarının tekli veya batch olarak güvenli biçimde okunması, özel XPath/regex
eşlemelerinin tasarlanması ve kararlı çıktıların workflow değişkenlerine bağlanması.

---

## Kontrat Değişikliği — 2026-07-15 (Vision metin çapası ofset tıklama)

`IVisionAutomationChannel.ClickTextOffsetAsync(anchorText, dx, dy, language, matchMode, clickType, timeoutMs)`
eklendi — OCR metin çapasının kelime kutusu merkezinden piksel ofsetle tıklar (etiketin yanındaki
boş input gibi hedefler). Yeni aktivite `Vision.ClickTextOffset` (kategori "Görüntü", capability
`vision`). Yeni picker kind `text-offset` (iki aşamalı: çapa metni seç + hedef nokta tıkla → dx/dy
otomatik). `SpyElementMessage`'a `Kind="text-offset"`, `AnchorText`, `Dx`, `Dy` + `FromTextOffset`;
`StudioHub.SupportedKinds`'e `text-offset`. Referans: çapanın OCR tight kelime kutusu **merkezi**
(picker-zamanı ve runtime aynı basis) → çapa çalışma anında yeniden konumlanır. Kısıt: `dx/dy`
sabit piksel ofsetidir; picker ile runtime **aynı çözünürlük/DPI**'da eşleşir, farklıysa ofset
ölçeklenmez (UiPath CV ile aynı kısıt). Picker OCR dili tasarım-zamanında sabit `tur+eng`.

Etkilenen paketler: Paket F (Vision), Studio picker metadata tüketicileri, Agent UI Spy transport.
Gerekçe: erişilebilirlik ağacı olmayan ekranlarda etiket-yanı boş alanlara tıklama (UiPath CV
"anchor + relative offset" modeli).

---

## Kontrat Değişikliği — 2026-07-16 (Proje Kapsamlı E-Fatura Profilleri)

Domain modeline `EInvoiceProfile` ve değişmez yayın snapshot'larını taşıyan
`EInvoiceProfileVersion` varlıkları eklenmiştir. Profiller `ProjectId` ile proje kapsamında
izole edilir; workflow'lar yayınlanmış profil kimliği ve pozitif sürüm numarasına sabitlenir.
Profil tanımı kök scalar alanları ve birden fazla kullanıcı tanımlı `list<object>` koleksiyonunu
destekler; örnek XML hiçbir kontrat veya kalıcılık modelinde tutulmaz.

Etkilenen paketler: Domain varlıkları, Infrastructure EF/persistence ve profil aktiviteleri,
WebAPI proje profil uçları, Studio proje sekmesi/Designer değişken kataloğu.
Gerekçe: XML eşlemelerini workflow node'undan ayırıp proje kapsamında sürümlü ve tekrar
kullanılabilir hale getirmek; profil şemasını nesne tabanlı RPA değişkenlerine otomatik taşımak.

---

## Kontrat Değişikliği — 2026-07-16 (E-Fatura profil alanı fallback regex)

`EInvoiceFieldDefinition`'a iki opsiyonel alan eklendi: `FallbackRegex` (string?) ve
`FallbackGroup` (string?). Anlamı: alanın birincil kaynağı (XPath/Standard/Notes + mevcut
`Regex` filtresi) hiçbir değer üretmezse, extractor scope'un düz metni (text node'lar
"\n" ile birleştirilmiş) üzerinde `FallbackRegex`'i koşar; `Multiple=true` tüm eşleşmeleri,
`false` ilk eşleşmeyi alır. Mevcut `Regex` alanının anlamı DEĞİŞMEDİ (XPath sonucu üzerine
filtre). Validator: `fallbackGroup` verilmişse `fallbackRegex` zorunlu; desen derlenemezse
BusinessException. Timeout `InvoiceParseOptions.EffectiveRegexTimeout` ile aynıdır.

Etkilenen paketler: EInvoice profil editörü (Studio `einvoice-mapping.model.ts` +
`einvoice-mapping-editor`), `EInvoiceProfileDefinitionValidator`, `EInvoiceProfileExtractor`.
`OutputSchemaJson` üretimi etkilenmez (fallback yalnız değer bulma stratejisidir, tip aynı).
Gerekçe: "önce XPath ile ara, bulamazsan regex ile ham metinde ara" kullanıcı akışı mevcut
modelde ifade edilemiyordu.
---

## Kontrat Değişikliği — 2026-07-16 (Offline Agent Licensing)

Offline lisans belgesi, kurulum kimliği ve agent kimliği için Domain kontratları eklendi.
Agent durumları `PendingActivation`, `Activated`, `Disabled` ve `Deactivated` olarak tanımlandı;
yalnızca `Activated` ve `Disabled` durumları lisans koltuğu tüketir. Taşıma belgeleri immutable
record, kalıcı lisans/agent modelleri `BaseEntity` türevi olarak tanımlandı. Agent aktivasyon kodları
ve credential değerleri yalnızca hash olarak saklanır ve WebAPI DTO'larında açığa çıkarılmaz.

Etkilenen paketler: Domain, Infrastructure persistence/authentication, WebAPI, Agent, Studio ve
LicenseGenerator.

Gerekçe: İnternet erişimi olmayan Orchestrator kurulumlarında kurulum-bağlı, vendor-imzalı lisans
doğrulaması ve aktive agent sayısının güvenli biçimde sınırlandırılması.

---

## Kontrat Değişikliği — 2026-07-16 (Offline Agent Licensing — Task 6: süreklilik kapısı)

**Yeni arayüz:** `src/RPA.Domain/Interfaces/IExecutionContinuationGate.cs` —
`EnsureMayStartNodeAsync(Guid jobRunId, string nodeId, CancellationToken)`. Runner SIRADAKİ node'u
başlatmadan önce danışır; izin yoksa **`RPA.Domain.Exceptions.ExecutionSuspendedException`**
(yeni, `SystemException` türevi; `JobRunId` + `NextNodeId` korunur) fırlatılır.

- **`BaseRunner`** ctor'a opsiyonel `IExecutionContinuationGate? continuationGate` parametresi aldı
  (son parametre, varsayılan `null` → mevcut çağıranlar etkilenmez; kapı yoksa sınır uygulanmaz).
  Kapı yalnız `RunSequenceAsync` içinde, node başlamadan ÖNCE çağrılır — çalışan node hiçbir
  koşulda yarıda kesilmez. Askıya alma `Fail(...)` + checkpoint verisiyle döner.
- **Agent:** `RPA.Agent.Connectivity.ConnectivityLease` (15 dk, `TimeProvider` ile sürülür; 14:59
  geçerli / 15:00 geçersiz), `ConnectivityLeaseContinuationGate` (kapının kira tabanlı
  implementasyonu), `AgentOutbox` + `AgentOutboxOverflowException` (anahtar tabanlı idempotent,
  atomik kalıcı, kapasite taşması AÇIK hata — sessiz kayıt düşürme yok).
- **`JobExecutionOutcome.IsSuspended`** eklendi (türetilmiş özellik; mevcut alanlar değişmedi).
- **Aktivite public imzaları DEĞİŞMEDİ.**

Etkilenen paketler: Domain (yeni arayüz + istisna), Infrastructure (BaseRunner), Agent
(Connectivity + Jobs). Studio/WebAPI/LicenseGenerator etkilenmez.
Gerekçe: Bağlantı koptuğunda çalışan node normal tamamlanma sınırına ulaşmalı, ancak 15 dakikalık
offline kira dolduktan sonra hiçbir yeni node başlamamalıdır (Spec — "Connectivity and Offline Lease").

---

## Kontrat Değişikliği — 2026-07-16 (Offline Agent Licensing — payload edition + müşteri adı)

`OfflineLicensePayload`'a iki **zorunlu** alan eklendi: `Edition` (string) ve `CustomerName`
(string) — tasarım spec'i (`docs/superpowers/specs/2026-07-16-offline-agent-licensing-design.md`,
"Vendor license generation" + "Studio Experience") lisans yükünün sürümü ve müşteri görünen adını
taşımasını ve Studio'nun bunları göstermesini şart koşuyordu; Task 1 bunları atlamıştı.
Boş/whitespace değer `ArgumentException` fırlatır (üretici operatörü her ikisini de girer).

- **Kanonik JSON sırası (imza altına giren baytlar) SABİT ve genişledi:** `schemaVersion,
  licenseId, revision, customerId, customerName, edition, installationId,
  installationPublicKeyFingerprint, maxActivatedAgents, issuedAt, expiresAt, features`.
  Yeni alanlar `customerId`'den hemen sonra (kimlik alanları bir arada) yerleştirildi.
  `CanonicalLicenseSerializer` yazma sırası bu kuralın tek kaynağıdır. `Edition`/`CustomerName`
  kurcalanması artık `MaxActivatedAgents` gibi imza doğrulamasını **bozar** (test edildi).
- **`schemaVersion` 1'de KALDI** — henüz hiçbir gerçek lisans üretilmedi/dağıtılmadı, dolayısıyla
  kırılacak eski imzalı belge yok; sürüm artırmak yalnız ölü bir migration yolu doğururdu.
- `LicenseStatus` (+ `GET /api/license/status` yanıtı) `customerName` ve `edition` alanlarını
  yüzeye çıkarır (lisans yoksa/imza geçersizse null).
- **Studio:** uydurma `edition:<ad>` feature-etiketi konvansiyonu ve `editionOf()` yardımcısı
  **silindi** (backend'de hiç var olmamıştı; ekran üretimde her zaman "—" gösterirdi). Sürüm artık
  `status.edition`'dan okunur; müşteri alanı `customerName ?? customerId` gösterir.

Etkilenen paketler: Domain (`RPA.Domain.Licensing`), Infrastructure lisanslama
(`CanonicalLicenseSerializer`, `LicenseDocumentJson`, `LicenseService`), WebAPI (`LicenseController`),
Studio (`orchestrator/licensing`) ve **henüz yazılmamış `RPA.LicenseGenerator` (Task 9)** —
üretici CLI, operatörden edition + müşteri görünen adı istemek ZORUNDADIR. Task 10 (E2E) henüz yok.

---

## Kontrat Değişikliği — 2026-07-16 (Offline Agent Licensing — agent credential rotasyonu)

Tasarım spec'i (`docs/superpowers/specs/2026-07-16-offline-agent-licensing-design.md`) credential
rotasyonunu şart koşuyordu ("agent credential storage and rotation";
"`POST /api/agents/{id}/rotate-credential` authorizes a controlled credential replacement flow";
"Credential rotation invalidates the previous credential immediately"), ancak Task 4 ucu hiç
kurmamıştı: `IAgentIdentityRepository.RotateCredentialAsync` (Task 1) ve
`EfAgentIdentityRepository.RotateCredentialAsync` (Task 3) **ölü koddu** (sıfır çağıran, test yok),
Task 8 de var olmayan uca UI uydurmayı doğru şekilde reddetmişti. Bu kayıt boşluğu kapatır.

- **Yeni uç:** `POST /api/agents/{id}/rotate-credential` (`AgentsController`) — diğer yönetim
  uçlarıyla **aynı** `LicenseAdministrator` politikası. Yanıt: yeni `RotateCredentialResponse`
  (`agentId`, `credential`). Plaintext **yalnızca bu yanıtta bir kez** döner; loglanmaz, düz
  metin kalıcılaşmaz. Üretim/hash şeması aktivasyon akışıyla **aynıdır** (`SecretGenerator.CreateToken`
  + `SecretHasher.Hash`; ikinci bir şema **icat edilmedi**); kalıcılaşan tek şey hash'tir
  (mevcut `RotateCredentialAsync` üzerinden).
- **Eski credential derhal geçersiz:** token değişimi (`AgentAuthController.Token`) yalnızca
  `AgentIdentity.CredentialHash` karşılaştırması yapar → hash üzerine yazıldığı an eski değer
  hiçbir yerde eşleşmez. Halihazırda verilmiş JWT'ler kendi 10 dk ömürleriyle dolar (`AgentTokenService`).
- **Durum kuralı:** yalnızca `Activated` agent rotasyona uygundur; aksi hâlde `409 AGENT_NOT_ACTIVATED`
  ve credential'a **dokunulmaz**. Gerekçe: `PendingActivation`'ın credential'ı yoktur,
  `Deactivated`'ınki silinmiştir (ikisi de aktivasyon akışından credential alır), `Disabled` ise
  zaten token alamaz — bu durumlarda rotasyon operatöre yanlış bir "yenilendi" izlenimi verirdi.
- **Studio:** `orchestrator/agents` ekranına rotasyon eylemi (yalnız `Activated` satırlarda).
  Task 8 desenleri birebir: eylem öncesi onay, yeni credential **bir kez** bellek-içi signal'den
  gösterilir (kapat/`ngOnDestroy` temizler), local/sessionStorage'a **asla** yazılmaz (test edilir),
  mutasyon sonrası yetkili yeniden okuma (`GET /api/agents` + `GET /api/license/status`).
- **Test altyapısı:** `RPA.WebAPI` → `InternalsVisibleTo("RPA.WebAPI.Tests")` (testler hash şemasını
  kopyalamak yerine üretim `SecretHasher`'ını çağırır); `RPA.WebAPI.Tests`'e
  `Microsoft.EntityFrameworkCore.InMemory` eklendi — rotasyonun token yolunu gerçekten etkilediği
  gerçek EF + gerçek `EfAgentIdentityRepository` ile uçtan uca kanıtlanır.

Etkilenen paketler: WebAPI (`Licensing/AgentsController`), Studio (`orchestrator/agents`),
WebAPI testleri. Domain/Infrastructure **imzaları değişmedi** (mevcut ölü metotlar artık çağrılıyor).
Agent tarafı rotasyon sonrası yeniden yapılandırma akışı (ajanın yeni credential'ı alması) kapsam
dışıdır — operatör credential'ı ajana elle taşır (aktivasyon kodu akışındaki gibi).

---

## Kontrat Değişikliği — 2026-07-16 (Offline Agent Licensing — Task 10: kira kablolaması)

Task 6 `IExecutionContinuationGate` + `ConnectivityLease` + `ConnectivityLeaseContinuationGate`
sözleşmesini tanımlamış ama **hiçbir yerde kablolamamıştı**: kapıyı kimse oluşturmuyordu, dolayısıyla
15 dakikalık offline sınırı **üretimde hiç uygulanmıyordu** (yalnız birim testlerinde vardı). Task 10
bu boşluğu kapatır. **Arayüz imzaları değişmedi.**

- **`AddAgentCore`:** `ConnectivityLease` (**singleton** — scope başına ayrı kira 15 dakikayı sürekli
  yeniden başlatırdı) + `IExecutionContinuationGate` → `ConnectivityLeaseContinuationGate` kaydedildi.
  `BaseRunner` (transient) opsiyonel `continuationGate` parametresini artık DI'dan çözer → sınır
  gerçekten uygulanır. Davranışla doğrulanır (`ConnectivityLeaseWiringTests.
  ResolvedWorkflowRunner_SuspendsAtNodeBoundary_WhenLeaseExpired`).
- **`HeartbeatBackgroundService`** ctor'a opsiyonel `ConnectivityLease? lease` parametresi aldı
  (son parametre, varsayılan `null` → mevcut çağıranlar/testler etkilenmez). **Kirayı besleyen tek
  kaynak budur:** başarılı heartbeat = "son BAŞARILI sunucu doğrulaması" → `RecordServerValidation()`;
  başarısız heartbeat → `MarkDisconnected()` (kira SÜRESİ kısalmaz — çalışan node normal sınırına
  ulaşmalıdır). Heartbeat aralığı (varsayılan 30 sn) 15 dk kiradan çok küçüktür.
- **Kapsam dışı (bilinçli):** `POST /api/agent-auth/refresh-lease` (spec'in API yüzeyinde var,
  implementasyonu YOK — heartbeat kira beslemesidir); hub connect/disconnect olaylarının
  `IsConnected`'ı beslemesi (`IsConnected`/`MarkDisconnected`'ın henüz tüketicisi yok — "yeni iş
  kabulünü durdur" akışı yazılmadı); yeniden bağlanınca askıya alınan node'dan devam.
  `docs/backlog/hybrid-licensing.md` içinde kayıtlıdır.

Etkilenen paketler: Agent (`AgentServiceCollectionExtensions`, `Hosting/HeartbeatBackgroundService`).
Domain/Infrastructure/WebAPI/Studio/LicenseGenerator **etkilenmez**.
Gerekçe: sözleşmesi tanımlanmış ama bağlanmamış bir kapı, uygulanmayan bir güvenlik sınırıdır.

---

## Kontrat Değişikliği — 2026-07-18 (Desktop.SendKeys yapısal tuş dizisi)

`Desktop.SendKeys` artık modifier + özel tuş (Ctrl+A, F4, Home/End/PageUp/PageDown, AltGr, Win…)
gönderebiliyor. Önceki implementasyon `keys`'i FlaUI `Keyboard.Type` ile yalnız **düz metin** olarak
yazıyordu; metadata'nın ima ettiği `'^s'` (Ctrl+S) sözdizimi hiç çalışmıyordu. Tasarım:
`docs/superpowers/specs/2026-07-18-desktop-sendkeys-structural-editor-design.md`.

- **Yeni value object:** `RPA.Domain.ValueObjects.KeystrokeStep` (record) + `KeystrokeStepType`
  enum (`Chord`, `Text`). Bir adım ya modifier(ler) + tek ana tuş (chord) ya da düz metindir;
  opsiyonel `WaitMs` taşır.
- **Yeni parser:** `RPA.Domain.ValueObjects.KeystrokeSequenceParser.Parse(string?)` — ham `keys`
  alanını JSON adım dizisine çevirir; **geçerli JSON dizi değilse tek `Text` adımı** (geriye
  uyumluluk: eski düz-metin `keys` değerleri korunur). Doğrulama hataları (tanınmayan tuş/modifier,
  boş chord, boş metin, boş girdi) → `BusinessException`. Parse tek yerdedir (tek kaynak).
- **`IDesktopAutomationChannel`** kontrat genişledi: yeni **opsiyonel overload**
  `SendKeysAsync(string? selector, IReadOnlyList<KeystrokeStep> steps)`. Mevcut string imza
  **korundu** (geriye uyumlu). `DesktopSendKeysActivity` artık `keys`'i parse edip tipli overload'ı
  çağırır; `Desktop.SendKeys` `keys` parametresi katalogda `pickerKind:"keystroke-sequence"` alır.
- **FlaUI implementasyonu** (`FlaUiDesktopAutomationChannel`): chord → modifier'lar
  `Keyboard.Pressing(VirtualKeyShort)` ile basılı tutulur, ana tuş `Keyboard.Type(VirtualKeyShort)`
  ile gönderilir, ters sırada bırakılır (`try/finally`). AltGr→`RMENU`, Win→`LWIN`. `UnavailableDesktop
  AutomationChannel` yeni overload'ı da uygular.
- **Studio:** yeni `KeystrokeSequenceEditorComponent` (`pickerKind:"keystroke-sequence"`, spy türü
  DEĞİL — editör ipucu, `selector-picker-button`'a null geçer). Modifier checkbox'ları
  (Ctrl/Shift/Alt/AltGr/Win) + gruplu ana-tuş dropdown'u (Harf/Rakam, F1–F12, Gezinme) + tip seçici
  (Tuş vuruşu/Metin) + bekleme (ms) + canlı önizleme. i18n `keystroke.*` (TR + EN).

Etkilenen paketler: Domain (yeni VO + parser), Infrastructure (Desktop aktivitesi + katalog),
Agent (FlaUI kanalı), Studio (yapısal editör). WebAPI/LicenseGenerator etkilenmez.
Doğrulama: Domain 31, Infrastructure 710, Agent 150, Studio 500 — tümü yeşil.

---

## Kontrat Değişikliği — 2026-07-20 (File.List klasör picker + çoklu uzantı + çıktı değişkeni)

Studio designer'da `File.List` node'u üç yönde geliştirildi; bunun için yeni bir 🎯 picker türü
**`folder`** eklendi (mevcut spy/picker altyapısına ek).

- **Yeni picker türü `folder`:** Agent makinesinde native klasör seçim diyaloğu
  (`FolderBrowserDialog`) açar, seçilen tam yolu alanın değerine yazar.
  - **`SpyElementMessage.FromFolder(folderPath, sessionId)`** (`Kind="folder"`, ElementId/Selector =
    yol).
  - **Yeni arayüz `IFolderPicker`** (`RPA.Agent.UISpy`) — `DetectOnceAsync → string?` (iptal → null).
    Impl `WinFormsFolderPicker` (STA thread, `AddAgentCore`'da kayıtlı, yalnız Windows).
  - **`ISpySessionCoordinator`** opsiyonel `IFolderPicker? folderPicker` ctor parametresi aldı ve
    `kind:"folder"` dalını işliyor (timeout ≥300 sn — kullanıcı elle klasör gezinir).
  - **`StudioHub.SupportedKinds`**'e `folder` eklendi.
  - Studio: `SpyKind`, `ActivityPort.pickerKind` ve `generic-property.spyPickerKind` `folder`
    değerini kabul eder; folder sonucu düz string yol olarak `elementId`'den okunur.
- **Çoklu uzantı filtresi:** `File.List` `pattern` alanı artık `;` veya `,` ile ayrılmış birden
  çok deseni destekler (örn. `*.pdf;*.xlsx`). Çalışma zamanı `FileListActivity.ParsePatterns`
  ile desenleri ayırır, `Directory.GetFiles` sonuçlarını birleştirir ve yol bazında
  benzersizleştirir. Studio'da `pattern` alanının altında örnek filtreler gösterilir.
- **Çıktı değişkeni:** `File.List`'e opsiyonel `outputVariable` girdisi (varsayılan `dosyalar`)
  eklendi (Web.GetText deseni). Çalışma zamanı dosya listesini bu değişkene bağlar; Studio designer
  seçilen ada `list<object>` bir workflow değişkeni (dosya şeması: name/path/size/createdAt/
  modifiedAt) oluşturur/günceller — sonraki node'lar (Logic.ForEach) alanlara autocomplete ile erişir.

Etkilenen paketler: Domain public arayüzleri değişmedi; Infrastructure (`FileListActivity`,
`ActivityRegistry`, `SpyElementMessage`), Agent (`SpySessionCoordinator`, yeni `WinFormsFolderPicker`,
DI), WebAPI (`StudioHub` whitelist), Studio (spy service, activity model, generic-property, designer).
Doğrulama: Infrastructure FileOps+SpyElementMessage 30, Agent SpySessionCoordinator 10, Studio 519 —
tümü yeşil.

---

## Kontrat Değişikliği — 2026-07-20 (WorkflowSchema: şema destekli değişken tipleri + File.* DI kaydı)

Üç ilişkili düzeltme; ikisi kontrata dokunuyor:

**1) `WorkflowSchema.json` — `variables[].type` enum'una `object` ve `list<object>` eklendi.**
Aktivite çıktılarına bağlanan değişkenler (`File.List` → `list<object>`, `EInvoice.ReadProfile`
→ `object`, `EInvoice.ReadProfileBatch` → `list<object>`) bu tipleri üretiyordu ama şema enum'u
yalnız `string,int,decimal,bool,DateTime,DataTable,JSON,Credential` kabul ediyordu → **her kaydet
şema doğrulamasında 400** (`WorkflowDesignService.SaveDraftAsync` → BusinessException). Değişiklik
salt eklemeli (mevcut workflow'lar etkilenmez). `WorkflowSchema.json` tek kaynak; Infrastructure'a
linked embedded resource olarak gömülür.

**2) `File.*` aktiviteleri DI'a kaydedildi** (kontrat değil, eksik wiring). `File.Copy/Move/Delete/
List/Zip/Unzip` katalogda vardı ama `WorkflowServiceCollectionExtensions`'ta keyed `IActivity`
kaydı yoktu → runner "Aktivite implementasyonu kayıtlı değil: 'File.List'" atıyordu. Regresyon
guard: `ActivityRegistryCoverageTests.FileActivities_CatalogEntries_HaveExecutableImplementations`.

**3) Studio (kontrat değil):** değişken paneli `normalizeVariable` artık `schema`/`description`'ı
koruyor; tip dropdown'u kendi tipini (`list<object>`) gösteriyor; `outputVariable` alanı serbest
yazılabilir combobox + **commit-on-change** (her tuşta değil, Enter/blur'da yayınlar) → ad yazarken
çöp değişken oluşmuyor. Yapısal görünüm de `File.List`/profil çıktı-şeması bağlamayı çalıştırıyor.

Etkilenen paketler: Domain (`WorkflowSchema.json`), Infrastructure (`WorkflowServiceCollection
Extensions`, validator testleri), Studio (designer + generic-property + variables panel).
Doğrulama: Infrastructure validator 57, ActivityRegistryCoverage 9, Studio 523 — tümü yeşil.

---

## Kontrat Değişikliği — 2026-07-20 (Node kullanıcı adı + yapısal görünüm varsayılanı)

**`WorkflowSchema.json`** node nesnesine opsiyonel **`label`** (string) alanı eklendi — kullanıcının
verdiği okunabilir node adı (örn. "Fatura no girişi"). Salt gösterim; çalışma zamanı semantiği YOK,
runner/aktiviteler okumaz. Salt eklemeli → mevcut workflow'lar etkilenmez.

- **Studio `WorkflowNode`** modeline `label?: string` eklendi.
- **Yapısal görünüm:** adım kartı başlığı `label` varsa onu gösterir (yoksa activity id); aktivite
  id'si ikincil bilgi olarak yanında kalır. Konteyner başlığında `label` tip etiketinin yerini alır,
  tip küçük rozet olarak yanında durur. Satır-içi yeniden adlandırma (✎ düğmesi / başlığa çift tık,
  Enter onaylar, Esc iptal). Yeni `StructuredAction` türü `{ kind: 'rename', target, label }`,
  yeni tree-ops `setItemLabel` (adımda `node.label`, konteynerde `props.label`; boş ad alanı siler).
  `setItemProps` konteynerde `label`'ı korur (özellik panelinin alanı değildir).
- **Designer varsayılan görünümü artık yapısal** (`structuredView = signal(true)`); serbest-graf
  canvas'a mevcut düğmeyle geçilir. Buna bağlı iki düzeltme:
  - `save()` grafı `canvas()?.serialize() ?? currentGraph() ?? workflow()` sırasıyla alır — yapısal
    görünümde canvas yoktur ve hiç düzenleme yapılmadıysa `currentGraph` boştur; önceki hâlinde
    kaydet **sessizce hiçbir şey yapmıyordu**.
  - Yapısal görünüm dikey kaydırması: `.structured-view` flex sütun + `.designer__canvas
    app-structured-view { flex: 1 1 auto; min-height: 0 }`. Önceki sabit `calc(100% - 37px)` paleti
    hesaba katmadığından node eklendikçe tuval taşıyor, scrollbar hiç çıkmıyordu. Zoom
    `transform: scale()` yerine CSS `zoom` ile uygulanır (transform düzeni etkilemediğinden
    büyütmede kaydırma alanı oluşmuyordu).

- **Node kopyalama:** yapısal kartlarda ⧉ düğmesi (`StructuredAction` türü `duplicate`). Yeni
  tree-ops `cloneItem` / `duplicateItem` / `itemAt`: öğe derin kopyalanır (props `structuredClone`
  ile — kopya ile özgün node değer paylaşmaz), her adım node'una TAZE id verilir, kopya özgünün
  hemen ardına eklenir ve seçili gelir. Konteynerlerde tüm lane içeriği özyinelemeli kopyalanır.
  Undo/redo geçmişine normal mutasyon olarak girer.
- **Geç gelen taslak:** `StructuredViewComponent` `workflow` input'unda `null` artık "henüz
  yüklenmedi" demektir ve tohum sayılmaz. Önceki hâlinde taslak HTTP ile geldiğinden ilk (null)
  bağlanma tohum sayılıyor, ağaç kalıcı olarak boş kalıyordu ("Görüntülenecek adım yok"); yalnız
  görünümden çıkıp dönünce (bileşen yeniden kurulunca) doluyordu.

Etkilenen paketler: Domain (`WorkflowSchema.json`), Studio (designer + yapısal görünüm).
Infrastructure/WebAPI/Agent **etkilenmez** (`label` runner tarafından okunmaz).
Doğrulama: Studio 540, Infrastructure şema/validator 59 — tümü yeşil.


## Kontrat Değişikliği — 2026-07-20 (SAP UI Spy "hedef göster" gerçek COM çözücüsü)

SAP node'larının `elementId` alanlarındaki 🎯 picker'ı çalışır hâle getirildi. 2026-07-11 Paket C
kaydında "kalan iş" olarak bırakılan `NullSapGuiElementResolver` (her noktada `null`) yerini gerçek
COM çözücüsüne bıraktı; ayrıca picker artık masaüstü picker'ıyla **aynı** etkileşim deneyimini sunar.

- **Yeni:** `ComSapGuiElementResolver` (`RPA.Infrastructure/UISpy/`) — çalışan SAP Logon'a bağlanır,
  `GuiSession.FindByPosition(x, y, false)` ile noktadaki bileşeni çözer. Kalıcı `SapStaThread`
  üzerinde marshallanır; SAP kapanırsa motor referansı bırakılıp sonraki çağrıda yeniden attach edilir.
  `AddAgentCore` içinde `ISapGuiElementResolver` olarak kayıtlı (Attended + Windows).
- **Yeni:** `SapGuiAutomation` (`RPA.Infrastructure/SAP/`) — SAPGUI attach yolu (ProgID + ROT,
  SAP Logon otomatik başlatma) ve COM koleksiyon yardımcıları `ComSapGuiSessionFactory`'den buraya
  taşındı; fabrika ile çözücü **tek kaynağı** paylaşır (kopya yok). Fabrikanın public davranışı aynı.
- **Yeni:** `SapComponentDescender` + `ISapComponentAccessor` — `FindByPosition` çoğu ekranda noktayı
  içeren **konteyneri** (`GuiUserArea`, subscreen) döndürür; kullanıcı metin alanını göstermek isterken
  **frame seçiliyordu**. Çözücü artık çocuklara inip noktayı içeren en derin bileşeni alır (çakışan
  kardeşlerde en küçük alanlı). COM'dan bağımsız saf mantık → birim testli (6 test).
- **Yeni:** `SapElementId.Normalize` — SAP mutlak ID'si (`/app/con[0]/ses[0]/wnd[0]/usr/...`)
  oturumdan bağımsız göreli forma (`wnd[0]/usr/...`) indirgenir. Aksi halde tasarım anındaki
  bağlantı/oturum indeksi ID'ye gömülür ve çalışma anında başka oturumda kırılır.
- **`ISapGuiElementResolver` kontratı genişledi (salt eklemeli):** `void Highlight(int x, int y)`
  varsayılan (no-op) üye. `SapGuiElementDetector.HighlightAt` bunu yüzeye çıkarır. Mevcut
  implementasyonlar ve testler etkilenmez.
- **`SapGuiSinglePicker` yeniden yazıldı:** önceden `DetectElementUnderCursor()`'ı **tek sefer**
  çağırıp dönüyordu — imleç 🎯'e basıldığı an tarayıcının üzerindeydi, dolayısıyla gerçek çözücüyle
  bile doğru element seçilemezdi. Artık `FlaUiDesktopSinglePicker` ile birebir aynı döngü: tasarımcı
  penceresi küçültülür → hover'da element vurgulanır (`GuiVComponent.Visualize`) → sol tık onaylar →
  `Esc` iptal eder → pencere eski yerleşimine döner. **Ctor değişti** (yeni: `IPickerWindowManager`,
  `ILogger`).
- **Yeni:** `IPickerWindowManager` / `Win32PickerWindowManager` / `NoopPickerWindowManager`
  (`RPA.Agent/UISpy/`) — picker'ların "tek ekran" pencere yönetimi soyutlandı (test edilebilirlik).

**Kapsam dışı:** SAP ID'leri hâlâ `elementId`'dir (selector DEĞİL) — SAP GUI Scripting'in adresleme
birimi budur; `Sap.Gui.*` katalog imzaları değişmedi.
**Ön koşul:** SAP GUI kurulu + SAP Logon açık + GUI Scripting etkin (istemci ve sunucu tarafında).

Etkilenen paketler: Paket C (SAP), Agent UI Spy. Domain/WebAPI/Studio **etkilenmez**.
Doğrulama: Infrastructure 733, Agent 155, Domain 31, WebAPI 129 — tümü yeşil; Release derleme 0 hata.
Gerçek COM yolu (FindByPosition/Visualize) SAP GUI kurulu makinede elle doğrulanmalıdır.


## Kontrat Değişikliği — 2026-07-21 (Sap.Gui.SendVKey — SAP sanal tuşları)

SAP'ta F8 (Çalıştır), F3 (Geri), F4 (Arama yardımı), F12 (İptal), Enter gibi tuşlar buton
tıklamaktan daha yaygın ve daha sağlamdır (ekran düzeni değişse de çalışır, odaktan bağımsızdır).
`sendVKey` yalnızca kanalın İÇİNDE kullanılıyordu (login'de Enter); dışarıya hiç açılmamıştı.

- **`ISapGuiChannel` genişledi (salt eklemeli):** `SendVKeyAsync(int vKey, string windowId = "wnd[0]")`.
  İç soyutlama `ISapGuiSession.SendVKeyAsync(int vKey, string windowId)`.
  `ComSapGuiSession` `findById(windowId)` → `sendVKey` (PENCERE üzerinde, element üzerinde değil);
  `StubSapGuiSession` `LastSentVKey`'e kaydeder. Aralık dışı VKey (`<0` / `>48`) → `BusinessException`.
- **Yeni aktivite `Sap.Gui.SendVKey`** (kategori "SAP", capability `sap-gui`): `key` (varsayılan
  "F8") + opsiyonel `windowId` (varsayılan `wnd[0]`; iletişim kutusu için `wnd[1]`).
  Katalog `ActivityRegistry.RegisterSapGui`; keyed DI `SapGuiServiceCollectionExtensions`.
- **Yeni:** `SapVirtualKey.Parse` (`RPA.Infrastructure/SAP/`) — kullanıcı-dostu ad → SAP VKey
  numarası. SAP standart tablosu: 0=Enter, 1–12=F1–F12, 13–24=Shift+F1–F12, 25–36=Ctrl+F1–F12,
  37–48=Ctrl+Shift+F1–F12. Ayrıca isimli kısayollar (Save/Ctrl+S=11, Back=3, Exit=15, Cancel=12,
  Execute=8) ve düz numara kabul edilir. Tanınmayan girdi → `BusinessException` (tasarım hatası).

**Kapsam dışı (bilinçli):** Studio'da tuş seçimi şimdilik **düz metin alanı** (varsayılan "F8");
açılır liste yazılmadı. `pickerKind` **verilmedi** — `generic-property.component.ts` bilinmeyen
pickerKind değerlerini spy türü olarak `spy.pick`'e geçirir ve StudioHub "Desteklenmeyen spy tipi"
hatası verirdi; yeni bir editör türü Studio tarafında ayrıca ele alınmalıdır. Sıralı çok-tuş
gönderimi (`F3,F3,F12`) de kapsam dışıdır.

Etkilenen paketler: Domain (`ISapGuiChannel`), Infrastructure (SAP kanal/oturum/aktivite/katalog/DI).
Agent/WebAPI/Studio **etkilenmez**. `ISapGuiChannel`/`ISapGuiSession`'ın başka implementasyonu yoktur.
Doğrulama: Infrastructure 783, Agent 155 — tümü yeşil; Release derleme 0 hata. Gerçek `sendVKey`
COM çağrısı SAP GUI kurulu makinede doğrulanmalıdır.


## Kontrat Değişikliği — 2026-07-21 (SAP picker: kök pencere tespiti + tuş ile onay)

2026-07-20 SAP picker kaydının iki takibi. Kullanıcı testinde picker "pencereyi küçültüyor ama
çerçeve yok, tıklama işlemiyor" davranışı gözlendi.

**1a) Pencere sınıfı kapısı KALDIRILDI (2026-07-21, saha testi sonrası).** Aşağıdaki (1) maddesi
kapıyı child yerine köke taşıyarak düzeltmeye çalıştı; **yetmedi**. Saha logu:
`(2404,181) SAP penceresi değil - child sınıfı: '#32768', kök sınıf: '#32768'` — `#32768` Windows'un
MENÜ pencere sınıfıdır. `WindowFromPoint` en derin child'ı verir ve SAP ekranında bu bir alt kontrol,
açık menü veya popup olabilir; hiçbiri `SAP_FRONTEND*` değildir. Sınıf tabanlı kapı yapısal olarak
yanlış negatif üretir ve picker'ı tamamen ölü bırakıyordu (çerçeve yok, tıklama tepkisiz).
**`SapGuiElementDetector` artık pencere sınıfına bakıp erken dönmez** — her noktada doğrudan
`ISapGuiElementResolver.ResolveAt`'i çağırır. Otorite SAP'tır: `FindByPosition` bir bileşen
döndürüyorsa nokta zaten SAP oturumundadır. `IsSapWindow` ve sınıf okumaları yalnızca `Diagnose`
çıktısında ipucu olarak kalır. Regresyon guard: `DetectAt_AlwaysAsksResolver_EvenWhenWindowClassLooksNonSap`.

**Tanılama (yeni).** Sessiz başarısızlık kaldırıldı: `ISapGuiElementResolver.LastError` (varsayılan
null üye) + `SapGuiElementDetector.Diagnose(x, y)`; SAP picker element bulamadığında **2 saniyede bir
`Warning`** seviyesinde sebebi loglar (Agent varsayılan seviyesi Information olduğundan görünür).

**1) Kök pencere tespiti (kısmi düzeltme — 1a tarafından geçersiz kılındı).** `SapGuiElementDetector` noktanın SAP penceresinde olup
olmadığını `INativeWindowApi.GetWindowClassAt` ile sınıyordu; bu `WindowFromPoint` kullanır ve
noktanın altındaki **alt (child) kontrolü** döndürür — SAP metin alanının üzerindeyken sınıf
`SAP_FRONTEND*` DEĞİLDİR. Sonuç: detector COM çözücüsünü **hiç çağırmadan** null dönüyordu; picker
hiçbir zaman element üretmiyordu. (2026-07-20'de değiştirilen çözücü doğruydu, bu kapı öndeydi.)
- **`INativeWindowApi` genişledi (salt eklemeli):** `string? GetRootWindowClassAt(int x, int y)`
  varsayılan üye (`GetWindowClassAt`'e düşer → mevcut implementasyonlar/testler etkilenmez).
  `Win32NativeWindowApi` `GetAncestor(hWnd, GA_ROOT)` ile top-level pencereye çıkar.
- Detector artık child **veya** kök sınıfı SAP ise devam eder; reddederken ikisini de loglar.

**2) Seçim onayı tuş kombinasyonuyla (UX DEĞİŞİKLİĞİ).** SAP picker'ı sol tıklama ile onaylıyordu;
SAP ekranında fare tıklaması alanı/butonu **tetikler** (yanlışlıkla transaction çalıştırabilir).
Onay artık kullanıcının Studio'da seçtiği tuş kombinasyonudur — image picker'ın dondurma tuşu
altyapısı yeniden kullanıldı.
- **`ISapGuiSinglePicker.DetectOnceAsync` imzası değişti:** yeni ilk parametre `ImagePickerOptions`
  (`IImageRegionPicker`/`ITextOffsetPicker` ile aynı desen). Varsayılan `F2`; Ctrl/Shift/Alt
  opsiyonel. Modifier'lar basış anında doğrulanır. `Esc` iptal aynen korundu.
- `SpySessionCoordinator` `kind:"sap"` için `optionsJson`'ı parse edip geçirir ve **timeout'u
  ≥300 sn**'ye çıkarır (kullanıcı hedef SAP ekranına elle gider; 60 sn yetmiyordu).
- **Studio:** tuş/modifier kontrolleri artık `sap` pickerKind'ında da görünür
  (`selector-picker-button`); ekran dondurma modu/geri sayımı yalnız `image`'a özgü kalır.
  Yeni i18n `picker.confirmKey` (TR + EN). SAP artık `spy.pick`'e seçenek **gönderir** (önceden
  `undefined`) — bunu doğrulayan iki Studio testi güncellendi.

**Ek — SAP picker "önce hazırlık, sonra tıkla" modu.** SAP'ta F1–F12 tuşlarının **tamamı**
transaction kısayoludur, dolayısıyla tuşla onay her ekranda uygun değildir. `kind:"sap"` artık
`ImagePickerOptions.CaptureMode` değerlerinin ikisini de kullanır:
- `"f2"` — seçim turu hemen başlar, onay seçilen tuş kombinasyonudur.
- `"timer"` — `DelaySeconds` bir **HAZIRLIK** süresidir (seçim değil): kullanıcı bu sürede hedef SAP
  ekranını açar; süre bitince seçim turu başlar, imleç altındaki element kırmızı çerçeveyle
  vurgulanır ve seçim **sol tıklama** ile onaylanır. Geri sayım sırasında da `Esc` iptal eder.
  Tıklama turu başlamadan önce sol butonun serbest olması beklenir (🎯'e/hazırlık sırasında yapılan
  tıklamalar seçim sanılmasın).

Picker `RunSelectionLoopAsync` (vurgu + onay) ve `WaitForCountdownAsync` (hazırlık) olarak ayrıldı.
Studio'da mod açılır listesi SAP için de görünür; etiketler SAP'ta farklıdır
(`picker.modeKeyConfirm`, `picker.modeTimerSap`, `picker.countdownSeconds` — TR + EN).

**Bilinen sınır (kapsam dışı):** tuşla onay modunda picker `GetAsyncKeyState` ile yoklama yapar,
tuşu **tüketmez** — basılan tuşu SAP da alır (F8 hem seçimi onaylar hem SAP'ta Çalıştır'ı tetikler).
Tüketen çözüm `RegisterHotKey`'dir (`GdiImageRegionPicker` bunu kullanır). Zamanlayıcı modu bu
sorundan tamamen muaftır ve SAP için önerilen moddur.

**Not:** `ImagePickerOptions` adı artık image'a özgü değildir (SAP onay tuşunu da taşır); tip
yeniden adlandırılmadı — image/text-offset/sap tüketicilerinin tümüne dokunmak gerekirdi.
`CaptureMode`/`DelaySeconds` SAP yolunda yok sayılır.

Etkilenen paketler: Infrastructure (UI Spy detector), Agent (SAP picker + koordinatör), Studio
(picker düğmesi + i18n). Domain/WebAPI **etkilenmez**.
Doğrulama: Infrastructure 785, Agent 155, Studio 540 — tümü yeşil; Release derleme 0 hata.
Gerçek COM yolu hâlâ SAP GUI kurulu makinede doğrulanmayı bekliyor.


## Kontrat Değişikliği — 2026-07-21 (SAP picker: adreslenebilirlik zorunluluğu)

Saha testi COM yolunun ÇALIŞTIĞINI kanıtladı (öz-test: 2 oturum, `wnd[0]` başlığı + ekran
dikdörtgeni okundu; seçim gerçekleşti). Ancak sonuç `element seçildi  (null)` idi: `FindByPosition`
bir bileşen döndürdü, `SapComponentDescender` noktayı kapsayan bir alt nesneye indi, ama o nesnenin
`Id`'si okunamıyordu → **boş `elementId`** alana yazılıyordu (sessiz başarısızlık: kullanıcıya
"seçildi" denir, alan boş kalır, aktivite çalışmaz).

- **`ISapComponentAccessor` genişledi:** `string? GetId(object node)`. `SapComponentDescender.Deepest`
  artık yalnız dikdörtgene değil **adreslenebilirliğe** de bakar: noktayı kapsayan en derin
  **ID'si okunabilen** bileşeni döndürür; ID'siz bir dala inilirse ID'si okunabilen en son ataya
  geri düşer (ID'siz ara nesnenin ALTINDA adreslenebilir bir alan varsa ona inmeye devam eder).
- **`ComSapGuiElementResolver`**: `Id` boş çıkarsa element **bulunamadı sayılır** (null döner) ve
  sebep `LastError`'a yazılır (tip + metin ile). Boş ID artık hiçbir koşulda Studio'ya gönderilmez.
- **Tanılama (bu turda eklendi):** `ISapGuiElementResolver.SelfTest()` (varsayılan üye) +
  `SapGuiElementDetector.SelfTest()` — imleç konumundan BAĞIMSIZ bağlantı kanıtı (attach durumu,
  oturum sayısı, her oturumun `wnd[0]` başlığı ve ekran dikdörtgeni). Picker başlangıcında
  `Information` seviyesinde loglanır. Ayrıca Esc'te oturum özeti (kaç örnek / kaçı çözüldü).
  Gerekçe: kullanıcı fareyi SAP'ın üzerinde tutarken konsola bakamıyor; imlece bağlı tanılama
  yanıltıcı örnekler üretiyordu (fare konsola giderken üzerinden geçtiği Outlook/Terminal
  pencereleri loglanıyordu).

**Onay tuşu olarak Caps Lock (yeni).** `ImagePickerOptions` artık `HotKey` değeri olarak
`"CapsLock"`i kabul eder (`VirtualKey` = `VK_CAPITAL` 0x14); F1–F12 desteği aynen korunur,
tanınmayan değer yine `F2`ye düşer. Gerekçe: SAP'ta F1–F12'nin **tamamı** transaction kısayoludur
(F2 seçim onaylarken SAP'ta da fonksiyon tetikliyordu); Caps Lock hiçbir SAP fonksiyonunu
tetiklemez. Studio tuş listesine `CapsLock` eklendi ve **`pickerKind:"sap"` için varsayılan
yapıldı** (kullanıcı değiştirebilir; image picker varsayılanı `F2` olarak kaldı).

Etkilenen paketler: Infrastructure (UI Spy), Agent (SAP picker logları + tuş ayrıştırma),
Studio (picker düğmesi). Domain/WebAPI etkilenmez.
Doğrulama: Infrastructure 792, Agent 162, Studio 540 — yeşil; Release derleme 0 hata.
**Hâlâ doğrulanmadı:** gerçek SAP alanında dolu bir `elementId` üretilmesi (saha testi bekleniyor).


## Kontrat Değişikliği — 2026-07-21 (SAP picker: FindByPosition sonucunun açılması)

Önceki turun "adreslenebilirlik" düzeltmesi yetmedi. Saha logu kesin kanıtı verdi:
`FindByPosition`, imleç **SAP penceresinin tamamen dışındayken** (Windows Terminal üzerinde) bile
boş olmayan bir nesne döndürüyor ve o nesnenin `Id`/`Type`/`Text` üçü birden okunamıyor. SAP dışı
bir noktada gerçek bileşen dönemeyeceğine göre elde tutulan şey **boş bir `GuiComponentCollection`**.

**Kök neden:** `Innermost` koleksiyon/bileşen ayrımını `Count` özelliğine bakarak yapıyordu. SAP
Scripting koleksiyonları eleman sayısını sürüme/tipe göre `Count` **veya `Length`** ile yayınlar;
`Count` okunamayınca sayı 0 sanılıyor, nesne "koleksiyon değil" kabul edilip **koleksiyonun kendisi**
bileşen olarak geri veriliyordu → `Id` boş → picker hiçbir noktada çalışmıyordu.

- **`SapGuiAutomation.GetCollectionCount`** artık `Count` ve `Length`'i sırayla dener.
- **`ISapComponentAccessor` genişledi:** `IReadOnlyList<object> GetCollectionItems(object node)`.
- **Yeni:** `SapComponentDescender.Unwrap(found, accessor)` — `FindByPosition` sonucunu gerçek
  bileşene açar. **Tip tahmini yapmaz**; ölçüt `Id`'nin okunabilmesidir: nesnenin kendisi
  adreslenebiliyorsa o, değilse koleksiyonun en içteki (son) adreslenebilir elemanı, hiçbiri
  değilse `null`. Boş koleksiyon artık doğru şekilde "sonuç yok" demektir.
- `Innermost` silindi (yerini `Unwrap` aldı). `ResolveAt` her çağrıda `LastError`'ı temizler —
  önceki noktanın hatası sonrakinin tanılamasına taşınmıyor.

**Ek — çağrı hatalarının yutulması giderildi (aynı gün).** `FindComponentAt` içindeki
`catch { continue; }` SAP'ın gerçek hatasını yutuyor, sonra durum "FindByPosition boş" diye
raporlanıyordu; **"hata fırlattı" ile "boş döndü" ayırt edilemiyordu** (saha logunda 408 örneğin
tamamı "boş" göründü, oysa çağrı hiç başarılı olmamış olabilir). Artık:
- `InvokeFindByPosition` iki imzayı da dener — `(x, y, scrollToElement)` ve `(x, y)` — çünkü SAP
  sürümleri arasında imza değişir; yanlış argüman sayısı sessiz başarısızlık üretiyordu.
- Hatalar toplanır ve `LastError`'a yazılır (tanılamada görünür).
- **Öz-test artık aktif ölçüm yapar:** her oturumun `wnd[0]` dikdörtgeninin TAM MERKEZİNDE
  `FindByPosition` dener ve sonucu (bulunan element ID'si / adreslenebilir bileşen yok / hata
  detayı) loglar. Fare konumundan bağımsız kesin kanıt.

Etkilenen paketler: Infrastructure (UI Spy + SAP COM yardımcıları). Agent/Domain/WebAPI/Studio
imzaları etkilenmez. Doğrulama: Infrastructure 797, Agent 162 — yeşil; Release derleme 0 hata.
**Hâlâ doğrulanmadı:** gerçek SAP alanında dolu `elementId` üretilmesi (saha testi bekleniyor).


## Kontrat Değişikliği — 2026-07-21 (SAP picker: konum tespiti ağaç taramasına geçti)

`FindByPosition` bu ortamda **hiçbir noktada** sonuç üretmedi (saha: 268/268 ve 408/408 boş), üstelik
hata da fırlatmadı — yani imza/argüman sorunu değil, çağrı sessizce boş dönüyor. Kovalamak yerine
ona olan bağımlılık kaldırıldı.

**Dayanak:** öz-test `findById("wnd[0]")` + `ScreenLeft/ScreenTop/Width/Height` okumalarının bu
ortamda ÇALIŞTIĞINI kanıtladı (wnd[0] dikdörtgeni doğru okundu: x=1912, y=-8, 1936x1048). Hit-test
için SAP'a ihtiyaç yok: imleci içeren en derin adreslenebilir bileşen pencere ağacından bulunabilir.

- **Yeni birincil yol `FindByWindowTree`:** her oturumun `wnd[0]`…`wnd[9]` pencereleri gezilir,
  noktayı içeren pencereler arasından **en yüksek indeksli** olan seçilir (açık iletişim kutusu ana
  ekranı kapatır), ardından mevcut `SapComponentDescender.Deepest` ile en derin adreslenebilir
  bileşene inilir. Yalnızca bu ortamda çalıştığı KANITLANMIŞ API'leri kullanır.
- `FindByPosition` **ikincil** yol olarak korundu (bazı sürümlerde özel kontrollerde isabetli olabilir).
- **`SapGuiAutomation.EnumerateSessions` tekrarları eler** (`Children` ve `Sessions` çoğu sürümde
  AYNI oturumları yayınlıyor; öz-testte tek oturum iki kez görünüyordu ve her tarama iki kat sürüyordu).
- Öz-test artık her iki yolu da pencere merkezinde ölçer ve `wnd[0]` çocuk sayısını raporlar.

Etkilenen paketler: Infrastructure (UI Spy + SAP COM yardımcıları). Agent/Domain/WebAPI/Studio
imzaları etkilenmez. Doğrulama: Infrastructure 797, Agent 162 — yeşil; Release derleme 0 hata.
**Hâlâ doğrulanmadı:** gerçek SAP alanında dolu `elementId` üretilmesi (saha testi bekleniyor).


## Kontrat Değişikliği — 2026-07-21 (SAP picker ÇALIŞIYOR + vurgu temizleme + harf tuşları)

**Saha doğrulaması geldi:** `element seçildi wnd[0]/tbar[0]/okcd (GuiOkCodeField)` — ağaç taraması
gerçek ve doğru element ID'si üretiyor. Öz-test `wnd[0]` çocuklarının (6 adet, konumlarıyla)
okunabildiğini gösterdi. `FindByPosition` aynı ortamda hâlâ "adreslenebilir bileşen yok" dönüyor
(eleman sayısı 2) → ağaç taramasına geçmek doğru karardı; ikincil yol olarak kalıyor.

**1) Vurgu çerçevesi temizleme (hata).** `Visualize(true)` çağrılıyor ama hiç `false` çağrılmıyordu;
SAP'ın çerçevesi kendiliğinden silinmediğinden gezinirken ekranda çerçeveler birikiyordu.
- `ISapGuiElementResolver.ClearHighlight()` (varsayılan no-op üye) + `SapGuiElementDetector.ClearHighlight()`.
- `ComSapGuiElementResolver` son vurgulanan bileşeni tutar; yeni vurgudan ÖNCE eskisini kapatır.
  Ekran değişip bileşen kaybolduysa hata yutulur (çerçeve zaten yok).
- Picker `finally` bloğunda `ClearHighlight()` çağırır → seçim/iptal sonrası ekranda çerçeve kalmaz.

**3) Tuş artık ONAY değil TETİKLEYİCİ (akış revizyonu).** Ctrl+T ile onaylama çalışmadı. Akış
her iki modda da tekleştirildi: **hazırlık → seçim turu → SOL TIKLA ile seç.** Hazırlığın bitiş
sinyali moda göre değişir — `"timer"` geri sayım, `"f2"` kullanıcının seçtiği tuş kombinasyonu
(varsayılan Ctrl+T, süre sınırsız: oturum zaman aşımına kadar bekler). Seçim **her zaman** sol
tıklamayla alınır; tuş yalnızca süreci başlatır.
- Yeni `WaitForHotKeyAsync` (Esc ile iptal edilebilir; tuş serbest bırakılmış halde başlar ki
  🎯'e basarken sızan tuş anında tetiklemesin). `RunSelectionLoopAsync` sadeleşti — artık
  `options`/`confirmWithClick` parametresi almıyor, onay tek biçimde tıklamadır.
- Studio etiketleri güncellendi: `picker.modeKeyConfirm` = "Tus ile baslat, tikla ile sec",
  `picker.modeTimerSap` = "Geri sayim, sonra tikla ile sec", `picker.confirmKey` =
  "Secimi baslatma tusu" (TR + EN).

**5) Bayat COM motoru → picker sessizce ölüyordu (hata).** Saha: Ctrl+T doğru iletildikten sonra
**0/272** örnek çözüldü — SAP penceresinin İÇİNDE bile. Ağaç taraması buna izin vermez (`wnd[0]`
noktayı içeriyorsa `Deepest` en kötü ihtimalle `wnd[0]`'ı döndürür), dolayısıyla tek açıklama oturum
listesinin BOŞ olmasıydı. `SapGuiAutomation.EnumerateSessions` COM hatalarını yutar ve boş liste
döner; önbelleklenen `_engine` bayatladığında (araya başka bir SAP oturumu/iş akışı girdiğinde)
hiçbir istisna oluşmadan her nokta "sonuç yok" oluyordu — ve tanılama bunu yanlışlıkla
"FindByPosition boş" diye raporluyordu.
- Yeni `GetSessions()`: oturum listesi boşsa motoru bırakıp **bir kez yeniden attach** ederek tekrar
  dener. Hâlâ boşsa `LastError` = "SAP oturumu görünmüyor (motor bayatlamış olabilir…)".
- `FindComponentAt` ve öz-test artık bu yolu kullanır.
- `Diagnose` metni düzeltildi: birincil yol ağaç taramasıdır, mesaj artık `FindByPosition`'ı
  suçlamıyor ("bu noktada SAP elementi yok").

**4) Studio seçenekleri `sap` için hiç göndermiyordu (hata).** `spy.service.ts` seçenek JSON'unu
yalnızca `image`/`text-offset` türlerinde iletiyordu; `sap` listede yoktu → Studio'da Ctrl+T seçilse
bile ajana `null` gidiyor, ajan varsayılana (`F2`) düşüyordu. **Aynı satır timeout'u da belirliyordu:**
`sap` (ve `folder`) 60 sn'lik varsayılanla kalıyordu, oysa ajan bu türlerde ≥300 sn bekler — kullanıcı
hedef ekranı hazırlarken Studio erken pes edip oturumu düşürüyordu. `manualPreparation` listesi
(image, text-offset, sap, folder) hem seçenek gönderimini hem uzun timeout'u (≥360 sn) kapsar.
Regresyon guard: `spy.service.spec.ts` → "passes sap picker options as JSON to the hub".

**2) Harf tuşları (Ctrl+T).** SAP'ta F1–F12'nin tamamı transaction kısayolu olduğundan onay tuşu
olarak harf + modifier gerekiyordu. `ImagePickerOptions.NormalizeHotKey` artık tek harf (A–Z) kabul
eder; `VirtualKey` harfin ASCII kodudur (0x41–0x5A). CapsLock ve F1–F12 desteği aynen korunur;
iki harfli/rakam/sembol girdi yine `F2`ye düşer. Studio tuş listesine A–Z eklendi ve
**`pickerKind:"sap"` varsayılanı `Ctrl+T`** yapıldı (önceki CapsLock varsayılanının yerine).

Etkilenen paketler: Infrastructure (UI Spy), Agent (tuş ayrıştırma + picker), Studio (picker düğmesi).
Domain/WebAPI etkilenmez. Doğrulama: Infrastructure 797, Agent 169, Studio 540 — yeşil.


## Kontrat Değişikliği — 2026-07-21 (Katalog ↔ aktivite parametre adı uyuşmazlıkları)

Saha: `Sap.Gui.GridRead` picker'dan doğru ID (`wnd[0]/usr/cntlGRID1/shellcont/shell`) alıyor ama
çalışma anında **"'gridId' parametresi boş olamaz"** ile patlıyordu. Sebep: katalog girdiyi
`gridElementId`, aktivite ise `gridId` adıyla okuyordu — Studio doğru alanı dolduruyor, runtime
başka ada bakıyordu.

**Bu bir sınıf hatasıydı, tek örnek değil.** Yeni regresyon testi
(`ActivityRegistryCoverageTests.Catalog_ParameterNames_MatchActivityMetadata`) katalog ile her
aktivitenin KENDİ `GetMetadata()` bildirimini karşılaştırır ve **8 uyuşmazlık** buldu:

| Aktivite | Düzeltme |
|---|---|
| `Sap.Gui.GridRead` | katalog `gridElementId`/`data` → **`gridId`/`rows`** (runtime'ın okuduğu adlar) |
| `Sap.Gui.Connect` | katalogdaki `session` çıktısı **silindi** (aktivite hiçbir çıktı döndürmüyor) |
| `Sap.Gui.Screenshot` | katalog `path` → **`screenshot`** (PNG bayt dizisi) |
| `Web.Goto`, `Web.FrameSwitch` | aktivitenin döndürdüğü **`session` çıktısı** kataloga eklendi |
| `Email.DownloadAttachment` | runtime'ın okuduğu **`credentialName` + `folder`** girdileri kataloga eklendi (Studio bu alanları hiç göstermiyordu) |
| `Api.HttpRequest` | aktivite metadata'sı yalnız `url` bildiriyordu; runtime'ın okuduğu `method`/`headers`/`body`/`authType`/`credentialName`/`timeoutSeconds` eklendi (`timeoutSeconds` kataloga da) |

**Kural:** katalog (`ActivityRegistry`) Studio'nun formu çizdiği kaynak, aktivitenin `GetMetadata()`'sı
runtime sözleşmesi; **ikisi aynı parametre adlarını kullanmak ZORUNDA** — test artık bunu tüm
aktiviteler için zorlar. Yeni aktivite eklerken ad uyuşmazlığı derleme değil test hatası verir.

**Ek — `Sap.Gui.GridRead` çıktı değişkeni.** Aktivite satır listesini yalnızca sabit `rows` adına
yazıyordu; kullanıcı sonucu kendi seçtiği bir workflow değişkenine bağlayamıyordu. `File.List`
deseni uygulandı: opsiyonel `outputVariable` girdisi (varsayılan `gridSatirlari`) — verilirse satır
listesi o değişkene de bağlanır (`rows` çıktısı geriye uyumluluk için korunur). Studio designer
seçilen ada `list<object>` bir workflow değişkeni oluşturur/günceller.
**File.List'ten farkı:** ALV kolonları çalışma anında (transaction/layout'a göre) belirlendiğinden
sabit alan şeması ÜRETİLEMEZ — değişken şemasız `list<object>`'tir, satır alanlarına ALV teknik
kolon adıyla erişilir (alan-düzeyi autocomplete yoktur).

**Etki:** `Sap.Gui.GridRead` node'u olan mevcut workflow'lar alanı **yeniden seçmelidir**
(`gridElementId` değeri artık okunmuyor). Diğer değişiklikler salt eklemeli/düzeltici.
Doğrulama: Infrastructure 798 — yeşil; Release solution derlemesi 0 hata.


## Kontrat Değişikliği — 2026-07-21 (ALV grid kolon sözleşmesi — tasarım anında yapı)

`Sap.Gui.GridRead` çıktısının yapısı yalnızca çalışma anında belli oluyordu; kullanıcı çalışma
anında süreç tasarlayamayacağı için satır alanları tasarımda görünmüyordu. Kolonlar artık
**seçim anında SAP'tan okunur** ve bir SÖZLEŞME olarak node'a yazılır.

- **`SapGuiElement.Columns`** (`IReadOnlyList<string>?`) eklendi — ALV grid seçildiğinde tasarım
  anındaki teknik kolon adları; grid dışı elementlerde `null`. **Tip adına göre tahmin yapılmaz**,
  ölçüt `ColumnOrder` koleksiyonunun okunabilmesidir (`TryReadGridColumns`).
- **`SpyElementMessage.Columns`** ile Studio'ya taşınır (salt eklemeli; diğer picker türleri etkilenmez).
- **Studio:** 🎯 grid seçiminde `elementId` yanında **`columns`** alanı da doldurulur
  (`generic-property.onPicked`). Designer bu kolonlardan satır şeması üretir → `{{satir.MATNR}}`
  autocomplete tasarım anında çalışır. Kolon yoksa şemasız `list<object>` (eski davranış).
- **Yeni aktivite girdisi `columns`** (JSON dizi, opsiyonel). Verildiğinde çalışma anı satırları
  sözleşmeye göre şekillenir:
  - tasarımda olup çalışma anında **gizlenmiş/eksik** kolon → `null` (alan yine de vardır, sonraki
    node ifadeleri kırılmaz),
  - çalışma anında **fazladan** gelen kolon → **yok sayılır**,
  - `columns` verilmemişse veya JSON bozuksa → sözleşmesiz davranılır (tüm çalışma-anı kolonları;
    bozuk sözleşme sessizce satırları BOŞALTMAZ).
  Sözleşme hem `rows` çıktısına hem `outputVariable`'a bağlanan listeye uygulanır.

Etkilenen paketler: Domain (VO), Infrastructure (UI Spy + GridRead + katalog), Studio (spy modeli,
generic-property, designer). Agent/WebAPI imzaları değişmedi.
Doğrulama: Infrastructure 805, Agent 169, Studio 543 — yeşil; Release solution 0 hata.
**Saha testi bekliyor:** gerçek ALV'de kolon okuma (`ColumnOrder`) ve sözleşmenin uygulanması.


## Kontrat Değişiklik Prosedürü

Arayüz / şema / enum değişikliği gerekirse:

1. **Gerekçe:** Bu AGENTS.md dosyasında `## Kontrat Değişikliği — [tarih]` başlığı ekle. Etkilenen paketleri listele.
   ```
   ## Kontrat Değişikliği — 2026-07-10

   IActivity.ExecuteAsync: Timeout parametresi eklendi (CancellationToken zaten var ama explicit param).
   Etkilenen paketler: WP-2.1 (katalog), WP-2.2 (BaseRunner), tüm aktivite implementasyonları (2.6–2.9).
   Gerekçe: UI Spy async'nin timeout ayarlanması isteniyor.
   ```

2. **Yapı:** Değiştir (arayüz / şema); tests yazma + çalıştır.

3. **Etki analizi:** Etkilenen paketleri belirt (alt ajanlar kontratı oku).

4. **Commit:** 
   ```
   git commit -m "refactor(contract): IActivity timeout parametresi

   IActivity.ExecuteAsync signature: timeout CancellationToken'ı ile explicit TimeoutSeconds eklenmiştir.
   Etkilenen: Tüm aktivite implementasyonları (ActivityCatalog metadata güncellendi).
   
   Kontrat Değişikliği (AGENTS.md dosyasında belirtildi)."
   ```

Kontratı değişen alt ajan ilgili paketleri revise eder — bu yüzden erken değişiklik maliyetli. **İlk task (2.1 katalog) çalıştırılmadan kontratı sabitle.**

---

## Alt Ajan İş Paketi Alma Checklist

Her iş paketi alma sırasında kontrol et:

- [ ] Plan dosyasında paket adını buldum (`docs/plans/2026-07-04-implementation.md`)
- [ ] Spec bölüm referansını okudum (örn. "Spec Bölüm 5.2, 6")
- [ ] **Kontrat dosyaları:**
  - [ ] İlgili arayüzleri buldum (`src/RPA.Domain/Interfaces/*.cs`)
  - [ ] Kullanacağım varlık sınıflarını okudum (`src/RPA.Domain/Entities/*.cs`)
  - [ ] Enum değerlerini biliyorum
  - [ ] Workflow JSON şemasını gerekirse inceledim
- [ ] Acceptance kriterini anladım
- [ ] TDD: test yapısını okudum
- [ ] Code review eforunu anladım (`/code-review [low/medium/high]`)

---

## Git Workflow

### Branch Stratejisi

Main branch'e yalnızca PR → review → merge. Her paketi kendi branch'te yapma (isteğe bağlı alt ajanlar tarafından).

### Commit Mesajı

```
<type>(<scope>): <subject>

<body>

<footer>
Co-Authored-By: Claude <agent-type> <noreply@anthropic.com>
```

Örnekler:
```
feat(domain): Project varlığı ve Workflow ilişkileri

- Project adı, açıklama, soft-delete
- Workflow → Project ilişkisi, OneToMany
- Unit testler ✓

Co-Authored-By: Claude Opus <noreply@anthropic.com>

---

feat(infrastructure): BaseRunner State Machine — If/Else/ForEach semantiği

Node graph'ını topologically sıraya sokma algoritması.
Değişken scope isolation (global/component/local).
Golden-file senaryolar (5 test pass).

Spec Bölüm 5.2 birebir implementasyon.

Co-Authored-By: Claude Opus <noreply@anthropic.com>
```

Types: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`, `ci`, `perf`

---

## Test Matriş

| Katman | Test Tipi | Test Dosyası | Eşik |
|--------|-----------|--------------|------|
| Domain | Unit (entity + value object) | tests/RPA.Domain.Tests/ | ≥80% coverage |
| Application | Unit (CQRS handler, service) + Integration | tests/RPA.Application.Tests/ | ≥75% coverage |
| Infrastructure | Unit (data access, SAP mock) + E2E (real SAP DEP) | tests/RPA.Infrastructure.Tests/ | ≥70% coverage + E2E separate |
| WebAPI | Unit (controller, SignalR) + E2E (HTTP) | tests/RPA.WebAPI.Tests/ | ≥65% coverage |

**TDD flow:** `dotnet test [Layer].Tests -v` — her task'ın sonunda hepsi PASS.

---

## Live Database

**Local development:** PostgreSQL 14+ (Npgsql sağlayıcısı). Docker: `docker run -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=rpa_dev -p 5432:5432 postgres:16`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=rpa_dev;Username=postgres;Password=postgres;"
  }
}
```

**Test:** In-memory database (`Microsoft.EntityFrameworkCore.InMemory`); migration script/metadata testleri SQLite ile.

Migrations: `dotnet ef migrations add <name> --project src/RPA.Infrastructure` sonra `dotnet ef database update`.

---

## Doğrulama Checklist (Pilot Öncesi)

Faz 6.5 pilot koşmadan:

- [ ] Tüm paketler code-review geçti
- [ ] Tüm unit testler `dotnet test` geçer
- [ ] Workflow JSON şeması valide (5+ senaryolu test)
- [ ] SAP bağlantı (DEP'te) başarılı
- [ ] OTP 5 kanal sağlam
- [ ] Action Center BusinessException kaydı oluşuyor
- [ ] Elasticsearch logları korelasyon ID'siyle taglı

Pilot: "Müşteri portalından (OTP'li giriş) veri çekip SAP MM01'de malzeme açma" ≥95% başarı, BusinessException'lar resolve ediliyor.

---

## Sorular / Belirsizlikler

Tüm belgeler `docs/` klasöründe tutulur. Plan uygulanırken spec/plan bölüm referansları ver; `AGENTS.md`'i güncelle.

---

## Kontrat Değişikliği — 2026-07-17 (Robot ↔ Agent sahiplik bağı; lisanslama review düzeltmeleri)

Offline lisanslamanın **Final Review Gates** adımında (high-effort code review + security review)
bulunan açıkların düzeltmesi. Üç güvenlik bulgusundan ikisi mevcut yapı içinde kapandı; üçüncüsü
(**IDOR**) şema değişikliği gerektirdiği için bu kayda konu.

**1) `Robot.AgentIdentityId` (Guid?) eklendi** — migration `AddRobotAgentOwnership`, index
`IX_Robots_AgentIdentityId`. **FK YOK** (bilinçli): `AgentIdentity` lisanslama sınırında yaşar,
robot kaydı ondan bağımsız var olabilir/silinebilir; `null` = ajansız/sunucu-içi kayıt.
- **`RobotRegistrationRequest.AgentIdentityId`** eklendi — uzak (SignalR) kayıtlarda **yalnız**
  ajanın JWT'sindeki `agent_id` claim'inden doldurulur, istemci gövdesinden **asla** okunmaz.
- **Yeni:** `IRobotService.RecordHeartbeatAsync(robotId, agentIdentityId, ct)` aşırı yüklemesi —
  sahiplik doğrular. Mevcut tek-parametreli imza **korundu** (sunucu-içi çağrılar; sahiplik
  kontrolü uygulanmaz). `RobotService.EnsureOwnership`: çağıran kimliği null **veya** robot
  sahipsizse kontrol atlanır (ilk sahiplenen ajana bağlanır), aksi halde uyuşmazlıkta
  `BusinessException("ROBOT_NOT_OWNED")`.
- **`RobotHub.Register/Heartbeat`**: sahiplik `Context.User`'daki `agent_id`'den okunur; claim
  yok/bozuksa `HubException("AGENT_IDENTITY_MISSING")`, sahiplik ihlalinde
  `HubException("ROBOT_NOT_OWNED")`. **Gerekçe:** `[Authorize(Policy="AgentClient")]` yalnız
  *kimlik* doğruluyordu; `robotId`/`MachineName` istemciden geldiği için aktive edilmiş herhangi
  bir ajan başka robotun grubuna kaydolup **ona atanan işleri alabiliyordu** (nesne düzeyinde
  yetkilendirme eksikti). Not: `AgentIdentity.MachineFingerprint` ile `Robot.MachineName` zaten
  aynı değeri (`AgentOptions.EffectiveMachineName`) taşıyordu — bağ örtük vardı, hiç doğrulanmıyordu.

**2) `ILicenseService.GetCurrentInstallationAsync()` eklendi.** `GetStatusAsync` artık kurulum
satırını **kimliğe göre** seçer ve imzalı yükteki `installationId`/parmak izini bu makinenin
kimliğiyle **her okumada** karşılaştırır (`LICENSE_INSTALLATION_MISMATCH`). **Gerekçe:** bağ yalnız
`ImportAsync`'te doğrulanıyordu → veritabanı kopyalanan ikinci sunucuda lisans geçerli kalıyordu.
Aynı değişiklik `SingleOrDefaultAsync(!IsDeleted)` kaynaklı "ikinci kurulum satırında 500" hatasını
da kapatır. `AgentsController` artık kurulum satırını kendi sorgulamaz.

**3) `POST /api/agent-auth/token` lisans doğrular.** Geçerlilik (`LICENSE_EXPIRED`) + ajanın bu
kuruluma aitliği (`LICENSE_INSTALLATION_MISMATCH`) kontrol edilir. **Gerekçe:** sona erme yalnız
import/aktivasyon yollarında zorlanıyordu; süresi dolmuş lisansta ajanlar 10 dk'lık token'ı
sonsuza dek yeniliyordu. **Test etkisi:** lisanssız ajanın token alması artık geçerli senaryo
değildir → token yolunu süren testler gerçek lisans seed etmelidir (`LicensedTestApp`).

**Diğer (kontrat dışı):** `LicenseDocumentJson` `public` + `Read(JsonElement)` — belge ayrıştırması
tek kaynak (`LicenseController.ReadSignedLicense` kopyası silindi), bozuk belge `JsonException` →
400 `LICENSE_DOCUMENT_INVALID`. Yeni `JwtSigningKey.Derive` — PBKDF2'nin üç kopyası (JwtTokenService,
AgentTokenService, Program.cs) tek kaynağa indi (önbellekli). `IInstallationIdentityService` +
`IVendorLicenseVerifier` **Singleton** (scoped kayıtta içteki semafor işlevsizdi). `BaseRunner`
tryCatch artık `ExecutionSuspendedException`'ı yutmuyor. `EfAgentIdentityRepository` mutasyon
yolları `AGENT_NOT_FOUND` atıyor → bilinmeyen id'de 404 (önceden 500).

**Etkilenen paketler:** Robot kayıt/heartbeat tüketicileri (Agent `RobotRegistrar` — `AgentOptions.AgentId`
ile sahiplik gönderir), lisanslama (WebAPI + Infrastructure), `IRobotService` implementasyonları.
**Doğrulama:** Domain 20, Application 6, Agent 142, LicenseGenerator 11, Infrastructure 708,
WebAPI 129, Studio 374 — tümü yeşil; Debug + Release derleme 0 hata.

---

**Versiyonlu:** 2026-07-04 — Kontrat Paketi sabit, TDD/review kuralları kesin.
