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

**Versiyonlu:** 2026-07-04 — Kontrat Paketi sabit, TDD/review kuralları kesin.
