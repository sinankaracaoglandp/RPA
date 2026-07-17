# ForEach `item` Şema Türetme & Döngü Görsel Netliği — Tasarım

**Tarih:** 2026-07-17
**Kapsam:** Yalnızca Studio (tasarım-zamanı). Runtime, `WorkflowSchema.json` ve `BaseRunner` **değişmez**.
**İlgili aktivite:** `Logic.ForEach`.

---

## 1. Problem

`Logic.ForEach` bir liste üzerinde döner ve her elemanı `itemVariable` (varsayılan `item`)
adıyla scope'a koyar. Runtime tarafı eksiksizdir: `BaseRunner.ExecuteForEachAsync`
her tur `state.Scope.SetVariable(itemVar, item)` çağırır ve `ExpressionEngine.ResolvePath`
`${item.alan}` noktalı erişimini zaten çözer.

Eksik olan **tasarım-zamanı bilgisidir**:

1. `Logic.ForEach` metadata'sı elemanı tipsiz tanımlar (`ActivityRegistry.cs`: yalnız
   `items: string` + `itemVariable: string`). Loop, "eleman şu alanlara sahip bir objedir"
   bilgisini hiçbir yere yazmaz → downstream node'ların autocomplete'i `item.alan`'ı bilmez.
2. `itemVariable` yalnız saklanır; property panelinde düzenleme UI'si yoktur (varsayılan `item`).
3. Canvas'ta döngü gövdesinin nerede başlayıp bittiği ve `exit`'in ayrı akış olduğu
   görsel olarak belirsizdir.

Bu tasarım tamamen Studio tarafında, runtime davranışına dokunmadan bu üç boşluğu kapatır.

## 2. Mevcut altyapı (yeniden kullanılacak)

- **Autocomplete kaynağı:** `expression-input.component.ts` önerilerini panele geçen
  `WorkflowVariable[]` listesinden üretir. Her `WorkflowVariable` opsiyonel `schema` taşır.
- **Şemadan alan türetme:** `variables-panel.component.ts` (`schemaFieldRows`,
  `variablePathsFor`) bir `array` şemasından `satir.alan` alanlarını **zaten** türetir.
- **Emsal desen:** `designer.component.ts` (`onProfileActivityPropertiesChange`,
  satır 167-189) `EInvoice.ReadProfile` için `outputSchemaJson`'dan bir `WorkflowVariable`
  türetip (batch → `list<object>`, tekil → `object` şema) değişken listesine ekler.
  Ad doğrulama regex'i `^[A-Za-z_][A-Za-z0-9_]*$`.
- **Döngü graf modeli (değişmez):** ForEach node portları `body` (gövde başlangıcı),
  `exit` (döngü sonrası akış), girişler `in` + `loop-back` (gövde sonu → tekrar).
  `BaseRunner.ResolveLoopBody` gövdeyi `body` bağlantısı (start) ile `loop-back` bağlantısı
  (end) arasından çözer. `WorkflowValidator.ValidateLoopGraph` tam bir `body`, tam bir
  `loop-back`, en fazla bir `exit` ve tüm gövde yollarının loop-back'e ulaşmasını zorunlu kılar.

## 3. Tasarım

Dört parça, hepsi Studio.

### 3.1 ForEach property paneli
- `items` — mevcut ifade girişi (`${faturalar}`).
- `itemVariable` — **yeni düzenlenebilir alan**; ad, varsayılan `item`. Doğrulama:
  `^[A-Za-z_][A-Za-z0-9_]*$`; geçersizse alan kırmızı işaretlenir ve enjeksiyon yapılmaz.
- **Elle alan editörü (fallback)** — yalnız `items` şemalı bir kaynağa çözülemediğinde
  görünür: `ad : tip` satırları. Alanlar node üzerinde (ör. `properties.itemFields`)
  saklanır. Şemalı kaynağa çözülünce editör "otomatik türetildi" bilgisiyle salt-okunur.

### 3.2 Şema çözümleyici (saf fonksiyon)
Girdi: `items` ifade string'i + mevcut `WorkflowVariable[]`.
1. İfadeden kök değişken adını ayıkla (`${ad}` / `{{ad}}`; yalnız tek, düz değişken referansı).
2. Değişkeni bul; `schema.type === 'array'` ise `schema.items` (eleman şeması) döndür → **otomatik**.
3. Çözülemezse node'daki elle alanlardan bir `object` şeması kur → **fallback**.
4. Kaynak `list<object>` değil de skaler liste ise (`schema.items` alan taşımaz):
   `item` skalerdir, alan yok; `${item}` yine kullanılabilir.

Karmaşık ifadeler (`${a.b[0]}`, fonksiyon çağrısı) otomatik türetilemez → fallback editörü açılır.
Bu sessiz bir durumdur, hata değildir.

### 3.3 Body-node tespiti (saf fonksiyon)
Girdi: seçili `nodeId`, workflow grafı.
ForEach'in `body` portundan başlayıp bağlantıları izleyerek (`loop-back`/`exit`'e kadar)
gövde node kümesini çıkarır. Çıktı: `nodeId → onu saran ForEach node'ları` eşlemesi.
İç içe ForEach'te bir node birden çok loop tarafından sarılabilir.

### 3.4 Enjeksiyon
Bir node seçildiğinde, onu saran her ForEach için 3.2'den türetilen sentetik `item`
`WorkflowVariable`'ı (adı `itemVariable`, şeması türetilmiş/elle) property paneline geçen
`variables` listesine **eklenir**. Böylece autocomplete `${fatura.tutar}`'ı gösterir.
Bu enjekte değişken **kalıcı workflow'a yazılmaz** (Variables paneline/JSON'a düşmez).
İsim çakışmasında (gerçek global değişken adıyla) en-içteki loop kazanır (shadowing);
kalıcıya yazılmadığı için kalıcı kirlenme olmaz.

### 3.5 Döngü gövde/çıkış görsel netliği (hafif)
Graf modeli **değişmez**; yalnız canvas görünürlüğü:
- **Gövde vurgusu:** Bir ForEach node'u seçiliyken 3.3 ile bulunan gövde node'ları
  belirgin bir stille (ince renkli çerçeve/arka plan) işaretlenir; seçim kalkınca kalkar.
- **Port etiketleri:** `body` → "Gövde (başlangıç)", `exit` → "Çıkış (döngü sonrası)",
  `loop-back` girişi → "Gövde sonu / tekrar" (i18n). Mevcut port toning korunur.
- **Bağlanmamış exit ipucu:** `exit` portu boşsa node üzerinde nazik bir "döngü sonrası
  akış bağlı değil" göstergesi (yalnız görsel hatırlatma; validator kuralı değişmez).

3.3'teki gezinti hem 3.4 hem 3.5 tarafından paylaşılır (tek kaynak).

## 4. Kenar durumlar

- `items` bir değişkene çözülemez → fallback editörü açılır (sessiz).
- Kaynak `list<object>` değil (skaler liste) → `item` alansız; ipucu gösterilir, `${item}` geçerli.
- Geçersiz `itemVariable` adı → enjeksiyon yok, alan kırmızı.
- İsim çakışması → panelde uyarı; en-içteki loop shadow eder.
- İç içe ForEach → her katman kendi `item`'ını ekler.
- `items` sonradan değişir → panel her seçimde yeniden hesaplar (signal), bayat şema kalmaz.

## 5. Test

- **Şema çözümleyici (unit):** `${faturalar}`/`{{faturalar}}` → eleman şeması; çözülemeyen
  ifade → null; `list<string>` → alansız.
- **Body-node tespiti (unit):** düz gövde, iç içe ForEach, loop-back kenarı, exit-sonrası
  node (kümenin dışında kalmalı).
- **Enjeksiyon (component):** gövde node'u seçilince `panel.variables` içinde `item` + alanları;
  exit-sonrası node'da yok; workflow JSON'a `item` yazılmadığının doğrulanması.
- **ForEach paneli (component):** `itemVariable` düzenleme, şemasız kaynakta elle alan
  editörünün belirmesi, geçersiz ad reddi.
- **Görsel (component):** ForEach seçiliyken gövde node'larının vurgu sınıfı alması;
  seçim kalkınca kaldırılması.

## 6. Kapsam dışı (bilinçli)

- **Görsel konteyner kutu** (gövde node'larını çevreleyen render edilmiş kutu, altta düz model):
  ayrı bir brainstorm + spec olarak ele alınacak. Bu spec yalnız hafif vurgu içerir.
- **Yapısal iç içe/child modeli** (`WorkflowSchema.json` + `BaseRunner` değişikliği): kontrat
  değişikliği ve mevcut workflow migration'ı gerektirir; kapsam dışı.
- `Logic.For` (sayaç → skaler `indexVariable`) ve `while` için şema türetme: gereksiz (YAGNI).
