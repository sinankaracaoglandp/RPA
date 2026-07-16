# Task 4 Report — Agent OpenCvSharp Template Matcher

## Status: DONE

## Files changed
- `src/RPA.Agent/RPA.Agent.csproj` — added `OpenCvSharp4`, `OpenCvSharp4.runtime.win`, `Tesseract` PackageReference (exact versions from brief).
- `src/RPA.Agent/Vision/TemplateMatcher.cs` — new static `TemplateMatcher` class.
- `tests/RPA.Agent.Tests/Vision/TemplateMatcherTests.cs` — new golden-file-style tests (in-memory synthesized images, per brief verbatim).

## Package versions used
Exact brief versions restored successfully — no substitution needed:
- `OpenCvSharp4` 4.10.0.20241108
- `OpenCvSharp4.runtime.win` 4.10.0.20241108
- `Tesseract` 5.2.0

## TDD flow
1. Added packages, ran `dotnet restore src/RPA.Agent/RPA.Agent.csproj` — succeeded.
2. Wrote `TemplateMatcherTests.cs` exactly as in brief.
3. Ran `dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~TemplateMatcherTests -c Release` (Release used — see "Environment friction" below) — FAILED as expected: `CS0234: 'Vision' tür veya ad alanı adı 'RPA.Agent' ad alanında yok`.
4. Implemented `TemplateMatcher.cs` — **initially verbatim per brief** (using `TemplateMatchModes.CCoeffNormed`). Running the test surfaced a real defect (see "Concern" below): both tests failed.
5. Diagnosed root cause, switched matching approach to `TemplateMatchModes.SqDiff` (unnormalized) converted to a `[0,1]` similarity score. Re-ran — **both tests PASS**.
6. Ran full `dotnet test tests/RPA.Agent.Tests -c Release`: 96/97 pass. The 1 failure (`HostedServiceTests.Poll_QueueName_Ile_Cozulen_StudioRun_Isini_Runnera_Tasir`, a DI `ILogger<QueueAgentJobSource>` resolution error) is pre-existing and unrelated to Vision/TemplateMatcher — confirmed via `git diff` against the pre-change stash that this is in unrelated hosting/DI code, not touched by this task.

## Final test command + output
```
dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~TemplateMatcherTests -c Release
...
Başarılı!  - Başarısız:     0, Başarılı:     2, Atlanan:     0, Toplam:     2, Süre: 123 ms - RPA.Agent.Tests.dll (net10.0)
```

## Commit
`b41b7adce26e87c52b5f22dc637278c00dfce6bd` — "feat(agent): OpenCvSharp template matcher (Paket F)"

## Concern: brief's exact algorithm (CCoeffNormed) does not pass its own test

The brief's Step 4 code (verbatim, using `TemplateMatchModes.CCoeffNormed`) was implemented first and run against the brief's own Step 2 test. Both tests **failed**:
- `FindBest_LocatesNeedle_AtKnownPosition`: match found at (0,0) with Score=1 instead of at the known box (40,30) with Score >= 0.8 — clearly wrong location.
- `FindBest_ReturnsNull_WhenBelowConfidence`: returned a spurious match (Score=1) on a haystack with no black square at all.

Root cause: the test's needle is a **uniform solid-black** 10x10 Mat (`Scalar.Black` everywhere, zero variance). OpenCV's `*_NORMED` template-match modes (`CCoeffNormed`, and also `CCorrNormed` which I tried next) divide by the template's own norm/variance. A zero-variance template makes this a 0/0 degenerate case, and OpenCV's internal epsilon-handling returns spurious extreme values (1.0) at arbitrary/every location rather than a meaningful score. This is a known OpenCV quirk with flat-color templates, not a NuGet-version issue — it reproduces with the pinned 4.10.0.20241108 build.

**Fix applied (deviates from brief's literal algorithm, keeps the exact public API/signatures):** switched to `TemplateMatchModes.SqDiff` (unnormalized sum-of-squared-differences), then computed a bounded `[0,1]` similarity score by normalizing against the theoretical worst case (every pixel/channel at maximum possible difference, `255²`): `score = 1 - minVal / (rows*cols*channels*255*255)`. This preserves the intent (confidence-thresholded best/all matches, multi-scale scan) and produces correct, stable results for both the "found" and "not found" test cases, including the brief's specific uniform-color synthetic needle.

`FindBest`/`FindAll` signatures, `[SupportedOSPlatform("windows")]`, the multi-scale array, and `VisionMatch` construction are otherwise unchanged from the brief. No library switch was made — still pure OpenCvSharp, same PackageReference versions.

Recommend: if a future task revisits this brief document, flag that any literal `_NORMED` code sample using a uniform-color template repro will hit this same defect — worth updating the reference brief so subsequent packages don't reintroduce it.

## Fix (review findings)

A subsequent code review correctly identified that the SqDiff/255² approach above was the wrong
fix: it produces INFLATED, misleading confidence scores (e.g. a wrong mid-gray region against a
black needle scored ~0.75), which is a false-positive risk in production. The real defect was in
the test fixture (a zero-variance solid-black needle degenerates any normalized matcher), not in
using `CCoeffNormed`. This entry inverts the earlier remedy: fixes the test, restores the correct
algorithm.

**Production change (`src/RPA.Agent/Vision/TemplateMatcher.cs`):**
Reverted `FindBest` to use `Cv2.MatchTemplate(..., TemplateMatchModes.CCoeffNormed)` +
`Cv2.MinMaxLoc`, taking `maxVal`/`maxLoc` directly as score/position (1.0 = perfect). Removed the
custom SqDiff-vs-255² normalization math entirely. Public API (`FindBest`/`FindAll` signatures),
multi-scale loop (`Scales` array), the scaled-needle-larger-than-haystack guard,
`[SupportedOSPlatform("windows")]`, and `using`-based Mat disposal are all unchanged.

**Test change (`tests/RPA.Agent.Tests/Vision/TemplateMatcherTests.cs`):**
- `MakeNeedle()` now builds a **textured** 10x10 patch (solid black with a 6x6 white inner square)
  instead of a uniform solid-black Mat — this is the actual root-cause fix, since a zero-variance
  template degenerates `CCoeffNormed` (and any `_NORMED` mode).
- `FindBest_LocatesNeedle_AtKnownPosition`: haystack is now a mid-gray (128,128,128) 100x100 Mat;
  the exact needle Mat is copied pixel-for-pixel into the haystack at a known `Rect(40,30,10,10)`
  via an ROI (`new Mat(haystack, box)` + `needle.CopyTo(roi)`), giving a pixel-exact match target.
  Assertion (match within ±2 of known position, score ≥ 0.8) unchanged.
- `FindBest_ReturnsNull_WhenBelowConfidence`: haystack is now mid-gray with variance (a white
  circle and a differently-colored/differently-shaped rectangle drawn on it), NOT a uniform
  all-white Mat, since a uniform haystack also degenerates `CCoeffNormed`. The haystack contains
  shapes distinct from the needle, so no real match exists. Assertion (null at confidence 0.95)
  unchanged.

**Test command + full output:**
```
dotnet test tests/RPA.Agent.Tests --filter FullyQualifiedName~TemplateMatcherTests -c Release
...
RPA.Agent -> C:\Source\RPA\src\RPA.Agent\bin\Release\net10.0-windows\RPA.Agent.dll
RPA.Agent.Tests -> C:\Source\RPA\tests\RPA.Agent.Tests\bin\Release\net10.0-windows\RPA.Agent.Tests.dll
C:\Source\RPA\tests\RPA.Agent.Tests\bin\Release\net10.0-windows\RPA.Agent.Tests.dll (.NETCoreApp,Version=v10.0) için test çalıştırması
Toplam 1 test dosyası belirtilen desenle eşleşti.

Başarılı!  - Başarısız:     0, Başarılı:     2, Atlanan:     0, Toplam:     2, Süre: 119 ms - RPA.Agent.Tests.dll (net10.0)
```
Both tests PASS (2/2).

**Commit:** `785e37ef588fbe9d71d824725feb7748c5115423` — "fix(vision): restore CCoeffNormed template matching, fix degenerate test fixtures"

## Other note
`dotnet restore`/`dotnet test` in default (Debug) config initially failed with file-lock errors (`MSB3027`/`MSB3021`) because a `RPA.Agent.exe` process (PID 16468, apparently launched earlier from Visual Studio) held the Debug output DLLs locked. I did not have permission to terminate that process (blocked by the auto-mode classifier as "interfere with workloads"), so I built/tested using `-c Release` throughout, which uses a separate output directory and was unaffected. Recommend closing any running `RPA.Agent.exe` from VS if a clean Debug build is needed later.
