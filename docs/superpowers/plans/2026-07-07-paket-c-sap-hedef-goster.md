# Paket C - SAP Hedef Goster: Implementasyon Plani

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Studio'da `Sap.Gui.*` element alanlarinda hedef goster dugmesi ile Agent uzerinden tek seferlik SAP element secimi yapmak: Studio picker baslatir, Hub oturumu route eder, Agent tek-secim modunda SAP elementini yakalar, sadece isteyen Studio baglantisina `DetectedElement` doner ve alan otomatik dolar.

**Architecture:** Mevcut UI Spy altyapisi korunur ama surekli `Clients.All` yayin akisi tek-secim/session akisi ile genisletilir. Kontrat C'nin ilk task'inda sabitlenir: `SpyElementMessage` session/kind tasir, `ActivityParameter` picker metadata tasir, `StudioHub` start/stop + caller-only routing sunar. Agent tarafinda var olan `SapGuiSpyService` ve `SignalRSpyElementTransport` bozulmadan `SpySessionCoordinator` ile sarilir. Studio tarafinda SignalR istemcisi `SpyService`, UI ise `SelectorPickerButtonComponent` ile generic property editor'a entegre edilir.

**Tech Stack:** .NET 10 (xUnit, SignalR), Angular 22 (signals, standalone, Vitest), existing SAP GUI spy abstractions.

**Spec:** `docs/superpowers/specs/2026-07-06-studio-toparlanma-design.md` Bolum 5 Paket C, Bolum 7 Kontrat Degisiklikleri, Bolum 8 Test Stratejisi.

## Global Constraints

- TDD zorunlu: failing test -> minimal impl -> pass -> commit.
- Kontrat dosyalarina dokunulacaksa ilk commit yalniz kontrat + etkilenen serialization/katalog testleri olmalidir.
- `CLAUDE.md` icine `## Kontrat Degisikligi - 2026-07-07 (Paket C SAP Hedef Goster)` basligi eklenmeden `IActivity.cs` veya `SpyElementMessage` degistirilmez.
- `WorkflowSchema.json` degismez; selector degeri workflow property bag icinde string kalir.
- Picker credential alanlarina baglanmaz. `PickerKind` yalniz selector/element alanlari icindir.
- UI'da kullaniciya gorunen yeni metinler `src/RPA.Studio/public/assets/i18n/tr.json` ve `en.json` icine eklenir.
- Hub yayinlari `Clients.All` ile devam etmemeli; sessionId baslatan Studio connection'a donmelidir. Eski REST smoke testleri gerekirse yeni davranisa gore guncellenir.
- Test komutlari:
  - Backend/Hub: `dotnet test tests/RPA.WebAPI.Tests --filter UiSpy`
  - Agent/Infrastructure: `dotnet test tests/RPA.Agent.Tests --filter Spy` ve `dotnet test tests/RPA.Infrastructure.Tests --filter UISpy`
  - Studio: `cd src/RPA.Studio && npm test -- --watch=false --include='**/{spy,selector,generic-property}.spec.ts'`
- Paket sonunda: `dotnet test` ve `cd src/RPA.Studio && npm test -- --watch=false` hedeflenir; SAP GUI ile manuel test ayrica not edilir.
- Review: `/code-review high` ve `/security-review` gerekir.

---

### Task 1: Kontrat degisikligi - SpyElementMessage + ActivityParameter.PickerKind

**Files:**
- Modify: `CLAUDE.md`
- Modify: `src/RPA.Infrastructure/UISpy/SapGuiElementSender.cs`
- Modify: `src/RPA.Domain/Interfaces/IActivity.cs`
- Modify: `src/RPA.Infrastructure/Workflow/ActivityCatalogBuilder.cs` (metadata builder kullaniliyorsa)
- Modify: `src/RPA.Studio/src/app/shared/models/activity.model.ts`
- Tests: `tests/RPA.Infrastructure.Tests/UISpy/SapGuiElementDetectorTests.cs`, activity catalog tests if present, Studio activity model/property specs if present

**Interfaces:**
- `SpyElementMessage` yeni alanlar:
  - `Guid SessionId` - tek-secim oturum kimligi; eski surekli akista `Guid.Empty` kabul edilir.
  - `string Kind` - `sap`, sonraki paketler icin `web` / `desktop`; default `sap`.
  - Web/desktop ileride kullanacak nullable alanlar simdiden kontrata eklenir: `Selector`, `TagName`, `InnerTextPreview`, `PageUrl`, `AutomationId`, `ControlType`, `Name`, `UiaPath`, `ProcessName`.
- `ActivityParameter` yeni alan:
  - `string? PickerKind` - null/empty = picker yok; `sap` = SAP picker.
- Studio modelinde input parametresi `pickerKind?: 'sap' | 'web' | 'desktop' | null`.

- [x] **Step 1: Failing characterization tests yaz**

`SapGuiElementDetectorTests` veya yeni `SpyElementMessageContractTests`:
- `SpyElementMessage.From(element, sessionId)` sessionId ve `Kind == "sap"` tasir.
- Eski `From(element)` cagrisi geriye uyumlu olarak `SessionId == Guid.Empty`, `Kind == "sap"` uretir.
- JSON serialization yeni alanlari camelCase/pascal case mevcut API davranisina uygun tasir.

Activity catalog testi:
- `Sap.Gui.Click` input `elementId` icin `PickerKind == "sap"`.
- `Sap.Gui.SetText` input `elementId` icin `PickerKind == "sap"`.
- `Sap.Gui.GetText` input `elementId` icin `PickerKind == "sap"`.
- `Sap.Gui.SelectTab` input `elementId` icin `PickerKind == "sap"`.
- `Sap.Gui.GridRead` input `gridId` icin `PickerKind == "sap"`.
- Credential inputlarda `PickerKind` null.

Studio model testi:
- mocked `/api/activities/...` metadata'sinda `pickerKind: "sap"` parse edilir ve GenericProperty tarafina ulasir.

- [x] **Step 2: FAIL gozle**

Run:
`dotnet test tests/RPA.Infrastructure.Tests --filter "SpyElementMessage|ActivityRegistryCoverage|SapGuiElementDetector"`

Expected: `SessionId` / `PickerKind` property yok.

- [x] **Step 3: CLAUDE.md kontrat kaydini ekle**

`CLAUDE.md` icinde mevcut kontrat degisikligi bloklarinin altina:

```md
## Kontrat Degisikligi - 2026-07-07 (Paket C SAP Hedef Goster)

UI Spy tek-secim oturumu ve Studio picker metadata'si icin kontrat genisletildi.

- `SpyElementMessage`: `SessionId` (Guid), `Kind` (`sap|web|desktop`) eklendi. Paket D/E icin web/desktop'a ozgu nullable alanlar simdiden eklendi.
- `ActivityParameter`: opsiyonel `PickerKind` eklendi. `null`/empty picker yok, `sap` SAP GUI picker demektir.
- `StudioHub`: `StartSpy(sessionId, kind)` ve `StopSpy(sessionId)` metotlari eklenecek; `ReceiveDetectedElement` sessionId ile caller-only yayina gececek.

Etkilenen paketler: Paket C (SAP picker), Paket D (Web picker), Paket E (Desktop picker), Studio activity metadata tuketicileri, Agent UI Spy transport.
Gerekce: Studio'da selector/element alanlarinin hedef goster dugmesiyle tek seferlik ve kullaniciya ozel secim yapabilmesi icin mevcut surekli `Clients.All` yayin kontrati yeterli degildir.
```

- [x] **Step 4: Kontrati uygula**

`SpyElementMessage`:
- Required `ElementId` aynen kalir.
- Yeni optional alanlar eklenir.
- `From(SapGuiElement element)` geriye uyumluluk icin kalir.
- `From(SapGuiElement element, Guid sessionId)` overload eklenir.

`ActivityParameter`:
- `public string? PickerKind { get; set; }`

`ActivityCatalogBuilder`:
- Input builder destekliyorsa `PickerKind` set edebilecek API eklenir; yoksa Sap.Gui activity metadata'larinda direkt object initializer kullanilir.

Sap GUI activity metadata:
- `elementId` / `gridId` selector alanlarina `PickerKind = "sap"` eklenir.

Studio `activity.model.ts`:
- Activity input modeline `pickerKind?: 'sap' | 'web' | 'desktop' | null` veya string union eklenir.

- [x] **Step 5: Testler PASS**

Run:
`dotnet test tests/RPA.Infrastructure.Tests --filter "SpyElementMessage|ActivityRegistryCoverage|SapGuiElementDetector"`
`cd src/RPA.Studio && npm test -- --watch=false --include='**/activity*.spec.ts'`

- [x] **Step 6: Commit**

```bash
git add CLAUDE.md src/RPA.Infrastructure/UISpy/SapGuiElementSender.cs src/RPA.Domain/Interfaces/IActivity.cs src/RPA.Infrastructure/Workflow/ src/RPA.Infrastructure/SAP/ src/RPA.Studio/src/app/shared/models/ tests/
git commit -m "refactor(contract): UI Spy tek-secim ve picker metadata kontrati

SpyElementMessage session/kind tasir; ActivityParameter PickerKind ile Studio
hangi alanlarda hedef goster dugmesi sunacagini katalogdan ogrenir.

Kontrat Degisikligi (CLAUDE.md dosyasinda belirtildi).

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 2: StudioHub session routing - StartSpy/StopSpy + caller-only DetectedElement

**Files:**
- Modify: `src/RPA.WebAPI/Hubs/StudioHub.cs`
- Modify: `src/RPA.WebAPI/Controllers/UiSpyController.cs` if REST endpoint remains for smoke/backcompat
- Test: `tests/RPA.WebAPI.Tests/UiSpyTests.cs`

**Interfaces:**
- `StartSpy(Guid sessionId, string kind)` records `sessionId -> Context.ConnectionId`; emits `StartSpy` command to agent group or, until robot grouping exists, all authenticated agent-capable connections.
- `StopSpy(Guid sessionId)` removes mapping and emits stop command.
- `ReceiveDetectedElement(SpyElementMessage element)` sends `DetectedElement` only to mapped Studio connection when `SessionId` is known.
- Backcompat: `SessionId == Guid.Empty` may still broadcast for existing smoke flow only if test explicitly requires it; Paket C picker path must never use broadcast.

- [x] **Step 1: Failing hub tests**

Add tests in `UiSpyTests.cs`:
- Two Studio hub connections start different sessions; element for session A is received only by connection A.
- Unknown session is ignored or returns error without broadcasting.
- `StopSpy(sessionId)` prevents later delivery.
- `StartSpy` rejects unsupported `kind`.
- `WithoutToken_IsRejected` existing test remains.

Use existing `HubConnection` integration pattern. For caller-only assertion, register `DetectedElement` handlers on two connections and assert only expected `TaskCompletionSource` completes.

- [x] **Step 2: FAIL gozle**

Run:
`dotnet test tests/RPA.WebAPI.Tests --filter UiSpy`

- [x] **Step 3: Implement session registry**

In `StudioHub`:
- Add static or DI singleton `ConcurrentDictionary<Guid, string>` for session owners. Prefer small service if testability becomes clearer: `ISpySessionRegistry`.
- Add constants:
  - `DetectedElementEvent = "DetectedElement"`
  - `StartSpyCommand = "StartSpy"`
  - `StopSpyCommand = "StopSpy"`
- Validate `kind` in `sap|web|desktop`.
- On disconnect, remove sessions owned by `Context.ConnectionId`.
- `ReceiveDetectedElement`:
  - If element null or empty `ElementId`: return.
  - If `SessionId != Guid.Empty` and registry has owner: `Clients.Client(owner).SendAsync(...)`.
  - If `SessionId != Guid.Empty` unknown: log warning and return.
  - Avoid `Clients.All` for session messages.

Robot grouping can be YAGNI for this task; command may be sent to `Clients.Others` as an interim until RobotHub association exists. Document this in code comment and plan follow-up if needed.

- [x] **Step 4: REST controller uyarlamasi**

`UiSpyController.Detect` currently uses `Clients.All`. Update it to call hub/session routing service or mark endpoint as legacy smoke. If kept, session-bearing payload must route caller-only through same registry.

- [x] **Step 5: Tests PASS**

Run:
`dotnet test tests/RPA.WebAPI.Tests --filter UiSpy`

- [x] **Step 6: Commit**

```bash
git add src/RPA.WebAPI/Hubs/StudioHub.cs src/RPA.WebAPI/Controllers/UiSpyController.cs tests/RPA.WebAPI.Tests/UiSpyTests.cs
git commit -m "feat(hub): UI Spy session routing ve caller-only yayin

StartSpy/StopSpy oturumlarini kaydeder; sessionId tasiyan DetectedElement
yalniz baslatan Studio baglantisina doner.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 3: Agent tek-secim koordinasyonu - SpySessionCoordinator

**Files:**
- Create: `src/RPA.Agent/UISpy/SpySessionCoordinator.cs`
- Modify: `src/RPA.Agent/UISpy/SapGuiSpyService.cs` if single-pick API needs extraction
- Modify: `src/RPA.Agent/UISpy/UiSpyHostedService.cs` only if current continuous polling must be paused during pick
- Modify: `src/RPA.Agent/AgentServiceCollectionExtensions.cs`
- Test: `tests/RPA.Agent.Tests/UISpy/SpySessionCoordinatorTests.cs`

**Interfaces:**
- `SpySessionCoordinator.StartAsync(Guid sessionId, string kind, CancellationToken ct)`
- `SpySessionCoordinator.StopAsync(Guid sessionId, CancellationToken ct)`
- Only one active session at a time for Paket C.
- For `kind == "sap"`, coordinator captures one SAP element and sends `SpyElementMessage` with same `SessionId`.
- Timeout/cancel returns no element and cleans state.

- [x] **Step 1: Failing tests**

Use mocked detector/transport. If `SapGuiSpyService` is hard to fake, extract small interface:

```csharp
public interface ISapGuiElementDetector
{
    Task<SapGuiElement?> DetectOnceAsync(CancellationToken ct = default);
}
```

Tests:
- `StartAsync_Sap_SendsOneElementWithSessionId`.
- `StartAsync_WhenNoElementBeforeTimeout_DoesNotSendAndClearsSession`.
- `StopAsync_CancelsActiveSession`.
- `StartAsync_WhenSessionAlreadyActive_RejectsOrReplacesDeterministically` (choose reject with BusinessException/InvalidOperationException).
- Unsupported kind fails.

- [x] **Step 2: FAIL gozle**

Run:
`dotnet test tests/RPA.Agent.Tests --filter SpySessionCoordinator`

- [x] **Step 3: Implement coordinator**

Implementation guidance:
- Keep current continuous `UiSpyHostedService` behavior unchanged unless it conflicts in runtime.
- Coordinator uses `SapGuiElementSender` or `ISpyElementTransport` directly to send `SpyElementMessage.From(element, sessionId)`.
- Timeout default 60 seconds via options:

```csharp
public sealed class SpySessionOptions
{
    public const string SectionName = "SpySession";
    public int TimeoutSeconds { get; set; } = 60;
}
```

- Ensure cleanup in `finally`.
- No sensitive text logging; log element id/type only.

- [x] **Step 4: Wire SignalR commands**

`SignalRSpyElementTransport` currently sends to Hub. Agent also needs to receive Hub `StartSpy` / `StopSpy` commands. Options:
- If current Agent has a Hub connection abstraction, extend it there.
- Otherwise add a small hosted service `SpyHubCommandHostedService` that connects to `/hubs/studio`, registers handlers for `StartSpy` and `StopSpy`, and calls coordinator.

Prefer using existing `RobotHubClient` patterns for token/base URL configuration.

- [x] **Step 5: Tests PASS**

Run:
`dotnet test tests/RPA.Agent.Tests --filter Spy`

- [x] **Step 6: Commit**

```bash
git add src/RPA.Agent/UISpy/ src/RPA.Agent/AgentServiceCollectionExtensions.cs tests/RPA.Agent.Tests/UISpy/
git commit -m "feat(agent): UI Spy tek-secim oturum koordinasyonu

Hub StartSpy/StopSpy komutlari Agent'ta tek SAP secim akisini baslatir;
secilen element sessionId ile Studio'ya geri gonderilir.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 4: Studio SpyService - SignalR pick(kind) promise akisi

**Files:**
- Create: `src/RPA.Studio/src/app/shared/services/spy.service.ts`
- Create: `src/RPA.Studio/src/app/shared/services/spy.service.spec.ts`
- Modify if needed: `src/RPA.Studio/src/app/auth/auth.service.ts` or existing SignalR token helper

**Interfaces:**
- `pick(kind: 'sap' | 'web' | 'desktop'): Promise<SpyElement>`
- Generates `sessionId` client-side.
- Starts SignalR connection lazily to `/hubs/studio`.
- Calls hub `StartSpy(sessionId, kind)`.
- Resolves when matching `DetectedElement` with same `sessionId` arrives.
- Timeout after 60 seconds, calls `StopSpy(sessionId)`.
- `cancel(sessionId)` or `cancelActive()` stops active pick.

- [x] **Step 1: Failing tests**

Mock SignalR HubConnectionBuilder if existing tests do so; otherwise wrap SignalR creation in injectable factory.

Tests:
- `pick('sap')` invokes `StartSpy` with generated sessionId.
- Matching `DetectedElement` resolves promise.
- Non-matching session event is ignored.
- Timeout invokes `StopSpy` and rejects with timeout.
- Cancel invokes `StopSpy` and rejects/cancels.

- [x] **Step 2: FAIL gozle**

Run:
`cd src/RPA.Studio && npm test -- --watch=false --include='**/spy.service.spec.ts'`

- [x] **Step 3: Implement SpyService**

Use existing auth token retrieval pattern. If no SignalR package is installed in Studio, add `@microsoft/signalr` only if already present in `package.json`; otherwise confirm package exists before coding. Network install is not part of this task unless dependency is already in lockfile.

`SpyElement` model:

```typescript
export interface SpyElement {
  sessionId: string;
  kind: 'sap' | 'web' | 'desktop';
  elementId: string;
  type?: string;
  text?: string;
  enabled?: boolean;
  changeable?: boolean;
  x?: number;
  y?: number;
}
```

- [x] **Step 4: Tests PASS**

Run:
`cd src/RPA.Studio && npm test -- --watch=false --include='**/spy.service.spec.ts'`

- [x] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/shared/services/spy.service.ts src/RPA.Studio/src/app/shared/services/spy.service.spec.ts
git commit -m "feat(studio): UI Spy SignalR picker servisi

SpyService pick(kind) ile session baslatir, DetectedElement cevabini bekler,
timeout/cancel durumunda StopSpy gonderir.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 5: SelectorPickerButtonComponent + GenericProperty entegrasyonu

**Files:**
- Create: `src/RPA.Studio/src/app/studio/designer/properties/selector-picker-button.component.ts`
- Create: `src/RPA.Studio/src/app/studio/designer/properties/selector-picker-button.component.html`
- Create: `src/RPA.Studio/src/app/studio/designer/properties/selector-picker-button.component.scss`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.html`
- Modify: `src/RPA.Studio/public/assets/i18n/tr.json`
- Modify: `src/RPA.Studio/public/assets/i18n/en.json`
- Tests: component specs

**Interfaces:**
- Button input: `pickerKind?: 'sap' | 'web' | 'desktop' | null`
- Button output: `picked = EventEmitter<SpyElement>`
- GenericProperty shows button beside fields whose metadata input has `pickerKind`.
- On picked SAP element, property value becomes `element.elementId`.

- [x] **Step 1: Failing tests**

`selector-picker-button.component.spec.ts`:
- Click calls `SpyService.pick('sap')`.
- While pending, button disabled/active state visible.
- Success emits picked element.
- Failure shows translated error state.

`generic-property.component.spec.ts`:
- Metadata input `{ name: 'elementId', type: 'string', pickerKind: 'sap' }` renders picker button.
- Picker result updates input value and emits `{ elementId: 'wnd[0]/usr/btn[OK]' }`.
- Credential input with pickerKind null renders no picker.

- [x] **Step 2: FAIL gozle**

Run:
`cd src/RPA.Studio && npm test -- --watch=false --include='**/{selector-picker-button,generic-property}.component.spec.ts'`

- [x] **Step 3: Implement component**

UI requirements:
- Use an icon-like button label. If no icon library exists, use accessible text `Hedef goster` and keep compact.
- Do not use emoji in source if file is ASCII; use text/icon from existing design system.
- Tooltip/title from i18n:
  - `picker.pick`: `Hedef goster` / `Pick target`
  - `picker.waitingSap`: `SAP ekraninda hedefe tiklayin` / `Click a target in SAP`
  - `picker.failed`: `Hedef secilemedi` / `Target could not be selected`
  - `picker.timeout`: `Hedef secimi zaman asimina ugradi` / `Target selection timed out`

GenericProperty:
- Add picker button next to text input only when `input.pickerKind` exists.
- On pick, call existing property change path so dirty tracking still works through `propertiesChange`.

- [x] **Step 4: Tests PASS**

Run:
`cd src/RPA.Studio && npm test -- --watch=false --include='**/{selector-picker-button,generic-property}.component.spec.ts'`

- [x] **Step 5: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/ src/RPA.Studio/public/assets/i18n/
git commit -m "feat(studio): SAP selector alanlarina hedef goster dugmesi

Katalogdaki pickerKind=sap metadata'si GenericProperty icinde picker dugmesi
render eder; secilen SAP elementId property degerine yazilir.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

### Task 6: Uctan uca entegrasyon ve guvenlik sertlestirme

**Files:**
- Modify tests/docs as needed
- Optional: `docs/operations/installation.md` for SAP picker runtime notes

- [x] **Step 1: Full automated tests**

Run:
`dotnet test tests/RPA.Infrastructure.Tests --filter UISpy`
`dotnet test tests/RPA.Agent.Tests --filter Spy`
`dotnet test tests/RPA.WebAPI.Tests --filter UiSpy`
`cd src/RPA.Studio && npm test -- --watch=false`

- [x] **Step 2: Security assertions**

Add/verify tests:
- Hub rejects unauthenticated StartSpy.
- Unsupported kind rejected.
- SessionId unknown does not broadcast.
- StopSpy by non-owner cannot stop another connection's session.
- `SpyElementMessage.Text` is not logged in Agent/Hub information logs.

- [ ] **Step 3: Manual SAP smoke**

Prereqs:
- Windows attended session.
- SAP GUI scripting enabled.
- RPA.WebAPI running.
- RPA.Agent running with hub URL/token configured.
- Studio logged in.

Flow:
1. Open `/projects`, create/open workflow.
2. Add `Sap.Gui.Click` or `Sap.Gui.SetText`.
3. Select node; property panel shows `elementId`.
4. Click target picker button.
5. Click SAP element.
6. `elementId` field fills with `wnd[0]/...`.
7. Dirty indicator turns on; save draft; reload; value persists.
8. Start picker and wait 60 seconds; timeout message appears and no stale session remains.

- [ ] **Step 4: Package review**

Run project review commands per rule:
- `/code-review high`
- `/security-review`

- [ ] **Step 5: Commit fixes from review**

Commit any review fixes separately with clear scope.

---

## Paket Kapanisi

- [x] Kontrat degisikligi CLAUDE.md'de kayitli ve ilk commit'te uygulanmis.
- [x] `SpyElementMessage.SessionId/Kind` ve `ActivityParameter.PickerKind` testlerle guvencede.
- [x] Hub session routing caller-only; `Clients.All` picker path'te yok.
- [x] Agent SAP tek-secim akisi sessionId ile tek element gonderiyor.
- [x] Studio `SpyService.pick('sap')` timeout/cancel destekli.
- [x] `Sap.Gui.*` element alanlarinda picker button gorunuyor ve property degerini dolduruyor.
- [x] Backend, Agent, Studio hedefli testler PASS.
- [ ] Manuel SAP smoke sonucu not edildi.
- [ ] `/code-review high` ve `/security-review` tamamlandi.
- [ ] Sonraki adim: Paket D plan/implementasyon (`web` picker), C'nin session altyapisi uzerine kurulacak.
