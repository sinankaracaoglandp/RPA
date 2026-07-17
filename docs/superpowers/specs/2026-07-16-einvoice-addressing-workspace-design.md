# E-Fatura Adresleme Çalışma Alanı Tasarımı

## Amaç

Kullanıcı e-fatura XML alanlarını designer içinde aramak zorunda kalmadan ana sayfadan ayrı bir “E-Fatura Adresleme” ortamına girer, örnek XML seçer, tag/regex bazlı alanları profil değişkenlerine bağlar ve yayınlanan profili designer’da seçerek değişken kataloğuna otomatik taşır.

## Kullanıcı Akışı

1. Ana sayfada Studio bölümünde “E-Fatura Adresleme” kartı görünür.
2. Kullanıcı karta tıklayınca proje/profil odaklı adresleme ekranına gider.
3. Bir profil seçer veya yeni profil oluşturur.
4. Örnek XML dosyası yükler.
5. XML ağacından tag seçerek alan tanımlar:
   - Kök alan: `faturaNo`, `faturaTarihi`, `cariVkn`, `kurBilgisi`, `iban`
   - Satır koleksiyonu: `satirlar`
   - Satır alanları: `MalzemeKodu`, `Aciklama`, `Miktar`, `Fiyat`, `KdvOrani`
6. Regex alanları özellikle notlardan IBAN/kur/özel açıklama çıkarmak için aynı editörde önizlenir.
7. Profil kaydedilir ve yayınlanır.
8. Designer’da `EInvoice.ReadProfile` veya `EInvoice.ReadProfileBatch` node’u seçildiğinde kullanıcı bu profili seçer.
9. Profil seçildikten sonra output schema designer değişken kataloğuna otomatik eklenir:
   - `fatura.faturaNo`
   - `fatura.faturaTarihi`
   - `fatura.satirlar`
   - ForEach içinde `satir.MalzemeKodu`, `satir.Miktar`, `satir.Fiyat`

## Ekran Ayrımı

- **E-Fatura Adresleme:** XML’den hangi bilgilerin nasıl okunacağını tanımlar.
- **Designer:** Tanımlanmış profil çıktılarıyla RPA akışını kurar.

Bu ayrım bilinçlidir; XML adresleme, süreç tasarımının içine gömülmez.

## UI Gereksinimleri

- Dashboard Studio kartı: “E-Fatura Adresleme”.
- Adresleme ekran başlığı kullanıcıya açık olmalı: “XML alanlarını değişkenlere bağla”.
- JSON textarea teknik/ikincil bilgi olarak kalmalı; ana kullanım form ve XML ağacı üzerinden yapılmalı.
- Profil editörü mevcut taslak JSON’u okuyup forma yüklemeli.
- Kök alan ve satır alanı ekleme butonları görünür olmalı.
- Satır koleksiyonu için `satirlar` varsayılanı hızlı oluşturulabilmeli.
- Profil yayınlandıktan sonra son sürüm ve değişken listesi görünmeli.

## Designer Entegrasyonu

- Profil seçimi property panelinde görünür olmalı.
- Profil seçilince `outputSchemaJson` veya eşdeğer schema bilgisi node properties’e yazılmalı.
- Designer’ın mevcut schema-aware variable kaydı bu bilgiyle çalışmalı.
- Kullanıcı designer’da profili seçtikten sonra değişken panelinde alanları görebilmeli ve workflow adımlarında kullanabilmeli.

## Test Kriterleri

- Dashboard’da `E-Fatura Adresleme` kartı `/einvoice-addressing` rotasına gider.
- Adresleme ekranında örnek XML seçilip kök alan ve satır alanı eklenebilir.
- Mevcut profil taslağı editöre yüklenir.
- `satirlar.MalzemeKodu` gibi koleksiyon alanları profil definition JSON’una yazılır.
- Designer property değişimi profil schema’sını değişken kataloğuna ekler.

## Kapsam Dışı

- Backend kontrat değişikliği yok.
- XML’i API/SAP’den canlı çekme bu pakette yok; profil runtime zaten `xmlContent`, `xmlContents`, dosya ve klasör kaynaklarını destekler.
- Micro muhasebe ekranına fiili giriş bu pakette yok; amaç o akışta kullanılacak değişkenleri designer’a taşımaktır.
