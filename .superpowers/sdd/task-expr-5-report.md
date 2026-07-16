# Task 5: Dönüşüm + Yardımcı Fonksiyonlar — Rapor

## TDD Süreci

### RED → GREEN

**Test Yazma (Başarısız):**
```
ConversionFunctionsTests.cs oluşturuldu:
- 11 test tanımı (ToInt, ToDecimal, ToDouble, ToStr, ToBool, Coalesce)
- Çalıştırma: 10 başarısız, 1 başarılı (mevcut HelperFunctions.All boş idi)
```

**İmplementasyon:**
1. `ConversionFunctions.cs`: 5 tip dönüşüm fonksiyonu
   - `ToInt`: Sayıya çevirme (truncate)
   - `ToDecimal`: Ondalık sayıya çevirme (kültür desteği)
   - `ToDouble`: Double dönüşümü
   - `ToStr`: Metne çevirme (opsiyonel format + kültür)
   - `ToBool`: Boolean dönüşümü (string, sayı, null işlemesi)

2. `HelperFunctions.cs`: 1 yardımcı fonksiyon
   - `Coalesce`: Null/boş kontrol (a veya yedek b)

**Kültür Sorun Çözümü:**
- İlk deneme: NumberStyles.Any → "3,5" → 35 (hatalı)
- Sebep: `new CultureInfo("tr-TR")` NumberFormat'ı doğru başlatmıyor
- Çözüm: `CultureInfo.GetCultureInfo(culture.Name)` ile sistem cache'inden yeniden alma
- Bu workaround, mevcut FunctionArgs.DefaultCulture tanımıyla uyumlu

**Test Sonuçları: PASS**
```
Başarılı: 11/11 ConversionFunctionsTests
```

## Tüm Express Suite Testleri

```bash
dotnet test --filter "FullyQualifiedName~Expressions"
```

**Sonuç:** ✅ 51/51 PASS
- DateFunctionsTests: 12 test
- StringFunctionsTests: 18 test  
- ConversionFunctionsTests: 11 test
- ExpressionEngineTests + ParserTests: 10 test

## Backward Compat (BaseRunner)

```bash
dotnet test --filter "FullyQualifiedName~BaseRunner"
```

**Sonuç:** ✅ 29/29 PASS

## Commit

```
020ae2b feat(expr): donusum + yardimci fonksiyonlar (ToInt/ToDecimal/ToStr/Coalesce)
```

## Dosyalar

- `src/RPA.Infrastructure/Workflow/Expressions/ConversionFunctions.cs` (impl)
- `src/RPA.Infrastructure/Workflow/Expressions/HelperFunctions.cs` (impl)
- `tests/RPA.Infrastructure.Tests/Workflow/Expressions/ConversionFunctionsTests.cs` (test)

## Notlar

**Kültür Workaround:** `CultureInfo.GetCultureInfo()` kullanarak, `new CultureInfo("tr-TR")` constructorının NumberFormat başlatma sorununu çözdü. Bu tip dönüşüm ve tarih/saat fonksiyonlarında benzer sorunları önler.

**Hata Yönetimi:** Tüm başarısız dönüşümler `BusinessException` atar (ifade config hatası = iş kuralı ihlali).

**Test Kapsamı:** Pozitif (başarılı dönüşüm) ve negatif (BusinessException) durumlar kapsanmış.

---

## Review Follow-up (culture re-fetch removal) — STOPPED, NOT COMMITTED

Coordinator asked to remove the `culture = CultureInfo.GetCultureInfo(culture.Name);` line as behavior-neutral cargo-cult. **Removing it FAILS 2 tests** — the line is load-bearing on this machine, so per instructions I STOPPED and did not commit.

### Exact failure after removal
```
ConversionFunctionsTests.ToDecimal_TrCulture  FAIL  Expected: 3.5  Actual: 35
ConversionFunctionsTests.ToDouble_TrCulture   FAIL  Expected: 2.5  Actual: 25
Başarısız: 2, Başarılı: 9
```

### Root cause (verified with standalone net10.0 repro)
On this Windows machine, `CurrentCulture=tr-TR` AND the OS regional number format has been **user-customized**:
```
new CultureInfo("tr-TR")            → dec='.'  grp=','   (honors Windows user overrides)
CultureInfo.GetCultureInfo("tr-TR") → dec=','  grp='.'   (standard culture, ignores overrides)
decimal.Parse("3,5", Any, new)  = 35     ← wrong
decimal.Parse("3,5", Any, get)  = 3.5    ← correct
```
`new CultureInfo(name)` defaults to `useUserOverride: true`, so it picks up the machine's customized regional settings (decimal='.'); `GetCultureInfo` returns the read-only standard culture. The coordinator's premise ("new CultureInfo initializes NumberFormat fine / re-fetch is inert") does not hold **when the OS has user regional overrides** — the re-fetch is behavior-changing here.

### The real underlying issue (recommendation, out of Task-5 scope)
`FunctionArgs.DefaultCulture = new("tr-TR")` inherits user overrides. The whole expression library intends *standard* tr-TR (comma decimal). Proper fix is at the shared source:
`DefaultCulture = CultureInfo.GetCultureInfo("tr-TR")` (or `new CultureInfo("tr-TR", useUserOverride: false)`).
That is a `FunctionArgs` contract change affecting Date/String/Conversion modules and their tests — should be a separate coordinated task, not a silent Task-5 edit. The ToDecimal re-fetch currently compensates locally only for ToDecimal/ToDouble (ToStr formatting still uses the override-honoring DefaultCulture, so ToStr is inconsistent — but no test currently exercises that divergence).

**Action taken:** reverted the file to the committed green state (11/11 pass). No new commit. Awaiting coordinator decision on the FunctionArgs.DefaultCulture fix.

---

## Root Fix Applied (coordinator-approved) — 2d2ad5e

Fixed at the shared source per coordinator direction.

**`FunctionArgs.cs`:** `DefaultCulture` changed from `new("tr-TR")` (inherits Windows user regional overrides, `useUserOverride:true`) to `CultureInfo.GetCultureInfo("tr-TR")` (standard, override-free, cached). This makes the whole expression library deterministic across robot machines regardless of local Windows regional customization.

**`ConversionFunctions.cs`:** removed the now-redundant local re-fetch line + comment in ToDecimal; uses `Culture(fn, a, 1)` directly.

### Verification (all green)
```
Expressions:  Başarılı 51/51
BaseRunner:   Başarılı 29/29
```
Specific assertions confirmed:
- `ToDecimal("3,5")` → 3.5m ✓
- `ToDecimal("3.5","en-US")` → 3.5m ✓
- `ToDouble("2,5")` → 2.5d ✓
- Date `Format(d,"dd.MM.yyyy")` → "15.01.2026" ✓ (unaffected — dd.MM.yyyy is override-independent)

**Commit:** 2d2ad5e fix(expr): FunctionArgs.DefaultCulture override-bagimsiz (deterministik tr-TR)
