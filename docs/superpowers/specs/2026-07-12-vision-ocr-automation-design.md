# Görüntü/OCR Fallback Otomasyonu — Paket F (`Vision.*`)

**Tarih:** 2026-07-12
**Durum:** Onaylandı (tasarım)
**Kapsam:** Erişilebilirlik ağacı (UIA/DOM) olmayan uygulamalar için piksel + metin tabanlı otomasyon.

---

## 1. Amaç ve Motivasyon

Bazı uygulamalar UI Automation (UIA) ağacı sunmaz: eski Win32, Java/Delphi custom-render arayüzler,
uzak masaüstü içinde koşan uygulamalar, canvas/oyun motoru tabanlı arayüzler. Bu uygulamalarda
`Desktop.*` (Paket E, UIA) ve `Web.*` (Playwright/DOM) kanalları çalışmaz.

Paket F, bu boşluğu **görüntü eşleme (template matching)** ve **OCR (metin okuma)** ile doldurur —
son çare (fallback) otomasyon. İnsanın ekrana bakıp tıklamasını taklit eder.

## 2. Mimari ve Konumlanma

`Desktop.*` (Paket E) deseninin birebir ikizi; UIA selector yerine **piksel + metin** hedefler.

**Onion katmanları:**

- **Domain** — yeni arayüz `src/RPA.Domain/Interfaces/IVisionAutomationChannel.cs`.
  Harici bağımlılık yok, sadece sözleşme. `IDesktopAutomationChannel` ile kardeş, ondan bağımsız.
- **Infrastructure** — `Vision.*` aktivite ailesi
  (`src/RPA.Infrastructure/Activities/Vision/VisionActivities.cs`), platform-nötr, yalnız arayüze
  bağlı → birim testlerde mock'lanır. Windows dışı fallback `UnavailableVisionAutomationChannel`
  (açık `SystemException` mesajı verir — `Desktop` desenindeki `UnavailableDesktopAutomationChannel`
  gibi).
- **Agent (net10.0-windows)** — gerçek implementasyon
  `src/RPA.Agent/Vision/TesseractOpenCvVisionChannel.cs`. Ekran yakalama (GDI), OpenCvSharp template
  matching, Tesseract OCR, gerçek fare/klavye (mevcut FlaUI/input altyapısı). `AddAgentCore` içinde
  Windows koşuluyla kaydedilir.

**Yetenek (capability):** `vision`. Katalog `ActivityRegistry.RegisterVision`; keyed DI kaydı
Infrastructure `WorkflowServiceCollectionExtensions`.

**Kütüphaneler (hepsi ücretsiz, ek lisans maliyeti yok):**

- `OpenCvSharp4` + `OpenCvSharp4.runtime.win` — template matching (Apache 2.0 / BSD).
- `Tesseract` NuGet — OCR (Apache 2.0).
- `.traineddata` dil dosyaları (tur, eng, deu, …) — Google resmi deposundan ücretsiz; Agent ile
  dağıtılır (robot makinesine dil paketi kurmaya gerek yok). Tek çağrıda çoklu dil (`tur+eng+deu`).

## 3. Hedef Ekran Varsayımı

Otomasyon **etkileşimli masaüstü oturumunda** çalışır (konsol veya RDP; giriş yapılmış, ekran
görünür) — mevcut `Desktop.*` kanalıyla aynı varsayım. Tam ekran GDI ile yakalanır; gerçek
fare/klavye ile tıklanır. Headless / session-0 servis oturumu desteklenmez (görünür ekran gerekir).

## 4. Aktivite Ailesi (`Vision.*`, kategori "Görüntü")

| Activity ID | Ne yapar | Girdi | Çıktı | Bulunamazsa |
|---|---|---|---|---|
| `Vision.Click` | Ekranda template görüntüyü bulur, merkezine tıklar | `image` (base64 PNG), `confidence` (0–1, vars. 0.8), `clickType` (left/right/double), `timeoutMs` | — | **SystemException** |
| `Vision.WaitFor` | Template görünene kadar bekler | `image`, `confidence`, `timeoutMs` | — | **SystemException** |
| `Vision.Exists` | Template ekranda var mı? Hata fırlatmaz | `image`, `confidence`, `timeoutMs` (vars. 0 = tek bakış) | `exists` (bool) | `false` döner |
| `Vision.GetText` | Bölgeden OCR ile metin okur | `region` (x,y,w,h — opsiyonel, boşsa tam ekran), `language` | `text` (string) | System (OCR hatası) |
| `Vision.ClickText` | OCR ile metni bulur, üstüne tıklar | `text` (aranan), `language`, `clickType`, `matchMode` (contains/exact), `timeoutMs` | — | **SystemException** |
| `Vision.TextExists` | Metin ekranda var mı? Hata fırlatmaz | `text`, `language`, `matchMode`, `timeoutMs` | `exists` (bool) | `false` döner |

**Ortak davranış:**

- **Çok dil:** `language` parametresi `tur+eng+deu` gibi çoklu değer alır; boşsa robot/workflow
  varsayılan diline düşer.
- **`image` parametresi:** base64 PNG (🎯 bölge picker'ı gömer). `PickerKind = "image"`. Dizayn
  ekranında küçük önizleme (thumbnail) olarak görünür.
- **`region` parametresi:** 🎯 ile bölge seçilir → `{x,y,w,h}`. Boşsa tam ekran.
- **Metin eşleme:** `Vision.GetText`/`ClickText`/`TextExists` normalize edilmiş karşılaştırma yapar
  (boşluk/case toleranslı); `matchMode` ile `contains` (vars.) / `exact`.
- **DPI/ölçek:** template matching çok-ölçekli (multi-scale) yapılır → farklı DPI'da makul tolerans.
  Yine de picker ile aynı makinede yakalama önerilir (kararlılık notu).
- **Exception sınıflandırması:** görüntü/metin bulunamadı & timeout → `System` (retry edilebilir);
  `Exists`/`TextExists` hiç fırlatmaz. Parametre doğrulama hataları → `BusinessException`.

## 5. 🎯 Picker — "image" modu (bölge seçimi)

UiPath "Indicate on screen" mantığı: kullanıcı ekranda nesnenin etrafına dikdörtgen çizer, görüntü
yakalanır ve workflow'a gömülür.

Mevcut tek-seçim picker altyapısı (`SpySessionCoordinator`, `StudioHub.StartSpy(sessionId, kind)`)
`kind:"image"` dalıyla genişletilir — SAP/Web/Desktop picker deseninin aynısı.

- **Yeni arayüz:** `IImageRegionPicker` (`RPA.Agent/UISpy/`), diğer picker'larla aynı sözleşme deseni.
- **İmplementasyon:** `GdiImageRegionPicker` — tüm ekranı kaplayan yarı saydam overlay açar, kullanıcı
  dikdörtgen çizer, `Esc` iptal. Seçilen bölgenin PNG'sini yakalar.
  - `image` parametresi için → yakalanan bölgenin **base64 PNG**'si (gömülür).
  - `region` parametresi için → `{x,y,w,h}` koordinat JSON'u.
- **Transport:** `SpyElementMessage` genişletilir — `Kind="image"`, nullable `ImageBase64` +
  `Region(x,y,w,h)`; `SpyElementMessage.FromImage(...)`.
- **İptal:** mevcut `NotifySpyCancelled` / `SpyCancelled` yolu aynen kullanılır (Esc/boş seçim).
- **Tek-ekran UX:** Desktop picker'daki gibi tasarımcı penceresini küçültme/geri getirme davranışı
  yeniden kullanılır (hedef uygulama görünsün).

**Studio tarafı:** `SpyKind`'e `image` eklenir; `PickerKind="image"` olan parametrelerde 🎯 bu
picker'ı çağırır. Base64 sonucu için parametre editörü küçük bir **önizleme thumbnail** gösterir.

## 6. Çalışma Zamanı Akışı

1. Robot `Vision.Click` node'una gelir → gömülü base64 PNG decode edilir (OpenCv `Mat`).
2. GDI ile o anki tam ekran yakalanır (`Mat`).
3. Çok-ölçekli `MatchTemplate` (TM_CCOEFF_NORMED) → skor haritası; `confidence` eşiğinin üstündeki
   en yüksek skorlu konum(lar).
4. `timeoutMs` içinde eşleşme yoksa periyodik yeniden yakala; süre dolunca `SystemException`.
5. Eşleşme merkezine gerçek fareyle tıkla (`clickType`).

OCR akışı benzer: bölge (veya tam ekran) yakala → Tesseract (`language`) → kelime kutuları + metin →
normalize karşılaştırma → `ClickText` için kelime kutusu merkezine tıkla.

## 7. İleriye Dönük Hazırlık — Anchor (Faz 2)

Anchor (bir nesneyi komşu işaretçisine göre bulma) **bu pakete dâhil değildir**, ama çekirdek buna
hazır tasarlanır:

- Template matcher ve OCR iç API'si baştan **`IReadOnlyList<Match>` (Rect + score)** döndürür.
  Aktiviteler şimdilik en yüksek skorlu tek eşleşmeyi kullanır.
- Böylece anchor eklenince (yön + mesafe skorlaması, iki-adımlı picker, `anchorImage`/`anchorText`
  parametreleri) çekirdek imza kırılmaz. Anchor ayrı küçük paket olarak temiz eklenir (~%25–35 ek iş).

## 8. Wiring / Kayıt

- **Agent `AddAgentCore`** → Windows ise `IVisionAutomationChannel` = `TesseractOpenCvVisionChannel`,
  `IImageRegionPicker` = `GdiImageRegionPicker`; `SpySessionCoordinator`'a `kind:"image"` dalı.
- **Infrastructure `WorkflowServiceCollectionExtensions`** → `Vision.*` aktiviteleri keyed `IActivity`;
  Windows dışı fallback `UnavailableVisionAutomationChannel`.
- **Katalog** `ActivityRegistry.RegisterVision`.

## 9. Test Stratejisi (TDD)

- **Infrastructure.Tests** — `Vision.*` aktiviteleri mock `IVisionAutomationChannel` ile: parametre
  doğrulama (Business), çıktı yazımı, `Exists`/`TextExists` fırlatmama davranışı, `language`/
  `confidence`/`matchMode` varsayılanları.
- **Agent-seviyesi testler (Windows, ayrı)** — gerçek OpenCvSharp golden-file (bilinen PNG içinde
  alt-görüntü bulma, farklı confidence eşikleri), Tesseract OCR bilinen görüntüde TR+EN+DE metin
  okuma. Windows dışı CI'da atlanır (`Desktop.*` deseniyle aynı).

## 10. Kontrat Değişikliği (CLAUDE.md'ye eklenecek — Paket F)

- Yeni arayüz `IVisionAutomationChannel`.
- `SpyElementMessage`: `Kind="image"`, nullable `ImageBase64` + `Region(x,y,w,h)`; `FromImage(...)`.
- `ActivityParameter.PickerKind` yeni değer: `"image"`.
- `StudioHub.StartSpy` `kind:"image"` kabul eder.
- **Etkilenen:** Studio picker metadata tüketicileri, Agent UI Spy transport. Web/SAP/Desktop
  picker'lar etkilenmez (additive değişiklik).

## 11. Kapsam Dışı (YAGNI)

- Anchor / relative-to-neighbor bulma (Faz 2 — çekirdek hazır).
- Headless / session-0 çalışma.
- Bulut Vision API.
- Görüntü dosya yolu ile referans (yalnız gömülü base64).
- Sürekli görsel izleme / event tabanlı tetikleme (yalnız aktif arama).
