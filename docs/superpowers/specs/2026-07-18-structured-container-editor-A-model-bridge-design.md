# Yapısal Konteyner Editörü — Alt-proje A: Belge Modeli + Köprü — Tasarım

**Tarih:** 2026-07-18
**Kapsam:** Yalnızca Studio, saf TypeScript. UI yok, Rete yok. Runtime, `WorkflowSchema.json`,
`BaseRunner` **değişmez.**
**Bağlam:** Bu, "yapısal (UiPath/Blockly tarzı) konteyner editörü" hedefinin ilk alt-projesidir.
Editör mevcut serbest-graf tasarımcının yerine geçmez; **ayrı bir mod** olarak eklenecektir
(sonraki fazlar). Alt-projeler: **A** (bu spec — model + köprü), **B** (render + otomatik yerleşim),
**C** (etkileşimli sürükle-bırak + otomatik tel), **D** (mevcut serbest grafların göçü). A, B/C/D'nin
tamamının dayandığı temeldir ve UI'sız tam test edilebilir.

---

## 1. Problem ve motivasyon

Hedeflenen yapısal editörde bir bölmenin (lane) içi, elle tel çekilen serbest bir alt-graf değil,
konuma göre sıralı **yapısal bir dizidir**; dallanma ve döngü iç içe konteynerlerdir. Bu editörün
her mutasyonu bir "yapısal ağaç" üzerinde iş görecek, ancak kalıcılık ve çalıştırma hâlâ mevcut düz
`WorkflowVersion` (nodes + connections) biçiminde olacaktır. Dolayısıyla **ilk gereken şey**, bu
yapısal ağaç ile düz `WorkflowVersion` arasında iki yönlü, doğrulanmış bir dönüştürücüdür (köprü).
Köprü olmadan render (B) veya etkileşim (C) inşa edilemez.

Düz model saf bir bağlantı grafı **değildir** ve köprü bunu birebir üretmelidir:
- **Diziler:** ardışık node'lar `{fromPort:'out', toPort:'in'}` bağlantısıyla.
- **Döngüler (forEach/for/while):** `body` (gövde başlangıcına), `loop-back` (gövde sonundan
  konteynere), `exit` (döngü sonrası akışa).
- **If:** `true` / `false` portları dallara; dallar sonra ortak ardıla yakınsar.
- **TryCatch:** çocuklar **bağlantı değil, node özelliğidir** — `tryNodeId`, `catchNodeId`,
  `finallyNodeId` (mevcut `canvas.serialize()` ile birebir; `BaseRunner.ExecuteTryCatchAsync`
  bu özellikleri okur).

## 2. Yapısal ağaç modeli

Saf tip tanımları (yeni dosya, Angular bağımlılığı yok):

```
StructuredSequence = StructuredItem[]

StructuredItem =
  | StepItem      { kind: 'step'; node: WorkflowNode }
  | ContainerItem { kind: 'container'; type: ContainerType; props: Record<string, unknown>;
                    lanes: Partial<Record<LaneName, StructuredSequence>> }

ContainerType = 'forEach' | 'for' | 'while' | 'if' | 'tryCatch'
LaneName      = 'body' | 'true' | 'false' | 'success' | 'failure' | 'out'

Konteyner tipine göre geçerli lane'ler:
  forEach | for | while  → { body }
  if                     → { true, false }
  tryCatch               → { success, failure, out }   // Try / Catch / Finally
```

- **Kök** = bir `StructuredSequence` (tüm workflow gövdesi).
- **StepItem** yalnızca yaprak node tiplerini taşır (activity, assign, log, delay, checkpoint,
  userPrompt, terminate, componentCall, merge). Node'un kendi `properties`/alanları `node` içinde durur.
- **ContainerItem.props** konteyner node'a özgü alanları taşır (ör. forEach `items`/`itemVariable`/
  `itemFields`, for `start`/`end`/`step`/`indexVariable`, if `condition`, tryCatch `exceptionVariable`).
- Konteynerler iç içe olabilir (lane içinde container). Konteyner içinde "serbest tel" yoktur; sıra
  = dizideki konumdur.
- `WorkflowNode` id'leri yapısal-modda üretilirken kararlı GUID'dir (`crypto.randomUUID`).
- `position` A tarafından ÜRETİLMEZ (topoloji konumdan bağımsız); render fazında (B) atanır.

## 3. Köprü: ağaç → düz `WorkflowVersion` (`treeToWorkflow`)

Her `StructuredItem` bir `WorkflowNode` üretir. Bağlantılar komşuluktan ve konteyner tipinden
türetilir. Anahtar kavram: her öğenin bir **head** (giriş node id) ve bir/çok **tail** (dallanma
yüzünden birden fazla olabilen çıkış node id) vardır.

Bir dizinin bağlanması: `linkSequence(seq, afterHeads)` — her öğenin tail'lerini bir sonraki öğenin
head'ine `out→in` ile bağlar; son öğenin tail'leri fonksiyona verilen `afterHeads`'e bağlanır
(dizinin ardılı; kök dizide boş).

Öğe tipine göre head/tail ve iç bağlantılar:

- **StepItem** — head = tail = `node.id`. İç bağlantı yok.
- **forEach / for / while** — head = konteyner id.
  - `{from: container, to: body.head, fromPort:'body'}`
  - `body` lane'i `linkSequence(body, afterHeads=[container])` ile bağlanır; ama son öğenin
    konteynere dönüşü `toPort:'loop-back'` olmalıdır → döngü lane'i için `afterHeads` yerine özel
    "loop-back kapanışı" uygulanır: `{from: bodyTail, to: container, toPort:'loop-back'}`.
  - Konteynerin **tail'i = konteynerin kendisi**, dış diziye `exit` portuyla bağlanır
    (`{from: container, to: nextHead, fromPort:'exit'}`).
- **if** — head = if id.
  - `{if, true.head, 'true'}`, `{if, false.head, 'false'}`.
  - Her lane `linkSequence` ile kendi içinde bağlanır; lane'in tail'leri konteynerin dış tail
    kümesine katılır. **Boş lane** varsa, o portun tail'i if node'un kendisidir (o port doğrudan
    ardıla gider → koşul o dalda false olduğunda akış sürer).
  - Konteynerin **tail'leri = true.tail'ler ∪ false.tail'ler** (boş lane için if id + ilgili port).
    Dış dizi bu tüm tail'leri sonraki head'e bağlar → dallar yakınsar.
- **tryCatch** — head = tryCatch id. Çocuklar **node özelliği** olarak yazılır (bağlantı değil):
  - `node.tryNodeId = success.head`, `node.catchNodeId = failure.head`, `node.finallyNodeId = out.head`
    (boş lane → ilgili özellik atanmaz).
  - `success` ve `failure` lane'leri `linkSequence(lane, afterHeads=[])` ile yalnız kendi içinde
    `out/in` zincirlenir (tail'leri açık kalır; runtime `BaseRunner` deseninde lane'i doğal sonuna
    kadar koşar).
  - **Devam (tryCatch sonrası) semantiği — dikkat:** `canvas.serialize()`, tryCatch kaynağındaki
    `out` portunu `finallyNodeId` olarak yutar; bu yüzden konteyner devamı `out` portuyla
    bağlanamaz. Konteynerin **tail'i = `out` (finally) lane'inin tail'idir**; devam bu tail'den
    normal `out/in` ile sonraki öğeye bağlanır. `out` lane'i boşsa devam semantiği (finally'siz
    tryCatch'in ardılı) belirsizdir ve **golden/runtime testiyle sabitlenecektir** (§5): en olası
    çözüm, finally boşsa köprünün tek-node'luk bir örtük geçiş (`merge`) ekleyip onu finally lane
    başı yapması; nihai karar runtime doğrulamasına bırakılır.

`position` atanmaz. Üretilen `WorkflowVersion.schemaVersion` "1.0", `id`/`name`/`version` çağırandan
gelir (varsayılan boş workflow alanları).

## 4. Köprü: düz → ağaç (`workflowToTree`, yapısal alt-küme)

A yalnız **kendi ürettiği iyi-biçimli grafı** geri okur (keyfi graf D'nindir). Algoritma:

1. Kök giriş node'unu bul (gelen `in`/branch kenarı olmayan node; mevcut `BaseRunner` entry deseni).
2. `readSequence(startId, stopAtHeads)`: `out` zincirini yürü; her node için:
   - Döngü/if/tryCatch ise `ContainerItem` kur: lane'leri portlardan (body/true/false) veya
     özelliklerden (tryNodeId/catchNodeId/finallyNodeId) topla, her lane'i özyinelemeli
     `readSequence` ile çöz. Konteynerin ardılı: döngü `exit`, if yakınsama hedefi, tryCatch `out`.
   - Aksi halde `StepItem`.
   - Bir sonraki node, `stopAtHeads` (ör. yakınsama noktası / lane sınırı) kümesine girene dek sürer.
3. **Yakınsama tespiti (if):** her iki dalın ortak ulaştığı ilk node, if konteynerinin ardılıdır;
   dallar o node'da durur (`stopAtHeads`).

## 5. Test

- **`treeToWorkflow` birim:**
  - Tek konteyner: forEach (body+loop-back+exit), for, while, if (iki dal + yakınsama, boş dal),
    tryCatch (üç özellik + out).
  - İç içe: döngü içinde if; if dalında döngü; tryCatch success içinde forEach.
  - Boş lane'ler; tek öğeli dizi; boş kök dizi.
- **`workflowToTree` birim:** yukarıdakilerin ürettiği düz grafları geri okuma.
- **Round-trip:** elle kurulmuş yapısal ağaçlar için `tree → flat → tree` özdeşliği (derin eşitlik);
  `flat → tree → flat` (yapısal alt-kümede) mantıksal eşdeğerlik (bağlantı kümesi eşitliği,
  sıralamadan bağımsız).
- **Runtime uyumu (golden):** `treeToWorkflow` çıktısının JSON'u mevcut
  `WorkflowValidator.ValidateWorkflowJson` semasından **geçmeli**. Testte üretilen grafı
  serialize edip şemaya karşı doğrula (Studio tarafında şema kopyası/karşılığı ile). Bu, if
  yakınsaması ve tryCatch özellik semantiğini gerçek doğrulayıcı sözleşmesine sabitler.
  - Not: Şema doğrulaması Studio'da mevcut bir yardımcı yoksa, golden test düz grafın
    **yapısal değişmezlerini** (her döngüde tam bir body+loop-back, if'te true+false, tryCatch'te
    tryNodeId, tek entry node, ulaşılabilirlik) doğrulayan bir kontrol ile ikame edilir.

## 6. Kapsam dışı (bilinçli)

- **B (render + otomatik yerleşim), C (etkileşimli sürükle-bırak + otomatik tel), D (mevcut serbest
  grafların göçü)** — ayrı brainstorm + spec.
- `position` üretimi, Rete entegrasyonu, mod geçişi UI'si.
- Keyfi (yapısal olmayan) düz grafların ağaca çevrilmesi ve indirgenemez grafların ele alınması — D.
- Undo/redo, kalıcılık akışı (mevcut draft servisleri kullanılacak; A yalnız modeli üretir).

## 7. Dosya yapısı (öngörü)

- `src/app/studio/designer/structured/structured-model.ts` — tipler (`StructuredSequence`,
  `StructuredItem`, `ContainerType`, `LaneName`) + küçük yapıcı yardımcılar.
- `src/app/studio/designer/structured/tree-to-workflow.ts` — `treeToWorkflow(tree, meta)`.
- `src/app/studio/designer/structured/workflow-to-tree.ts` — `workflowToTree(workflow)`.
- İlgili `*.spec.ts` dosyaları (birim + round-trip + golden).
