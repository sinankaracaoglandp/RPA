# Task 2 Report — Vision.ClickTextOffset aktivitesi + parse + kayıt

## Özet
`Vision.ClickTextOffset` aktivitesi eklendi: OCR ile bir metin çapası (anchor) bulunur, kelime
kutusunun merkezinden `(dx,dy)` piksel ofsetiyle tıklanır. `anchor` girişi tek birleşik JSON
(`{anchorText, dx, dy}`, `PickerKind="text-offset"`). `TextOffsetSpec.Parse` boş/geçersiz JSON'da
`BusinessException` fırlatır. Kanal çağrısı: `IVisionAutomationChannel.ClickTextOffsetAsync`
(Task 1'de eklendi).

## Değişen dosyalar
- `src/RPA.Infrastructure/Activities/Vision/VisionActivities.cs` — `VisionClickTextOffsetActivity` + `TextOffsetSpec` eklendi.
- `src/RPA.Infrastructure/Workflow/WorkflowServiceCollectionExtensions.cs` — keyed DI kaydı.
- `src/RPA.Infrastructure/Workflow/ActivityRegistry.cs` — katalog girişi (`RegisterVision`).
- `tests/RPA.Infrastructure.Tests/Activities/VisionActivitiesTests.cs` — 3 yeni test.
- `tests/RPA.Infrastructure.Tests/Workflow/VisionCatalogTests.cs` — InlineData + sayaç 7→8.

## TDD Kanıtı

### RED (test öncesi implementasyon yok)
```
dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~ClickTextOffset
...
VisionActivitiesTests.cs(128,28): error CS0246: 'VisionClickTextOffsetActivity' türü veya ad
  alanı adı bulunamadı (bir using yönergeniz veya derleme başvurunuz mu eksik?)
VisionActivitiesTests.cs(138,28): error CS0246: ... (aynı hata, ikinci test)
VisionActivitiesTests.cs(148,28): error CS0246: ... (aynı hata, üçüncü test)
```
Beklenen FAIL doğrulandı (derleme hatası — tip tanımlı değil).

### GREEN (implementasyon + DI + katalog sonrası, tüm Vision testleri)
```
dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~Vision
...
Başarılı!  - Başarısız: 0, Başarılı: 20, Atlanan: 0, Toplam: 20, Süre: 223 ms
  - RPA.Infrastructure.Tests.dll (net10.0)
```
20/20 PASS (3 yeni ClickTextOffset testi + 17 mevcut Vision testi, katalog testleri dahil).

## Commit
`1753b0d` — `feat(vision): Vision.ClickTextOffset aktivitesi + katalog/DI`

Not: Repo `.gitignore`'unda `bin/`/`obj/` hariç tutulmadığından, build çıktı dosyaları da commit'e
dahil oldu (mevcut repo davranışı — bu görevle ilgisiz).

## Durum
DONE. Sapma yok; brief'teki kod birebir uygulandı.
