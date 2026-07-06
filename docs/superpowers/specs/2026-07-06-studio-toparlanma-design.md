# Studio Toparlanma — Tasarım Spec'i

**Tarih:** 2026-07-06
**Durum:** Onaylandı (kullanıcı ile brainstorming sonucu)
**İlgili belgeler:** `docs/specs/2026-07-04-rpa-platform-v3-design.md` (ana spec),
`docs/plans/2026-07-05-faz5-studio-ui.md` (Faz 5 planı), `CLAUDE.md` (kontrat paketi)

---

## 1. Problem Tanımı

Faz 5 Studio UI teslim edildi ancak uçtan uca kullanılabilirlik testinde beş kritik eksik/hata tespit edildi:

| # | Sorun | Tür | Kök durum |
|---|-------|-----|-----------|
| 1 | Canvas'a bırakılan nesneye tıklayınca nesne **siliniyor** | Bug | Seçim/silme olay çakışması (analiz edilecek) |
| 2 | Nesne seçilince **özellik paneli açılmıyor** | Bug | Panel kodu var; seçim→panel veri akışı kopuk |
| 3 | Nesnelerin çoğunda **özellik görünmüyor** | Bug | Backend kataloğu tam (45 aktivite, input tanımlı); UI'a ulaşmıyor — #2 ile aynı kök şüphesi |
| 4 | Düğümler arası **mouse ile bağlantı kurulamıyor** | Eksik | Node kartındaki soket span'ları dekoratif; pointer olayı ve Rete connection-plugin kaydı yok |
| 5 | **Kaydet yok** — proje/workflow kalıcılığı hiç yok | Eksik | Ne UI ne API mevcut |
| 6 | SAP/Web sayfalarında **mouse ile eleman seçimi (picker) yok** | Eksik | Agent tarafı SAP dedektörü hazır (Task 4.4); Studio tarafı ve uçtan uca akış yok |
| 7 | **Windows masaüstü otomasyonu** hiç yok | Eksik | Ne aktivite ailesi ne picker var |

### Doğrulanmış tespitler (kod incelemesi, 2026-07-06)

- `ActivityRegistry.cs`: 45 aktivitenin **tamamında** input/output/default tanımları mevcut.
  Yalnız `Sap.Nco.Rollback` (parametresiz — doğru) ve `Sap.Gui.Screenshot` (yalnız output — doğru) inputsuz.
- `src/RPA.Agent/UISpy/`: `SapGuiSpyService` + `SignalRSpyElementTransport` hazır — imleç altındaki
  SAP elementini tespit edip `StudioHub.ReceiveDetectedElement`'e gönderiyor.
- `src/RPA.WebAPI/Hubs/StudioHub.cs`: `DetectedElement` olayını tüm Studio istemcilerine yayınlıyor (JWT korumalı).
- `canvas.component.ts`: `connectNodes()` programatik API'si var; `nodepicked` → `nodeSelect` emit zinciri var.
- `node.component.html`: `.canvas-node__socket--in/--out` span'ları `aria-hidden`, hiçbir olay bağlı değil.
- `designer.component.html:37`: `[canvas]="canvas"` — `@ViewChild` template'e bağlanıyor (kırılgan desen, şüpheli #1).
- Workflow CRUD API yok; yalnız `WorkflowDeploymentController` mevcut.
- Domain'de `Project`, `Workflow`, `WorkflowVersion` varlıkları tanımlı — B paketi kontrat değişikliği gerektirmez.

---

## 2. Hedef ve Başarı Kriterleri

**Hedef:** Studio'da uçtan uca şu akış çalışsın:
proje aç → toolbox'tan aktivite sürükle → düğümleri bağla → özellikleri doldur (gerekirse 🎯 ile
SAP/Web/masaüstü ekranından mouse ile eleman göster) → Kaydet → kapat/aç, kaldığın yerden devam.

**Başarı kriterleri (paket bazında kabul testleri Bölüm 8'de):**
1. Nesne tıklanınca silinmez, seçilir; özellik paneli seçili aktivitenin formunu gösterir.
2. 45 katalog aktivitesinin tamamı için özellik formu üretilir.
3. İki düğüm mouse sürüklemesiyle bağlanabilir; bağlantı seçilip silinebilir.
4. Workflow bir projeye kaydedilir, listeden açılır; Ctrl+S çalışır; kaydedilmemiş değişiklik göstergesi var.
5. `Sap.Gui.*` selector alanlarında 🎯 ile SAP ekranından tıklayarak element ID alınır.
6. `Web.*` selector alanlarında 🎯 ile tarayıcıdan tıklayarak selector alınır.
7. `Desktop.*` aktiviteleri katalogda; 🎯 ile herhangi bir Windows uygulamasından eleman seçilir.

---

## 3. Kapsam Dışı

- Uzak robot ekranında picker (ekran yayını gerektirir) — tasarımcı ve Agent **aynı makinede** varsayılır.
- Workflow yayınlama/versiyon onay akışının değişmesi (mevcut `WorkflowDeploymentController` olduğu gibi kalır).
- Simple mode'a picker entegrasyonu (Advanced mode hedeflenir; Simple mode mevcut davranışını korur).
- Selector sağlamlaştırma/otomatik onarım (self-healing selectors) — gelecek faz.

---

## 4. Mimari Genel Bakış

```
┌─────────────────────────── Tasarımcı Makinesi ───────────────────────────┐
│                                                                           │
│  Studio (Angular, tarayıcı)          RPA.Agent (Windows, tray)            │
│  ┌─────────────────────────┐         ┌───────────────────────────┐        │
│  │ Designer                │         │ UISpy                     │        │
│  │  ├ Canvas (Rete.js)     │         │  ├ SapGuiSpy (mevcut)     │        │
│  │  ├ PropertiesPanel      │         │  ├ WebSpy (Playwright) D  │        │
│  │  │   └ 🎯 SelectorPicker│         │  ├ DesktopSpy (FlaUI)  E  │        │
│  │  ├ Kaydet / Ctrl+S    B │         │  └ SpySession (tek-seçim) │        │
│  │  └ SpyService (SignalR) │         └────────────┬──────────────┘        │
│  └───────────┬─────────────┘                      │                       │
└──────────────┼────────────────────────────────────┼───────────────────────┘
               │  /hubs/studio (WSS, JWT)           │
        ┌──────▼────────────────────────────────────▼──────┐
        │ RPA.WebAPI                                        │
        │  ├ StudioHub  (mevcut; spy start/stop eklenir)    │
        │  ├ WorkflowsController (YENİ — B)                 │
        │  └ ActivitiesController (mevcut)                  │
        └──────────────────────┬────────────────────────────┘
                               │
                        PostgreSQL (Project / Workflow / WorkflowVersion)
```

**Temel akışlar:**

- **Tasarım verisi:** Canvas ↔ `WorkflowVersion.JsonDefinition` (WorkflowSchema.json v1.0) ↔ WorkflowsController ↔ DB.
- **Picker köprüsü:** Studio 🎯 tıklar → `StudioHub.StartSpy(sessionId, kind)` → Agent ilgili
  spy'ı tek-seçim kipinde başlatır → kullanıcı hedef uygulamada elemana tıklar → Agent
  `ReceiveDetectedElement(sessionId, element)` → Hub yalnız isteyen Studio bağlantısına yayınlar →
  alan dolar, spy kapanır.

---

## 5. Paket Tasarımları

### Paket A — Tasarım Ekranını Kullanılır Hale Getirme

**Katman:** yalnız Studio (Angular). Backend değişikliği yok.

#### A.1 Bug: tıklayınca silinme + özellik paneli açılmama (birlikte ele alınır)

Kök neden analizi `superpowers:systematic-debugging` ile yapılır. Bilinen şüpheliler:

1. `designer.component.html`'de `[canvas]="canvas"` — `@ViewChild` referansının template binding'i.
   İlk change detection'da `undefined`; `PropertiesPanelComponent.activityType/properties` getter'ları
   `canvas` olmadan boş döner. **Çözüm adayı:** canvas referansını `signal` ile taşı
   (`viewChild()` fonksiyonel API) veya seçim anında panel girdilerini (activityType + properties)
   düz veri olarak designer üzerinden geçir — panel canvas'a hiç dokunmasın (tercih edilen;
   bağımlılığı azaltır, test edilebilirliği artırır).
2. Silinme: `NodeComponent`'teki ✕ butonu `stopPropagation` yapıyor ama Rete'nin pointer
   yakalaması Angular `click`'ten önce çalışıyor olabilir; ya da `mountNode`'daki
   `nodeRefs.get(id)?.destroy()` çift-mount senaryosunda yanlış ref'i yok ediyor olabilir.
   Debug sırasında repro + failing test yazılmadan düzeltme yapılmaz.

**Kabul:** Node'a tıkla → node seçili kalır, silinmez; panel o aktivitenin formunu gösterir.
Regression testi: click → `nodeSelect` emit + node sayısı değişmez.

#### A.2 Mouse ile düğüm bağlama

- `node.component.html`'deki `--in/--out` soket span'larına `pointerdown` bağlanır.
- **Yaklaşım:** Rete connection-plugin'in soket kayıt mekanizması yerine, mevcut manuel SVG
  overlay deseniyle tutarlı **kendi sürükleme akışımız**: `pointerdown`(out soketi) →
  geçici path çizimi (`redrawConnections` altyapısı yeniden kullanılır) → `pointerup`
  hedef node'un in soketi/kartı üzerindeyse `connectNodes(from, to)`.
  *Gerekçe:* Canvas zaten Rete render pipeline'ını bypass edip Angular bileşeni mount ediyor;
  connection-plugin'in soket render sözleşmesine dönmek daha büyük refactor olur (YAGNI).
- Bağlantı seçimi: SVG path'e `click` → seçili sınıfı + `Delete` tuşu → `deleteConnection`.
- Kurallar: kendine bağlanma yok (mevcut `connectNodes` koruması), aynı çift arasında mükerrer
  bağlantı engellenir (yeni kontrol).

**Kabul:** out→in sürüklemesiyle bağlantı oluşur ve `graphChanged` emit edilir; path'e tıkla +
Delete siler; self/mükerrer bağlantı reddedilir.

#### A.3 Metadata entegrasyonu

- `addNode` sırasında katalogdan `DefaultProperties` çekilip node'un `properties` bag'ine kopyalanır
  (toolbox zaten kataloğu yüklüyor — metadata toolbox'tan `addActivity`'ye taşınır, ek HTTP çağrısı yok).
- Node etiketi `DisplayName` olur (`activityId` alt başlıkta kalır — mevcut şablon zaten böyle).
- `GenericPropertyComponent` hata/boş durumları görünür mesaja bağlanır
  (mevcut `error` flag'i şablonda kullanıcıya gösterilir; i18n anahtarı eklenir).

#### A.4 Katalog kapsama testi

Jest testi: `/api/activities` mock kataloğundaki her aktivite için `GenericPropertyComponent`
form alanı üretebiliyor mu (her input tipi → doğru HTML input türü eşlemesi). Backend'de mevcut
katalog snapshot'ı test fixture'ı olarak kullanılır — katalog büyüyünce test otomatik kapsar.

---

### Paket B — Proje/Workflow Kalıcılığı

**Katman:** WebAPI + Application + Studio. Kontrat değişikliği yok (mevcut entity'ler kullanılır).

#### B.1 Backend — WorkflowsController (YENİ)

| Endpoint | Amaç |
|----------|------|
| `GET  /api/projects` | Proje listesi (id, ad, açıklama, workflow sayısı) |
| `POST /api/projects` | Proje oluştur `{ name, description }` |
| `GET  /api/projects/{projectId}/workflows` | Projedeki workflow'lar (id, ad, son güncelleme) |
| `POST /api/projects/{projectId}/workflows` | Workflow oluştur `{ name }` — boş taslak versiyonla |
| `GET  /api/workflows/{workflowId}/draft` | Taslak `WorkflowVersion` (JsonDefinition dahil) |
| `PUT  /api/workflows/{workflowId}/draft` | Taslağı kaydet `{ jsonDefinition }` — şema v1.0 valide edilir |

- Taslak modeli: `WorkflowVersion.Status == ComponentStatus.Draft` olan **tek kayıt** taslaktır
  (entity mevcut: `Status`, `JsonDefinition`, `Version` alanları). `PUT` bu kaydın
  `JsonDefinition`'ını günceller, yeni versiyon yaratmaz; taslak yoksa oluşturulur.
  Yayınlama mevcut deployment akışında kalır.
- Application katmanı: `WorkflowDesignService` (CQRS mevcut desene uygun) + FluentValidation.
- JsonDefinition kaydedilmeden `WorkflowSchema.json` v1.0'a karşı doğrulanır; geçersizse 400 +
  hata listesi (BusinessException sınıfı).
- Soft-delete ve `CreatedBy/UpdatedBy` BaseEntity konvansiyonuyla otomatik.

#### B.2 Studio — Projelerim ekranı + Kaydet

- **Route'lar:** `/studio/projects` (liste), `/studio/designer/:workflowId` (tasarım).
  Mevcut `/studio/designer` (parametresiz) yeni-taslak modu olarak kalır; kaydetmek istenirse
  "Projeye kaydet" diyaloğu proje+ad seçtirir.
- **Projelerim:** proje kartları → workflow listesi → "Aç" (designer'a yönlendirir), "Yeni proje",
  "Yeni workflow". Dashboard'a giriş kartı eklenir.
- **Designer başlık çubuğu:** workflow adı + kirli göstergesi (`●`) + **Kaydet** butonu.
  `Ctrl+S` klavye kısayolu (tarayıcı varsayılanı engellenir). Kaydet: `canvas.serialize()` →
  `PUT .../draft`; başarıda gösterge temizlenir, hatada toast.
- **Kirli takibi:** `graphChanged` emit'i → dirty=true; kaydetme → dirty=false.
  Sayfadan ayrılırken dirty ise `canDeactivate` guard'ı onay sorar.
- `WorkflowDraftService` genişletilir: `consumePending` (mevcut, şablon galerisi akışı) korunur;
  `load(workflowId)` / `save(workflowId, version)` eklenir.

---

### Paket C — SAP 🎯 Hedef Göster (mevcut altyapının uçtan uca bağlanması)

**Katman:** Studio + WebAPI (StudioHub) + Agent. **Kontrat değişikliği:** `SpyElementMessage`
genişler — CLAUDE.md prosedürü işletilir (Bölüm 7).

#### C.1 Spy oturum modeli (tek-seçim kipi)

Mevcut Agent spy'ı sürekli akış modunda; tasarım hedefi **istek-üzerine tek seçim**:

1. Studio: kullanıcı `Sap.Gui.Click` özelliklerinde `elementId` yanındaki 🎯'e tıklar.
2. Studio → Hub: `StartSpy(sessionId, kind: "sap")` (sessionId = GUID, Studio üretir).
3. Hub → Agent (SignalR grup: tasarımcının robotu): spy başlat komutu.
4. Agent: SAP spy'ı highlight kipinde başlatır; kullanıcı SAP ekranında elemanın üzerine gelir,
   **tıklayarak** (veya `Ctrl+tıklama` — SAP oturumunu etkilememek için implementasyonda
   doğrulanacak) seçimi onaylar; `Esc` iptal eder.
5. Agent → Hub: `ReceiveDetectedElement(sessionId, element)`; Agent spy'ı kapatır.
6. Hub → **yalnız** `sessionId`'yi başlatan bağlantıya `DetectedElement` yayınlar
   (mevcut `Clients.All` düzeltilir — güvenlik: başka tasarımcının seçimi başkasına gitmez).
7. Studio: alan dolar, 🎯 kipi kapanır. 60 sn'de seçim gelmezse timeout + kullanıcıya mesaj.

#### C.2 Studio bileşenleri

- `SpyService` (yeni, `shared/services`): `/hubs/studio` SignalR istemcisi; `pick(kind) →
  Promise<SpyElement>`; bağlantı tembel açılır (mevcut `DebugService` deseni izlenir).
- `SelectorPickerButtonComponent` (yeni): input yanına 🎯; aktifken "SAP ekranında hedefe
  tıklayın… (Esc iptal)" durumu gösterir.
- Hangi alanların picker alacağı **katalog metadata'sından** gelir: `ActivityParameter`'a
  `PickerKind` (none/sap/web/desktop) alanı eklenir; registry'de `Sap.Gui.*` elementId alanları
  `sap` işaretlenir. (Kontrat eki — Bölüm 7.)

#### C.3 Agent tarafı

- `SpySessionCoordinator` (yeni): Hub'dan start/stop komutu alır, `SapGuiSpyService`'i tek-seçim
  kipiyle sarar (mevcut sürekli-akış servisi korunur; koordinatör dedup yerine "onaylanan tek
  element" semantiği uygular).
- Yalnız attended modda ve aktif kullanıcı oturumunda çalışır (mevcut `UiSpyHostedService`
  koşulu korunur).

---

### Paket D — Web 🎯 Hedef Göster

**Katman:** Agent (+ küçük Studio/Hub genişletmesi — C'nin `kind: "web"` hali).

- **WebSpyService (Agent, yeni):** Playwright ile tarayıcı başlatır **veya** mevcut spy
  oturumundaki tarayıcıya bağlanır (CDP). Spy başlarken sayfaya overlay script'i enjekte edilir:
  - `mouseover`: imleç altındaki elemanı renkli çerçeveyle vurgular + selector önizlemesi tooltip'i.
  - `click`: varsayılan davranış engellenir (`preventDefault`), seçim onaylanır.
  - `Esc`: iptal.
- **Selector üretim stratejisi (öncelik sırası):** `#id` (varsa ve sayfada benzersizse) →
  `[data-testid]` → `[name]`/`[aria-label]` benzersiz nitelik → kısa CSS yolu
  (en yakın benzersiz atadan itibaren, `nth-of-type` en son çare). Üretilen selector sayfada
  `querySelectorAll` ile doğrulanır (tam 1 eşleşme şartı).
- Element mesajı `SpyElementMessage`'ın `kind: "web"` biçimiyle aynı köprüden akar; ek alanlar:
  `selector`, `tagName`, `innerTextPreview`, `pageUrl`.
- Studio: `Web.*` selector alanları `PickerKind=web`; aynı `SelectorPickerButtonComponent` kullanılır.
- **Sınır:** Spy için açılan tarayıcı Agent'ın Playwright'ıdır; kullanıcının kendi Chrome'una
  bağlanma (attach-to-running) bu pakette yoktur — "Aç ve göster" akışı: 🎯'e basınca URL sorulur
  ya da workflow'daki `Web.Open`+`Web.Goto` değerlerinden sayfa açılır.

---

### Paket E — Windows Masaüstü Otomasyonu

**Katman:** Domain (enum/registry değil — yalnız katalog kaydı), Infrastructure, Agent, Studio.

#### E.1 Aktivite ailesi (`Desktop.*`, kategori "Masaüstü", capability `desktop`)

| Aktivite | Girdiler (özet) | Çıktı |
|----------|-----------------|-------|
| `Desktop.Attach` | processName / windowTitle (regex) | window handle (JSON) |
| `Desktop.Launch` | path, arguments?, waitForIdle? | window handle |
| `Desktop.Click` | selector (UIA yolu), clickType? | — |
| `Desktop.SetText` | selector, text | — |
| `Desktop.GetText` | selector | text |
| `Desktop.SelectItem` | selector, item | — |
| `Desktop.SendKeys` | selector?, keys | — |
| `Desktop.WaitFor` | selector, timeoutMs? | — (Timeout → System) |
| `Desktop.Screenshot` | selector? | path |

- **Kütüphane:** FlaUI (UIA3). Implementasyon `RPA.Infrastructure/Activities/Desktop/`.
- **Selector formatı:** UIA yolu — `AutomationId` öncelikli, yoksa
  `ControlType+Name` zinciri (örn. `Window[Title~'Hesap.*']/Pane/Edit[AutomationId='amount']`).
  Format `WorkflowSchema.json`'a dokunmaz (selector düz string'dir).
- Exception sınıflandırması: element bulunamadı/timeout → System; iş kuralı reddi yok.

#### E.2 DesktopSpy (Agent)

- FlaUI ile imleç altındaki UIA elementi tespit (`AutomationElement.FromPoint`),
  vurgulama (FlaUI highlight rect), tıklama ile onay — C'deki `SpySessionCoordinator`'a
  `kind: "desktop"` olarak eklenir. Element mesajı ek alanlar: `automationId`, `controlType`,
  `name`, `uiaPath`, `processName`.
- SAP GUI pencereleri DesktopSpy'da da görünür; kullanıcı SAP için `sap` picker'ı kullanmalı —
  DesktopSpy SAP penceresi algılarsa uyarı gösterir (element yine de seçilebilir).

---

## 6. Veri Akışı Özetleri

**Kaydet:** Canvas `serialize()` → `WorkflowVersion.JsonDefinition` (schema v1.0) →
`PUT /api/workflows/{id}/draft` → FluentValidation + şema doğrulama → EF Core → PostgreSQL.

**Picker (üç tür ortak):**
`🎯 tık` → `SpyService.pick(kind)` → `StudioHub.StartSpy(sessionId, kind)` → Agent spy (tek-seçim)
→ kullanıcı hedef uygulamada tıklar → `ReceiveDetectedElement(sessionId, element)` →
Hub caller-only yayın → alan dolar. Timeout 60 sn; `Esc`/panel kapatma → `StopSpy(sessionId)`.

---

## 7. Kontrat Değişiklikleri (CLAUDE.md prosedürüne girecekler)

Paket C başlangıcında tek seferde kayıt altına alınır:

1. **`SpyElementMessage`** (RPA.Infrastructure.UISpy): `SessionId` (Guid), `Kind`
   (`sap|web|desktop`) alanları eklenir; web/desktop'a özgü alanlar nullable eklenir.
   Etkilenen: Agent UISpy (üretici), StudioHub (taşıyıcı), Studio SpyService (tüketici, yeni).
2. **`ActivityParameter`** (Domain, IActivity.cs yanı): `PickerKind` opsiyonel alanı
   (`None` varsayılan). Etkilenen: ActivityRegistry (Sap.Gui/Web/Desktop kayıtları),
   ActivitiesController DTO'su, Studio `activity.model.ts`.
3. **`StudioHub`**: `StartSpy(sessionId, kind)` / `StopSpy(sessionId)` metotları;
   `ReceiveDetectedElement` imzasına sessionId; yayın `Clients.All` → caller-only.
   Etkilenen: Agent `SignalRSpyElementTransport`.

Paket E'nin `Desktop.*` kayıtları **kontrat değişikliği değildir** (registry'ye ekleme).
Paket A ve B kontrata dokunmaz.

---

## 8. Test Stratejisi

TDD zorunlu (CLAUDE.md): her alt görev failing test → minimal impl → pass → commit.

| Paket | Test odağı |
|-------|-----------|
| A | Canvas etkileşim testleri (Jest + jsdom, mevcut spec deseni): tık→seçim (silinmez), sürükle→bağlantı, self/mükerrer red, Delete ile bağlantı silme; GenericProperty katalog kapsama testi (45 aktivite fixture) |
| B | Backend: controller unit + InMemory EF entegrasyon (şema validasyonu 400 senaryoları); Studio: dirty guard, Ctrl+S, save round-trip (serialize→PUT→GET→loadWorkflow eşdeğerliği golden test) |
| C | Hub unit: caller-only yayın, session eşleme, timeout; Agent: tek-seçim kipi (mock detector); Studio: SpyService pick() promise akışı |
| D | Selector üretici saf birim testleri (HTML fixture'ları: id'li, testid'li, hiçbiri olmayan derin DOM); benzersizlik doğrulaması; overlay script'i Playwright ile smoke E2E |
| E | Aktiviteler: FlaUI mock arayüz arkasında unit; gerçek Notepad/Calculator ile E2E (ayrı kategori, CI-dışı); UIA yol üretici birim testleri |

Review eforu: A-B `medium`, C-D-E `high`; C-D-E'de ek `/security-review`
(spy uzaktan komut yüzeyi + Hub yetkilendirme).

**Güvenlik notları:** Spy yalnız attended modda; `StartSpy` JWT'li Studio bağlantısından gelir ve
Agent yalnız kendi makinesinin kullanıcı oturumunda çalışır; seçilen element içerik önizlemeleri
(innerText) loglanmaz (PII); Credential tipli alanlara picker bağlanmaz.

---

## 9. Uygulama Sırası ve Bağımlılıklar

```
A (bağımsız) ──► B (bağımsız) ──► C ──► D ──► E
                                  │
                                  └─ C'nin oturum altyapısı D ve E'nin önkoşuludur
```

- A→B→C→D→E; her paket tek başına teslim edilebilir ve Studio'yu bir kademe kullanılır bırakır.
- A ile B teknik olarak paralel yürüyebilir (farklı katmanlar); plan, tek ajan sıralı akışı
  varsayar, paralelleştirme yürütme sırasında opsiyoneldir.
- Kontrat değişiklikleri (Bölüm 7) C'nin ilk alt görevi olarak yapılır ve commit'lenir;
  D/E o kontratın üzerine gelir.

---

## 10. Riskler

| Risk | Etki | Azaltma |
|------|------|---------|
| A.1 kök nedeni tahminlerden farklı çıkar | Süre uzar | Systematic-debugging; tahminlere göre değil repro'ya göre düzeltme |
| SAP tıklama-onayı SAP oturumunu tetikler (yanlışlıkla buton basma) | Yanlış veri girişi | Ctrl+tıklama / kısayol onayı alternatifi; implementasyonda gerçek SAP GUI ile doğrulama |
| Playwright overlay bazı sayfalarda CSP'ye takılır | Web picker o sayfada çalışmaz | `page.addInitScript` (CSP'den etkilenmez) kullanımı; sınır dokümante edilir |
| FlaUI bazı uygulamalarda (WPF-dışı, custom-drawn) eleman göremez | Desktop picker sınırlı | Bilinen sınır olarak dokümante; görüntü-tabanlı seçim kapsam dışı |
| `Clients.All` → caller-only geçişi mevcut Task 4.4 testlerini kırar | Test güncellemesi | Kontrat değişikliği kapsamında testler birlikte güncellenir |
