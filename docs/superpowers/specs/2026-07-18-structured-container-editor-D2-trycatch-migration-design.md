# Yapısal Konteyner Editörü — Alt-proje D2: tryCatch Yapısal Göçü — Tasarım

**Tarih:** 2026-07-18
**Kapsam:** Yalnızca Studio (saf TS). Runtime/`WorkflowSchema.json`/`BaseRunner`/serialize değişmez.
**Bağlam:** D1 (serbest-graf göçü) tamamlandı; keyfi dizi/if/döngü grafları yapısal ağaca indirgeniyor,
tryCatch içeren graf net sebeple fallback'e düşüyordu. D2, `reduceWorkflow`'a tryCatch desteği ekler.

---

## 1. Kapsam ve karar özeti

- `reduceWorkflow`'a **tryCatch** eklenir (D1'deki erken tryCatch-reddi kaldırılır). Forward
  (`treeToWorkflow`) tryCatch'i A'da zaten üretir; **değişmez**. B host zaten `reduceWorkflow`
  kullandığından tryCatch içeren graf otomatik **düzenlenebilir** olur (fallback yerine).
- **Kanonik "sonrası" semantiği:** Runtime'da tryCatch'in ayrı devam portu yoktur (`out` portu
  `finallyNodeId` olarak tüketilir; `BaseRunner.ExecuteTryCatchAsync` try→catch→finally koşar, sonra
  finally'nin doğal sonundan devam eder). Bu yüzden yapısal modelde **tryCatch dizide son öğedir** ve
  "sonrası" (bir sonraki node / yakınsamaya kadar) **finally lane'ine katlanır**. Round-trip kanonik
  forma normalize eder (`[tryCatch, X]` → `[tryCatch{finally:[…,X]}]`). Runtime/kontrat değişmez.

## 2. Algoritma — `reduceWorkflow` tryCatch

`structured-reducer.ts` içinde:

- **Erken reddi kaldır:** `workflow.nodes.some(type==='tryCatch') → fallback` satırı silinir.
- **Giriş tespiti düzeltmesi:** tryCatch çocukları bağlantı değil node-özelliğidir; `tryNodeId`/
  `catchNodeId`/`finallyNodeId` ile işaret edilen head'ler **bağımsız giriş sayılmaz**. Giriş
  adaylarından bu id kümesi çıkarılır (yoksa "çok giriş" yanılgısı).
- **`reduceRegion` tryCatch dalı:**
  - `rec = node` özelliklerinden `tryNodeId`/`catchNodeId`/`finallyNodeId`.
  - `success = tryNodeId ? reduceRegion(tryNodeId, null) : []` — try/catch lane'leri kendi içinde
    biten zincirler (dangling kuyruk; `stop=null` doğal sonda durur).
  - `failure = catchNodeId ? reduceRegion(catchNodeId, null) : []`.
  - `out = finallyNodeId ? reduceRegion(finallyNodeId, stop) : []` — **devamı `stop`'a kadar soğurur**
    (katlama).
  - **Boş finally:** `finallyNodeId` tek, props'suz bir `merge` node'una işaret ediyorsa `out = []`
    (A forward konvansiyonunu geri alır: boş finally → merge geçişi).
  - `container('tryCatch', propsOf(node), { success, failure, out })` push edilir; tryCatch
    **terminal** → `cur = stop` (bölge döngüsü biter).
  - **Doğrulama:** `success`/`failure`/`out` node kümeleri **ayrık** ve tek-giriş olmalı; örtüşme veya
    dışarıdan sızıntı → kesin `reason` (D1 deseni: `"'X' node'u iki bölgeden ulaşılıyor"`).

## 3. Round-trip / runtime güveni

- **Test:** elle kurulmuş tryCatch ağacı → `treeToWorkflow` (A forward) → `reduceWorkflow` →
  `ok:true`; konteyner tipi `tryCatch`, lane'ler beklenen içerikte; finally'ye katlanan devam
  doğrulanır.
- Runtime davranışı zaten sabit (`ExecuteTryCatchAsync` finally bloğu finally zincirini sonuna kadar
  koşar); ek runtime testi gerekmez — göç yalnız tasarım-zamanı okumadır.

## 4. Test

- **reduceWorkflow (unit):** try/catch/finally dolu tryCatch → `ok:true` + lane içerikleri; boş finally
  (merge-strip) → `out:[]`; tryCatch'ten sonraki devamın finally'ye katlanması; if içinde tryCatch
  (iç içe); örtüşen/çok-girişli tryCatch → `reason`.
- **Mevcut D1 testi güncellenir:** "rejects a tryCatch graph" → artık tryCatch **indirgenir**
  (`ok:true`); o test yerini "reduces a tryCatch graph" alır.
- **B host:** tryCatch içeren workflow → `tree` + düzenlenebilir (fallback değil).

## 5. Kapsam dışı (bilinçli)

- Node çoğaltmayla indirgeme, indirgenemez alt-bölge için opak blok — sonraki (istenirse).
- Editörde "tryCatch'ten sonra kardeş öğe ekleme" kısıtı — gereksiz; reload'da finally'ye normalize
  edilir (kanonik). Kullanıcı yine finally lane'ine ekleyerek aynı sonucu alır.
- Runtime/`WorkflowSchema.json`/serialize/kontrat değişmez.

## 6. Dosya yapısı

- `structured/edit/structured-reducer.ts` (+spec) — tryCatch dalı + giriş tespiti düzeltmesi.
- (B host değişikliği YOK — zaten `reduceWorkflow` kullanıyor.)
