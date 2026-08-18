# Yapısal Konteyner Editörü — Alt-proje B: Render + Salt-Okunur Görünüm — Tasarım

**Tarih:** 2026-07-18
**Kapsam:** Yalnızca Studio (Angular). Salt-okunur. Runtime/`WorkflowSchema.json`/`BaseRunner` değişmez.
**Bağlam:** "Yapısal konteyner editörü" hedefinin ikinci alt-projesi. Alt-proje **A** (model + köprü)
tamamlandı (`designer/structured/`: `structured-model`, `tree-to-workflow`, `workflow-to-tree`,
`structural-invariants`). B, A'nın ürettiği `StructuredSequence`'i **iç içe kutular + lane'ler**
olarak özyinelemeli Angular + CSS ile render eder ve designer'a **salt-okunur bir "Yapısal görünüm"**
ekler. Düzenleme (C) ve keyfi grafların göçü (D) ayrı alt-projelerdir.

---

## 1. Kapsam ve karar özeti

- **Render tekniği:** Özyinelemeli Angular bileşenleri + CSS akışı (dikey flex istifleme; konteyner
  içeriğe göre büyür). **Rete yok, elle koordinat yok, SVG yok.**
- **Salt-okunur:** Düzenleme, sürükle-bırak ekleme, otomatik-tel, yeniden sıralama **yoktur** (C).
- **Görünüm toggle:** Designer'a "Yapısal görünüm" düğmesi; açıkken serbest-graf canvas gizlenir,
  mevcut workflow A ile ağaca çevrilip render edilir. Çevrilemezse (keyfi graf / tryCatch-ters
  köprü kısıtı) nazik bilgi kutusu gösterilir.
- **Gezinme:** Kapsayıcı `overflow:auto`; boş alanı sürükleyerek pan; `Ctrl+tekerlek` ve `+/−`
  düğmeleriyle CSS `transform: scale()` zoom.
- Yeni bağımlılık eklenmez.

## 2. Bileşen yapısı (özyinelemeli)

Hepsi `src/app/studio/designer/structured/view/` altında, standalone.

- **`StructuredViewComponent`** (host):
  - Girdi: `@Input() workflow: WorkflowVersion | null`.
  - `workflowToTree(workflow)` çağrısını `try/catch` ile sarar. Başarı → kök `StructuredSequence`'i
    render eder. Hata (keyfi graf / tryCatch-ters) → render yerine bilgi kutusu
    ("Bu workflow yapısal görünüme uygun değil — Faz C/D").
    Boş/`null` workflow → boş durum ipucu.
  - Gezinme kabuğunu barındırır (pan/zoom sarmalayıcı + zoom kontrolleri).
- **`StructuredSequenceComponent`** `@Input() items: StructuredSequence`:
  - Dikey flex; her öğe için bir `StructuredItemComponent`. Boş dizi → ince "boş" ipucu.
- **`StructuredItemComponent`** `@Input() item: StructuredItem`:
  - `item.kind === 'step'` → adım kartı.
  - `item.kind === 'container'` → başlıklı konteyner kutusu + `lanesFor(item.type)` sırasıyla her
    lane bir etiketli bölüm; bölüm içeriği yine `StructuredSequenceComponent` (özyineleme).

Özyineleme derinliği workflow yuvalanmasıyla sınırlıdır (döngüsüz ağaç).

## 3. Görsel dil (salt-okunur)

- **Adım kartı** (`step`): mevcut canvas node kartının görsel dilini (renk/ikon/başlık düzeni)
  yeniden kullanır — ikon + başlık (aktivite görünen adı ya da node tipi) + `activityId` alt-satırı.
  Salt-okunur: silme düğmesi, port soketleri, breakpoint göstergesi **yoktur**.
- **Konteyner kutusu** (`container`): başlıklı çerçeve; başlık = tip etiketi (Her Biri İçin / Sayaç /
  While / Eğer / Dene-Yakala, i18n) + kısa props özeti (forEach `items`; for `start..end`; while/if
  `condition`; tryCatch `exceptionVariable`). İçeriğe göre büyür; iç içe girinti kenar/dolguyla.
- **Lane'ler**: etiketli bölümler — döngü **Gövde**; if **Doğru / Yanlış**; tryCatch
  **Dene / Yakala / Finally** (i18n). Boş lane "boş" ipucuyla ince görünür.

## 4. Designer entegrasyonu + gezinme

- **Toggle:** Designer'a "Yapısal görünüm" düğmesi (mevcut Basit/Gelişmiş toggle deseniyle; ör. bir
  signal `structuredView()`). Açıkken serbest-graf `app-canvas` gizlenir ve `app-structured-view`
  mevcut workflow ile (`currentGraph() ?? workflow()`) gösterilir. Kapalıyken mevcut davranış aynen
  korunur (regresyon yok).
- **Gezinme kabuğu:** `StructuredViewComponent` içinde bir `overflow:auto` kapsayıcı + içte bir
  `transform-origin: top left; transform: scale(z)` sarılan içerik.
  - **Pan:** boş alanda `pointerdown`+sürükleme kapsayıcının `scrollLeft/scrollTop`'unu günceller.
  - **Zoom:** `+/−` düğmeleri ve `Ctrl+wheel` `z` faktörünü değiştirir (mevcut canvas zoom
    sınırlarına benzer clamp; ör. 0.4–2.0). İçerik elle konumlanmaz; yalnız görsel ölçek.
- **Fallback ve host sınırı:** Host `workflowToTree`'yi `try/catch` ile sarar; `throw` (tryCatch-ters
  veya çözülemeyen giriş) → bilgi mesajı. **Önemli:** `workflowToTree` her yapısal-olmayan grafı
  `throw` etmez (yalnız tryCatch'te ve giriş/çözüm bulunamayınca). Bu yüzden host, güvenli render
  için ek bir **güvence** uygular: dönüşten sonra `treeToWorkflow(back)` ile geri çevirip
  `checkStructuralInvariants` + bağlantı-kümesi eşdeğerliğini doğrular; eşleşmezse (graf yapısal
  alt-küme değil) fallback mesajına düşer. Böylece keyfi serbest-graf **sessizce yanlış render
  edilmez** — ya doğru render edilir ya da fallback. Keyfi grafın sadık yorumu **D**'nindir.

## 5. Test

- **`StructuredItemComponent` / `StructuredSequenceComponent` (component):** verilen
  `StructuredSequence` → DOM: iç içe konteyner yuvalanması (döngü içinde if), lane etiketleri
  (Gövde/Doğru/Yanlış), adım kartı başlığı + activityId, boş-lane ipucu.
- **`StructuredViewComponent` (component):**
  - Yapısal alt-küme `WorkflowVersion` → `workflowToTree` başarı → iç içe kutular render.
  - tryCatch içeren / keyfi graf → `workflowToTree` `throw` → fallback bilgi kutusu (render yok).
  - `null`/boş workflow → boş durum.
- **Gezinme (component, jsdom sınırları):** zoom `+` düğmesi ölçek stilini artırır; `−` azaltır;
  clamp sınırlarında durur. (Pan davranışı jsdom'da sınırlı; ölçek/clamp birim olarak test edilir.)
- **Designer entegrasyonu (component):** "Yapısal görünüm" açınca `app-canvas` gizlenir,
  `app-structured-view` görünür; kapatınca tersi. Kapalı varsayılan davranış değişmez.

## 6. Kapsam dışı (bilinçli)

- Düzenleme, sürükle-bırak ekleme, otomatik-tel bağlama, yeniden sıralama, node seçme/silme — **C**.
- Keyfi (yapısal olmayan) grafların yorumu/göçü ve tryCatch-ters köprüsü — **D** (ve A'nın
  ertelenmiş tryCatch-ters kısmı). Bu nedenle tryCatch içeren mevcut workflow'lar B'de fallback'e düşer.
  Not: renderer bileşeni tryCatch konteynerini çizebilir (elle bir ağaç verilirse); sınır yalnız
  **host dönüşümündedir**.
- Rete entegrasyonu, elle node konumlandırma, `position` üretimi.
- Kalıcılık akışı değişikliği (görünüm salt-okunur; workflow'u değiştirmez).

## 7. Dosya yapısı (öngörü)

- `src/app/studio/designer/structured/view/structured-view.component.ts|html|scss` — host + gezinme.
- `src/app/studio/designer/structured/view/structured-sequence.component.ts|html|scss` — dizi.
- `src/app/studio/designer/structured/view/structured-item.component.ts|html|scss` — adım/konteyner.
- İlgili `*.spec.ts` dosyaları.
- Designer değişiklikleri: `designer.component.ts|html` (toggle + koşullu görünüm), i18n `tr/en`
  (`structured.*` anahtarları: tip etiketleri, lane adları, fallback mesajı, boş durum).
