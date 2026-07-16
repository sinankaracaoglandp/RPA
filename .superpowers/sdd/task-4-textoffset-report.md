# Task 4 Report — Mesaj sözleşmesi + koordinatör dalı + picker arayüzü (Vision.ClickTextOffset)

## TDD Evidence

### Step 1-4: SpyElementMessage.FromTextOffset
RED:
```
error CS0117: 'SpyElementMessage' bir 'FromTextOffset' tanımı içermiyor
```
GREEN:
```
Başarılı!  - Başarısız: 0, Başarılı: 1, Atlanan: 0, Toplam: 1
```

### Step 5-8: SpySessionCoordinator text-offset branch
RED:
```
error CS0246: 'ITextOffsetPicker' türü veya ad alanı adı bulunamadı
error CS0246: 'TextOffsetPick' türü veya ad alanı adı bulunamadı
error CS1739: 'SpySessionCoordinator' için en iyi yeniden yükleme, 'textOffsetPicker' adlı bir parametre içermiyor
```
GREEN (both new text-offset tests + existing 3 image coordinator tests run together):
```
Başarılı!  - Başarısız: 0, Başarılı: 5, Atlanan: 0, Toplam: 5
```

## Changes

- `src/RPA.Infrastructure/UISpy/SapGuiElementSender.cs`: added `AnchorText`/`Dx`/`Dy` to `SpyElementMessage`; added `FromTextOffset` factory (Kind="text-offset").
- `src/RPA.Agent/UISpy/SpySessionCoordinator.cs`: added `ITextOffsetPicker` interface, `TextOffsetPick` record, optional `_textOffsetPicker` field/ctor param (last param, preserves existing named-arg call sites), `text-offset` kind branch (parse validity, null-picker throw, 300s timeout, message via `FromTextOffset`).
- `tests/RPA.Infrastructure.Tests/UISpy/SpyElementMessageTests.cs`: added `FromTextOffset_SetsKindAndFields`.
- `tests/RPA.Agent.Tests/UISpy/SpySessionCoordinatorTextOffsetTests.cs` (new): `Start_TextOffsetKind_SendsTextOffsetMessage`, `Start_TextOffsetKind_NoPicker_Throws`.

## Commit

`9af21f9` feat(spy): text-offset mesaj + koordinator dali + ITextOffsetPicker

Note: `task-4-report.md` already existed for an unrelated earlier "Task 4" (OpenCvSharp Template
Matcher), so this report was written to `task-4-textoffset-report.md` instead to avoid overwriting it.
