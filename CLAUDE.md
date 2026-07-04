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

## Kontrat Değişiklik Prosedürü

Arayüz / şema / enum değişikliği gerekirse:

1. **Gerekçe:** Bu CLAUDE.md dosyasında `## Kontrat Değişikliği — [tarih]` başlığı ekle. Etkilenen paketleri listele.
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
   
   Kontrat Değişikliği (CLAUDE.md dosyasında belirtildi)."
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

**Local development:** SQL Server LocalDB (instalasyon yapma `sqllocaldb create mssqllocaldb`):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=RPA_Dev;Trusted_Connection=true;"
  }
}
```

**Test:** In-memory database (`Microsoft.EntityFrameworkCore.InMemory`) veya Test Containers (SQL Server docker).

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

Tüm belgeler `docs/` klasöründe tutulur. Plan uygulanırken spec/plan bölüm referansları ver; `CLAUDE.md`'i güncelle.

---

**Versiyonlu:** 2026-07-04 — Kontrat Paketi sabit, TDD/review kuralları kesin.
