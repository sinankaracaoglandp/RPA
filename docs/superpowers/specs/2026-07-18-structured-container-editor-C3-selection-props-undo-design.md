# Yapısal Konteyner Editörü — Alt-proje C3: Seçim + Özellik Paneli + Undo/Redo — Tasarım

**Tarih:** 2026-07-18
**Kapsam:** Yalnızca Studio (Angular + saf TS). Runtime/`WorkflowSchema.json`/`BaseRunner` değişmez.
**Bağlam:** "Yapısal konteyner editörü" C dilimlerinden üçüncüsü. A (model+köprü), B (render+görünüm),
C1 (mutasyon+minimal etkileşim), C2 (sürükle-bırak) tamamlandı. C3, yapısal moda **node seçme +
mevcut özellik paneliyle parametre düzenleme + geri-al/yinele** ekler.

---

## 1. Kapsam ve karar özeti

- **Seçim + özellik düzenleme:** Node'a tıkla → seç → **mevcut `PropertiesPanelComponent`**'te
  parametreleri düzenle. Yeni panel UI'si yok; designer'ın var olan paneli beslenir.
- **Panel değişkenleri:** Yalnız workflow'un tanımlı değişkenleri (ForEach item enjeksiyonu **kapsam
  dışı**).
- **Undo/Redo:** Geçmiş yığınıyla tüm mutasyonlar geri alınıp yinelenir; düğmeler + klavye.
- Yeni bağımlılık yok. C1 düğmeleri, C2 sürükle-bırak, B pan/zoom/palet korunur (üstüne eklenir).
- Kapsam dışı: keyfi/tryCatch graf düzenleme (fallback), değişken enjeksiyonu, çoklu seçim/kopyala.

## 2. Seçim + özellik paneli entegrasyonu

- `StructuredViewComponent` bir **`selected: StructuredItem | null` signal** tutar. Adım kartına /
  konteyner başlığına tıklama seçer (seçili stil vurgusu); canvas boşluğuna tıklama seçimi kaldırır.
  Tıklama, sürükle/düğme tıklamalarıyla çakışmayacak şekilde ele alınır (düğme/drag-handle olayları
  `stopPropagation`).
- Seçilen öğeden **activityType + properties** türetilir:
  - Adım: `activityType = node.activity`, `properties = node.properties ?? {}`.
  - Konteyner: `activityType = CONTROL_ACTIVITY_OF[type]` (`if→Logic.If`, `forEach→Logic.ForEach`,
    `for→Logic.For`, `while→Logic.While`, `tryCatch→Logic.TryCatch`), `properties = item.props`.
  - Eşleme küçük bir yerel sabittir (`structured/edit/control-activity-map.ts`); canvas'taki
    `NODE_TYPE_TO_CONTROL_ACTIVITY` ile aynı değerler, kopyası ayrı tutulur (canvas iç sabiti export
    değil).
- **`@Output() nodeSelect = EventEmitter<StructuredSelection | null>`** —
  `StructuredSelection = { activityType?: string; properties: Record<string, unknown> }`.
- **Designer wiring:** yapısal seçim `nodeSelect` → designer `selectedActivityType` +
  `selectedProperties` signal'lerine yazılır → mevcut özellik paneli (sağ) olduğu gibi render eder.
  Panel bağlaması `[variables]="panelVariables()"` **değişmez**: yapısal modda graf-node seçimi
  olmadığından `injectedLoopVariables(null,…)` boştur → `panelVariables()` doğal olarak yalnız
  workflow değişkenlerine iner (ekstra dallanma gerekmez).
- **Geri akış:** `PropertiesPanel.propertiesChange` → designer `onPropertiesChange`. Yapısal moddayken
  (`structuredView()`), canvas yerine `StructuredViewComponent.updateSelectedProps(props)` çağrılır
  (ViewChild/olay ile) → seçili öğenin props'u ağaçta güncellenir → `commit` → `graphChanged`.

## 3. Props güncelleme (yeni tree-op)

`structured/edit/tree-ops.ts`:
- **`updateItemAt(tree, path: Path, fn: (item) => StructuredItem)`** (dışa açık yardımcı) — `path`'teki
  öğeyi `fn` ile değiştirir (immutable; `updateSeqAt` üzerine).
- **`setItemProps(tree, path, props: Record<string, unknown>): StructuredSequence`** —
  - Adım (`kind==='step'`): `node = { ...node, properties: props }` (activity parametreleri bag'de).
  - Konteyner (`kind==='container'`): `{ ...item, props }`.
  Adres, seçili öğeden `findPath(tree, selectedRef)` ile.

## 4. Undo/Redo

- `StructuredViewComponent`: `past: StructuredSequence[]`, `future: StructuredSequence[]`.
- **`commit(next)`** (tüm mutasyonların tek geçidi): yeni ağacı set etmeden ÖNCE mevcut ağacı `past`'a
  iter, `future`'ı temizler; sonra `tree.set(next)` + `graphChanged.emit`.
- **`undo()`:** `past` boş değilse → mevcut ağaç `future`'a; `tree.set(past.pop())`; `graphChanged`.
  **`redo()`:** tersi. `canUndo`/`canRedo` düğme durumları.
- **Seçim tazeliği:** undo/redo sonrası seçili referans ağaçta olmayabilir → `selected` temizlenir
  (panel için `nodeSelect.emit(null)`).
- **Denetimler:** toolbar'da geri-al/yinele düğmeleri (pasiflik `canUndo`/`canRedo`); klavye
  `Ctrl+Z` (undo), `Ctrl+Shift+Z` / `Ctrl+Y` (redo) — yalnız `editable` + bileşen odaklıyken
  (`@HostListener('keydown', ...)` veya scroll kapsayıcı odağı; free-graph kısayollarıyla çakışmaz).
- **Tohumlama:** ağaç tohumlanınca `past`/`future` boştur; toggle `@if` bileşeni yeniden kurduğundan
  her yapısal oturum taze geçmişle başlar.

## 5. Test

- **tree-ops (unit):** `setItemProps` — adım `node.properties` değişir, konteyner `item.props` değişir,
  ikisi de immutable; `updateItemAt` seçili öğeyi değiştirir.
- **`StructuredViewComponent` (component):**
  - adım seç → `nodeSelect` `{ activityType, properties }`; konteyner seç → `Logic.ForEach` + props.
  - `updateSelectedProps(props)` → ağaç güncellenir + `graphChanged`.
  - `undo()` bir mutasyonu geri alır + `graphChanged`; `redo()` geri getirir; `canUndo`/`canRedo`
    sınırları; undo sonrası `nodeSelect.emit(null)`.
  - `fallback`'te seçim/düzenleme yok.
- **Designer (component):** yapısal `nodeSelect` → `selectedActivityType`/`selectedProperties` beslenir;
  yapısal moddayken `onPropertiesChange` structured-view'a yönlenir (canvas'a değil).

## 6. Kapsam dışı (bilinçli)

- Keyfi (yapısal-olmayan) / tryCatch graf düzenleme — **D** (fallback).
- ForEach item değişken enjeksiyonu — panel yalnız workflow değişkenleri.
- Çoklu seçim, kopyala/yapıştır/kes, sürükle-tutamaç ayrımı — sonraki iyileştirmeler.
- Pan/zoom (B), palet + sürükle-bırak (C2), C1 düğmeleri değişmez.

## 7. Dosya yapısı (öngörü)

- `structured/edit/tree-ops.ts` (+spec) — `updateItemAt`, `setItemProps`.
- `structured/edit/control-activity-map.ts` — `CONTROL_ACTIVITY_OF` sabiti (+ küçük spec veya
  tree-ops spec içinde).
- `structured/view/structured-item.component.*` — seçim tıklaması + seçili stil + `select` çıkışı.
- `structured/view/structured-sequence.component.*` — `select` yeniden-yayını.
- `structured/view/structured-view.component.*` — `selected` signal + `nodeSelect` çıkışı +
  `updateSelectedProps` + undo/redo (yığın + düğmeler + klavye).
- `designer.component.ts|html` — yapısal `nodeSelect` bağlama; `onPropertiesChange` yapısal dallanma;
  yapısal modda panel `variables()` (enjeksiyonsuz).
- i18n `structured.*` — `undo`/`redo`.
