# Desktop.SendKeys — Yapısal Tuş Editörü Tasarımı

**Tarih:** 2026-07-18
**Kapsam:** `Desktop.SendKeys` aktivitesinin gerçek modifier/özel tuş desteği + Studio yapısal editörü
**Etkilenen paketler:** Domain (paylaşımlı DTO/parser), Infrastructure (Desktop aktiviteleri), Agent
(FlaUI kanal implementasyonu), Studio (yapısal editör).

---

## Problem

Mevcut `Desktop.SendKeys` aktivitesi (`src/RPA.Infrastructure/Activities/Desktop/DesktopActivities.cs`)
tek bir `keys` string alanı sunar ve metadata örneğinde `'^s'` (Ctrl+S) sözdizimini ima eder. Ancak
implementasyon (`FlaUiDesktopAutomationChannel.SendKeysAsync`) altta yalnızca `Keyboard.Type(keys)`
çağırır; FlaUI bu string'i **düz metin** olarak yazar ve `^s`'yi Ctrl+S diye yorumlamaz. Sonuç:

- Metin girişi çalışır (`09.07.2026`).
- `Ctrl+A`, `F4`, `Home`, `PageDown`, `Windows` gibi modifier/özel tuşlar **çalışmaz** (metadata yanıltıcı).

Kullanıcı; F1–F12, Ctrl/Shift/Alt/AltGr/Win, Home/End/PageUp/PageDown/Windows tuşlarını seçebildiği,
düz metin de girebildiği (örn. `Ctrl+A` ile tümünü seç) **yapısal bir editör** istiyor.

---

## Genel Yaklaşım

Menülerde odak kaybını önlemek için `Vision.ClickSequence`'te uygulanan **tek node / sıralı adım**
deseni burada da kullanılır: tek `Desktop.SendKeys` node'u içinde birden çok adım sırayla çalışır,
node'lar arası odak kaybı olmaz. Adım tipleri: **tuş vuruşu (chord)** ve **metin**.

`IDesktopAutomationChannel.SendKeysAsync(string? selector, string keys)` **mevcut imzası korunur**
(kontrat sabit); `keys` artık JSON adım dizisi taşır. Parse tek yerde (paylaşımlı DTO/parser) yapılır;
kanala tipli liste iletmek için opsiyonel bir overload eklenir.

---

## Bölüm 1 — Veri Modeli & Serileştirme

`keys` parametresi JSON adım dizisi taşır:

```jsonc
[
  { "type": "chord", "modifiers": ["ctrl"], "key": "A", "waitMs": 0 },
  { "type": "text",  "text": "09.07.2026",           "waitMs": 100 },
  { "type": "chord", "modifiers": [], "key": "Enter", "waitMs": 0 }
]
```

**Adım alanları:**

| Alan | Anlam | Kural |
|------|-------|-------|
| `type` | `chord` \| `text` | Zorunlu |
| `modifiers` | `ctrl`, `shift`, `alt`, `altgr`, `win` alt kümesi | Yalnız `chord`; çoklu seçilebilir |
| `key` | Tek ana tuş (palet) | `chord` için zorunlu |
| `text` | Düz metin | `text` için zorunlu (boş olamaz) |
| `waitMs` | Adımdan sonra bekleme (ms) | Opsiyonel, ≥0; varsayılan 0 |

**Ana tuş paleti:**

- **Harf/Rakam:** A–Z, 0–9
- **Fonksiyon:** F1–F12
- **Gezinme:** Home, End, PageUp, PageDown, Up, Down, Left, Right, Tab, Enter, Esc, Space,
  Backspace, Delete, Insert

**Modifier değerleri:** `ctrl`, `shift`, `alt`, `altgr`, `win`.

**Geriye uyumluluk:** `keys` değeri geçerli JSON dizi **değilse** tek bir `text` adımı olarak
yorumlanır. Böylece mevcut workflow'ların düz-metin `keys` değerleri bozulmaz.

---

## Bölüm 2 — Kanal & Runtime Davranışı

### Paylaşımlı DTO + parser

Domain/Infrastructure'da paylaşımlı `KeystrokeStep` DTO'su ve parser'ı tanımlanır (parse tek yerde):

- Parser JSON diziyi `KeystrokeStep` listesine çevirir; JSON değilse tek `text` adımı döndürür.
- Doğrulama: tanınmayan tuş, boş chord (`key` yok), geçersiz modifier, boş metin → hata.

### Aktivite katmanı (`DesktopActivities.cs`, platform-nötr)

- `keys` parse edilir ve doğrulanır.
- Doğrulama hatası → **`BusinessException`** (kullanıcı girdi hatası).
- Ayrıştırılmış tipli adım listesi kanala iletilir (yeni overload).

### Kanal implementasyonu (`FlaUiDesktopAutomationChannel`, Windows/FlaUI)

- Selector doluysa hedef `Focus()` (mevcut davranış).
- Her adım tipine göre:
  - **text** → `Keyboard.Type(text)` (mevcut düz-metin davranışı).
  - **chord** → modifier'lar `Keyboard.Pressing(...)` ile basılı tutulur, ana tuş
    `Keyboard.Type(VirtualKeyShort)` ile gönderilir, sonra ters sırada bırakılır (`using`/`try-finally`
    ile bırakma garanti). Eşleme:
    - `ctrl` → `CONTROL`, `shift` → `SHIFT`, `alt` → `MENU` (LMENU), `altgr` → `RMENU`,
      `win` → `LWIN`.
    - Ana tuş palet adı → `VirtualKeyShort` (A–Z, KEY_0–KEY_9, F1–F12, HOME, END, PRIOR, NEXT,
      UP/DOWN/LEFT/RIGHT, TAB, ENTER/RETURN, ESCAPE, SPACE, BACK, DELETE, INSERT).
- Her adım sonrası `waitMs > 0` ise `Task.Delay(waitMs)`.
- Element bulunamama/COM hatası → **`SystemException`** (mevcut sınıflandırma korunur).

### İmza kararı

`SendKeysAsync(string? selector, string keys)` **imzası korunur** (kontrat sabit; eski çağıranlar
etkilenmez). Kanala tipli adım listesi iletmek için **opsiyonel overload** eklenir
(`SendKeysAsync(string? selector, IReadOnlyList<KeystrokeStep> steps)`). Aktivite parse edip tipli
overload'ı çağırır. `UnavailableDesktopAutomationChannel` yeni overload'ı da uygular (mevcut deseniyle
`SystemException`/no-op).

---

## Bölüm 3 — Studio Yapısal Editör (UI)

Yeni bileşen **`KeystrokeSequenceEditorComponent`** (`vision-sequence-editor` deseni birebir), yeni
`pickerKind` değeri **`"keystroke-sequence"`** ile `generic-property` içinde render edilir.
`Desktop.SendKeys`'in `keys` parametresi katalogda bu `pickerKind`'ı alır — bu bir **spy türü değil**,
yalnız editör ipucudur (`selector-picker-button`'a null geçer, vision-sequence gibi).

**Editör düzeni** — adım listesi; her adımda:

- **Tip seçici:** `Tuş vuruşu` / `Metin`.
- **Tuş vuruşu** seçiliyse:
  - Modifier'lar: 5 checkbox — `Ctrl` `Shift` `Alt` `AltGr` `Win`.
  - Ana tuş: gruplu dropdown (Harf/Rakam, Fonksiyon F1–F12, Gezinme).
  - Canlı önizleme etiketi: örn. `Ctrl + Shift + End`.
- **Metin** seçiliyse: metin kutusu.
- **Sonraki bekleme (ms):** sayısal alan (opsiyonel).
- Adım işlemleri: **ekle / sil / yukarı / aşağı** (vision-sequence deseni).

Değer, backend'in beklediği JSON adım dizisi olarak `valueChange` ile emit edilir. Eski düz-metin
değer yüklenirse tek `Metin` adımı olarak parse edilir.

**i18n:** yeni anahtarlar — `keystroke.stepType/chord/text/modifiers/ctrl/shift/alt/altgr/win/
mainKey/keyGroupLetters/keyGroupFunction/keyGroupNavigation/addStep/waitMs/preview` (TR + mevcut
diğer diller).

---

## Test Stratejisi

- **Domain/Infrastructure parser + aktivite** (`RPA.Infrastructure.Tests`, `IDesktopAutomationChannel`
  mock):
  - JSON ayrıştırma (chord + text + waitMs).
  - Geriye uyumluluk: JSON olmayan `keys` → tek text adımı.
  - Doğrulama hataları → `BusinessException` (tanınmayan tuş, boş chord, geçersiz modifier, boş metin).
  - Adım sırası ve içeriği: mock'a giden tipli liste doğrulanır.
- **FlaUI kanalı:** gerçek tuş basımı (STA/COM) birim testi kapsamı dışı (mevcut desktop testleriyle
  tutarlı). `VirtualKeyShort` eşleme tablosu için saf eşleme fonksiyonu birim testi yazılabilir.
- **Studio** (`KeystrokeSequenceEditorComponent` spec): adım ekle/sil/sırala, tip değişimi, modifier
  toggling, JSON emit, eski değer parse (vision-sequence spec deseni).

---

## Kapsam Dışı (YAGNI)

- Modifier'ı basılı tutup birden çok tuş gönderme (her chord kendi modifier'ını basıp bırakır).
- Ayrı basılı-tut/bırak adımları.
- SAP/Web kanallarında tuş gönderme (yalnız Desktop kanalı).

---

## Kontrat Notu

`IDesktopAutomationChannel.SendKeysAsync`'e tipli overload eklenmesi kontrat genişlemesidir; mevcut
string imza korunduğundan geriye uyumludur. Uygulama tamamlandığında `AGENTS.md`'ye
`## Kontrat Değişikliği — 2026-07-18` kaydı eklenecektir.
