# Yapısal Konteyner Editörü — Alt-proje C1: Mutasyon Temeli + Minimal Etkileşim — Tasarım

**Tarih:** 2026-07-18
**Kapsam:** Yalnızca Studio (Angular + saf TS). Runtime/`WorkflowSchema.json`/`BaseRunner` değişmez.
**Bağlam:** "Yapısal konteyner editörü" hedefinin üçüncü alt-projesinin (C — etkileşimli düzenleme)
ilk dilimi. A (model + köprü) ve B (render + salt-okunur görünüm) tamamlandı. C, kullanıcı
tercihiyle üç dilime ayrıldı: **C1** (bu spec — mutasyon işlemleri + ağaç-doğruluk-kaynağı +
kalıcılık + minimal denetimler), **C2** (sürükle-bırak + otomatik-tel), **C3** (node seçme +
özellik paneli + undo/redo).

---

## 1. Kapsam ve karar özeti

- **Doğruluk kaynağı:** Yapısal modda **değişebilir yapısal ağaç** (StructuredSequence). Düzenlemeler
  ağacı mutasyona uğratır; her değişiklikte `treeToWorkflow` ile `WorkflowVersion` üretilip mevcut
  taslak kaydı akışına verilir.
- **Etkileşim (C1):** Yalnız minimal denetimler — her öğede **[sil] [↑] [↓]**, her lane'de ve kök
  dizide **[+ ekle]**. **Sürükle-bırak YOK** (C2). **Node seçme + özellik paneli + undo/redo YOK** (C3).
- **Kalıcılık:** Yeni tel/persist kodu yok; A'nın köprüsü (`treeToWorkflow`) + designer'ın mevcut
  `onGraphChanged` + taslak kaydı akışı kullanılır.
- Yeni bağımlılık eklenmez.

## 2. Ağaç adresleme + saf mutasyon işlemleri

Yeni saf modül: `src/app/studio/designer/structured/edit/tree-ops.ts` (Angular bağımlılığı yok).

**Path (adresleme):** Kök diziden bir öğeye inen adımlar. Her adım bir konteynere girişi ve dizi
indeksini taşır:
```
type PathStep = { lane: LaneName; index: number };   // konteyner lane'ine iniş
type Path = { steps: PathStep[]; index: number };    // son: hedef öğenin bulunduğu dizi + indeks
```
Kök dizideki bir öğe: `{ steps: [], index: i }`. Bir konteynerin `body` lane'inin `j`. öğesi:
`{ steps: [{ lane:'body', index: containerIndex }], index: j }`.

**İşlemler (hepsi immutable — yeni ağaç döndürür):**
- `insertItem(tree, seqPath, index, item): StructuredSequence` — `seqPath` ile adreslenen diziye
  `index` konumuna ekler. (`seqPath` = hedef diziye inen `PathStep[]`; kök için `[]`.)
- `removeItem(tree, path): StructuredSequence` — `path`'teki öğeyi çıkarır.
- `moveItem(tree, path, delta): StructuredSequence` — öğeyi aynı dizide `delta` (±1) kaydırır;
  sınırda (ilk öğede ↑ / son öğede ↓) **no-op** (aynı ağaç mantığı).
- `findPath(tree, item): Path | null` — öğeyi **referans eşitliğiyle** ağaçta arar, path'ini döndürür
  (UI olayları öğe referansı taşır; path'i bu fonksiyon çıkarır → özyinelemeli bileşenler path
  threading yapmaz).

**Yapıcılar:**
- `newStep(activityId: string): StepItem` — `{ id: crypto.randomUUID(), type:'activity', activity: activityId }`.
- `newContainer(type: ContainerType): ContainerItem` — `lanesFor(type)`'a göre boş lane'lerle
  (`{}` yerine her lane `[]`), boş props ile.

## 3. Düzenlenebilir kaynak + kalıcılık

`StructuredViewComponent` (B'den) düzenlenebilir hale gelir:
- Salt-okunur computed `tree` yerine **değişebilir `tree` signal** + `mode: 'empty'|'tree'|'fallback'`.
- `@Input() workflow` **ağacı yalnız bir kez tohumlar** (`private seeded` bayrağı). Tohumlama B'nin
  `convert()` mantığıdır (`workflowToTree` + round-trip/invariant güvence). Çevrilemezse `fallback`
  (düzenleme kapalı, salt-okunur mesaj). Toggle `@if (structuredView())` bileşeni kurup yıktığından,
  moda **her girişte** güncel workflow'dan taze tohumlanır; kendi yaydığı güncellemeler input'a geri
  dönse bile `seeded` sayesinde ağaç sıfırlanmaz (geri-besleme döngüsü yok).
- **`@Output() graphChanged = EventEmitter<WorkflowVersion>()`**: her mutasyondan sonra
  `treeToWorkflow(tree())` yayınlanır.
- Designer bağlaması: `<app-structured-view [workflow]="currentGraph() ?? workflow()"
  (graphChanged)="onGraphChanged($event)">`. `onGraphChanged` zaten `currentGraph`'ı set eder +
  `dirty`'yi işaretler → mevcut kaydet akışı devreye girer.

Mutasyon uygulaması: UI olayı (`{action, target, lane?, newItem?}`) gelir → `findPath(tree, target)`
→ ilgili tree-op → `tree.set(next)` → `graphChanged.emit(treeToWorkflow(next))`.

## 4. Minimal etkileşim UI

Düzenleme denetimleri yalnız `mode==='tree'` iken görünür.

- **`StructuredItemComponent`**: her öğe kartı/kutusu için **[sil] [↑] [↓]** düğmeleri; olayları
  öğe **referansıyla** yukarı yayar (`@Output() itemAction = EventEmitter<{action:'delete'|'up'|'down';
  target: StructuredItem}>()`). Konteyner lane'i başlığında **[+ ekle]** (`@Output() addToLane =
  EventEmitter<{container: ContainerItem; lane: LaneName; item: StructuredItem}>()`).
- **Kök dizi**: `StructuredViewComponent` şablonunda kök için bir **[+ ekle]** (kök diziye ekler).
- **[+ ekle] menüsü:** kontrol tipleri doğrudan (Eğer / Her Biri İçin / Sayaç / While / Dene-Yakala)
  + "Aktivite…" → mevcut `ActivityCatalogService` (`getActivities`/katalog) ile kompakt aktivite
  açılır listesi. Seçim `newContainer(type)` ya da `newStep(activityId)` üretir. Eklenen aktivite
  props'suz gelir (props C3).
- Olaylar recursive bileşenlerde `StructuredSequenceComponent` üzerinden `StructuredViewComponent`'e
  taşınır (yalnız event yeniden-yayını; path yok — path `findPath` ile çıkar).

## 5. Test

- **tree-ops (unit):** `insertItem`/`removeItem`/`moveItem`/`findPath` — kök dizi, iç içe lane
  (döngü body'sine ekle/sil), sınır (ilk ↑ / son ↓ no-op), boş lane'e ekle, `findPath` referans
  bulur / bulamazsa null. `newStep`/`newContainer` doğru şekil (boş lane'ler).
- **`StructuredViewComponent` (component):** öğe sil → `graphChanged` doğru `WorkflowVersion`
  (beklenen node/bağlantı) yayar; lane'e kontrol node ekle → ağaç güncellenir + yayın; `fallback`
  workflow'da düzenleme denetimleri render edilmez; input echo ağacı sıfırlamaz (`seeded`).
- **`StructuredItemComponent` (component):** [sil]/[↑]/[↓]/[+] doğru olayları **referansla** yayar.
- **Designer (component):** yapısal görünümdeki `graphChanged` → `dirty` true + `currentGraph`
  güncellenir (mevcut `onGraphChanged` yolu).

## 6. Kapsam dışı (bilinçli)

- Sürükle-bırak (toolbox→lane, konumla sırala, lane'ler arası taşı, otomatik-tel jestleri) — **C2**.
- Node seçme + özellik paneli entegrasyonu + undo/redo — **C3**.
- tryCatch içeren / keyfi (yapısal-olmayan) grafların düzenlenmesi — `fallback` (A'nın tryCatch-ters
  kısıtı + D). Bu graflar yapısal modda salt-okunur mesaja düşer.
- Eklenen aktivitenin props/parametre düzenlemesi — **C3** (C1'de aktivite yalnız `activityId` ile).
- Pan/zoom (B'de mevcut) değişmez.

## 7. Dosya yapısı (öngörü)

- `src/app/studio/designer/structured/edit/tree-ops.ts` + `tree-ops.spec.ts` — saf mutasyonlar.
- `structured/view/structured-view.component.*` — değişebilir tree signal + graphChanged + kök [+ ekle]
  + mutasyon uygulama (`findPath` + tree-ops).
- `structured/view/structured-item.component.*` — [sil]/[↑]/[↓]/[+ ekle] denetimleri + olay çıkışları.
- `structured/view/structured-sequence.component.*` — olay yeniden-yayını (item → view).
- `structured/view/add-menu` (küçük menü) — kontrol tipleri + aktivite açılır listesi
  (`ActivityCatalogService`).
- `designer.component.html` — `(graphChanged)="onGraphChanged($event)"` bağlaması.
- i18n `structured.*` genişler (ekle/sil/yukarı/aşağı/aktivite etiketleri).
