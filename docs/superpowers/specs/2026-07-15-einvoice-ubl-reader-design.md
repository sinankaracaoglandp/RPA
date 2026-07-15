# E-Fatura UBL Okuyucu ve XML/Regex Eşleme Editörü Tasarımı

**Tarih:** 2026-07-15  
**Durum:** Onaylandı  
**Kapsam:** UBL-TR XML okuma, tekli ve toplu workflow aktiviteleri, Studio eşleme editörü

## Amaç

UBL-TR tabanlı e-fatura XML dosyalarındaki standart ve firmaya özel alanları güvenli biçimde okuyup workflow değişkenlerine aktarmak. Üretilen değerler `Desktop.*`, `Web.*`, `Api.*`, `Sap.*`, `Logic.ForEach` ve diğer mevcut node'lar tarafından kullanılabilmelidir.

## Kapsam

- Tek bir dosya yolu veya XML içeriğini okuyan `EInvoice.ReadUbl` aktivitesi.
- Dosya yolu/XML içeriği koleksiyonlarını okuyan `EInvoice.ReadUblBatch` aktivitesi.
- Standart UBL-TR alanlarının otomatik çıkarılması.
- XPath, regex, regex grubu ve tip dönüşümüyle özel alan eşleme.
- XML ağacı, kural tanımı ve canlı sonuçtan oluşan üç panelli Studio editörü.
- Tasarım zamanı örnek XML dosyasının yalnızca bellekte kullanılması.
- Çıktıların bütün nesne, satır listesi, özel alan sözlüğü ve ayrı workflow değişkenleri olarak bağlanması.

İlk sürüm PDF ve taranmış görüntü okumaz. Bunlar XML bulunmadığında kullanılabilecek sonraki bir yedek okuma paketidir.

## Mimari

### Ortak parser motoru

`UblInvoiceParser` aşağıdaki sorumluluklara sahiptir:

1. XML'i güvenli okuyucu ayarlarıyla yüklemek.
2. UBL namespace'lerini belge üzerinden çözmek.
3. Standart UBL-TR alanlarını çıkarmak.
4. Kullanıcı tanımlı XPath ve regex kurallarını çalıştırmak.
5. Sonuçları kültürden bağımsız tiplere dönüştürmek.
6. Tekli ve batch aktivitelerine aynı sonuç modelini sağlamak.

Aktiviteler parser davranışını tekrar etmez. `EInvoice.ReadUbl` tek belge orkestrasyonunu, `EInvoice.ReadUblBatch` koleksiyon ve hata politikasını yönetir.

### Veri akışı

```text
Dosya yolu / XML değişkeni / API-SAP çıktısı
                      ↓
          Güvenli XML yükleme ve doğrulama
                      ↓
      Standart UBL-TR alanlarını otomatik okuma
                      ↓
       Özel XPath → regex → tip dönüşümü
                      ↓
       InvoiceData / InvoiceBatchItemResult[]
                      ↓
 Desktop / Web / API / SAP / Logic.ForEach node'ları
```

## Aktivite sözleşmeleri

### `EInvoice.ReadUbl`

Girdiler:

- `filePath`: sabit, değişken veya expression ile sağlanan tek dosya yolu.
- `xmlContent`: sabit, değişken veya expression ile sağlanan tek XML metni.
- `mappings`: editörün ürettiği özel eşleme listesi.
- `outputBindings`: seçilen alanları ayrı workflow değişkenlerine yazan bağlama listesi.

`filePath` ve `xmlContent` aynı anda dolu olamaz; tam olarak biri sağlanmalıdır. API, SAP, e-posta veya başka bir node'dan gelen XML, değişken/expression yoluyla `xmlContent` alanına bağlanır.

Çıktılar:

- `invoice`: bütün `InvoiceData` nesnesi.
- `lines`: `Logic.ForEach` ile doğrudan kullanılabilen `InvoiceLineData[]`.
- `customFields`: kullanıcı tanımlı alan sözlüğü.
- `outputBindings` ile adlandırılmış ayrı workflow değişkenleri.

### `EInvoice.ReadUblBatch`

Girdiler:

- `filePaths`: dosya yolu dizisi/listesi.
- `xmlContents`: XML metni dizisi/listesi.
- `mappings` ve `outputBindings`.
- `errorMode`: `Continue` veya `Stop`; varsayılan `Continue`.

Batch çağrısında kaynak koleksiyonlarından yalnızca biri sağlanır. Çıktı `InvoiceBatchItemResult[]` biçimindedir. Her öğe `success`, `invoice`, `sourceIndex` ve güvenli `error` alanlarını içerir. `Continue` hatalı kaydı sonuçlara ekleyip diğer belgeleri işler; `Stop` ilk hatada aktiviteyi sonlandırır.

Tekli ve batch aktiviteleri ayrı tutulur; böylece bir node'un çıktısı çalışma zamanında bazen nesne bazen liste olmaz.

## Standart veri modeli

`InvoiceData` aşağıdaki grupları taşır:

- Kimlik: UUID, fatura numarası, düzenleme tarih/saat, tip, senaryo, para birimi.
- Satıcı/alıcı: unvan, VKN/TCKN, vergi dairesi, adres ve iletişim.
- Satırlar: ürün kodu, ad/açıklama, miktar, birim, birim fiyat, iskonto, vergiler ve satır toplamı.
- Toplamlar: vergisiz tutar, vergi, iskonto, vergi dahil tutar ve ödenecek tutar.
- Vergiler: KDV, tevkifat ve istisna bilgileri.
- `notes[]`: tüm fatura notları.
- `exchangeRate`: standart UBL alanından veya not kuralından bulunan kur.
- `paymentAccounts[]`: standart ödeme hesabından veya not kuralından bulunan IBAN'lar.
- `customFields`: özel eşleme sonuçları.

## Özel eşleme modeli

```json
{
  "name": "orderNumber",
  "source": "XPath",
  "scopeXPath": null,
  "valueXPath": "//cbc:Note",
  "regex": "Sipariş No:\\s*(?<value>\\S+)",
  "group": "value",
  "type": "string",
  "required": false,
  "multiple": false
}
```

Tekrarlanan satır alanlarında `scopeXPath` her öğenin bağlamını belirler; `valueXPath` göreli XPath olabilir. `source` şu seçenekleri destekler:

- Standart UBL alanı
- Belirli XPath
- Tüm fatura notları
- Her fatura satırının notları

Regex isteğe bağlıdır ve ham XML'e değil, seçilen XPath/tag değerine uygulanır. `group` sayısal veya adlandırılmış grup olabilir. Desteklenen ilk tipler `string`, `decimal`, `integer`, `date`, `boolean` ve bunların liste biçimleridir.

## Notlardan kur ve IBAN çıkarma

Kur ve IBAN önce standart UBL konumlarından okunur:

- Kur: `PricingExchangeRate/CalculationRate`
- IBAN: `PaymentMeans/PayeeFinancialAccount/ID`

Standart alan bulunamazsa `cbc:Note` değerleri ayrı ayrı taranır. Editör kur ve IBAN için düzenlenebilir hazır regex şablonları sunar. Eşleşmenin kaynak notu sonuç metadata'sında korunur. IBAN boşluklardan arındırılarak, ondalık kur ise nokta/virgül biçimleri normalize edilerek döndürülür.

## Studio eşleme editörü

Editör üç eşzamanlı panelden oluşur:

1. **XML ağacı:** Tasarım zamanı örnek dosyayı gösterir; tekrarlanan node sayısını ve örnek değerleri belirtir.
2. **Alan kuralı:** Hedef alan, kaynak, scope XPath, value XPath, regex, grup, tip, `required` ve `multiple` ayarlarını düzenler.
3. **Canlı önizleme:** Ham seçili değer, regex grupları, dönüşüm sonucu ve üretilen JSON'u gösterir.

Bir XML node'una tıklamak namespace-aware XPath üretir. Kullanıcı XPath'i elle değiştirebilir. Editör kur, IBAN ve yaygın not alanları için başlangıç şablonları sağlar.

Örnek XML içeriği veya dosya yolu workflow JSON'una yazılmaz. Workflow'a yalnızca eşleme kuralları ve çıktı bağlamaları kaydedilir.

## Mevcut node'larla etkileşim

Standart ve özel sonuçlar workflow değişkenlerine bağlanır. Örneğin `invoiceNumber → faturaNo` bağlaması sonrasında `Desktop.SetText` node'u `{{faturaNo}}` kullanabilir. `lines` çıktısı `Logic.ForEach` girdisi olur; her turda ürün kodu, miktar ve fiyat mevcut Desktop/Web/SAP node'larına aktarılır.

## Güvenlik ve sınırlar

- DTD işleme ve dış entity çözümleme kapalıdır; XXE engellenir.
- XML boyutu ve derinliği yapılandırılabilir üst sınırlarla korunur.
- Regex işlemlerinde timeout zorunludur.
- XPath ifadeleri yalnızca belge üzerinde değerlendirilir; dış kaynak erişimi yoktur.
- Örnek XML, gerçek XML içeriği ve tam fatura verileri loglanmaz.
- Hata mesajları kaynak indeksini ve kural adını içerebilir; XML içeriğini içermez.
- `required` olmayan eşleşme boş sonuç üretir; zorunlu eşleşme yoksa açıklayıcı doğrulama hatası oluşur.
- Tarih ve sayılar deterministik biçimde dönüştürülür; Türkçe ve invariant ondalık gösterimler desteklenir.

## Hata yönetimi

Parser hataları şu kategorilerde raporlanır:

- Kaynak doğrulama: iki kaynağın birden verilmesi veya hiç verilmemesi.
- Güvenli XML/biçim: bozuk XML, sınır aşımı veya yasak DTD/entity.
- Eşleme: geçersiz XPath, regex veya regex grubu.
- Veri: zorunlu alan eksikliği veya başarısız tip dönüşümü.

Tekli aktivite bu hataları node hatası olarak yükseltir. Batch aktivitesi `errorMode` politikasına göre öğe sonucuna kaydeder veya ilk hatada durur.

## Test stratejisi

Backend testleri:

- Namespace'li UBL-TR örneğinden standart alanlar ve satırlar.
- Dosya yolu ve değişkenden gelen `xmlContent` kaynakları.
- Aynı anda iki kaynak veya kaynaksız çağrı reddi.
- XPath, regex, adlandırılmış/sayısal grup ve tip dönüşümleri.
- Fatura notlarından kur ve IBAN çıkarma; standart alanın önceliği.
- Tekrarlanan satır scope'u ve çoklu sonuçlar.
- DTD/XXE reddi, XML sınırları ve regex timeout'u.
- Batch `Continue` ve `Stop` davranışları.
- Çıktıların workflow değişkenlerine bağlanması.

Studio testleri:

- XML ağacının namespace ve tekrar sayılarını göstermesi.
- Node seçiminden XPath üretimi.
- Regex grupları ve dönüşümün canlı önizlemesi.
- Geçersiz XPath/regex geri bildirimi.
- Kur ve IBAN hazır şablonları.
- Değer/değişken/expression kaynak bağlama.
- Örnek XML ve yolunun workflow JSON'una kaydedilmemesi.

## Kontrat etkisi

Yeni aktivite kimlikleri, parametreleri ve çıktı tipleri `WorkflowSchema.json`, aktivite kataloğu ve Studio aktivite modellerine eklenecektir. Uygulama başlamadan önce AGENTS.md içinde kontrat değişikliği kaydı oluşturulacak; mevcut public arayüz imzaları değiştirilmeyecektir.

## Kabul kriterleri

1. Kullanıcı tasarım zamanı XML seçip tag'e tıklayarak XPath oluşturabilir.
2. Seçilen değer üzerinde regex çalıştırıp grupları canlı görebilir.
3. Standart UBL-TR alanları özel kural gerektirmeden çıkarılır.
4. Kur ve IBAN standart alanlardan veya not regex'lerinden bulunabilir.
5. Tekli ve koleksiyon kaynakları tip güvenli ayrı aktivitelerle işlenir.
6. Çıktılar mevcut workflow node'larında ve `Logic.ForEach` içinde kullanılabilir.
7. Örnek fatura verisi workflow'a veya loglara yazılmaz.
8. Güvenlik, parser, batch ve Studio editör testleri geçer.
