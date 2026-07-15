# Tasarım — İfade Fonksiyon Kütüphanesi + Kod Tamamlama

**Tarih:** 2026-07-15
**Kapsam:** Spec Bölüm 5.2'de "tam ifade dili S2'ye ertelendi" notunun karşılığı.
**İlgili mevcut kod:** `src/RPA.Infrastructure/Workflow/ExpressionEvaluator.cs`,
`src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.ts`.

---

## 1. Amaç ve Problem

Bugün workflow ifadeleri yalnız `${değişken}` / `{{değişken}}` token'ı, JSON yolu (`${data.alan}`),
karşılaştırma (`${a} == ${b}`) ve literal destekliyor — **fonksiyon çağrısı yok.** Kullanıcı bir
değişkeni işlerken tarih biçimleme, string işleme ve tip dönüşümü yapamıyor; Studio'da fonksiyon
keşfi/kod tamamlama yok.

Bu tasarım: (a) `${...}` içinde **iç içe + aritmetik** fonksiyon çağrısı destekleyen gerçek bir ifade
motoru; (b) date/string/dönüşüm fonksiyon kütüphanesi; (c) Studio'da **satır içi autocomplete.**

---

## 2. Kararlar (brainstorming çıktısı)

| Konu | Karar |
|------|-------|
| Söz dizimi | `Fonksiyon(arg)` — token içinde (metot zinciri yok) |
| İfade gücü | İç içe + aritmetik (tam parser: tokenizer→parser→AST) |
| Autocomplete | Satır içi açılır liste (IDE hissi) |
| Kültür | `tr-TR` varsayılan; opsiyonel son `kültür` argümanıyla aşılır |
| Katalog dağıtımı | Backend API endpoint (tek kaynak) |

---

## 3. İfade Motoru (`ExpressionEngine`, Infrastructure)

Token içeriğini (`${...}` içindeki metin) değerlendiren yeni motor. Katmanlar:

1. **Tokenizer** — sayı, string ("..."/'...'), identifier (nokta yolu dahil: `data.alan`),
   fonksiyon adı, virgül, parantez, aritmetik (`+ - * /`), karşılaştırma (`== != > < >= <=`).
2. **Parser** — recursive-descent / Pratt (operatör önceliği: `*//` > `+-` > karşılaştırma).
   Üretim: AST (Literal, VariableRef(path), FunctionCall(name, args[]), Binary(op, l, r)).
3. **Evaluator** — AST'yi `VariableScope` + `FunctionRegistry` ile değerlendirir.
   - `VariableRef` → bugünkü `ResolvePath` mantığı (değişken + iç içe JSON alan; JToken→native).
   - `FunctionCall` → registry'den fonksiyonu bulur, argümanları değerlendirip çağırır.
   - `Binary` → sayısal ise aritmetik/karşılaştırma; `+` string operand varsa birleştirme.

### `ExpressionEvaluator` ile ilişki (geriye uyum)

`ExpressionEvaluator` public API'si (`EvaluateValue`, `EvaluateString`, `EvaluateCondition`)
**değişmez.** İçeride:
- `${...}` / `{{...}}` token ayrıştırması ve şablon/koşul katmanı **korunur** (mevcut regex'ler:
  `TokenPattern`, `SingleTokenPattern`, `MustacheTokenPattern`, `NormalizeExpression`).
- Bir token'ın **içeriği** artık `ResolvePath` yerine `ExpressionEngine.Evaluate(content, scope)` ile
  değerlendirilir. Düz nokta-yolu (`data.alan`) motorun `VariableRef` yaprağı olduğundan **birebir aynı**
  sonuç döner (golden testlerle güvence).
- `EvaluateCondition`: tüm koşul tek `${...}` ise → motor boolean ifadeyi değerlendirir; değilse
  mevcut operatör-ayırma mantığı korunur (operandlar `ResolveOperand` → içi motorla değerlendirilen
  token'lar olabilir). Böylece `${a} == ${b}` ve `${Length(ad) > 3}` birlikte çalışır.
- `EvaluateValue`: tek `${...}` → motorun ham tipli sonucu; şablon → string; token yok → mevcut
  `ParseLiteral`.

> Sonuç: dıştaki token/şablon/koşul katmanı aynı; yalnız "token içeriğini çöz" adımı güçlenir.

---

## 4. Fonksiyon Kütüphanesi

Statik, kategorize `FunctionRegistry`. Her giriş: `Name`, `Category`, `Parameters` (ad+tip+opsiyonel),
`ReturnType`, `Description`, `Example`, ve `Func<IReadOnlyList<object?>, object?> Invoke`.

**Tarih (`Category="Tarih"`):**
`Now()` `Today()` `AddDays(d,n)` `AddMonths(d,n)` `AddYears(d,n)` `AddHours(d,n)` `AddMinutes(d,n)`
`Format(d,desen,[kültür])` `ToDate(s,[desen],[kültür])` `Year(d)` `Month(d)` `Day(d)`
`DayOfWeek(d)` `DateDiffDays(a,b)`

**String (`Category="Metin"`):**
`Upper(s)` `Lower(s)` `Trim(s)` `Length(s)` `Substring(s,bas,[uz])` `Replace(s,eski,yeni)`
`Contains(s,alt)` `StartsWith(s,ö)` `EndsWith(s,ö)` `IndexOf(s,alt)` `PadLeft(s,n,[ch])`
`PadRight(s,n,[ch])` `Concat(...)`

**Dönüşüm (`Category="Dönüşüm"`):**
`ToInt(x)` `ToDecimal(x,[kültür])` `ToDouble(x,[kültür])` `ToStr(x,[desen],[kültür])` `ToBool(x)`

**Yardımcı (`Category="Yardımcı"`):**
`Coalesce(a,b)` — a null/boş ise b.

Genişletilebilir: yeni fonksiyon = registry'ye bir giriş (runtime + autocomplete otomatik kazanır).

### Kültür kuralı
Varsayılan `tr-TR` (`CultureInfo("tr-TR")`). `Format/ToDate/ToDecimal/ToDouble/ToStr` opsiyonel son
`kültür` string argümanı alır (`"en-US"`, `"tr-TR"`, ...); geçersiz kültür → BusinessException.

---

## 5. Hata Yönetimi

Tümü **`BusinessException`** (kullanıcı-yazımı config; beklenen sınıf → Action Center, retry değil):
- Parse/söz dizimi hatası: `"İfade ayrıştırılamadı: <konum/sebep>"`.
- Bilinmeyen fonksiyon: `"Bilinmeyen fonksiyon: 'Formatt'"`.
- Argüman sayısı/tipi: `"Format 2-3 argüman alır, 1 verildi"`, `"ToInt: 'abc' sayıya çevrilemedi"`.
- Geçersiz kültür: `"Geçersiz kültür: 'xx-YY'"`.

Sınıflandırma runtime'da motordan fırlar; BaseRunner mevcut ExceptionClassifier ile Business işler.

---

## 6. Fonksiyon Kataloğu API'si

**`GET /api/expression/functions`** → `FunctionMetadataDto[]`:
```json
{ "name": "Format", "category": "Tarih", "returnType": "string",
  "parameters": [ {"name":"d","type":"date","optional":false},
                  {"name":"desen","type":"string","optional":false},
                  {"name":"kültür","type":"string","optional":true} ],
  "description": "Tarihi verilen desene göre biçimler.",
  "example": "Format(Now(), \"dd.MM.yyyy\")" }
```
Kaynak: `FunctionRegistry` (backend tek kaynak). Application katmanında bir `IExpressionFunctionCatalog`
servis, WebAPI'de `ExpressionController`. Studio bir kez çekip cache'ler.

---

## 7. Studio — Satır İçi Autocomplete

`expression-input.component`'e IDE tarzı öneri katmanı:
- **Tetikleme:** yazdıkça, imleç altındaki kısmi kelimeye göre (bir `${...}` token'ı içindeyken).
  Öneri kaynakları: **workflow değişkenleri** (mevcut `variables` Input'u) + **fonksiyon kataloğu**
  (yeni `ExpressionFunctionService` API'den).
- **Liste:** ad + kategori rozeti + imza/açıklama ipucu; kısmi kelimeyle filtre (case-insensitive).
- **Klavye:** ↑↓ gezinme, Enter/Tab seç, Esc kapat.
- **Ekleme:** değişken → `{{ad}}` (mevcut davranış); fonksiyon → `Ad()` ve imleç parantez içine
  konumlanır (ilk argümana hazır).
- Mevcut değişken picker düğmesi korunur (ayrı erişim); autocomplete onu tamamlayıcıdır.
- Genişletilmiş editör (`openEditor`) modunda da aynı öneri katmanı çalışır.

Yeni servis `ExpressionFunctionService` (Angular) — `GET /api/expression/functions` çağrısı + cache
+ filtre yardımcıları. i18n: kategori adları ve ipucu metinleri.

---

## 8. Test Stratejisi

**Infrastructure (motor):**
- Tokenizer/parser birim testleri: iç içe (`Format(AddDays(Now(),7),"dd.MM.yyyy")`), aritmetik önceliği
  (`ToInt(x)*2+1`), string birleştirme, karşılaştırma.
- Her fonksiyon için davranış testi (tr-TR + opsiyonel kültür argümanı).
- Hata sınıfı testleri: parse hatası / bilinmeyen fonksiyon / argüman uyumsuzluğu / kültür → Business.
- **Geriye uyum golden testleri:** mevcut `${var}`, `{{var}}`, `${data.alan}`, `${a} == ${b}`,
  şablon string, literal senaryoları — sonuçlar değişmeden geçer. (Mevcut ExpressionEvaluator testleri
  regresyon güvencesidir.)

**WebAPI:** `GET /api/expression/functions` katalog döner (kategori/imza alanları dolu).

**Studio:** autocomplete filtre (kısmi kelime), klavye seçimi, fonksiyon/değişken ekleme (parantez +
imleç), API cache; `ExpressionFunctionService` mock'lu.

---

## 9. Kapsam Dışı / Notlar (YAGNI)

- Metot zinciri (`deger.ToUpper()`) — kapsam dışı (karar: `Fonksiyon(arg)`).
- Koleksiyon/dizi fonksiyonları (`Split`→dizi, `Join`, `Map`) — v1'de skaler-dönüşlü fonksiyonlar;
  dizi dönüş tipi ayrı iş.
- Tasarım-zamanı sunucu-taraflı ifade doğrulama (canlı hata altı çizme) — v1'de yalnız autocomplete;
  doğrulama runtime + net Business mesajı. İleride opsiyonel `POST /api/expression/validate`.
- Roslyn kullanılmaz (System.InvokeCode ayrı, sandbox'sız/güvenilir-tasarımcı); ifade motoru hafif ve
  yan-etkisiz (yalnız saf fonksiyonlar + değişken okuma).
