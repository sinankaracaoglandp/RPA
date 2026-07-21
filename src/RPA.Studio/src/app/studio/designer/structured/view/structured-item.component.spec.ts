import { TestBed } from '@angular/core/testing';
import { StructuredItemComponent, StructuredAction } from './structured-item.component';
import { step, container } from '../structured-model';

describe('StructuredItemComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [StructuredItemComponent] }));

  it('renders a step card with title and activity id', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', step({ id: 'n1', type: 'activity', activity: 'Web.Click' }));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="structured-step"]')).toBeTruthy();
    expect(el.textContent).toContain('Web.Click');
  });

  it('renders a container box with a type label and lane sections', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', container('if', { condition: '{{c}} == 1' }, {
      true: [step({ id: 't', type: 'activity', activity: 'A' })], false: [],
    }));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="structured-container"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="lane-true"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="lane-false"]')).toBeTruthy();
  });

  it('emits delete action carrying the item reference when editable', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    const item = step({ id: 's', type: 'activity', activity: 'A' });
    f.componentRef.setInput('item', item);
    f.componentRef.setInput('editable', true);
    f.detectChanges();
    const events: StructuredAction[] = [];
    f.componentInstance.action.subscribe((e) => events.push(e));
    (f.nativeElement.querySelector('[data-testid="item-delete"]') as HTMLButtonElement).click();
    expect(events[0]).toEqual({ kind: 'delete', target: item });
  });

  it('does not render edit controls when not editable', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', step({ id: 's', type: 'activity', activity: 'A' }));
    f.componentRef.setInput('editable', false);
    f.detectChanges();
    expect(f.nativeElement.querySelector('[data-testid="item-delete"]')).toBeFalsy();
  });

  it('emits select with the item reference on card click', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    const item = step({ id: 's', type: 'activity', activity: 'A' });
    f.componentRef.setInput('item', item);
    f.componentRef.setInput('editable', true);
    f.detectChanges();
    let selected: { item: unknown; additive: boolean } | undefined;
    f.componentInstance.select.subscribe((e) => (selected = e));
    (f.nativeElement.querySelector('[data-testid="structured-step"]') as HTMLElement).click();
    expect(selected).toEqual({ item, additive: false });
  });

  it('flags the selection as additive when ctrl is held', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    const item = step({ id: 's', type: 'activity', activity: 'A' });
    f.componentRef.setInput('item', item);
    f.componentRef.setInput('editable', true);
    f.detectChanges();
    let selected: { additive: boolean } | undefined;
    f.componentInstance.select.subscribe((e) => (selected = e));

    (f.nativeElement.querySelector('[data-testid="structured-step"]') as HTMLElement)
      .dispatchEvent(new MouseEvent('click', { bubbles: true, ctrlKey: true }));

    expect(selected!.additive).toBe(true);
  });

  it('marks the selected item', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    const item = step({ id: 's', type: 'activity', activity: 'A' });
    f.componentRef.setInput('item', item);
    f.componentRef.setInput('selectedRef', item);
    f.detectChanges();
    expect(f.nativeElement.querySelector('.structured-item--selected')).toBeTruthy();
  });

  it('renders an incoming flow connector and entry port when not first', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', step({ id: 's', type: 'activity', activity: 'A' }));
    f.componentRef.setInput('first', false);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="flow-link"]')).toBeTruthy();
    expect(el.querySelector('.structured-port--in')).toBeTruthy();
  });

  it('omits the incoming connector and entry port for the first item', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', step({ id: 's', type: 'activity', activity: 'A' }));
    f.componentRef.setInput('first', true);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="flow-link"]')).toBeFalsy();
    expect(el.querySelector('.structured-port--in')).toBeFalsy();
  });

  it('renders an exit port when not last and omits it for the last item', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', step({ id: 's', type: 'activity', activity: 'A' }));
    f.componentRef.setInput('last', false);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('.structured-port--out')).toBeTruthy();

    const g = TestBed.createComponent(StructuredItemComponent);
    g.componentRef.setInput('item', step({ id: 's2', type: 'activity', activity: 'A' }));
    g.componentRef.setInput('last', true);
    g.detectChanges();
    expect((g.nativeElement as HTMLElement).querySelector('.structured-port--out')).toBeFalsy();
  });

  it('shows a type icon in the container header (forEach → 🔁)', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', container('forEach', {}, { body: [] }));
    f.detectChanges();
    const icon = f.nativeElement.querySelector('[data-testid="container-icon"]') as HTMLElement;
    expect(icon).toBeTruthy();
    expect(icon.textContent).toContain('🔁');
  });

  it('exposes the container control type for type-based styling', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', container('forEach', {}, { body: [] }));
    f.detectChanges();
    expect(f.nativeElement.querySelector('[data-testid="structured-container"][data-type="forEach"]')).toBeTruthy();
  });

  it('renders lanes as cdkDropList and items as cdkDrag when editable', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', container('forEach', {}, { body: [step({ id: 'b', type: 'activity', activity: 'A' })] }));
    f.componentRef.setInput('editable', true);
    f.detectChanges();
    expect(f.nativeElement.querySelector('.cdk-drop-list')).toBeTruthy();
    expect(f.nativeElement.querySelector('.cdk-drag')).toBeTruthy();
  });

  it('renders a drop slot for an empty lane WHILE editable (aim target)', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', container('if', {}, { true: [], false: [] }));
    f.componentRef.setInput('editable', true);
    f.detectChanges();

    // Boş lane'in bırakma hedefi düzenleme modunda da görünmeli — sürükleyip bırakılacak yer burası.
    expect(f.nativeElement.querySelectorAll('[data-testid="lane-empty"]').length).toBe(2);
  });
});

describe('StructuredItemComponent — kullanıcı adı (label)', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [StructuredItemComponent] }));

  it('shows the user label as the step title and keeps the activity id visible', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', step({
      id: 'n1', type: 'activity', activity: 'Web.Fill', label: 'Fatura no girişi',
    }));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="item-title"]')!.textContent).toContain('Fatura no girişi');
    expect(el.textContent).toContain('Web.Fill');
  });

  it('shows the user label on a container header alongside its type', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', container('forEach', { items: '${a}', label: 'Faturaları gez' }, { body: [] }));
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('[data-testid="item-title"]')!.textContent)
      .toContain('Faturaları gez');
  });

  it('emits a rename action with the typed label', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    const item = step({ id: 's', type: 'activity', activity: 'A' });
    f.componentRef.setInput('item', item);
    f.componentRef.setInput('editable', true);
    f.detectChanges();
    const events: StructuredAction[] = [];
    f.componentInstance.action.subscribe((e) => events.push(e));

    (f.nativeElement.querySelector('[data-testid="item-rename"]') as HTMLButtonElement).click();
    f.detectChanges();
    const input = f.nativeElement.querySelector('[data-testid="item-rename-input"]') as HTMLInputElement;
    input.value = 'Fatura tarihi girişi';
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));

    expect(events[0]).toEqual({ kind: 'rename', target: item, label: 'Fatura tarihi girişi' });
  });
});
