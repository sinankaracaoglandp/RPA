# Paket A — Tasarım Ekranını Kullanılır Hale Getirme: Implementasyon Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Studio tasarım ekranındaki canvas'ı temel düzeyde kullanılır yapmak: tıklayınca silinme bug'ı, seçim→özellik paneli akışı, mouse ile düğüm bağlama, metadata entegrasyonu (DefaultProperties + DisplayName) ve katalog kapsama testleri.

**Architecture:** Tüm değişiklikler Studio (Angular 22, standalone components, signals) içinde; tek istisna katalog kapsama testinin backend yarısı (xUnit, RPA.Infrastructure.Tests). Canvas Rete.js 2 üstünde manuel Angular köprüsü kullanıyor (render pipeline'ı bypass edilip `createComponent` ile node mount ediliyor; bağlantılar manuel SVG overlay'de çiziliyor) — bu desene sadık kalınır, Rete connection-plugin'in soket sözleşmesine **dönülmez**.

**Tech Stack:** Angular 22 (signals, standalone), Rete.js 2 (`rete`, `rete-area-plugin`), Vitest + jsdom (`ng test`), xUnit (.NET 10, `dotnet test`).

**Spec:** `docs/superpowers/specs/2026-07-06-studio-toparlanma-design.md` Bölüm 5 / Paket A.

## Global Constraints

- TDD zorunlu: her adım failing test → minimal impl → pass → commit (proje CLAUDE.md).
- Frontend test komutu: `cd src/RPA.Studio && npm test -- --watch=false` (tek dosya: `npm test -- --watch=false --include='**/canvas.component.spec.ts'`).
- Backend test komutu: `dotnet test tests/RPA.Infrastructure.Tests`.
- i18n: kullanıcıya görünen her yeni metin `src/RPA.Studio/src/assets/i18n/tr.json` **ve** `en.json`'a eklenir; şablonda `| translate` pipe kullanılır.
- Naming: mevcut BEM benzeri SCSS (`canvas-node__socket--out`), `data-testid` nitelikleri test seçicisi olarak.
- Commit mesajı gövdesi Türkçe, tip öneki İngilizce (`fix`, `feat`, `test`); footer:
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
- Kontrat dosyalarına (Domain arayüzleri/şema) dokunulmaz — bu pakette kontrat değişikliği yok.
- Backend'in çalışması gerekmez; tüm frontend testleri `provideHttpClientTesting` ile mock'lanır.

---

### Task 1: Bug — node'a tıklayınca silinme (repro testi + kök neden + düzeltme)

**Files:**
- Test: `src/RPA.Studio/src/app/studio/designer/canvas/canvas.component.spec.ts` (mevcut dosyaya test ekle)
- Modify (kök nedene göre biri/birkaçı): `src/RPA.Studio/src/app/studio/designer/canvas/canvas.component.html:55-64`, `canvas.component.ts:250-270`, `node.component.html`

**Interfaces:**
- Produces: davranış garantisi — node kartına tıklamak node'u SİLMEZ, yalnız seçer (`nodeSelect` emit). Task 2 ve 4 bu garantiye dayanır.

Bu bir hata ayıklama görevi: önce repro testi yazılır, kök neden **superpowers:systematic-debugging** ile doğrulanır, düzeltme repro'ya göre yapılır (tahmine göre değil).

- [ ] **Step 1: Repro/regression testini yaz**

`canvas.component.spec.ts` dosyasının en altındaki `describe` bloğuna ekle:

```typescript
describe('node click behaviour (regression: click must not delete)', () => {
  it('keeps the node in the graph and emits nodeSelect when the card is clicked', async () => {
    await ready();
    const id = await component.addNode('Web.Click');
    fixture.detectChanges();

    const selections: (string | null)[] = [];
    component.nodeSelect.subscribe((v) => selections.push(v));

    const card: HTMLElement | null =
      fixture.nativeElement.querySelector('[data-testid="canvas-node"]');
    expect(card).toBeTruthy();

    // Gerçek kullanıcı tıklaması: pointerdown → pointerup → click sırası.
    card!.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));
    card!.dispatchEvent(new MouseEvent('pointerup', { bubbles: true }));
    card!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(component.editor.getNodes().length).toBe(1); // SİLİNMEMELİ
    expect(component.editor.getNode(id)).toBeDefined();
    expect(selections).toContain(id);
  });

  it('keeps the node DOM card rendered after click (no visual disappearance)', async () => {
    await ready();
    await component.addNode('Web.Click');
    fixture.detectChanges();

    const card: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="canvas-node"]');
    card.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    await new Promise((r) => setTimeout(r, 0)); // async destroy/mount kuyruğunu boşalt

    const after = fixture.nativeElement.querySelectorAll('[data-testid="canvas-node"]');
    expect(after.length).toBe(1);
    expect(after[0].querySelector('[data-testid="canvas-node-title"]')).toBeTruthy();
  });

  it('deletes the node ONLY via the delete button', async () => {
    await ready();
    const id = await component.addNode('Web.Click');
    fixture.detectChanges();

    const del: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="canvas-node-delete"]');
    del.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    await new Promise((r) => setTimeout(r, 0));

    expect(component.editor.getNode(id)).toBeUndefined();
    expect(component.editor.getNodes().length).toBe(0);
  });
});
```

- [ ] **Step 2: Testleri çalıştır, hangilerinin FAIL olduğunu gözlemle**

Run: `cd src/RPA.Studio && npm test -- --watch=false --include='**/canvas.component.spec.ts'`

Beklenti: en az bir yeni test FAIL (kullanıcı repro'su: tıklayınca silinme). **Üçü de PASS olursa** bug jsdom'da repro olmuyor demektir — o durumda tarayıcıda repro adımına geç (Step 3) ve bulguyu teste geri taşı.

- [ ] **Step 3: Kök nedeni doğrula (systematic-debugging)**

Sıralı hipotez listesi — her biri için doğrulama yöntemi:

| # | Hipotez | Doğrulama |
|---|---------|-----------|
| H1 | `canvas.component.html:62-63`'teki `(keydown.delete)/(keydown.backspace)` container'da; node kartı `tabindex="0"` olduğundan tıklama sonrası odak/klavye etkileşimi delete'i tetikliyor | Handler'ları geçici yorum satırı yap → repro kayboluyorsa H1 |
| H2 | `canvas.component.ts` `mountNode` (satır ~250): Rete `nodepicked` sonrası node'u yeniden render ediyor; aynı host element'e ikinci Angular bileşeni mount edilip eski `ref.destroy()` çağrısı YENİ görünümün DOM'unu da temizliyor → kart görsel olarak kayboluyor ("silinmiş" görünüyor, graph'ta duruyor) | Step 1'in 2. testi bunu ayırt eder: `editor.getNodes().length===1` ama DOM'da kart yok/boş ise H2 |
| H3 | ✕ silme butonunun tıklama alanı CSS ile kartın genişine taşıyor | jsdom'da `del.getBoundingClientRect` anlamsız; tarayıcıda DevTools ile buton hover alanını kontrol et |
| H4 | Rete area'nın pointer yakalaması ile Angular `(click)` çakışması `nodeDelete` emit'ine sızıyor | `mountNode` içindeki `nodeDelete.subscribe` callback'ine geçici `console.trace()` koy; tıklamada tetikleniyorsa çağrı zincirini oku |

Tarayıcı repro'su gerekiyorsa: `cd src/RPA.Studio && npm start` → login → Studio → Designer; toolbox'tan bir aktivite bırak, kart gövdesine tıkla.

- [ ] **Step 4: Minimal düzeltmeyi uygula**

Kök nedene karşılık gelen düzeltme (yalnız doğrulananı uygula):

**H1 ise** — klavye silme davranışını koru ama yalnız container odaktayken ve seçim varken çalışsın; olayın node kartından kabarcıklanmasını engelle. `node.component.html` kök div'ine ekle:

```html
(keydown.delete)="$event.stopPropagation()"
(keydown.backspace)="$event.stopPropagation()"
```

**H2 ise** — `canvas.component.ts` `mountNode` içinde eski ref'i YENİ bileşen yaratılmadan ÖNCE yok et ve aynı element için mükerrer mount'u engelle:

```typescript
private mountNode(data: { element: HTMLElement; type: string; payload: unknown }): void {
  if (data?.type !== 'node') {
    return;
  }
  const node = data.payload as FlowNode;
  try {
    // Aynı node yeniden render ediliyorsa önce eski görünümü kaldır —
    // yeni bileşen mount edildikten SONRA destroy etmek yeni DOM'u da siler.
    const existing = this.nodeRefs.get(node.id);
    if (existing) {
      if (existing.location.nativeElement === data.element) {
        // Aynı element, bileşen zaten canlı: sadece girdiyi tazele.
        existing.setInput('node', this.toView(node));
        existing.changeDetectorRef.detectChanges();
        return;
      }
      existing.destroy();
      this.nodeRefs.delete(node.id);
    }
    const ref = createComponent(NodeComponent, {
      environmentInjector: this.envInjector,
      hostElement: data.element,
    });
    ref.setInput('node', this.toView(node));
    ref.instance.nodeSelect.subscribe((id: string) => this.select(id));
    ref.instance.nodeDelete.subscribe((id: string) => void this.deleteNode(id));
    this.appRef.attachView(ref.hostView);
    ref.changeDetectorRef.detectChanges();
    this.nodeRefs.set(node.id, ref);
  } catch {
    // Headless/edge rendering failures must never corrupt the graph model.
  }
}
```

**H3 ise** — `node.component.scss` `&__delete` bloğuna sınır koy (`flex: 0 0 auto` zaten var; taşma `padding`/pseudo-element kaynaklıysa onu daralt).

**H4 ise** — `node.component.html` ✕ butonuna `(pointerdown)="$event.stopPropagation()"` ekle ve kartın `(click)`'ini `(pointerup)` tabanlı seçime çevirme; yalnız yayılım kesilir.

- [ ] **Step 5: Testleri çalıştır — üçü de PASS**

Run: `cd src/RPA.Studio && npm test -- --watch=false --include='**/canvas.component.spec.ts'`
Expected: PASS (mevcut eski testler dahil hiçbiri kırılmamış olmalı).

- [ ] **Step 6: Tarayıcıda elle doğrula**

`npm start` → Designer'da: aktivite bırak → kart gövdesine tıkla (seçilir, silinmez) → ✕'e tıkla (silinir) → kart seçiliyken `Delete` tuşu (silinir — mevcut davranış korunur).

- [ ] **Step 7: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/canvas/
git commit -m "fix(studio): node'a tıklama artık silmiyor, yalnız seçiyor

Repro/regression testleri eklendi (tıkla→seç, ✕→sil, DOM kartı kaybolmaz).
Kök neden: <doğrulanan hipotezi buraya yaz>.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Seçim → özellik paneli veri akışı (ViewChild bağımlılığını kaldır)

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/properties-panel.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/properties-panel.component.html` (değişmez — girişler aynı adla kalır; kontrol et)
- Modify: `src/RPA.Studio/src/app/studio/designer/designer.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/designer.component.html:37`
- Test: `src/RPA.Studio/src/app/studio/designer/properties/properties-panel.component.spec.ts` (varsa güncelle, yoksa oluştur), `src/RPA.Studio/src/app/studio/designer/designer.component.spec.ts` (varsa güncelle)

**Interfaces:**
- Consumes: `CanvasComponent.getNodeActivityId(id)`, `getNodeProperties(id)`, `updateNodeProperties(id, props)` (mevcut, `canvas.component.ts:466-483`); Task 1'in "tıkla→seç" garantisi.
- Produces: `PropertiesPanelComponent` yeni API — `@Input() activityType?: string`, `@Input() properties: Record<string, unknown>`, `@Output() propertiesChange`. **Canvas'a referansı kalmaz.** DesignerComponent yeni sinyaller: `selectedActivityType()`, `selectedProperties()`. Task 3-4 designer'daki bu akışı bozmamalı.

**Neden:** Panel şu an `@ViewChild` canvas referansını template binding ile alıyor (`[canvas]="canvas"`) — ilk change detection'da `undefined`, getter'lar canvas'sız boş dönüyor; panel açılmama bug'ının birincil şüphelisi. Paneli saf veri bileşenine çevirmek hem bug'ı deterministik biçimde kapatır hem test edilebilirliği artırır (spec A.1, tercih edilen çözüm).

- [ ] **Step 1: Failing test — panel saf girdilerle çalışmalı**

`properties-panel.component.spec.ts` (yeni dosya ya da mevcutun üzerine):

```typescript
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PropertiesPanelComponent } from './properties-panel.component';

describe('PropertiesPanelComponent', () => {
  let fixture: ComponentFixture<PropertiesPanelComponent>;
  let component: PropertiesPanelComponent;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PropertiesPanelComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(PropertiesPanelComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  it('shows the empty state when no activity is selected', () => {
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[data-testid="properties-panel-empty"]'),
    ).toBeTruthy();
  });

  it('renders the generic editor form for a non-web activity from plain inputs', () => {
    component.activityType = 'Sap.Gui.Click';
    component.properties = { elementId: 'wnd[0]/usr/btn' };
    fixture.detectChanges();

    // GenericPropertyComponent metadata'yı katalogdan çeker.
    const req = http.expectOne('/api/activities/Sap.Gui.Click');
    req.flush({
      activityId: 'Sap.Gui.Click',
      displayName: 'SAP GUI Tıkla',
      inputs: [{ name: 'elementId', type: 'string', required: true }],
    });
    fixture.detectChanges();

    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('[data-testid="prop-elementId"]');
    expect(input).toBeTruthy();
    expect(input.value).toBe('wnd[0]/usr/btn');
  });

  it('emits propertiesChange when a field is edited', () => {
    component.activityType = 'Sap.Gui.Click';
    component.properties = {};
    fixture.detectChanges();
    http.expectOne('/api/activities/Sap.Gui.Click').flush({
      activityId: 'Sap.Gui.Click',
      displayName: 'SAP GUI Tıkla',
      inputs: [{ name: 'elementId', type: 'string', required: true }],
    });
    fixture.detectChanges();

    const emitted: Record<string, unknown>[] = [];
    component.propertiesChange.subscribe((v) => emitted.push(v));

    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('[data-testid="prop-elementId"]');
    input.value = 'wnd[0]/usr/txtNew';
    input.dispatchEvent(new Event('input', { bubbles: true }));

    expect(emitted).toEqual([{ elementId: 'wnd[0]/usr/txtNew' }]);
  });
});
```

- [ ] **Step 2: Çalıştır — FAIL gözle**

Run: `cd src/RPA.Studio && npm test -- --watch=false --include='**/properties-panel.component.spec.ts'`
Expected: FAIL — `activityType` diye bir `@Input` yok (mevcut bileşen `canvas` + `selectedNodeId` alıyor).

- [ ] **Step 3: PropertiesPanelComponent'i saf veri bileşenine çevir**

`properties-panel.component.ts` — tam yeni içerik:

```typescript
import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslatePipe } from '../../../core/translate.pipe';
import { WebPropertyRouterComponent, isWebActivityType } from './web-property-router.component';
import { GenericPropertyComponent } from './generic-property.component';

/**
 * Properties panel shown alongside the canvas (Faz 5 Task 5.6). Seçili node'un
 * aktivite tipi ve özellikleri designer tarafından DÜZ VERİ olarak verilir —
 * panel canvas'a referans tutmaz (Paket A: ViewChild bağlama bug'ı düzeltmesi).
 */
@Component({
  selector: 'app-properties-panel',
  standalone: true,
  imports: [CommonModule, TranslatePipe, WebPropertyRouterComponent, GenericPropertyComponent],
  templateUrl: './properties-panel.component.html',
})
export class PropertiesPanelComponent {
  @Input() activityType?: string;
  @Input() properties: Record<string, unknown> = {};
  @Output() readonly propertiesChange = new EventEmitter<Record<string, unknown>>();

  get isWebActivity(): boolean {
    return isWebActivityType(this.activityType);
  }

  onPropertiesChange(value: Record<string, unknown>): void {
    this.propertiesChange.emit(value);
  }
}
```

`properties-panel.component.html` — boş-durum koşulunu `selectedNodeId` yerine `activityType`'a bağla (satır 6):

```html
  @if (!activityType) {
```

(dosyanın kalanı aynen kalır — `[activityType]`, `[properties]`, `(propertiesChange)` bağlamaları zaten bu adlarla).

- [ ] **Step 4: DesignerComponent'i seçim verisini taşıyacak şekilde güncelle**

`designer.component.ts` — sınıfa sinyaller ve handler'lar (mevcut `selectedNodeId` kalır):

```typescript
  readonly selectedActivityType = signal<string | undefined>(undefined);
  readonly selectedProperties = signal<Record<string, unknown>>({});

  onNodeSelect(nodeId: string | null): void {
    this.selectedNodeId.set(nodeId);
    if (nodeId && this.canvas) {
      this.selectedActivityType.set(this.canvas.getNodeActivityId(nodeId));
      this.selectedProperties.set(this.canvas.getNodeProperties(nodeId));
    } else {
      this.selectedActivityType.set(undefined);
      this.selectedProperties.set({});
    }
    // In debug mode (Advanced only), clicking a node toggles its breakpoint.
    if (!this.isSimpleMode() && this.debugMode() && nodeId) {
      this.debug.toggleBreakpoint(nodeId);
    }
  }

  onPropertiesChange(properties: Record<string, unknown>): void {
    const nodeId = this.selectedNodeId();
    if (nodeId) {
      this.canvas?.updateNodeProperties(nodeId, properties);
      this.selectedProperties.set(properties);
    }
  }
```

`designer.component.html:37` — panel bağlamasını değiştir:

```html
    <app-properties-panel
      [activityType]="selectedActivityType()"
      [properties]="selectedProperties()"
      (propertiesChange)="onPropertiesChange($event)"
    ></app-properties-panel>
```

- [ ] **Step 5: Tüm frontend testlerini çalıştır**

Run: `cd src/RPA.Studio && npm test -- --watch=false`
Expected: PASS. Mevcut designer/properties spec'lerinde eski `[canvas]` API'sine referans varsa aynı desene (düz girdi) güncelle — davranış iddiaları değişmez, yalnız kurulum değişir.

- [ ] **Step 6: Tarayıcıda elle doğrula**

Aktivite bırak → tıkla → sağ panelde form alanları görünür; alanı değiştir → başka node seç → geri dön → değer korunmuş.

- [ ] **Step 7: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/
git commit -m "fix(studio): özellik paneli seçimde artık açılıyor — ViewChild bağımlılığı kaldırıldı

Panel saf veri bileşenine çevrildi (activityType/properties @Input,
propertiesChange @Output); designer seçim verisini sinyallerle taşıyor.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Node oluşturmada DefaultProperties + DisplayName

**Files:**
- Modify: `src/RPA.Studio/src/app/shared/models/activity.model.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/canvas/canvas.component.ts:358-374` (`addNode`)
- Modify: `src/RPA.Studio/src/app/studio/designer/toolbox/toolbox.component.ts:100-105` (`addActivity`)
- Modify: `src/RPA.Studio/src/app/studio/simple-mode/simplified-toolbox.component.ts` (aynı çağrı deseni varsa — kontrol et)
- Test: `src/RPA.Studio/src/app/studio/designer/toolbox/toolbox.component.spec.ts`, `canvas.component.spec.ts`

**Interfaces:**
- Consumes: backend kataloğu `defaultProperties` alanını zaten serileştiriyor (Domain `ActivityMetadata.DefaultProperties` → camelCase JSON); `FlowNode` constructor'ı `properties` parametresini zaten alıyor (`canvas.component.ts:42-47`).
- Produces: `CanvasComponent.addNode(activityId, options)` options'a `properties?: Record<string, unknown>` eklenir. Toolbox `addNode`'u `label: meta.displayName, properties: {...meta.defaultProperties}` ile çağırır. Task 5 bu akışın testine dayanmaz (bağımsız).

- [ ] **Step 1: Failing testler**

`canvas.component.spec.ts`'e ekle:

```typescript
  it('creates a node with the provided initial properties and label', async () => {
    await ready();
    const id = await component.addNode('Logic.Delay', {
      label: 'Bekle',
      properties: { durationMs: 1000 },
    });
    const node = component.editor.getNode(id)!;
    expect(node.label).toBe('Bekle');
    expect(node.properties).toEqual({ durationMs: 1000 });
  });
```

`toolbox.component.spec.ts`'e ekle (mevcut spec kurulum desenini izle; katalog `HttpTestingController` ile flush ediliyor):

```typescript
  it('passes displayName and defaultProperties to the canvas when adding an activity', async () => {
    // Katalog cevabında defaultProperties'li bir aktivite olsun:
    // { activityId: 'Logic.Delay', displayName: 'Bekle', category: 'Mantık',
    //   defaultProperties: { durationMs: 1000 } }
    const canvas = {
      addNode: vi.fn().mockResolvedValue('node-1'),
    } as unknown as CanvasComponent;
    component.canvas = canvas;
    // ... (mevcut spec'teki katalog flush kurulumunu kullan)

    await component.addActivity('Logic.Delay');

    expect(canvas.addNode).toHaveBeenCalledWith('Logic.Delay', {
      label: 'Bekle',
      properties: { durationMs: 1000 },
    });
  });
```

- [ ] **Step 2: Çalıştır — FAIL gözle**

Run: `cd src/RPA.Studio && npm test -- --watch=false --include='**/{canvas,toolbox}.component.spec.ts'`
Expected: FAIL — `addNode` options'ta `properties` yok; toolbox düz `activityId` geçiyor.

- [ ] **Step 3: Modeli ve addNode'u genişlet**

`activity.model.ts` — `ActivityMetadata`'ya alan ekle:

```typescript
export interface ActivityMetadata {
  activityId: string;
  displayName: string;
  category?: string;
  description?: string;
  icon?: string;
  inputs?: ActivityPort[];
  outputs?: ActivityPort[];
  /** Katalogda tanımlı başlangıç özellik değerleri (node oluşturmada kopyalanır). */
  defaultProperties?: Record<string, unknown>;
}
```

`canvas.component.ts` `addNode` (satır 358):

```typescript
  async addNode(
    activityId: string,
    options: {
      type?: WorkflowNodeType;
      label?: string;
      position?: NodePosition;
      properties?: Record<string, unknown>;
    } = {},
  ): Promise<string> {
    this.assertWritable();
    const type = options.type ?? 'activity';
    const label = options.label ?? activityId;
    const node = new FlowNode(
      label,
      type,
      type === 'activity' ? activityId : undefined,
      { ...(options.properties ?? {}) },
    );
    // ... (kalanı aynen)
```

- [ ] **Step 4: Toolbox'ı metadata geçecek şekilde güncelle**

`toolbox.component.ts` `addActivity` (satır 100):

```typescript
  async addActivity(activityId: string, position?: { x: number; y: number }): Promise<void> {
    if (this.canvas) {
      const meta = this.activities().find((a) => a.activityId === activityId);
      await this.canvas.addNode(activityId, {
        ...(position ? { position } : {}),
        ...(meta?.displayName ? { label: meta.displayName } : {}),
        ...(meta?.defaultProperties ? { properties: { ...meta.defaultProperties } } : {}),
      });
    }
    this.activityAdded.emit({ activityId, position });
  }
```

`simplified-toolbox.component.ts`'i aç; `canvas.addNode` çağrısı varsa aynı deseni uygula (kendi katalog sinyalinden metadata bul). Yoksa dokunma.

- [ ] **Step 5: Testler PASS**

Run: `cd src/RPA.Studio && npm test -- --watch=false`
Expected: PASS.

- [ ] **Step 6: Tarayıcıda doğrula**

"Bekle" aktivitesini bırak → kart başlığı "Bekle" (Logic.Delay alt başlıkta) → panelde `durationMs` alanı 1000 ön-dolu.

- [ ] **Step 7: Commit**

```bash
git add src/RPA.Studio/src/app/shared/models/activity.model.ts src/RPA.Studio/src/app/studio/
git commit -m "feat(studio): node oluşturmada katalog DisplayName ve DefaultProperties kullanılıyor

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Mouse ile düğüm bağlama + bağlantı seçme/silme

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/canvas/node.component.ts`, `node.component.html`, `node.component.scss`
- Modify: `src/RPA.Studio/src/app/studio/designer/canvas/canvas.component.ts`, `canvas.component.html`, `canvas.component.scss`
- Modify: `src/RPA.Studio/src/assets/i18n/tr.json`, `src/RPA.Studio/src/assets/i18n/en.json`
- Test: `canvas.component.spec.ts`, `node.component.spec.ts` (mevcutsa ekle, yoksa oluştur)

**Interfaces:**
- Consumes: `connectNodes(fromId, toId)` (mevcut), `deleteConnection(id)` (mevcut), `redrawConnections()` SVG overlay altyapısı, Task 1'in tıklama garantisi.
- Produces: `CanvasComponent` public API — `beginConnection(nodeId: string): void`, `completeConnection(targetNodeId: string): Promise<string | null>`, `cancelConnection(): void`, `selectedConnection: string | null`. `NodeComponent` yeni output'lar — `connectStart = EventEmitter<string>`, `connectDrop = EventEmitter<string>`. `connectNodes` mükerrer bağlantıyı reddeder (null döner). Paket B `graphChanged` emisyonuna dayanacak (mevcut davranış korunur).

**Tasarım:** Rete connection-plugin'in soket sözleşmesine dönmek yerine mevcut manuel SVG overlay deseniyle tutarlı üç-metotlu bir sürükleme akışı: out soketinde `pointerdown` → `beginConnection` (geçici kesikli path, `pointermove` ile imleci izler) → hedef kartta/in soketinde `pointerup` → `completeConnection` → `connectNodes`. Boşlukta `pointerup`/`Escape` → `cancelConnection`. Testler DOM pointer olaylarına değil bu public API'ye yazılır (jsdom'da PointerEvent güvenilmez); DOM bağlama katmanı ince tutulur ve elle doğrulanır.

- [ ] **Step 1: Failing testler — bağlantı yaşam döngüsü**

`canvas.component.spec.ts`'e ekle:

```typescript
describe('interactive connection lifecycle', () => {
  it('creates a connection via beginConnection → completeConnection', async () => {
    await ready();
    const a = await component.addNode('A');
    const b = await component.addNode('B');

    component.beginConnection(a);
    const connId = await component.completeConnection(b);

    expect(connId).toBeTruthy();
    expect(component.editor.getConnections().length).toBe(1);
    const conn = component.editor.getConnections()[0];
    expect(conn.source).toBe(a);
    expect(conn.target).toBe(b);
  });

  it('cancelConnection drops the pending connection without creating one', async () => {
    await ready();
    const a = await component.addNode('A');
    const b = await component.addNode('B');

    component.beginConnection(a);
    component.cancelConnection();
    const connId = await component.completeConnection(b);

    expect(connId).toBeNull(); // pending yoktu
    expect(component.editor.getConnections().length).toBe(0);
  });

  it('refuses a duplicate connection between the same pair', async () => {
    await ready();
    const a = await component.addNode('A');
    const b = await component.addNode('B');
    await component.connectNodes(a, b);

    const dup = await component.connectNodes(a, b);

    expect(dup).toBeNull();
    expect(component.editor.getConnections().length).toBe(1);
  });

  it('completeConnection on the source node itself refuses (self-connection)', async () => {
    await ready();
    const a = await component.addNode('A');
    component.beginConnection(a);
    const connId = await component.completeConnection(a);
    expect(connId).toBeNull();
    expect(component.editor.getConnections().length).toBe(0);
  });

  it('selects a connection and deletes it via deleteSelectedConnection', async () => {
    await ready();
    const a = await component.addNode('A');
    const b = await component.addNode('B');
    const connId = await component.connectNodes(a, b);

    component.selectConnection(connId!);
    expect(component.selectedConnection).toBe(connId);

    await component.deleteSelectedConnection();
    expect(component.editor.getConnections().length).toBe(0);
    expect(component.selectedConnection).toBeNull();
  });

  it('emits graphChanged when a connection is created interactively', async () => {
    await ready();
    const a = await component.addNode('A');
    const b = await component.addNode('B');
    const events: unknown[] = [];
    component.graphChanged.subscribe((g) => events.push(g));

    component.beginConnection(a);
    await component.completeConnection(b);

    expect(events.length).toBeGreaterThan(0);
  });
});
```

`node.component.spec.ts`'e ekle (dosya yoksa `canvas.component.spec.ts` kurulum desenini izleyerek oluştur — `TestBed` + `NodeComponent`, `node` input'u zorunlu):

```typescript
  it('emits connectStart on pointerdown at the out socket', () => {
    const emitted: string[] = [];
    component.connectStart.subscribe((id) => emitted.push(id));
    fixture.detectChanges();

    const outSocket: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="canvas-node-socket-out"]');
    outSocket.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));

    expect(emitted).toEqual([component.node.id]);
  });

  it('emits connectDrop on pointerup over the card', () => {
    const emitted: string[] = [];
    component.connectDrop.subscribe((id) => emitted.push(id));
    fixture.detectChanges();

    const card: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="canvas-node"]');
    card.dispatchEvent(new MouseEvent('pointerup', { bubbles: true }));

    expect(emitted).toEqual([component.node.id]);
  });
```

- [ ] **Step 2: Çalıştır — FAIL gözle**

Run: `cd src/RPA.Studio && npm test -- --watch=false --include='**/{canvas,node}.component.spec.ts'`
Expected: FAIL — `beginConnection` / `connectStart` tanımsız.

- [ ] **Step 3: NodeComponent'e soket olaylarını ekle**

`node.component.ts` — output'lar ve handler'lar:

```typescript
  @Output() readonly connectStart = new EventEmitter<string>();
  @Output() readonly connectDrop = new EventEmitter<string>();

  onOutSocketDown(event: Event): void {
    // Rete'nin node-drag yakalamasını engelle; bağlantı sürüklemesi başlasın.
    event.stopPropagation();
    event.preventDefault();
    this.connectStart.emit(this.node.id);
  }

  onPointerUp(): void {
    this.connectDrop.emit(this.node.id);
  }
```

`node.component.html` — kök div'e `(pointerup)="onPointerUp()"` ekle; soket span'larını güncelle:

```html
  <span
    class="canvas-node__socket canvas-node__socket--in"
    data-testid="canvas-node-socket-in"
    aria-hidden="true"
  ></span>
  ...
  <span
    class="canvas-node__socket canvas-node__socket--out"
    data-testid="canvas-node-socket-out"
    aria-hidden="true"
    (pointerdown)="onOutSocketDown($event)"
  ></span>
```

`node.component.scss` `&__socket` bloğuna sürükleme hedefi büyüsün diye:

```scss
    cursor: crosshair;

    &:hover {
      transform: translateX(-50%) scale(1.4);
    }
```

- [ ] **Step 4: CanvasComponent'e bağlantı yaşam döngüsünü ekle**

`canvas.component.ts` — alanlar (sınıfın `selectedNodeId` yakınına):

```typescript
  private pendingConnectionFrom: string | null = null;
  private pendingPath?: SVGPathElement;
  private selectedConnectionId: string | null = null;
```

Public API (`// --- public API ---` bölümüne):

```typescript
  /** Out soketinden bağlantı sürüklemesi başlatır (geçici kesikli çizgi). */
  beginConnection(nodeId: string): void {
    this.assertWritable();
    if (!this.editor.getNode(nodeId)) {
      return;
    }
    this.pendingConnectionFrom = nodeId;
    this.ensurePendingPath();
  }

  /** Sürüklemeyi hedef node üzerinde tamamlar; kural ihlalinde null döner. */
  async completeConnection(targetNodeId: string): Promise<string | null> {
    const from = this.pendingConnectionFrom;
    this.cancelConnection();
    if (!from) {
      return null;
    }
    return this.connectNodes(from, targetNodeId);
  }

  /** Bekleyen bağlantı sürüklemesini iptal eder ve geçici çizgiyi kaldırır. */
  cancelConnection(): void {
    this.pendingConnectionFrom = null;
    this.pendingPath?.remove();
    this.pendingPath = undefined;
  }

  selectConnection(connectionId: string | null): void {
    this.selectedConnectionId = connectionId;
    this.redrawConnections();
  }

  get selectedConnection(): string | null {
    return this.selectedConnectionId;
  }

  async deleteSelectedConnection(): Promise<boolean> {
    if (!this.selectedConnectionId) {
      return false;
    }
    const id = this.selectedConnectionId;
    this.selectedConnectionId = null;
    return this.deleteConnection(id);
  }
```

`connectNodes`'a mükerrer koruması (mevcut `if (!source || !target || fromId === toId)` bloğunun hemen ardına):

```typescript
    const duplicate = this.editor
      .getConnections()
      .some((c) => c.source === fromId && c.target === toId);
    if (duplicate) {
      return null;
    }
```

Geçici çizgi yardımcıları (`// --- helpers ---` bölümüne):

```typescript
  private ensurePendingPath(): void {
    if (!this.connectionGroup || this.pendingPath) {
      return;
    }
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('class', 'canvas-connections__path canvas-connections__path--pending');
    path.setAttribute('data-testid', 'canvas-connection-pending');
    path.setAttribute('fill', 'none');
    this.connectionGroup.appendChild(path);
    this.pendingPath = path;
  }

  /** Container koordinatındaki imleç konumuna göre geçici çizgiyi günceller. */
  private updatePendingPath(clientX: number, clientY: number): void {
    if (!this.pendingConnectionFrom || !this.pendingPath) {
      return;
    }
    const from = this.socketPosition(this.pendingConnectionFrom, 'out');
    if (!from) {
      return;
    }
    const rect = this.reteContainer.nativeElement.getBoundingClientRect();
    const t = this.area.area.transform;
    const to: NodePosition = {
      x: (clientX - rect.left - t.x) / t.k,
      y: (clientY - rect.top - t.y) / t.k,
    };
    this.pendingPath.setAttribute('d', ConnectionComponent.buildPath(from, to));
  }
```

DOM bağlama — `setup()` sonunda (`this.ready = true;` öncesi):

```typescript
    container.addEventListener('pointermove', (e: PointerEvent) =>
      this.updatePendingPath(e.clientX, e.clientY),
    );
    container.addEventListener('pointerup', () => this.cancelConnection());
    container.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        this.cancelConnection();
      }
    });
    // Bağlantı path'ine tıklayınca seç (event delegation — path'ler her çizimde yenilenir).
    this.connectionSvg?.addEventListener('click', (e: MouseEvent) => {
      const target = e.target as SVGElement;
      const connId = target?.getAttribute?.('data-connection-id');
      this.selectConnection(connId ?? null);
    });
```

`mountNode` içinde yeni output abonelikleri (`nodeDelete.subscribe` satırının ardına):

```typescript
      ref.instance.connectStart.subscribe((id: string) => this.beginConnection(id));
      ref.instance.connectDrop.subscribe((id: string) => void this.completeConnection(id));
```

`redrawConnections`'da seçili path'e sınıf (path oluşturma döngüsünde `class` satırını değiştir):

```typescript
      path.setAttribute(
        'class',
        conn.id === this.selectedConnectionId
          ? 'canvas-connections__path canvas-connections__path--selected'
          : 'canvas-connections__path',
      );
      path.setAttribute('pointer-events', 'stroke');
```

`canvas.component.html:62-63` — Delete davranışını genişlet (bağlantı seçiliyse önce onu sil):

```html
    (keydown.delete)="onDeleteKey()"
    (keydown.backspace)="onDeleteKey()"
```

ve `canvas.component.ts`'e:

```typescript
  onDeleteKey(): void {
    if (this.selectedConnectionId) {
      void this.deleteSelectedConnection();
    } else if (this.selectedNodeId) {
      void this.deleteNode(this.selectedNodeId);
    }
  }
```

(Not: Task 1'de bu handler'lar değiştirilmiş olabilir — mevcut duruma uyarla, davranış: bağlantı > node önceliği.)

`canvas.component.scss`'e (`.canvas-connections` mevcut stillerinin yanına):

```scss
.canvas-connections__path--pending {
  stroke-dasharray: 6 4;
  opacity: 0.6;
}

.canvas-connections__path--selected {
  stroke: #2563eb;
  stroke-width: 3;
}
```

- [ ] **Step 5: Testleri çalıştır — PASS**

Run: `cd src/RPA.Studio && npm test -- --watch=false`
Expected: hepsi PASS (eski bağlantı testleri dahil).

- [ ] **Step 6: i18n — yoksa ekle**

`tr.json` / `en.json` içinde `canvas` bölümüne (varsa yapıya uy):

```json
"connection": { "delete": "Bağlantıyı sil", "hint": "Bağlantı için alt noktadan sürükleyin" }
```
```json
"connection": { "delete": "Delete connection", "hint": "Drag from the bottom socket to connect" }
```

(UI'da şimdilik yalnız aria/tooltip olarak gerekirse kullanılır; kullanılmıyorsa eklenmesi zorunlu değil — kullanıcıya görünen yeni metin yoksa bu adımı atla.)

- [ ] **Step 7: Tarayıcıda elle doğrula**

İki aktivite bırak → alttaki mavi soketten sürükle (kesikli çizgi imleci izler) → ikinci kartın üstüne bırak (bağlantı oluşur) → boşluğa bırak (iptal) → aynı çifti tekrar bağlamayı dene (reddedilir) → çizgiye tıkla (kalınlaşır/maviye döner) → `Delete` (silinir) → node sürüklemenin hâlâ çalıştığını kontrol et (soket dışından sürükle).

- [ ] **Step 8: Commit**

```bash
git add src/RPA.Studio/src/app/studio/designer/canvas/ src/RPA.Studio/src/assets/i18n/
git commit -m "feat(studio): mouse ile düğüm bağlama ve bağlantı seç/sil

Out soketinden sürükle → hedef karta bırak; geçici kesikli çizgi;
self/mükerrer bağlantı reddi; bağlantı tıkla-seç + Delete ile silme.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Katalog kapsama testleri (backend + frontend)

**Files:**
- Test (backend): `tests/RPA.Infrastructure.Tests/Workflow/ActivityRegistryCoverageTests.cs` (yeni)
- Test (frontend): `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.spec.ts` (mevcutsa ekle, yoksa oluştur)

**Interfaces:**
- Consumes: `ActivityRegistry.BuildCatalog()` (`src/RPA.Infrastructure/Workflow/ActivityRegistry.cs:27`), `GenericPropertyComponent.inputType()` eşlemesi (`generic-property.component.ts:48-62`).
- Produces: kalıcı güvence — katalog büyüdükçe testler otomatik kapsar. Başka task bunlara bağımlı değil.

**Amaç (spec A.4):** "nesnelerde özellik yok" şikayetinin bir daha yaşanmaması: (1) backend'de her aktivitenin input tanımı ve desteklenen tipte olduğu, (2) frontend'in her desteklenen tip için form alanı üretebildiği güvenceye alınır.

- [ ] **Step 1: Backend failing test**

`tests/RPA.Infrastructure.Tests/Workflow/ActivityRegistryCoverageTests.cs`:

```csharp
namespace RPA.Infrastructure.Tests.Workflow;

using RPA.Infrastructure.Workflow;
using Xunit;

/// <summary>
/// Katalog kapsama güvencesi (Paket A): her aktivitenin özellik formu üretilebilir
/// olmalı — input'lar tanımlı ve tipleri Studio GenericPropertyComponent'in
/// desteklediği kümede. Yeni aktivite eklendiğinde bu testler otomatik kapsar.
/// </summary>
public class ActivityRegistryCoverageTests
{
    /// <summary>Studio generic editörünün form alanına eşleyebildiği tipler.</summary>
    private static readonly HashSet<string> SupportedInputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "int", "number", "decimal", "bool", "boolean",
        "JSON", "DataTable", "Credential",
    };

    /// <summary>Bilinçli olarak input'suz aktiviteler (parametre gerektirmez).</summary>
    private static readonly HashSet<string> KnownInputlessActivities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sap.Nco.Rollback",   // BAPI_TRANSACTION_ROLLBACK — parametresiz
        "Sap.Gui.Screenshot", // yalnız çıktı üretir
    };

    [Fact]
    public void EveryActivity_HasInputs_OrIsKnownInputless()
    {
        var catalog = ActivityRegistry.BuildCatalog();
        var missing = catalog.Values
            .Where(a => a.Inputs.Count == 0 && !KnownInputlessActivities.Contains(a.ActivityId))
            .Select(a => a.ActivityId)
            .ToList();

        Assert.True(missing.Count == 0,
            $"Input tanımı olmayan aktiviteler (bilinçliyse KnownInputlessActivities'e ekleyin): {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryInput_UsesASupportedType()
    {
        var catalog = ActivityRegistry.BuildCatalog();
        var offenders = catalog.Values
            .SelectMany(a => a.Inputs.Select(i => (a.ActivityId, i.Name, i.Type)))
            .Where(x => !SupportedInputTypes.Contains(x.Type))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Desteklenmeyen input tipi: {string.Join(", ", offenders.Select(o => $"{o.ActivityId}.{o.Name}:{o.Type}"))}");
    }

    [Fact]
    public void EveryActivity_HasDisplayNameAndCategory()
    {
        var catalog = ActivityRegistry.BuildCatalog();
        var offenders = catalog.Values
            .Where(a => string.IsNullOrWhiteSpace(a.DisplayName)
                     || a.DisplayName == a.ActivityId
                     || string.IsNullOrWhiteSpace(a.Category))
            .Select(a => a.ActivityId)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"DisplayName/Category eksik: {string.Join(", ", offenders)}");
    }
}
```

- [ ] **Step 2: Çalıştır — sonucu gözlemle**

Run: `dotnet test tests/RPA.Infrastructure.Tests --filter ActivityRegistryCoverage`
Expected: PASS beklenir (katalog incelemesine göre tamdır) — bu bir karakterizasyon/güvence testi; FAIL ederse mesajdaki aktivitelerin registry kaydını tamamla (kod incelemesi 2026-07-06'da tümü tamdı, FAIL yalnız bu plan yazıldıktan sonra eklenen bir aktiviteden gelebilir).

- [ ] **Step 3: Frontend failing test — tip → alan eşlemesi**

`generic-property.component.spec.ts`'e ekle (dosya yoksa `properties-panel.component.spec.ts` kurulum desenini kopyala — TestBed + `provideHttpClientTesting`):

```typescript
  it('renders a correctly-typed form field for every supported port type', () => {
    component.activityType = 'Test.AllTypes';
    component.properties = {};
    fixture.detectChanges();

    http.expectOne('/api/activities/Test.AllTypes').flush({
      activityId: 'Test.AllTypes',
      displayName: 'Tüm Tipler',
      inputs: [
        { name: 'fString', type: 'string', required: true },
        { name: 'fInt', type: 'int' },
        { name: 'fNumber', type: 'number' },
        { name: 'fDecimal', type: 'decimal' },
        { name: 'fBool', type: 'bool' },
        { name: 'fBoolean', type: 'boolean' },
        { name: 'fJson', type: 'JSON' },
        { name: 'fTable', type: 'DataTable' },
        { name: 'fCred', type: 'Credential' },
      ],
    });
    fixture.detectChanges();

    const typeOf = (name: string): string =>
      (fixture.nativeElement.querySelector(`[data-testid="prop-${name}"]`) as HTMLInputElement)
        .type;

    expect(typeOf('fString')).toBe('text');
    expect(typeOf('fInt')).toBe('number');
    expect(typeOf('fNumber')).toBe('number');
    expect(typeOf('fDecimal')).toBe('number');
    expect(typeOf('fBool')).toBe('checkbox');
    expect(typeOf('fBoolean')).toBe('checkbox');
    expect(typeOf('fJson')).toBe('text');
    expect(typeOf('fTable')).toBe('text');
    expect(typeOf('fCred')).toBe('password'); // Credential asla düz metin gösterilmez
  });

  it('shows a visible error message when catalog metadata cannot be loaded', () => {
    component.activityType = 'Missing.Activity';
    fixture.detectChanges();
    http.expectOne('/api/activities/Missing.Activity').flush(
      { error: 'yok' },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();

    const status = fixture.nativeElement.querySelector('.generic-property__status--error');
    expect(status).toBeTruthy();
    expect(status.textContent).toContain('yüklenemedi');
  });
```

- [ ] **Step 4: Çalıştır — durumu gözlemle**

Run: `cd src/RPA.Studio && npm test -- --watch=false --include='**/generic-property.component.spec.ts'`
Expected: PASS beklenir (`inputType` eşlemesi ve hata şablonu mevcut, `generic-property.component.ts:48-62` + `.html:4-7`). FAIL ederse eşlemedeki eksik tipi `inputType`'a ekle — davranış zaten spec'te tanımlı: number-türleri → `number`, bool-türleri → `checkbox`, `credential` → `password`, kalanı → `text`.

- [ ] **Step 5: Tüm test paketlerini çalıştır**

Run: `cd src/RPA.Studio && npm test -- --watch=false && cd ../.. && dotnet test tests/RPA.Infrastructure.Tests`
Expected: hepsi PASS.

- [ ] **Step 6: Commit**

```bash
git add tests/RPA.Infrastructure.Tests/Workflow/ActivityRegistryCoverageTests.cs src/RPA.Studio/src/app/studio/designer/properties/
git commit -m "test(catalog): katalog kapsama güvencesi — her aktivite form üretebilir

Backend: input tanımı + desteklenen tip + DisplayName/Category zorunlu.
Frontend: 9 port tipinin tamamı doğru HTML input türüne eşleniyor;
katalog hatası kullanıcıya görünür.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Paket Kapanışı

- [ ] Tüm testler: `cd src/RPA.Studio && npm test -- --watch=false` ve `dotnet test` → PASS
- [ ] Uçtan uca elle senaryo: aktivite bırak → tıkla (silinmez, panel açılır, varsayılanlar dolu) → ikinci aktivite → soketten bağla → bağlantıyı seç/sil → panelde alan düzenle → başka node seçip geri dön (değer korunur)
- [ ] `/code-review medium` çalıştır (proje kuralı)
- [ ] Sonraki adım: Paket B planı (`docs/superpowers/plans/`e ayrı plan olarak yazılacak — Workflow CRUD API + Projelerim + Kaydet)
