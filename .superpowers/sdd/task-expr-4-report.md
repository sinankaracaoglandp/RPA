# Task 4: String Fonksiyonları — Tamamlanma Raporu

## TDD Aşamaları

### STEP 1: Test Yazma (RED)
Tarih: 2026-07-15

`tests/RPA.Infrastructure.Tests/Workflow/Expressions/StringFunctionsTests.cs` oluşturuldu.
- 14 test case yazıldı (Upper, Lower, Trim, Length, Substring 2 varyant, Replace, Contains, StartsWith, EndsWith, IndexOf, PadLeft, PadRight, Concat).

### STEP 2: Red Doğrulaması
```
dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~StringFunctions
Başarısız:    14, Başarılı:     0, Atlanan:     0, Toplam:    14
```
Beklenen durum: Tüm testler FAIL (StringFunctions.All boş array döndüğü için).

### STEP 3: Implementation
`src/RPA.Infrastructure/Workflow/Expressions/StringFunctions.cs` güncellenmiştir.

14 fonksiyon eklendi:
1. **Upper** — tr-TR kültürüyle büyük harfe çevir
2. **Lower** — tr-TR kültürüyle küçük harfe çevir
3. **Trim** — baş/son boşlukları kaldır
4. **Length** — karakter sayısı (int olarak dönüş)
5. **Substring** — (start, start+length) alt dize, length opsiyonel
6. **Replace** — StringComparison.Ordinal ile değiştir
7. **Contains** — Ordinal arama
8. **StartsWith** — Ordinal başlangıç kontrolü
9. **EndsWith** — Ordinal bitiş kontrolü
10. **IndexOf** — İlk konumu bulma (-1 yoksa)
11. **PadLeft** — Sola doldu (varsayılan ' ')
12. **PadRight** — Sağa doldu (varsayılan ' ')
13. **Concat** — Variadic: `P("...", "any")` motoru tarafından tanınır

Hata işleme:
- `Sub()` helper: Substring aralığı kontrol → BusinessException
- `PadChar()` helper: Padding karakteri (null→' ', multi-char→ilk)

### STEP 4: Green Doğrulaması
```
dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~StringFunctions
Başarısız:     0, Başarılı:    14, Atlanan:     0, Toplam:    14
```
Tüm testler PASS ✓

### STEP 5: Commit
```
commit ec21f62
feat(expr): metin fonksiyonlari (Upper/Substring/Replace/Concat/...)
tr-TR duyarli Upper/Lower; Concat variadic; arg araligi kontrolu → Business.
```

---

## Kontrol Çizelgesi

- [x] Tests yazıldı (14 case)
- [x] RED doğrulandı (14 FAIL)
- [x] Implementation tamamlandı
- [x] GREEN doğrulandı (14 PASS)
- [x] Commit yapıldı
- [x] Hata işleme (Substring, PadChar)
- [x] tr-TR kültür (Upper/Lower)
- [x] Variadic Concat (motorun `IsVariadic` uyumlu)

---

## Notlar

- Tüm fonksiyonlar `FunctionArgs` helper'larını kullanır (AsString, AsInt, P, DefaultCulture).
- Parametreler katalog metadatasında tutulur (ExpressionFunctionInfo).
- Kategori: "Metin" (Cat constant).
- StringComparison.Ordinal (kültürsüz) Binary compare için kullanılmıştır.
- Tüm argüman hatası → ExpressionErrors.Business() → BusinessException.

---

## Teslimat Dosyaları

1. **Güncellenmiş:** `src/RPA.Infrastructure/Workflow/Expressions/StringFunctions.cs`
2. **Yeni:** `tests/RPA.Infrastructure.Tests/Workflow/Expressions/StringFunctionsTests.cs`

---

**Durum:** ✓ TAMAMLANDI

---

## İnceleme Düzeltmesi — PadLeft/PadRight negatif uzunluk (2026-07-15)

**Sorun:** `PadLeft`/`PadRight` `uzunluk` argümanını doğrudan BCL `string.PadLeft(int,char)` metoduna
veriyordu. Negatif uzunluk (`PadLeft("7", -1, "0")`) ham `ArgumentOutOfRangeException` (SYSTEM)
fırlatıyordu — arg/aralık hataları BusinessException olmalı kısıtını ihlal.

**Düzeltme:**
- Yeni `PadLength(fn, v)` helper: `AsInt` sonrası negatif ise `ExpressionErrors.Business($"{fn}: uzunluk {len} negatif olamaz.")`. Hem PadLeft hem PadRight kullanır (Sub() guard stiliyle uyumlu).
- Regresyon testleri: `PadLeft_NegativeLength_ThrowsBusiness`, `PadRight_NegativeLength_ThrowsBusiness`.

**Test sonucu:**
```
dotnet test tests/RPA.Infrastructure.Tests --filter FullyQualifiedName~StringFunctions
Başarısız:     0, Başarılı:    16, Atlanan:     0, Toplam:    16, Süre: 81 ms
```

**Commit:** `b03e168` — fix(expr): PadLeft/PadRight negatif uzunluk → BusinessException
