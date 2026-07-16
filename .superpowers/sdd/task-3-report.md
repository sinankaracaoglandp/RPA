# Task 3 Report: Kanal implementasyonu + OCR/ofset yardımcıları (Agent)

## TDD Evidence

### RED
Before creating `VisionOffset.cs`, building `src/RPA.Agent` failed with two errors:
1. `RPA.Agent.Tests` referenced `RPA.Agent.Vision.VisionOffset` which did not exist.
2. `TesseractOpenCvVisionChannel` did not implement `IVisionAutomationChannel.ClickTextOffsetAsync(string, int, int, string, string, string?, int)`:
```
C:\Source\RPA\src\RPA.Agent\Vision\TesseractOpenCvVisionChannel.cs(18,52): error CS0535:
'TesseractOpenCvVisionChannel', 'IVisionAutomationChannel.ClickTextOffsetAsync(string, int, int, string, string, string?, int)' arabirim üyesini uygulamaz
```

### GREEN
After creating `VisionOffset.cs`, `OcrEngine.cs`, and modifying `TesseractOpenCvVisionChannel.cs`:

- `dotnet build src/RPA.Agent/RPA.Agent.csproj` → **Oluşturma başarılı oldu. 0 Uyarı, 0 Hata.**
- `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~Vision` →
  **Başarılı! - Başarısız: 0, Başarılı: 10, Atlanan: 0, Toplam: 10.**

## Changes

- CREATE `src/RPA.Agent/Vision/VisionOffset.cs` — pure `ClickPoint(VisionMatch anchorBox, int dx, int dy)` static helper, matches brief exactly.
- CREATE `src/RPA.Agent/Vision/OcrEngine.cs` — extracted Tesseract word-box OCR logic (`Read(Mat image, string tessdataPath, string language)`), identical iterator/bounding-box logic to what was previously inline in `RunOcr`. Returns `(string Text, List<OcrWord> Words)`.
- MODIFY `src/RPA.Agent/Vision/TesseractOpenCvVisionChannel.cs`:
  - `RunOcr` now delegates to `OcrEngine.Read(image, _tessdataPath, language)` and maps `OcrEngine.OcrWord` → private `OcrWord` record, preserving the existing `SystemException` wrapping (`catch (Exception ex) when (ex is not RPA.Domain.Exceptions.SystemException)`).
  - Added `ClickTextOffsetAsync(string anchorText, int dx, int dy, string language, string matchMode, string? clickType, int timeoutMs)` implementing `IVisionAutomationChannel.ClickTextOffsetAsync`. Reuses existing `PollForTextAsync` to find the anchor word box; throws `SystemException` if not found within timeout; otherwise computes click point via `VisionOffset.ClickPoint` and calls existing `DoClick`.
- CREATE `tests/RPA.Agent.Tests/Vision/VisionOffsetTests.cs` — 2 tests (offset from center, zero-offset = center), verbatim from brief.

## Build/Test Summary

- `RPA.Agent.csproj` build: SUCCESS (0 warnings, 0 errors).
- `RPA.Agent.Tests` (filter `Vision`): 10/10 passed.
- Full solution now compiles since `TesseractOpenCvVisionChannel` fully implements `IVisionAutomationChannel`.

## Commit

`ecee5cf` — `feat(vision): ClickTextOffset kanal impl + OcrEngine/VisionOffset`
(4 files changed: OcrEngine.cs created, VisionOffset.cs created, TesseractOpenCvVisionChannel.cs modified, VisionOffsetTests.cs created)

## Concerns

None. `OcrEngine.Read` preserves the exact word-box iteration/bounding-box logic that was previously inline in `RunOcr`, so runtime (this task) and the Task-5 picker will produce identical word boxes as required.

Note: this file previously contained an unrelated report ("IRobotDispatcher ajan seçim algoritması") from an earlier numbering cycle; overwritten with this task's report.
