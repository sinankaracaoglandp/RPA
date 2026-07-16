# RPA Platform v3 — Proje Kuralları

## Kontrat Degisikligi - 2026-07-11 (Credential Vault Management)

`ICredentialVault` sozlesmesine plaintext icermeyen guvenli listeleme eklendi:
- **Yeni:** `ListSecretsAsync(string? tag = null) -> IEnumerable<VaultSecretReference>`
- **Yeni DTO:** `VaultSecretReference { Key, Metadata }`

Mevcut `GetSecretAsync`, `StoreSecretAsync`, `DeleteSecretAsync`, `ExistsAsync`, `ListSecretsByTagAsync` imzalari degismedi.
Etkilenen paketler: WebAPI Credentials endpoint, DPAPI Vault, HashiCorp Vault, Studio Orchestrator Credentials ekrani.
Gerekce: Kullanicinin credential degerini UI uzerinden Vault'a yazabilmesi ve listede yalnizca key/metadata gorebilmesi icin secret degerini dondurmeyen listeleme sozlesmesi gerekliydi.

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
Co-Authored-By: Codex <agent-type> <noreply@anthropic.com>
```

Örnekler:
```
feat(domain): Project varlığı ve Workflow ilişkileri

- Project adı, açıklama, soft-delete
- Workflow → Project ilişkisi, OneToMany
- Unit testler ✓

Co-Authored-By: Codex Opus <noreply@anthropic.com>

---

feat(infrastructure): BaseRunner State Machine — If/Else/ForEach semantiği

Node graph'ını topologically sıraya sokma algoritması.
Değişken scope isolation (global/component/local).
Golden-file senaryolar (5 test pass).

Spec Bölüm 5.2 birebir implementasyon.

Co-Authored-By: Codex Opus <noreply@anthropic.com>
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
