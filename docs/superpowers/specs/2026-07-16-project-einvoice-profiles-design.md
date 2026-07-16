# Proje Kapsamlı E-Fatura Profilleri Tasarımı

**Tarih:** 2026-07-16  
**Durum:** Kullanıcı tarafından yönü onaylandı; yazılı spec incelemesi bekleniyor.  
**Branch:** `feat/einvoice-mapping-workspace`

## 1. Amaç

Kullanıcı, workflow tasarlamadan önce proje içinde örnek UBL/XML dosyalarını inceleyebilmeli; fatura ve tekrarlanan alt koleksiyon alanlarını XPath, regex, standart UBL alanı veya not kaynağıyla eşleyebilmeli; bu eşlemeyi sürümlü bir profil olarak yayınlayabilmelidir.

Designer'da profil çağrıldığında profil şeması otomatik olarak nesne tabanlı workflow çıktısına dönüşmeli ve normal RPA değişken seçicilerinde kullanılmalıdır. XML eşleme ayrıntıları node özellik paneline sıkıştırılmayacaktır.

## 2. Mevcut Altyapı ve Korunacak Parçalar

Mevcut aşağıdaki parçalar yeniden kullanılacaktır:

- Güvenli UBL parser: DTD kapalı, boyut/derinlik sınırı ve log maskelemesi.
- Standart UBL, XPath, InvoiceNotes, LineNotes ve timeout korumalı regex eşlemeleri.
- Tekil ve toplu XML/dosya girdileri.
- API veya SAP node'larından gelen XML metni ve XML listesi desteği.
- `Continue` / `Stop` toplu hata politikası.
- `ForEach` ve nesne property-path değerlendirmesi.
- Studio XML ağacı, kural editörü ve worker tabanlı regex önizleme bileşenleri.

Mevcut `EInvoice.ReadUbl` ve `EInvoice.ReadUblBatch` aktiviteleri düşük seviyeli gelişmiş kullanım olarak korunacaktır.

## 3. Proje İçi E-Fatura Profilleri Sekmesi

Proje detayına yalnızca bu alana özel **E-Fatura Profilleri** sekmesi eklenecektir.

Sekme aşağıdaki işlemleri sunar:

- Profil listeleme ve yeni profil oluşturma.
- Taslak profil düzenleme.
- Örnek XML dosyası yükleme; örnek içerik yalnız tarayıcı belleğinde tutulur.
- XML ağacından scalar alan veya tekrarlanan koleksiyon kapsamı seçme.
- XPath, regex, standart UBL, InvoiceNotes ve LineNotes kuralları.
- Alan adı, veri tipi, zorunluluk ve çoklu değer ayarları.
- Örnek veri üzerinde canlı önizleme.
- Taslak kaydetme ve yeni sürüm yayınlama.
- Yayınlanmış sürümlerin salt okunur görüntülenmesi.

Örnek XML içeriği veritabanında, workflow JSON'unda, profil tanımında veya loglarda saklanmaz.

## 4. Profil ve Sürüm Modeli

Profil proje kapsamındadır; başka projeden doğrudan görülemez.

### EInvoiceProfile

- `Id`
- `ProjectId`
- `Name`
- `Description`
- `DraftDefinitionJson`
- standart audit ve soft-delete alanları

### EInvoiceProfileVersion

- `Id`
- `ProfileId`
- `Version` (artan pozitif sayı)
- `DefinitionJson` (değişmez snapshot)
- `OutputSchemaJson` (Designer değişken kataloğunun okuyacağı şema)
- `PublishedAt`
- `PublishedBy`

İlk yayın `v1` üretir. Yayınlanmış sürüm değiştirilemez. Taslakta yapılan sonraki değişiklik yeni bir sürüm olarak yayınlanır. Workflow profil kimliği ve sürüm numarasına sabitlenir; otomatik olarak son sürüme geçmez.

## 5. Dinamik Çıktı Şeması

Profil tek bir kök nesne şeması tanımlar. Node üzerindeki `outputVariable` varsayılan olarak profil adından üretilir ve kullanıcı tarafından değiştirilebilir.

Örnek kullanım:

```text
{{fatura.faturaNo}}
{{fatura.faturaTarihi}}
{{fatura.iban}}
{{fatura.kur}}
{{fatura.satirlar}}
```

Profil alanları sabit `InvoiceData` veya `FaturaSatiri` sınıfıyla sınırlanmaz. Çalışma zamanı çıktısı profil şemasına göre oluşturulan dinamik sözlük/nesne yapısıdır.

Desteklenen alan türleri:

- `string`
- `integer`
- `decimal`
- `date`
- `boolean`
- `object`
- `list<object>`

## 6. Kullanıcı Tanımlı Koleksiyonlar

Profil birden fazla tekrarlanan koleksiyon tanımlayabilir:

- `satirlar`
- `vergiler`
- `iskontolar`
- `odemeBilgileri`
- kullanıcı tarafından verilen diğer geçerli alan adları

Her koleksiyon bir `scopeXPath` ve kendi çocuk alan kurallarına sahiptir.

Örnek:

```text
satirlar
  scopeXPath: /Invoice/cac:InvoiceLine
  MalzemeKodu: ./cac:Item/cbc:SellersItemIdentification/cbc:ID
  Aciklama: ./cac:Item/cbc:Description
  Fiyat: ./cac:Price/cbc:PriceAmount
  Miktar: ./cbc:InvoicedQuantity
```

Designer, `ForEach {{fatura.satirlar}}` seçildiğinde döngü öğesinin şemasını bilir ve aşağıdaki alanları değişken seçicisinde gösterir:

```text
{{satir.MalzemeKodu}}
{{satir.Aciklama}}
{{satir.Fiyat}}
{{satir.Miktar}}
```

Alan ve koleksiyon adları geçerli workflow identifier kurallarına uymalı ve büyük/küçük harf duyarsız olarak benzersiz olmalıdır.

## 7. Profil Tabanlı Workflow Node'ları

### EInvoice.ReadProfile

Tek fatura işler ve bir dinamik nesne üretir.

Girdiler:

- `profileId`
- `profileVersion`
- `sourceMode`: `FilePath | XmlContent`
- `filePath`
- `xmlContent`
- `outputVariable`

Çıktı:

- Kullanıcının belirlediği kök değişken altında profil nesnesi.

### EInvoice.ReadProfileBatch

Aynı profil sürümüne uygun çoklu faturaları işler ve dinamik nesne listesi üretir.

Girdiler:

- `profileId`
- `profileVersion`
- `sourceMode`: `Folder | FilePaths | XmlContents`
- `folderPath`
- `fileFilter` (varsayılan `*.xml`)
- `includeSubfolders` (varsayılan `false`)
- `filePaths`
- `xmlContents`
- `errorMode`: `Continue | Stop`
- `outputVariable`

Çıktı:

- Kullanıcının belirlediği kök değişken altında profil nesnesi listesi.
- `Continue` modunda öğe bazlı güvenli hata bilgileri.

API ve SAP kaynakları ayrı node türü oluşturmaz. Önceki node tek XML üretiyorsa `XmlContent`, XML listesi üretiyorsa `XmlContents` moduna bağlanır.

## 8. Designer Entegrasyonu

Profil node'u seçildiğinde Designer:

1. Proje kapsamındaki yayınlanmış profilleri listeler.
2. Profil seçilince yayınlanmış sürümleri listeler.
3. Seçilen sürümün `OutputSchemaJson` değerini yükler.
4. `outputVariable` altında nesne/list şemasını workflow değişken kataloğuna ekler.
5. Değişken seçicilerde nokta erişimi ve koleksiyon öğe alanlarını gösterir.
6. Profilin daha yeni sürümü varsa uyarı gösterir; kullanıcı onayı olmadan sürümü değiştirmez.

Workflow doğrulaması profil/sürümün aynı projede ve yayınlanmış olduğunu kontrol eder. Çalışma zamanı da aynı kontrolü tekrar yapar.

## 9. API Sözleşmesi

Proje kapsamlı uçlar:

```text
GET    /api/projects/{projectId}/einvoice-profiles
POST   /api/projects/{projectId}/einvoice-profiles
GET    /api/projects/{projectId}/einvoice-profiles/{profileId}
PUT    /api/projects/{projectId}/einvoice-profiles/{profileId}/draft
POST   /api/projects/{projectId}/einvoice-profiles/{profileId}/publish
GET    /api/projects/{projectId}/einvoice-profiles/{profileId}/versions
GET    /api/projects/{projectId}/einvoice-profiles/{profileId}/versions/{version}
DELETE /api/projects/{projectId}/einvoice-profiles/{profileId}
```

API örnek XML kabul etmez ve döndürmez. Profil tanımı ve çıktı şeması JSON olarak taşınır.

## 10. Güvenlik ve Hata Davranışı

- XML dosya yolu/içeriği ve XML listeleri `Sensitive` metadata ile maskelenir.
- DTD/entity çözümleme kapalı kalır.
- Boyut, derinlik ve regex timeout sınırları korunur.
- Profil başka projeye aitse `404`/yetki hatası döner; varlığı sızdırılmaz.
- Profil sürümü yoksa veya yayınlanmamışsa workflow başlamadan doğrulama hatası oluşur.
- Zorunlu alan bulunamazsa güvenli `BusinessException` üretilir.
- Batch `Continue` öğe hatasını güvenli sonuç olarak taşır; `Stop` ilk hatada durur.
- Hata mesajları XML içeriğini veya eşleşen hassas değeri içermez.

## 11. Katmanlar

- **Domain:** Profil ve sürüm varlıkları; tanım/şema kontratları.
- **Application:** Proje izolasyonlu CRUD, publish, sürümleme ve şema doğrulama servisleri.
- **Infrastructure:** EF mapping/migration, profil repository, dinamik parser adaptörü, klasör kaynağı ve workflow aktiviteleri.
- **WebAPI:** Proje kapsamlı profil endpoint'leri.
- **Studio:** Proje sekmesi, profil editörü, sürüm seçimi, node özellikleri ve dinamik değişken kataloğu.

Onion bağımlılık yönü korunur.

## 12. Test Kapsamı

- Profil CRUD, soft-delete ve proje izolasyonu.
- Taslak/publish ve değişmez sürüm snapshot'ları.
- Geçersiz profil, alan adı, tip ve çakışan alan doğrulaması.
- Scalar alanlar ve birden fazla dinamik koleksiyon.
- Bağıl XPath ve regex/standart/not kuralları.
- Tekil dosya ve XML içeriği.
- Klasör, filtre, alt klasör seçeneği, dosya listesi ve XML listesi.
- API/SAP kaynaklı XML listesi entegrasyonu.
- Profil node'u → dinamik nesne → normal RPA node'u.
- Profil listesi → `ForEach` → dinamik satır alanı.
- Designer profil/sürüm seçimi ve otomatik değişken görünürlüğü.
- Log/observer olaylarında XML sızıntısı olmaması.
- Eski düşük seviyeli UBL node'larının geriye uyumluluğu.

## 13. Kontrat Etkisi

Bu çalışma yeni Domain varlıkları, workflow activity kimlikleri ve workflow JSON özellikleri ekler. Uygulama başlamadan önce `AGENTS.md` dosyasına kontrat değişikliği kaydı eklenecek; Domain şeması, Infrastructure katalog/runner ve Studio activity modeli birlikte güncellenecektir.

## 14. Kapsam Dışı

- Profilleri projeler arasında paylaşan ortak kütüphane.
- Profilin otomatik olarak son sürüme yükseltilmesi.
- JSON/CSV/API response için genel amaçlı eşleme merkezi.
- Örnek XML'in sunucuya veya veritabanına kaydedilmesi.
- İç içe koleksiyonların sınırsız rekürsif tasarımı; ilk sürüm kök scalar alanlar ve kök altındaki birden fazla `list<object>` koleksiyonuyla sınırlıdır.
