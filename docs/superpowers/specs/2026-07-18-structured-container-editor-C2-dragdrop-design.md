# Yapısal Konteyner Editörü — Alt-proje C2: Sürükle-Bırak + Otomatik-Tel — Tasarım

**Tarih:** 2026-07-18
**Kapsam:** Yalnızca Studio (Angular + saf TS). Runtime/`WorkflowSchema.json`/`BaseRunner` değişmez.
**Bağlam:** "Yapısal konteyner editörü" C dilimlerinden ikincisi. A (model+köprü), B (render+görünüm),
C1 (mutasyon + minimal etkileşim) tamamlandı. C2, C1'in üzerine **sürükle-bırak** ekler: yapısal
paletten lane'lere yeni öğe bırakma + mevcut öğeleri lane içinde/arası sürükleyerek sıralama/taşıma.
**Otomatik-tel ayrı iş değildir** — mutasyon `tree-ops` → `treeToWorkflow` üretir (C1 boru hattı).

---

## 1. Kapsam ve karar özeti

- **Mekanizma:** Angular CDK DragDrop (`@angular/cdk` ^22 zaten bağımlılık ve toolbox'ta kullanımda).
- **Palet:** Yapısal modda küçük bir palet (kontrol tipleri + katalog aktiviteleri) sürükle-kaynağı;
  lane'e/köke bırakınca **yeni** öğe eklenir (palet dizisi mutasyona uğramaz — öğe üretilir).
- **Mevcut öğeler:** Lane içinde/arası sürüklenerek sıralanır/taşınır.
- **C1 düğmeleri korunur** ([+ ekle]/[sil]/[↑]/[↓]) — erişilebilir alternatif; sürükle üstüne eklenir.
- Yeni bağımlılık yok. Kapsam dışı: node seçme + özellik paneli + undo/redo (**C3**); keyfi grafların
  göçü / tryCatch düzenleme (**D**, fallback).

## 2. Mimari (CDK + referans-tabanlı adresleme)

- Her **lane** ve **kök dizi** bir `cdkDropList`; hepsi tek bir **`cdkDropListGroup`** içinde → iç içe
  olsalar bile CDK otomatik birbirine bağlar (elle `cdkDropListConnectedTo` yok).
- Her **öğe** `cdkDrag`; `[cdkDragData]="item"`. **Palet çipleri** de `cdkDrag`,
  `[cdkDragData]="{ factory }"` (yeni öğe üreticisi).
- **Adresleme referansla (C1 felsefesi, path-threading yok):** her `cdkDropList`'in
  `[cdkDropListData]` değeri o lane/kök **dizi referansıdır**. Bırakmada host, ağaçta o referansın
  adımlarını `findSeqPath` ile bulur.
- **Bırakma eşlemesi** (`onDrop(event)`):
  - `previousContainer.data` bir `{ factory }` ise (palet) → hedef diziye `insertItem(toSteps,
    currentIndex, factory())`.
  - Aynı dizi (`previousContainer === container`) → `reorderInSeq`.
  - Farklı lane → öğeyi kaynaktan çıkar, hedefe ekle (aşağıda).
  - Ardından C1'deki gibi `commit` → `treeToWorkflow(next)` yayılır → designer taslak kaydı.

## 3. Yeni tree-ops + bırakma index semantiği

`structured/edit/tree-ops.ts`'e iki ekleme (mevcut `removeItem`/`insertItem` yeniden kullanılır):

- **`findSeqPath(tree: StructuredSequence, seq: StructuredSequence): PathStep[] | null`** — bir dizi
  **referansını** ağaçta arar; adım yolunu döndürür (kök dizi = `[]`, bulunamazsa `null`). Kök dizinin
  kendisi `tree` ile aynı referanssa `[]`.
- **`reorderInSeq(tree, seqSteps: PathStep[], fromIndex, toIndex): StructuredSequence`** — adreslenen
  diziyi CDK `moveItemInArray` semantiğiyle taşır: kopya al, `splice(fromIndex,1)`, `splice(toIndex,0,
  item)` (ek index ayarı YOK — CDK `currentIndex`'i doğrudan hedef indekstir).

**Lane'ler arası** (host'ta orkestrasyon; yeni op gerekmez):
```
item  = fromSeqRef[previousIndex]
t1    = removeItem(tree, { steps: findSeqPath(tree, fromSeqRef)!, index: previousIndex })
toStp = findSeqPath(t1, toSeqRef)!        // silme yalnız kaynağın ata zincirini yeniden kurar;
                                          // farklı alt-ağaçtaki hedef dizi referansı KORUNUR
next  = insertItem(t1, toStp, currentIndex, item)
```
Nesting kaymaları (silmenin ata indekslerini kaydırması) referans-tabanlı yeniden bulmayla
(`findSeqPath(t1, toSeqRef)`) doğru kalır. CDK, sürüklenen öğenin **içindeki** listelere bırakmayı
zaten engeller, dolayısıyla hedef asla taşınan öğenin içinde olmaz.

## 4. Palet + wiring

- **Yeni `StructuredPaletteComponent`** (`structured/view/structured-palette.component.*`):
  kontrol tipleri (`if/forEach/for/while/tryCatch`) + katalog aktiviteleri (`ActivityCatalogService`)
  için `cdkDrag` çipleri. `[cdkDragData]="{ factory: () => newContainer(type) }"` /
  `{ factory: () => newStep(activityId) }`. Palet listesi asla mutasyona uğramaz.
- **`StructuredViewComponent`:** render `cdkDropListGroup` ile sarılır; palet toolbar/yan tarafta;
  `onDrop(event: CdkDragDrop<...>)` host metodu. `commit` C1 ile aynı.
- **`StructuredItemComponent`:** her lane `<section cdkDropList [cdkDropListData]="laneItems(...)"
  (cdkDropListDropped)="drop.emit($event)">`; her öğe `cdkDrag [cdkDragData]="item"`. `drop` olayı
  yukarı yayılır (item → sequence → view). `editable` kapılı.
- **`StructuredSequenceComponent`:** kök diziyi `cdkDropList [cdkDropListData]="items"` yapar; `drop`
  olayını yayar. (İç içe lane dropList'leri item bileşenindedir.)
- Modüller: `CdkDrag`, `CdkDropList`, `CdkDropListGroup` `@angular/cdk/drag-drop`'tan ilgili
  bileşenlerin `imports`'una eklenir.

## 5. Test

- **tree-ops (unit):** `findSeqPath` (kök `[]` / iç içe lane / bulunamaz `null`); `reorderInSeq`
  (ileri, geri, aynı-yer no-op).
- **Host `onDrop` (unit):** `CdkDragDrop`-benzeri düz nesne kur (`{ previousContainer:{data},
  container:{data}, previousIndex, currentIndex, item:{data} }`) → çağır:
  - palet ekleme (`previousContainer.data={factory}`) → hedef diziye yeni öğe, doğru `WorkflowVersion`.
  - aynı-lane reorder → sıra değişir.
  - lane'ler arası taşıma → kaynaktan çıkar, hedefe girer.
- **Palet (component):** çip `cdkDragData.factory` doğru öğe (`newContainer`/`newStep`) üretir.
- **Wiring (smoke):** `editable` iken lane'ler `cdkDropList`, öğeler `cdkDrag` niteliği taşır;
  `editable` değilken taşımaz (C1 salt-render korunur).

## 6. Kapsam dışı (bilinçli)

- Node seçme + özellik paneli + undo/redo — **C3**.
- Keyfi (yapısal-olmayan) grafların göçü / tryCatch düzenleme — **D** (fallback).
- Dokunmatik ince ayar, otomatik-kaydırma eşiği, sürükleme önizleme stili gibi CDK ince ayarları —
  gerekirse ayrı iyileştirme.
- Pan/zoom (B) davranışı değişmez; sürükleme pan ile çakışmayacak şekilde öğe/palet üstünden başlar.

## 7. Dosya yapısı (öngörü)

- `structured/edit/tree-ops.ts` (+spec) — `findSeqPath`, `reorderInSeq`.
- `structured/view/structured-palette.component.*` (+spec) — sürükle paleti.
- `structured/view/structured-item.component.*` — lane `cdkDropList` + öğe `cdkDrag` + `drop` çıkışı.
- `structured/view/structured-sequence.component.*` — kök `cdkDropList` + `drop` çıkışı.
- `structured/view/structured-view.component.*` — `cdkDropListGroup` + palet + `onDrop` + commit.
- i18n `structured.*` genişler (palet başlığı vb.).
