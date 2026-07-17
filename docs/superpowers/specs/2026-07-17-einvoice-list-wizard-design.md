# E-Fatura Liste (Satır) Sihirbazı — Tasarım

**Tarih:** 2026-07-17
**Kapsam:** Studio `einvoice-mapping-editor` bileşeni — tekrar eden fatura satırlarının
(kalemler, vergi alt toplamları vb.) liste değişkeni olarak kullanıcı dostu tanımlanması.

## Problem

Mevcut editörde kök alanlar (tek değer) ağaçtan tıkla-ekle ile kolayca tanımlanıyor, ancak
satır listeleri (`EInvoiceCollectionDefinition`) hâlâ elle "koleksiyon adı + scope XPath + her
alan için ayrı form" doldurularak tanımlanıyor. Bu, XML/XPath bilgisi olmayan kullanıcı için
kullanışsız. Kullanıcı, listenin bulunduğu node'u seçtikten sonra sistemin kolonları ve örnek
değerleri göstermesini, kendisinin sadece istediği alanları işaretleyip adlandırmasını istiyor.

## Mevcut altyapı (değişmez)

- `EInvoiceCollectionDefinition { name, scopeXPath, fields: EInvoiceMappingRule[] }` — hazır.
- `selectNode` tekrar eden/dallı node seçilince `collectionScopeXPath` doldurur — hazır.
- `relativizeXPath`, `buildXPath`, `repeatedCount`, `previewProfileDefinition` — hazır.
- **Domain kontratı, JSON şeması ve backend değişmez.** Bu tümüyle Studio UI işidir.

## Çözüm — "Liste Sihirbazı"

### 1. Genel akış

1. Örnek XML yüklenir (mevcut adım).
2. Editör üstünde **"Bulunan listeler"** şeridi: tekrar eden node'lar **buton** olarak çıkar
   (`InvoiceLine — 3 satır`). Panel değil, tıklanabilir buton.
3. Bir listeye tıklanınca **Keşif tablosu** açılır.
4. Kullanıcı liste adını girer (`kalemler`), tabloda alanları işaretler, ad/tip önerilerini
   düzeltir, gerekirse "Böl" ile miktar/birim ayırır.
5. **"Listeyi oluştur"** butonu → `EInvoiceCollectionDefinition` üretir ve mevcut koleksiyon
   önizleme/kayıt yoluna ekler.

Manuel form "gelişmiş" olarak kalır; varsayılan deneyim sihirbazdır.

### 2. Liste keşfi

- Ağaç gezilir; **aynı ebeveyn altında aynı yerel adla 2+ kez** geçen node grupları aday liste.
- Kart/buton: yerel ad + satır sayısı; scope XPath arka planda tutulur.
- İç içe listeler (her `InvoiceLine` altında `TaxSubtotal`) ayrı buton.
- Zaten tanımlı listeler "✓ tanımlı" rozetiyle; tekrar tıklama mevcut tanımı düzenler.
- `repeatedCount`/`buildXPath` ile yapılır; yeni kontrat yok.

### 3. Keşif tablosu (kolon tarama)

Liste seçilince ilk scope node'u örnek alınır ve altındaki alanlar taranır:

- **Yaprak node'lar** derinlikten bağımsız düz listelenir; göreli yol gösterilir
  (`cbc:ID`, `cac:Item/cbc:Name`, `cac:Price/cbc:PriceAmount`).
- **Attribute'lar** ayrı satır (`cbc:InvoicedQuantity/@unitCode`) — standart UBL birim kodu
  tek tıkla kolon olur (her zaman açık öneri).
- Her satır:
  - **Örnek değer** ilk satırdan.
  - **Önerilen ad**: son segment, prefix atılmış, camelCase (`InvoicedQuantity` →
    `invoicedQuantity`, `@unitCode` → `unitCode`).
  - **Önerilen tip**: örnek değerden tahmin (ondalık→decimal, tam sayı→integer,
    tarih deseni→date, diğer→string).
- Boş/eksik alanlar da listelenir.
- Kolonlar: `☐ Ekle | Alan yolu | Örnek değer | Ad (düzenlenebilir) | Tip (dropdown) | 🔀 Böl`.
- Üretilen `valueXPath` `relativizeXPath` ile scope'a göre relatifleştirilir → mevcut
  `EInvoiceMappingRule` şemasına birebir oturur.

### 4. Değer + birim ayırma

İki durum:

1. **Standart UBL** — birim `@unitCode` attribute'unda. Attribute zaten ayrı satır olarak
   önerilir (her zaman açık).
2. **Serbest metin** — `100 ADET`, `100 KG` gibi tek alanda. Alan satırındaki **"🔀 Böl"**
   butonu, alanı iki alana böler:
   - `<ad>` (decimal, sayısal kısım)
   - `<ad>Birim` (string, birim kısmı)
   Ayırma regex `(?<value>[\d.,]+)\s*(?<unit>\D+)` ile yapılır ve mevcut `regex`/`group`
   alanlarına yazılır — ekstra kontrat gerekmez. Sayı alanı `group='value'`, birim alanı
   aynı `valueXPath` + `group='unit'` (string).

### 5. Görsel / etkileşim direktifleri

- **frontend-design skill** rehberliğinde tasarlanır.
- Tüm tıklanacak keşif öğeleri **buton** (panel/kart-div değil).
- Tema uyumlu (mevcut Studio stilleri).

## Kontrat etkisi

**Yok.** Domain, JSON şeması, backend, `EInvoiceMappingRule`/`EInvoiceCollectionDefinition`
imzaları değişmez. Yalnız `einvoice-mapping-editor` bileşeni (ts/html/scss) + testleri.

## Test

- Keşif: çok satırlı örnek XML → doğru tekrar eden node'ları bulur.
- Kolon tarama: iç içe yapraklar + attribute'lar + göreli yollar doğru.
- Ad/tip türetme birim testleri.
- "Böl": tek alandan iki kural (value/unit) üretimi.
- Üretilen `EInvoiceCollectionDefinition` `previewProfileDefinition` ile doğru satırları verir.
