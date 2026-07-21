# Yapısal görünümde paletten tıkla-ekle

**Tarih:** 2026-07-21
**Kapsam:** Studio designer → yapısal görünüm (`structured/view`)
**Kontrat etkisi:** Yok (Domain/Infrastructure/WebAPI/Agent etkilenmez)

## Problem

Yapısal görünümde palet çipleri yalnızca `cdkDrag` ile ağaca eklenebiliyor. Sürükleme,
tek bir aktivite eklemek için gereğinden ağır bir etkileşim; uzun listelerde ve zoom
uygulanmış tuvalde hedefi tutturmak zor.

## Çözüm

Palet çiplerine iki tıklama tetikleyicisi eklenir:

- çipe **çift tık**,
- çipin sağ üst köşesindeki küçük **+** düğmesine **tek tık**.

İkisi de aynı işi yapar: çipin mevcut `factory()`'si çağrılır ve üretilen `StructuredItem`
ağaca eklenir. Sürükle-bırak yolu aynen korunur.

## Bileşen sınırları

`StructuredPaletteComponent` bugün ağaca hiç dokunmuyor; yalnız drag verisi taşıyor.
Bu ayrım korunur:

- **Palette** — `@Output() add = EventEmitter<StructuredItem>`. "Hangi node" dışında
  hiçbir şey bilmez; yerleştirme mantığı içermez.
- **StructuredViewComponent** — `addFromPalette(item)` ile karşılar. Yerleştirme
  mantığının tek sahibi budur (ağacın ve seçimin sahibi zaten odur).

`+` düğmesi `mousedown` olayında `stopPropagation()` çağırır; aksi halde `cdkDrag`
sürüklemeyi başlatır ve tıklama kaybolur.

## Yerleştirme kuralı — REVİZE EDİLDİ (2026-07-21, kullanım sonrası)

Aşağıdaki kural C kullanımda yetersiz bulundu: bir `while`/`forEach` bloğu seçip toolbox'tan
node eklemek en sık istenen akış ve C bunu konteynerin ARDINA koyuyordu. **Yürürlükteki kural:**

| Seçili olan | Yeni öğe nereye |
|---|---|
| **Bir lane** (panel tıklanmış) | **O lane'in sonuna** — en yüksek öncelik |
| Yok | Kökün sonuna |
| Bir adım | Aynı dizide, `p.index + 1` |
| Bir konteyner | **İÇİNE, ilk lane'inin sonuna** — `while`/`forEach`/`for` → `body`, `if` → `true`, `tryCatch` → `success` |
| Konteyner lane'i içindeki bir adım | O lane içinde, `p.index + 1` |

### Lane seçimi (2026-07-21 eki)

Konteyner panelinin boş alanına tıklamak o lane'i **ekleme hedefi** yapar (`selectedLane`
signal'i; kesikli vurgu). Lane bir node değildir: özellik paneli açmaz, `nodeSelect` `null`
yayar ve `selected` temizlenir; tersine bir kart seçmek lane seçimini temizler.

Anlamlı olduğu yerler yalnız **çok lane'li** konteynerlerdir: `if` (ise / değilse),
`tryCatch` (**üç** lane: dene / yakala / sonunda). Tek lane'li `while`/`for`/`forEach`'te
lane seçimi konteyner seçimiyle aynı sonucu verir (zararsız, tutarlılık için açık bırakıldı).
Başka bir kullanım yeri yoktur — lane kavramı yalnız konteynerlerde vardır.

Ayrıca: soldaki toolbox'tan eklenen **kontrol aktiviteleri** (`Logic.If`/`ForEach`/…) düz adım
değil, konteyner bloğu olarak eklenir (`CONTAINER_OF_ACTIVITY` ters eşlemesi) — `structured-add-menu`
bunları listesinden eler, toolbox elemez.

### Reddedilen özgün kural C (kayıt için)

`addFromPalette`:

| Seçili olan | Yeni öğe nereye |
|---|---|
| Yok | Kökün sonuna (`insertItem(tree, [], tree.length, item)`) |
| Bir adım | Aynı dizide, `p.index + 1` |
| Bir konteyner (`if`/`forEach`/`for`/`while`/`tryCatch`) | Aynı dizide, `p.index + 1` — **içine değil, ardına** |
| Konteyner lane'i içindeki bir adım | O lane içinde, `p.index + 1` |

Seçim bir "imleç konumu" gibi davranır ve imleç asla kendiliğinden bir konteynerin
içine atlamaz.

**Gerekçe (B alternatifi neden reddedildi):** "konteyner seçiliyse içine ekle"
davranışında seçimin iki anlamı olur — hem *düzenlenen öğe* hem *ekleme kabı*.
Kullanıcı bir `forEach` kartını koleksiyon alanını doldurmak için seçtiğinde, paletten
eklenen node sessizce döngü gövdesine düşer. C'de kural tek cümledir: **seçilinin ardına.**

**Kabul edilen maliyet:** boş bir konteyner gövdesine ilk adım tıklayarak eklenemez;
lane içindeki mevcut `+` ekleme menüsü ya da sürükleme kullanılır.

## Mutasyon ve seçim

Ekleme `commit()` üzerinden gider → undo/redo geçmişi ve `graphChanged` yayını mevcut
yoldan gelir. Yeni öğe eklendikten sonra **seçili hale gelir** (`onSelect`) — `duplicate`
davranışıyla tutarlıdır; art arda çift tıklayarak lineer akış kurmayı mümkün kılar ve
özellik paneli hemen doğru node'u gösterir.

## Testler

`structured-palette.component.spec.ts`
- çipe çift tık `add` yayınlar (doğru factory ile),
- `+` düğmesine tık `add` yayınlar,
- `+` üzerindeki `mousedown` sürüklemeye sızmaz.

`structured-view.component.spec.ts`
- seçim yokken kökün sonuna ekler,
- adım seçiliyken hemen ardına ekler,
- **konteyner seçiliyken içine değil ardına ekler** (kural C'nin ayırt edici testi),
- lane içindeki adım seçiliyken öğe lane içinde kalır,
- ekleme `undo` ile geri alınır.

## Dokunulan dosyalar

- `structured-palette.component.{ts,html,scss}`
- `structured-view.component.{ts,html}`
- yukarıdaki iki spec dosyası
- i18n: `structured.addChip` (TR + EN)
