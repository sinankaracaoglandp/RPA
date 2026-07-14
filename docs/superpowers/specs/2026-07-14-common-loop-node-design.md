# Ortak Loop Node Tasarımı

## Amaç

Studio designer'da `Logic.While`, `Logic.For` ve `Logic.ForEach` node'larının döngü akışını açıkça kurabilmesini sağlamak. Her node ortak bağlantı semantiğini kullanır; yalnızca iterasyon ayarları tipe özeldir.

## Seçilen Yaklaşım

Ortak bir loop sözleşmesi ve tipe göre ayrışan özellikler kullanılacak. Bu, kopyalanmış üç ayrı canvas/runner uygulamasına tercih edildi. Tamamen serbest graph cycle yaklaşımı da reddedildi; normal node'lar arasında istenmeyen ve sonsuz döngülere yol açabilir.

## Ortak Bağlantı Sözleşmesi

Tüm loop node'ları aynı akış noktalarını kullanır:

- `in`: Önceki node'dan döngüye giriş.
- `body`: İterasyon gövdesinin ilk node'una çıkış.
- `loop-back`: Gövdenin son node'undan loop node'undaki ayrı geri-dönüş girişine kontrollü bağlantı.
- `exit`: Koşul sağlanmadığında veya iterasyon tamamlandığında devam edilecek node.

`loop-back`, genel amaçlı bir cycle değildir. Yalnızca bir loop gövdesinin sonundan onu sahiplenen loop node'una bağlanabilir. Canvas bu kuralı bağlantı kurulurken doğrular; runner aynı ilişkiyi kontrollü loop semantiği olarak yorumlar. Diğer graph cycle'ları geçersiz kalır.

Bir loop node'u en fazla bir `body` ve bir `exit` bağlantısı taşır. `body` zorunludur; `exit` opsiyoneldir ve yoksa döngü tamamlandığında akış sona erer. Gövde sonundan doğru loop node'una bir `loop-back` bulunması zorunludur.

## Node Tipleri

### Logic.While

- `condition`: Her iterasyon öncesinde değerlendirilen boolean ifade.
- Koşul `true` ise `body`, `false` ise `exit` izlenir.

### Logic.For

- `start`: Başlangıç değeri.
- `end`: Dahil olan bitiş değeri.
- `step`: Artış veya azalış miktarı; sıfır olamaz.
- `indexVariable`: Mevcut sayaç değerinin yazılacağı değişken.

`start=1`, `end=3`, `step=1` değerleri `1, 2, 3` iterasyonlarını üretir. Negatif `step` azalan aralıkları destekler. Aralığın yönü ile `step` uyumsuzsa gövde hiç çalışmaz ve `exit` izlenir.

### Logic.ForEach

- `items`: İterasyon kaynağı olan koleksiyon ifadesi/değişkeni.
- `itemVariable`: Mevcut elemanın yazılacağı değişken.

Boş koleksiyonda gövde çalışmaz ve `exit` izlenir.

## Studio Tasarımı

Ortak bir loop port tanımı `while`, `for` ve `forEach` için `body` ve `exit` çıkış soketlerini; `in` ve `loop-back` giriş soketlerini üretir. Böylece ilk giriş ile iterasyon sonu geri dönüşü görsel ve serileştirilmiş modelde birbirinden ayrılır.

Property panel ortak loop alanlarını paylaşır ve node tipine özel alanları ayrı bir yapılandırmayla gösterir. Toolbox'ta `Logic.For` ayrı bir aktivite olarak listelenir.

## Runner Davranışı

Runner ortak bir loop yürütme yardımcısı kullanır:

1. Node tipine özel iterasyon durumu hazırlanır.
2. Devam koşulu kontrol edilir.
3. Koşul sağlanıyorsa `body` ile başlayan gövde, `loop-back` sınırına kadar bir kez çalıştırılır.
4. İterasyon durumu ilerletilir ve kontrol tekrarlanır.
5. Tamamlandığında `exit` hedefi izlenir.

Mevcut maksimum iterasyon/step koruması tüm loop tiplerinde korunur. `step=0`, eksik gövde, yanlış loop-back hedefi ve birden fazla `body`/`exit` bağlantısı validasyon hatasıdır.

## Kontrat Değişikliği

Workflow kontratına aşağıdakiler eklenecektir:

- Node tipi: `for`.
- Loop çıkış portları: `body`, `exit`.
- Bağlantı hedef portu `toPort`: `in` veya `loop-back`.
- `Logic.For` alanları: `start`, `end`, `step`, `indexVariable`.

Uygulama öncesinde `AGENTS.md` dosyasına tarihli kontrat değişikliği kaydı ve etki analizi eklenecektir. Etkilenen alanlar Domain workflow şeması, Infrastructure workflow modeli/runner/aktivite kataloğu, Studio workflow modeli/canvas/property paneli ve bunların testleridir.

## Test Stratejisi

TDD sırasıyla uygulanır:

1. Studio testleri ortak portları, bağlantı serileştirmesini ve geçersiz cycle reddini kapsar.
2. Runner testleri While, artan/azalan/döngüsüz For ve boş/dolu ForEach senaryolarını kapsar.
3. Kontrat testleri yeni node tipi, alanlar ve portları doğrular.
4. Regresyon testleri mevcut workflow'ların yüklenmesini ve normal DAG cycle korumasını doğrular.

## Kabul Kriterleri

- Kullanıcı While, For veya ForEach node'una önceki akışı bağlayabilir.
- Kullanıcı loop gövdesini ve döngü sonrası akışı ayrı soketlerden bağlayabilir.
- Gövdenin son node'u kontrollü olarak sahip loop node'una geri bağlanabilir.
- Runner gövdeyi doğru sayıda çalıştırır ve tamamlandığında `exit` hedefine geçer.
- While, For ve ForEach aynı canvas validasyonu ve runner loop altyapısını paylaşır.
