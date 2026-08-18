# Yapısal Konteyner Editörü — Alt-proje D1: Serbest-Graf → Yapısal Ağaç Göçü — Tasarım

**Tarih:** 2026-07-18
**Kapsam:** Yalnızca Studio (saf TS + Angular host). Runtime/`WorkflowSchema.json`/`BaseRunner` değişmez.
**Bağlam:** "Yapısal konteyner editörü" son büyük parçası (D — göç) ilk dilimi. A (model+köprü),
B (render+görünüm), C1–C3 (mutasyon/DnD/seçim+özellik+undo) tamamlandı. Şu an keyfi serbest-graf
workflow'lar yapısal görünümde **fallback**'e düşüyor (B'nin kör guard'ı). D1 bunu, keyfi grafın
**dizi/if/döngü** sınıfını sağlam indirgeyip; indirgenemezde **kesin tanı** veren bir analizöre çevirir.

---

## 1. Kapsam ve karar özeti

- **Ya-hep-ya-hiç:** Graf tam yapısal ağaca indirgenebilirse çevrilir; değilse fallback'te kalır **ama
  kesin NEDEN gösterilir**.
- **Sınıf:** diziler + if (yakınsamalı) + döngüler (validator-zorunlu `body`/`loop-back`/`exit`).
  **tryCatch** içeren graf net mesajla fallback (`'tryCatch yapısal göçü D2'`).
- Runtime/kontrat/serialize değişmez; salt tasarım-zamanı okuma.
- Yeni bağımlılık yok.

## 2. Algoritma — SESE-bölge özyinelemeli indirgeyici

Yeni saf modül: `src/app/studio/designer/structured/edit/structured-reducer.ts`.

```
type ReduceResult = { ok: true; tree: StructuredSequence } | { ok: false; reason: string };
export function reduceWorkflow(workflow: WorkflowVersion): ReduceResult;
```

**Ön hazırlık:**
- Kenarları `out`/`in` ile modelle; **loop-back** kenarları geri-kenar (indirgemede izlenmez, döngü
  tespitinde kullanılır).
- **Tek giriş:** loop-back dışı gelen kenarı olmayan node. 0 veya >1 ise → `{ ok:false, reason:
  'Birden fazla giriş node'u' }` (veya 'Giriş node'u bulunamadı').

**`reduceRegion(entryId, stopId | null): StructuredSequence`** (hata durumunda `ReducerError(reason)`
fırlatır; `reduceWorkflow` yakalar → `{ ok:false, reason }`):
- `current = entryId`; `current !== stop && current != null` iken:
  - Node **tryCatch** → `throw ReducerError('tryCatch yapısal göçü sonraki fazda (D2)')`.
  - Node bir **döngü** (`forEach/for/while`): `body` hedefi = bodyHead; body bölgesi tek-giriş
    doğrulanır; `body = reduceRegion(bodyHead, loopNodeId)` (loop-back'te durur); konteyner props
    node alanlarından; `current = exit hedefi`.
  - Node **if**: `conv = convergence(ifId)` (aşağıda). `conv` yoksa veya dallar **sızdırıyorsa** →
    `throw ReducerError(kesin mesaj)`. `true`/`false` dalları `reduceRegion(head, conv)`; boş dal
    (port doğrudan `conv`'a) → `[]`; `current = conv`.
  - Node **adım** (activity/assign/log/…): tek `out` hedefi olmalı; hedefe **yalnız bu kenardan**
    gelinmeli (dışarıdan ikinci giriş varsa → indirgenemez `reason`). `StepItem`; `current = out`.
  - Desteklenmeyen node tipi → `throw ReducerError("Desteklenmeyen node: '<tip>'")`.

**`convergence(ifId)` + doğrulama:**
- `trueHead`/`falseHead` = if'in `true`/`false` hedefleri.
- **Post-dominator:** her iki daldan ileri (loop-back hariç) ulaşılabilir kümelerin kesişimindeki,
  her iki daldan da geçilen **ilk** node. Yoksa → `reason: "'<if>' dalları yakınsamıyor"`.
- **Sızıntı/tek-giriş doğrulaması:** `conv`'a kadar her dalın **kendi bölgesi** dışına çıkan kenar
  olmamalı; `conv`'a yalnız dal-kuyruklarından (veya boş dalda if portundan) gelinmeli; bir bölge-içi
  node dışarıdan da hedefleniyorsa → `reason: "'<node>' iki daldan/dışarıdan ulaşılıyor (yakınsama
  yok)"`.

## 3. Tanı (diagnostics)

`reason` kesin ve kullanıcıya yönelik Türkçe cümle. Örnekler:
- `"Birden fazla giriş node'u"`
- `"'X' node'u iki daldan ulaşılıyor (yakınsama yok)"`
- `"'Y' node'u dal-içinden bölge dışına atlıyor"`
- `"'if-1' dalları yakınsamıyor"`
- `"tryCatch yapısal göçü sonraki fazda (D2)"`
- `"Desteklenmeyen node: 'merge'"` (uygunsa)

B host bu `reason`'ı fallback mesajında gösterir.

## 4. B host entegrasyonu

`StructuredViewComponent.convert()` mevcut `workflowToTree` + değişmez/sayı-guard mantığını
**`reduceWorkflow`** ile değiştirir:
- `{ ok:true, tree }` → `mode='tree'`, ağaç tohumlanır (düzenlenebilir, C1–C3 aynen çalışır).
- `{ ok:false, reason }` → `mode='fallback'`; `reason` bir signal'e yazılır ve fallback mesajı
  `{{ 'structured.fallbackReason' | translate }}: {{ reason }}` biçiminde gösterir (kör mesaj yerine).
- reduceWorkflow ya-hep-ya-hiç doğru olduğundan (tek-giriş SESE bölgeleri) eski **sayı-guard kaldırılır**
  — indirgeyici yetkilidir. A'nın `treeToWorkflow` çıktısı da normal indirgenebilir graf olarak
  ele alınır (round-trip korunur).
- `graphChanged`/kalıcılık/undo akışı değişmez. A'nın `workflowToTree`'si kendi round-trip birim
  testleri için **kalır** (B artık `reduceWorkflow` kullanır).

## 5. Test

- **reduceWorkflow (unit) — indirgenebilir:** doğrusal dizi; if+yakınsama; if boş-dal; iç içe
  (döngü içinde if / if içinde döngü); forEach/for/while → `ok:true` + beklenen ağaç yapısı.
- **indirgenemez:** birden fazla giriş; if dal-sızıntısı (bir daldan diğerine/ötesine kenar);
  yakınsama dışarıdan ulaşılıyor; desteklenmeyen node → `ok:false` + `reason` içerik doğrulaması.
- **tryCatch** içeren → `ok:false`, `reason` tryCatch mesajı.
- **round-trip uyumu:** A'nın `treeToWorkflow` çıktısı (dizi/if/döngü örnekleri) için `reduceWorkflow`
  beklenen ağacı üretir (yapı olarak `workflowToTree` ile aynı fikirde).
- **B host (component):** indirgenebilir workflow → `tree` + düzenlenebilir; indirgenemez → fallback +
  `reason` DOM'da görünür; tryCatch → fallback reason.

## 6. Kapsam dışı (bilinçli)

- **tryCatch ters göçü** — **D2** (finally/after sınırı runtime golden testiyle sabitlenir).
- İndirgenemez alt-bölgeyi kısmi/opak blok olarak sarma; node çoğaltmayla indirgeme — sonraki dilimler.
- Birden fazla girişin otomatik onarımı (yalnız tanı).
- Runtime/`WorkflowSchema.json`/serialize/kontrat değişmez.

## 7. Dosya yapısı (öngörü)

- `structured/edit/structured-reducer.ts` (+spec) — `reduceWorkflow` + iç yardımcılar (`convergence`,
  kenar/dominator analizi, `ReducerError`).
- `structured/view/structured-view.component.ts|html` — `convert()` `reduceWorkflow`'a geçer;
  `fallbackReason` signal + mesajda gösterim.
- i18n `tr.json`/`en.json` — `structured.fallbackReason`.
