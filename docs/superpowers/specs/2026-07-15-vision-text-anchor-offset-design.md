# Tasarım — Vision: Metin Çapasına Göre Ofset Tıklama (`Vision.ClickTextOffset`)

**Tarih:** 2026-07-15
**Paket:** F (Görüntü/OCR Fallback Otomasyonu) — "Anchor Faz 2" park edilmiş işin ilk parçası.
**İlgili spec:** `docs/superpowers/specs/2026-07-12-vision-ocr-automation-design.md`

---

## 1. Amaç ve Problem

Erişilebilirlik ağacı (UIA/DOM) olmayan ekranlarda, kendi başına ayırt edilemeyen bir hedefe
ulaşmak gerekir — en yaygını **bir etiketin yanındaki boş input** (ör. "Malzeme No" etiketinin
sağındaki metin kutusu). Boş kutunun kendine ait ayırt edici bir görüntüsü yoktur; ama yanındaki
etiket metni OCR ile güvenilir bulunur.

Çözüm: **metin çapası (anchor) + piksel ofset**. Çalışma anında çapa metni OCR ile bulunur, ondan
`(dx, dy)` piksel kadar kaydırılıp tıklanır.

### UiPath referansı

UiPath Computer Vision (erişilebilirlik ağacı olmayan ekranlar için) altta tam olarak bunu yapar:
hedefin çapaya göre **bağıl ofsetini** saklar, oynatırken önce çapayı OCR/CV ile bulup ofseti uygular.
"Akıllı yön/hizalama" hissi verse de saklanan değer bağıl piksel ofsetidir. Bu tasarım aynı modeli
izler; elimizde yalnız OCR kelime kutuları olduğundan (gerçek element sınırı yok) piksel ofset en
sağlam ve deterministik yoldur.

---

## 2. Kapsam

**Kapsam içi:** Metin çapa → piksel ofset tıklama. Tek yeni aktivite, görsel picker, Studio editörü.

**Kapsam dışı (bu iş paketinde değil):**
- Görüntü (template) çapa → ofset.
- "Çapaya en yakın tekrarlı hedef" ayırt etme (`FindAll` tabanlı) — ayrı iş.
- Yön + akıllı hizalama (OCR kutularıyla güvenilir değil).

---

## 3. Çalışma Mantığı (Runtime Motoru)

Yeni aktivite: **`Vision.ClickTextOffset`** — DisplayName "Çapaya Göre Tıkla (OCR)", kategori
"Görüntü", capability `vision`.

### Parametreler

| Ad | Tip | Zorunlu | Açıklama |
|----|-----|---------|----------|
| `anchorText` | string | ✓ | OCR ile bulunacak çapa metni (ör. "Malzeme No"). |
| `dx` | int | ✓ | Çapa referans noktasından tıklama noktasına yatay ofset (piksel). |
| `dy` | int | ✓ | Dikey ofset (piksel). |
| `language` | string | — | OCR dil(ler)i, vars. `tur+eng`. |
| `matchMode` | string | — | `contains` (vars.) / `exact`. |
| `clickType` | string | — | `left` (vars.) / `right` / `double`. |
| `timeoutMs` | int | — | Çapa arama zaman aşımı, vars. 5000. |

Studio'da `anchorText`+`dx`+`dy` üçlüsü tek bir birleşik alan olarak (`PickerKind="text-offset"`)
🎯 ile doldurulur; JSON `{"anchorText":...,"dx":...,"dy":...}` taşınır. `language/matchMode/
clickType/timeoutMs` ayrı standart alanlar.

### Akış (çalışma anında)

1. OCR ile çapa metnini bul (mevcut `PollForTextAsync` + `OcrTextMatch` yeniden kullanılır) →
   çapanın **OCR tight kelime kutusu**.
2. Referans noktası = **kelime kutusunun merkezi** `(cx, cy)`.
3. Tıklama noktası = `(cx + dx, cy + dy)` (yakalanan sanal-ekran koordinatında).
4. `DoClick(...)` — sanal-ekran orijinini zaten ekler (çoklu monitör).

Çapa `timeoutMs` içinde bulunamazsa → `SystemException` (mevcut "bulunamadı" deseni; mesaj çapa
metnini içerir). `anchorText` boşsa → `BusinessException` (parametre doğrulama).

### Referans Noktası Kararı

Ofset her zaman **OCR tight kelime kutusunun merkezine** göre saklanır *ve* uygulanır. Kritik nokta:
picker-zamanı referansı da runtime referansı da **aynı tanım** (yeniden-OCR edilen tight kutu merkezi)
olduğundan, kullanıcının picker'da çizdiği kaba dikdörtgenin boyutu/konumu ofseti etkilemez ve
çözünürlük/DPI farkında ofset kayması olmaz.

> Merkez seçildi (sol-orta / sağ-orta yerine): kutu boyutu dalgalanmasına simetrik olarak en dayanıklı.

---

## 4. Kanal Arayüzü (Kontrat Değişikliği)

`src/RPA.Domain/Interfaces/IVisionAutomationChannel.cs` — tek metot eklenir:

```csharp
/// <summary>
/// OCR ile anchorText'i bulur, kelime kutusunun merkezinden (dx,dy) ofsetle tıklar.
/// Çapa bulunamazsa SystemException.
/// </summary>
Task ClickTextOffsetAsync(string anchorText, int dx, int dy,
    string language, string matchMode, string? clickType, int timeoutMs);
```

Implementasyonlar:
- `TesseractOpenCvVisionChannel` (RPA.Agent) — `PollForTextAsync` ile çapa kutusunu bulur, merkez +
  ofset hesaplar, `DoClick` çağırır.
- `UnavailableVisionAutomationChannel` (RPA.Infrastructure) — "ajan yok" `SystemException` (mevcut desen).

CLAUDE.md'ye `## Kontrat Değişikliği — 2026-07-15` notu eklenir (etkilenen paket: F/Vision).

---

## 5. Görsel Picker (Agent)

Yeni picker kind: **`text-offset`**. `GdiImageRegionPicker`'ın dondurma (ArmForm — F2/zamanlayıcı)
altyapısını yeniden kullanır; sonra donmuş snapshot üzerinde iki aşama:

- **Aşama A — çapa:** kullanıcı çapa etiketinin çevresine dikdörtgen çizer (mevcut `SelectionForm`).
  Kırpıntı OCR edilir → çapa metni (string) + tight kelime kutusu (snapshot koordinatı).
- **Aşama B — hedef:** kullanıcı hedef noktasına tek tık → tıklama noktası (snapshot koordinatı).
- **Sonuç:** `dx = tıkX − çapaKutuMerkezX`, `dy = tıkY − çapaKutuMerkezY`; `{anchorText, dx, dy}` +
  çapa kırpıntısının base64 önizlemesi.

Yeni arayüz `ITextOffsetPicker` (SAP/Web/Image picker deseniyle aynı); `SpySessionCoordinator`
opsiyonel `ITextOffsetPicker?` parametresi alır ve `kind:"text-offset"` dalını işler. `AddAgentCore`
içinde kayıt.

Aşama A'daki OCR için mevcut `TesseractOpenCvVisionChannel` OCR yolu (kırpıntıdan kelime kutuları)
yeniden kullanılır; picker Agent'ta olduğundan doğrudan erişilebilir.

### Mesaj sözleşmesi

`SpyElementMessage`'a eklenir: `Kind="text-offset"`, `AnchorText` (string?), `Dx` (int?), `Dy` (int?)
ve `FromTextOffset(anchorText, dx, dy, previewBase64, sessionId)`. `StudioHub.SupportedKinds`'e
`"text-offset"` eklenir (aksi halde "Desteklenmeyen spy tipi" hatası).

---

## 6. Studio Editörü

`vision-sequence-editor` desenine benzer küçük bir bileşen (`text-offset-editor`):
- 🎯 düğmesi picker'ı `kind:"text-offset"` ile çağırır (mevcut image picker gibi F2/zamanlayıcı
  seçenekleriyle — donduruma ihtiyaç var).
- Dönen `anchorText / dx / dy` gösterilir ve **elle düzeltilebilir** (çapa metnini veya ofseti ince
  ayarlamak için).
- Çapa önizleme küçük resmi gösterilir (varsa).
- Değer JSON `{anchorText,dx,dy}` olarak `valueChange` ile dışarı verilir.

`generic-property.component` `PickerKind="text-offset"` için bu editörü render eder (mevcut
`image-sequence` dalı deseni). i18n anahtarları eklenir (`picker.anchorText`, `picker.offsetX`,
`picker.offsetY`, `picker.recapture`).

---

## 7. Kayıt (DI + Katalog)

- `WorkflowServiceCollectionExtensions` — `Vision.ClickTextOffset` keyed `IActivity`.
- `ActivityRegistry.RegisterVision` — katalog girişi (yukarıdaki parametre tablosu).

---

## 8. Testler (TDD)

| Katman | Test | İçerik |
|--------|------|--------|
| Infrastructure.Tests | `VisionCatalogTests` | `Vision.ClickTextOffset` katalogda; parametre adları/tipleri; `anchorText` boşsa `BusinessException`. |
| Infrastructure.Tests | `VisionActivities` | Mock `IVisionAutomationChannel` ile `ClickTextOffsetAsync` doğru argümanlarla çağrılıyor. |
| Agent.Tests | Ofset hesabı | Sahte OCR sonucu (çapa kutusu) + `dx/dy` → beklenen tıklama koordinatı. `DoClick` sınırını test edilebilir kılmak için ofset hesabı saf bir fonksiyona ayrılır. |

Picker UI'ı (GDI/STA/global hotkey) mevcut desende olduğu gibi birim-test edilmez.

---

## 9. Riskler / Notlar

- **Çok kelimeli çapa:** `OcrTextMatch` kelime-başına eşleşir; "Malzeme No" gibi iki kelimelik çapa
  tek kelime kutusuna eşleşmeyebilir. v1'de kullanıcı en ayırt edici **tek kelimeyi** seçmeli
  (picker OCR'ı hangi kelimeyi döndürürse o `anchorText` olur). Çok-kelime/öbek eşleşmesi ileri iş.
- **Çapa tekrarı:** Aynı metin ekranda birden çok kez varsa ilk eşleşme kullanılır (mevcut
  `PollForTextAsync.FirstOrDefault` davranışı). "Çapaya en yakın" ayırt etme kapsam dışı.
- Ofset yatay/dikey büyükse hedef farklı bir çözünürlükte kayabilir; OCR tabanlı referans bunu
  büyük ölçüde azaltır ama tam ölçek-bağımsızlık garanti değildir (kabul edilen sınır).
