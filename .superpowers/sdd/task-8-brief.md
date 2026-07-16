## Task 8: Studio — satır içi autocomplete (`expression-input`)

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.ts` (+`.html`,`.scss`)
- Test: `src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.spec.ts` (mevcut olabilir — genişlet; yoksa oluştur)

**Interfaces:**
- Consumes: `ExpressionFunctionService` (Task 7); mevcut `variables: WorkflowVariable[]` Input'u; mevcut `value`/`applyValue` altyapısı.
- Produces: bileşende autocomplete durumu + davranışı:
  - `suggestionsOpen: boolean`, `suggestions: AutocompleteItem[]`, `activeIndex: number`.
  - `type AutocompleteItem = { kind: 'variable' | 'function'; label: string; detail: string; insert: string }`.
  - `updateSuggestions(caretText: string)`, `applySuggestion(item)`, `onKeydown(event)` (↑↓/Enter/Tab/Esc).

- [ ] **Step 1: Autocomplete davranış testini yaz (FAIL)**

`expression-input.component.spec.ts`'e ekle (mevcut spec varsa; yoksa bu dosyayı oluştur; bileşeni `new` ile veya TestBed ile kur — mevcut spec hangisini kullanıyorsa onu izle). Servisi mock'la:

```typescript
import { ExpressionInputComponent } from './expression-input.component';
import { ExpressionFunctionInfo } from '../../../shared/services/expression-function.service';

function fnInfo(name: string, category: string): ExpressionFunctionInfo {
  return { name, category, returnType: 'string', parameters: [], description: '', example: `${name}()` };
}

describe('ExpressionInputComponent autocomplete', () => {
  let component: ExpressionInputComponent;
  const fnService = {
    load: () => ({ subscribe: () => undefined }),
    filter: (prefix: string) =>
      [fnInfo('Format', 'Tarih'), fnInfo('Upper', 'Metin')].filter((f) =>
        f.name.toLowerCase().startsWith(prefix.toLowerCase()),
      ),
  };

  beforeEach(() => {
    component = new ExpressionInputComponent(fnService as never);
    component.variables = [{ name: 'ad', type: 'string' } as never];
  });

  it('suggests matching functions and variables for a partial word', () => {
    component.updateSuggestions('Up');
    expect(component.suggestions.some((s) => s.kind === 'function' && s.label === 'Upper')).toBe(true);
    expect(component.suggestionsOpen).toBe(true);
  });

  it('suggests variables by partial name', () => {
    component.updateSuggestions('a');
    expect(component.suggestions.some((s) => s.kind === 'variable' && s.label === 'ad')).toBe(true);
  });

  it('inserting a function replaces the trailing partial word with Name()', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe((v) => emitted.push(v));
    // Kullanıcı "x = Up" yazdı; öneri son kelime "Up"a göre açıldı.
    component.value = 'x = Up';
    component.updateSuggestions('Up');
    const upper = component.suggestions.find((s) => s.label === 'Upper')!;
    component.applySuggestion(upper);
    // "Up" silinip "Upper()" ile değişmeli → "x = Upper()" (UpUpper() DEĞİL).
    expect(emitted[emitted.length - 1]).toBe('x = Upper()');
  });

  it('Escape closes the suggestion list', () => {
    component.updateSuggestions('Up');
    component.onKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(component.suggestionsOpen).toBe(false);
  });
});
```

> Not: mevcut ctor imzası parametresizdir (`inject(ChangeDetectorRef)`). Bileşene `ExpressionFunctionService`'i **constructor injection** ile ekle (test `new` kullanıyorsa param olarak geçilebilir) VEYA `inject()` ile alıp testte TestBed kur. Mevcut spec dosyasının kurulum stilini (bare-new vs TestBed) izle; `cdr` zaten `inject` ile alınıyorsa `ExpressionFunctionService`'i de `inject` ile al ve testi TestBed'e çevir. Tutarlılık için mevcut spec stilini koru.

- [ ] **Step 2: Testi çalıştır (FAIL)**

Run: `cd src/RPA.Studio && npx ng test --watch=false --include='**/expression-input.component.spec.ts'`
Expected: FAIL — autocomplete üyeleri yok.

- [ ] **Step 3: Bileşene autocomplete ekle**

`expression-input.component.ts`'e ekle (mevcut alanları/metotları koruyarak):

1. Import + servis:
```typescript
import { ExpressionFunctionService, ExpressionFunctionInfo } from '../../../shared/services/expression-function.service';
```
2. Alanlar (sınıf gövdesine):
```typescript
  private readonly fnService = inject(ExpressionFunctionService);

  suggestionsOpen = false;
  activeIndex = 0;
  suggestions: AutocompleteItem[] = [];
  private currentPartial = '';

  ngOnInit(): void {
    this.fnService.load().subscribe();
  }
```
> `implements ControlValueAccessor` yanına `OnInit` ekle; `import { OnInit } from '@angular/core'`.
3. Tip (dosya sonuna, sınıf dışına):
```typescript
export interface AutocompleteItem {
  kind: 'variable' | 'function';
  label: string;
  detail: string;
  insert: string;
  caretOffsetFromEnd: number; // eklenen metnin sonundan imleç kaç karakter geri
}
```
4. Öneri üretimi + uygulama + klavye:
```typescript
  /** İmleç altındaki kısmi kelimeye göre değişken + fonksiyon önerilerini hesaplar. */
  updateSuggestions(partial: string): void {
    const q = (partial ?? '').trim();
    this.currentPartial = q;
    const vars: AutocompleteItem[] = (this.variables ?? [])
      .filter((v) => v.name.toLowerCase().startsWith(q.toLowerCase()))
      .map((v) => ({ kind: 'variable', label: v.name, detail: v.type ?? 'değişken', insert: `{{${v.name}}}`, caretOffsetFromEnd: 0 }));
    const fns: AutocompleteItem[] = this.fnService
      .filter(q)
      .map((f: ExpressionFunctionInfo) => ({
        kind: 'function',
        label: f.name,
        detail: `${f.category} · ${this.signature(f)}`,
        insert: `${f.name}()`,
        caretOffsetFromEnd: 1, // parantez içine konumlan
      }));
    this.suggestions = [...vars, ...fns];
    this.activeIndex = 0;
    this.suggestionsOpen = this.suggestions.length > 0 && q.length > 0;
    this.cdr.markForCheck();
  }

  applySuggestion(item: AutocompleteItem): void {
    // İmleç sonundaki kısmi kelimeyi (currentPartial) öneriyle değiştir; yoksa sona ekle.
    const base =
      this.currentPartial.length > 0 && this.value.endsWith(this.currentPartial)
        ? this.value.slice(0, this.value.length - this.currentPartial.length)
        : this.value;
    this.applyValue(`${base}${item.insert}`);
    this.suggestionsOpen = false;
    this.cdr.markForCheck();
  }

  onKeydown(event: KeyboardEvent): void {
    if (!this.suggestionsOpen) { return; }
    switch (event.key) {
      case 'ArrowDown': event.preventDefault(); this.activeIndex = Math.min(this.activeIndex + 1, this.suggestions.length - 1); break;
      case 'ArrowUp': event.preventDefault(); this.activeIndex = Math.max(this.activeIndex - 1, 0); break;
      case 'Enter':
      case 'Tab':
        if (this.suggestions[this.activeIndex]) { event.preventDefault(); this.applySuggestion(this.suggestions[this.activeIndex]); }
        break;
      case 'Escape': this.suggestionsOpen = false; break;
    }
    this.cdr.markForCheck();
  }

  private signature(f: ExpressionFunctionInfo): string {
    const ps = f.parameters.map((p) => (p.optional ? `[${p.name}]` : p.name)).join(', ');
    return `${f.name}(${ps})`;
  }
```
5. `handleInput`'u öneri güncellemesiyle bağla (mevcut gövdeye ekle):
```typescript
  handleInput(value: string): void {
    this.applyValue(value);
    this.clearVariableError();
    this.updateSuggestions(this.currentPartialWord(value));
  }

  /** İmleç sonundaki (son) kelime parçasını döndürür — basit v1: son harf öbeği. */
  private currentPartialWord(value: string): string {
    const m = /([A-Za-z_ğüşöçıİĞÜŞÖÇ][A-Za-z0-9_ğüşöçıİĞÜŞÖÇ]*)$/.exec(value ?? '');
    return m ? m[1] : '';
  }
```

- [ ] **Step 4: HTML — öneri listesi**

`expression-input.component.html`'de ana input'a `(keydown)="onKeydown($event)"` ekle ve input grubunun altına öneri paneli koy (mevcut değişken picker panelinin yanına):

```html
<ul class="suggestion-list" *ngIf="suggestionsOpen" role="listbox">
  <li
    *ngFor="let s of suggestions; let i = index"
    role="option"
    [class.active]="i === activeIndex"
    (mousedown)="applySuggestion(s)"
  >
    <span class="s-label">{{ s.label }}</span>
    <span class="s-kind" [class.fn]="s.kind === 'function'">{{ s.kind === 'function' ? 'ƒ' : '{}' }}</span>
    <span class="s-detail">{{ s.detail }}</span>
  </li>
</ul>
```
> `(mousedown)` kullan (blur'dan önce tetiklenir → seçim kaybolmaz).

- [ ] **Step 5: SCSS — öneri paneli stili**

`expression-input.component.scss`'e ekle:

```scss
.suggestion-list {
  position: absolute;
  z-index: 20;
  margin: 2px 0 0;
  padding: 4px 0;
  max-height: 220px;
  overflow-y: auto;
  min-width: 220px;
  background: var(--surface, #fff);
  border: 1px solid var(--border, #ccc);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
  list-style: none;

  li {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 10px;
    cursor: pointer;
    font-size: 13px;

    &.active,
    &:hover { background: var(--hover, #eef2ff); }

    .s-kind { font-family: monospace; opacity: 0.6; &.fn { color: #6d28d9; } }
    .s-detail { margin-left: auto; opacity: 0.65; font-size: 12px; }
  }
}
```
> Panelin doğru konumlanması için input sarmalayıcıya `position: relative` olduğundan emin ol (yoksa ekle).

- [ ] **Step 6: Testi çalıştır (PASS)**

Run: `cd src/RPA.Studio && npx ng test --watch=false --include='**/expression-input.component.spec.ts'`
Expected: PASS.

- [ ] **Step 7: Studio derleme**

Run: `cd src/RPA.Studio && npm run build`
Expected: BAŞARILI (yalnız önceden var olan SCSS budget uyarıları kabul).

- [ ] **Step 8: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.ts src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.html src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.scss src/RPA.Studio/src/app/studio/designer/properties/expression-input.component.spec.ts
git commit -m "feat(studio): expression-input satir-ici autocomplete

Degisken + fonksiyon onerileri (kismi kelime), imza/kategori ipucu, ok/Enter/Tab/Esc,
fonksiyon secince Name() + imlec parantez ici. ExpressionFunctionService kaynagi.

Co-Authored-By: Claude Opus <noreply@anthropic.com>"
```

---

