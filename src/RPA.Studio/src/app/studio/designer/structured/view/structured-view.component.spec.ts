import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { StructuredViewComponent } from './structured-view.component';
import { treeToWorkflow } from '../tree-to-workflow';
import { step, container } from '../structured-model';
import { newStep } from '../edit/tree-ops';
import { WorkflowNode } from '../../../../shared/models/workflow.model';

function dropEvent(prevData: unknown, contData: unknown, prevIdx: number, curIdx: number, itemData: unknown): never {
  return { previousContainer: { data: prevData }, container: { data: contData },
    previousIndex: prevIdx, currentIndex: curIdx, item: { data: itemData } } as never;
}

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });
const ids = () => { let i = 0; return () => `c${++i}`; };

describe('StructuredViewComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [StructuredViewComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  it('renders the structured tree for a structural-subset workflow', () => {
    const wf = treeToWorkflow([
      container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] }),
      step(n('after')),
    ], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="structured-view-tree"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="structured-container"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="structured-view-fallback"]')).toBeFalsy();
  });

  it('renders a tryCatch workflow as an editable tree (D2)', () => {
    const wf = treeToWorkflow(
      [container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('fin'))] })],
      { idGen: ids() },
    );
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="structured-view-tree"]')).toBeTruthy();
    expect(el.querySelector('[data-type="tryCatch"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="structured-view-fallback"]')).toBeFalsy();
  });

  it('shows the fallback for a non-structural free-graph', () => {
    const wf = {
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [n('a'), n('b'), n('c')],
      connections: [
        { from: 'a', to: 'c', fromPort: 'out', toPort: 'in' },
        { from: 'b', to: 'c', fromPort: 'out', toPort: 'in' },
      ],
    };
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf as never);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('[data-testid="structured-view-fallback"]')).toBeTruthy();
  });

  it('zoom in/out changes the zoom factor within clamp bounds', () => {
    const wf = { schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [{ id: 'a', type: 'activity', activity: 'X' }], connections: [] };
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf as never);
    f.detectChanges();
    const cmp = f.componentInstance;
    const start = cmp.zoom();
    cmp.zoomIn();
    expect(cmp.zoom()).toBeGreaterThan(start);
    for (let i = 0; i < 20; i++) { cmp.zoomIn(); }
    expect(cmp.zoom()).toBeLessThanOrEqual(2);
    for (let i = 0; i < 40; i++) { cmp.zoomOut(); }
    expect(cmp.zoom()).toBeGreaterThanOrEqual(0.4);
  });

  it('shows an empty state for a null workflow', () => {
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', null);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('[data-testid="structured-view-empty"]')).toBeTruthy();
  });

  it('offers an editable root add slot for an empty (node-less) workflow', () => {
    const wf = { schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0', nodes: [], connections: [] };
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf as never);
    f.detectChanges();
    TestBed.inject(HttpTestingController).match('/api/activities').forEach((r) => r.flush([]));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="structured-view-tree"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="root-add"] [data-testid="add-toggle"]')).toBeTruthy();
  });

  it('applies a delete and emits an updated workflow', () => {
    const wf = treeToWorkflow([step(n('a')), step(n('b'))], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    let emitted: { nodes: unknown[] } | undefined;
    f.componentInstance.graphChanged.subscribe((g) => (emitted = g as unknown as { nodes: unknown[] }));
    const items = f.nativeElement.querySelectorAll('[data-testid="item-delete"]');
    (items[0] as HTMLButtonElement).click();
    expect(emitted).toBeTruthy();
    expect(emitted!.nodes.length).toBe(1);
  });

  it('palette drop inserts a new item and emits workflow', () => {
    const wf = treeToWorkflow([step(n('a'))], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;
    let emitted: { nodes: unknown[] } | undefined;
    cmp.graphChanged.subscribe((g) => (emitted = g as unknown as { nodes: unknown[] }));
    const root = cmp.tree();
    cmp.onDrop(dropEvent('__palette__', root, 0, 1, { factory: () => newStep('Web.Click') }));
    expect(cmp.tree()).toHaveLength(2);
    expect(emitted!.nodes.length).toBe(2);
  });

  it('reorders within the root sequence on same-list drop', () => {
    const wf = treeToWorkflow([step(n('a')), step(n('b'))], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;
    const root = cmp.tree();
    cmp.onDrop(dropEvent(root, root, 0, 1, root[0]));
    expect((cmp.tree()[0] as { node: WorkflowNode }).node.id).toBe('b');
  });

  it('emits nodeSelect with activityType/properties when a step is selected', () => {
    const wf = treeToWorkflow([step(n('a'))], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;
    let sel: { activityType?: string; properties: Record<string, unknown> } | null = null;
    cmp.nodeSelect.subscribe((s) => (sel = s));
    cmp.onSelect(cmp.tree()[0]);
    expect(sel!.activityType).toBe('X');
  });

  it('emits nodeSelect with Logic.ForEach activity + props when a forEach block is selected', () => {
    const wf = treeToWorkflow(
      [container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] })],
      { idGen: ids() },
    );
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;
    let sel: { activityType?: string; properties: Record<string, unknown> } | null = null;
    cmp.nodeSelect.subscribe((s) => (sel = s));
    cmp.onSelect(cmp.tree()[0]);
    expect(sel!.activityType).toBe('Logic.ForEach');
    expect(sel!.properties).toEqual({ items: '${xs}', itemVariable: 'x' });
  });

  it('updateSelectedProps updates the selected item and emits workflow', () => {
    const wf = treeToWorkflow([container('forEach', { items: '${a}' }, { body: [step(n('b'))] }), step(n('after'))], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;
    cmp.onSelect(cmp.tree()[0]);
    let emitted = false;
    cmp.graphChanged.subscribe(() => (emitted = true));
    cmp.updateSelectedProps({ items: '${b}', itemVariable: 'x' });
    expect((cmp.tree()[0] as { props: unknown }).props).toEqual({ items: '${b}', itemVariable: 'x' });
    expect(emitted).toBe(true);
  });

  it('undo restores the previous tree and clears selection', () => {
    const wf = treeToWorkflow([step(n('a'))], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;
    cmp.addToRoot(step(n('b')));
    expect(cmp.tree()).toHaveLength(2);
    expect(cmp.canUndo).toBe(true);
    let cleared = false;
    cmp.nodeSelect.subscribe((s) => { if (s === null) { cleared = true; } });
    cmp.undo();
    expect(cmp.tree()).toHaveLength(1);
    expect(cleared).toBe(true);
    cmp.redo();
    expect(cmp.tree()).toHaveLength(2);
  });

  it('coalesces consecutive prop edits into one undo step', () => {
    const wf = treeToWorkflow([container('forEach', { items: '${a}' }, { body: [step(n('b'))] })], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;
    cmp.onSelect(cmp.tree()[0]);
    cmp.updateSelectedProps({ items: '${b}' });
    cmp.updateSelectedProps({ items: '${bc}' });
    cmp.undo();
    expect((cmp.tree()[0] as unknown as { props: { items: string } }).props.items).toBe('${a}');
  });

  it('shows the precise reason on a non-reducible workflow', () => {
    const wf = {
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [n('a'), n('b'), n('c')],
      connections: [
        { from: 'a', to: 'c', fromPort: 'out', toPort: 'in' },
        { from: 'b', to: 'c', fromPort: 'out', toPort: 'in' },
      ],
    };
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf as never);
    f.detectChanges();
    const el = f.nativeElement.querySelector('[data-testid="structured-view-fallback"]') as HTMLElement;
    expect(el).toBeTruthy();
    expect(el.textContent).toContain('giriş');
  });

  it('does not render edit controls for a fallback workflow', () => {
    const wf = {
      schemaVersion: '1.0', id: 'w', name: 'w', version: '1.0.0',
      nodes: [n('a'), n('b'), n('c')],
      connections: [
        { from: 'a', to: 'b', fromPort: 'out', toPort: 'in' },
        { from: 'a', to: 'c', fromPort: 'out', toPort: 'in' },
      ],
    };
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf as never);
    f.detectChanges();
    expect(f.nativeElement.querySelector('[data-testid="item-delete"]')).toBeFalsy();
    expect(f.nativeElement.querySelector('[data-testid="structured-view-fallback"]')).toBeTruthy();
  });

  it('shows an add slot inside an empty lane (editable)', () => {
    const wf = treeToWorkflow(
      [container('if', { condition: '{{c}}' }, { true: [step(n('t'))], false: [] })],
      { idGen: ids() },
    );
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    TestBed.inject(HttpTestingController).match('/api/activities').forEach((r) => r.flush([]));
    f.detectChanges();

    const falseLane = f.nativeElement.querySelector('[data-testid="lane-false"]') as HTMLElement;
    expect(falseLane).toBeTruthy();
    // boş dalda hem ekleme slotu hem de sürükle-bırak hedefi görünür
    expect(falseLane.querySelector('[data-testid="add-toggle"]')).toBeTruthy();
    expect(falseLane.querySelector('[data-testid="lane-empty"]')).toBeTruthy();
  });

  it('adds a node into a lane when the add control is used (no drag)', () => {
    const wf = treeToWorkflow(
      [container('forEach', { items: '${xs}' }, { body: [step(n('b'))] })],
      { idGen: ids() },
    );
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    TestBed.inject(HttpTestingController).match('/api/activities').forEach((r) => r.flush([]));
    f.detectChanges();

    const bodyLane = f.nativeElement.querySelector('[data-testid="lane-body"]') as HTMLElement;
    (bodyLane.querySelector('[data-testid="add-toggle"]') as HTMLButtonElement).click();
    f.detectChanges();
    (bodyLane.querySelector('[data-testid="add-type-if"]') as HTMLButtonElement).click();
    f.detectChanges();

    const bodyItems = (f.componentInstance.tree()[0] as { lanes: { body: unknown[] } }).lanes.body;
    expect(bodyItems).toHaveLength(2);
  });
});

describe('StructuredViewComponent — geç gelen taslak', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [StructuredViewComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  it('seeds from the workflow that arrives after the initial null binding', () => {
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', null); // taslak henüz yüklenmedi
    f.detectChanges();
    expect(f.componentInstance.mode()).toBe('empty');

    f.componentRef.setInput('workflow', {
      schemaVersion: '1.0', id: 'w1', name: 'W', version: '1.0.0',
      nodes: [{ id: 'a', type: 'activity', activity: 'Web.Click' }],
      connections: [],
    });
    f.detectChanges();

    expect(f.componentInstance.mode()).toBe('tree');
    expect(f.componentInstance.tree()).toHaveLength(1);
  });
});

describe('StructuredViewComponent — kopyala', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [StructuredViewComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  it('duplicates a step, emits the new graph and selects the copy', () => {
    const f = TestBed.createComponent(StructuredViewComponent);
    const original = step({
      id: 'a', type: 'activity', activity: 'Desktop.SendKeys', properties: { keys: '[]' },
    });
    f.componentRef.setInput('workflow', treeToWorkflow([original]));
    f.detectChanges();

    const emitted: unknown[] = [];
    f.componentInstance.graphChanged.subscribe((g) => emitted.push(g));
    const seeded = f.componentInstance.tree()[0];
    f.componentInstance.onAction({ kind: 'duplicate', target: seeded });

    const tree = f.componentInstance.tree();
    expect(tree).toHaveLength(2);
    const a = (tree[0] as { node: WorkflowNode }).node;
    const b = (tree[1] as { node: WorkflowNode }).node;
    expect(b.activity).toBe('Desktop.SendKeys');
    expect(b.id).not.toBe(a.id);
    expect(emitted).toHaveLength(1);
    expect(f.componentInstance.selected()).toBe(tree[1]);
  });

  it('makes the duplicate undoable', () => {
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', treeToWorkflow([step(n('a'))]));
    f.detectChanges();
    f.componentInstance.onAction({ kind: 'duplicate', target: f.componentInstance.tree()[0] });
    expect(f.componentInstance.tree()).toHaveLength(2);
    f.componentInstance.undo();
    expect(f.componentInstance.tree()).toHaveLength(1);
  });

  function seededView() {
    const wf = treeToWorkflow([
      step(n('a')),
      container('forEach', { items: '${xs}', itemVariable: 'x' }, { body: [step(n('b'))] }),
    ], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    return f;
  }

  it('appends to the root when nothing is selected', () => {
    const cmp = seededView().componentInstance;
    cmp.addFromPalette(newStep('Web.Click'));

    const t = cmp.tree();
    expect(t).toHaveLength(3);
    expect((t[2] as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });

  it('inserts right after the selected step', () => {
    const cmp = seededView().componentInstance;
    cmp.onSelect(cmp.tree()[0]);
    cmp.addFromPalette(newStep('Web.Click'));

    const t = cmp.tree();
    expect(t).toHaveLength(3);
    expect((t[1] as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });

  it('inserts INTO a selected container, at the end of its first lane', () => {
    const cmp = seededView().componentInstance;
    expect(cmp.tree()[1].kind).toBe('container');
    cmp.onSelect(cmp.tree()[1]);
    cmp.addFromPalette(newStep('Web.Click'));

    const t = cmp.tree();
    expect(t).toHaveLength(2); // kök büyümez
    const body = (t[1] as { lanes: Record<string, unknown[]> }).lanes['body'];
    expect(body).toHaveLength(2);
    expect((body[1] as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });

  it('inserts into the first lane of a multi-lane container (if → true)', () => {
    const wf = treeToWorkflow(
      [container('if', { condition: '${x}' }, { true: [], false: [step(n('e'))] })],
      { idGen: ids() },
    );
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;

    cmp.onSelect(cmp.tree()[0]);
    cmp.addFromPalette(newStep('Web.Click'));

    const lanes = (cmp.tree()[0] as { lanes: Record<string, unknown[]> }).lanes;
    expect(lanes['true']).toHaveLength(1);
    expect((lanes['true'][0] as { node: { activity: string } }).node.activity).toBe('Web.Click');
    expect(lanes['false']).toHaveLength(1); // dokunulmaz
  });

  it('stays inside the lane when a step inside a container is selected', () => {
    const cmp = seededView().componentInstance;
    const body = (cmp.tree()[1] as { lanes: Record<string, unknown[]> }).lanes['body'];
    cmp.onSelect(body[0] as never);
    cmp.addFromPalette(newStep('Web.Click'));

    const nextBody = (cmp.tree()[1] as { lanes: Record<string, unknown[]> }).lanes['body'];
    expect(nextBody).toHaveLength(2);
    expect((nextBody[1] as { node: { activity: string } }).node.activity).toBe('Web.Click');
    expect(cmp.tree()).toHaveLength(2);
  });

  it('inserts into the lane the user selected (if → false)', () => {
    const wf = treeToWorkflow(
      [container('if', { condition: '${x}' }, { true: [], false: [step(n('e'))] })],
      { idGen: ids() },
    );
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;

    cmp.onSelectLane({ container: cmp.tree()[0] as never, lane: 'false' });
    cmp.addFromPalette(newStep('Web.Click'));

    const lanes = (cmp.tree()[0] as { lanes: Record<string, unknown[]> }).lanes;
    expect(lanes['false']).toHaveLength(2);
    expect((lanes['false'][1] as { node: { activity: string } }).node.activity).toBe('Web.Click');
    expect(lanes['true']).toHaveLength(0); // ilk lane'e düşmez
  });

  it('selecting a step clears the lane selection', () => {
    const cmp = seededView().componentInstance;
    cmp.onSelectLane({ container: cmp.tree()[1] as never, lane: 'body' });
    cmp.onSelect(cmp.tree()[0]);
    cmp.addFromPalette(newStep('Web.Click'));

    const t = cmp.tree();
    expect(t).toHaveLength(3); // kökte, seçili adımın ardında
    expect((t[1] as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });

  // ---- Çoklu seçim (Ctrl+tık) ----

  it('ctrl-click adds to and removes from the multi selection', () => {
    const cmp = seededView().componentInstance;
    const [a, box] = cmp.tree();

    cmp.onSelect(a);
    cmp.onSelect(box, true);
    expect(cmp.selectedItems()).toEqual([a, box]);

    cmp.onSelect(box, true); // aynı öğeye tekrar → seçimden çıkar
    expect(cmp.selectedItems()).toEqual([a]);
  });

  it('emits a null property selection while multiple items are selected', () => {
    const f = seededView();
    const cmp = f.componentInstance;
    const seen: unknown[] = [];
    cmp.nodeSelect.subscribe((s: unknown) => seen.push(s));

    cmp.onSelect(cmp.tree()[0]);
    cmp.onSelect(cmp.tree()[1], true);

    expect(seen[seen.length - 1]).toBeNull();
  });

  it('deletes every selected item in one undo step', () => {
    const cmp = seededView().componentInstance;
    const [a, box] = cmp.tree();
    cmp.onSelect(a);
    cmp.onSelect(box, true);

    cmp.deleteSelection();
    expect(cmp.tree()).toHaveLength(0);

    cmp.undo();
    expect(cmp.tree()).toHaveLength(2);
  });

  it('duplicates the whole selection after the last selected item', () => {
    const cmp = seededView().componentInstance;
    const [a, box] = cmp.tree();
    cmp.onSelect(a);
    cmp.onSelect(box, true);

    cmp.duplicateSelection();
    expect(cmp.tree()).toHaveLength(4);
  });

  it('moves the whole selection when a selected item is dragged', () => {
    const cmp = seededView().componentInstance;
    const a = cmp.tree()[0];
    const box = cmp.tree()[1] as { lanes: Record<string, unknown[]> };
    cmp.onSelect(a);

    cmp.onDrop({
      previousContainer: { data: cmp.tree() },
      container: { data: box.lanes['body'] },
      previousIndex: 0,
      currentIndex: 1,
      item: { data: a },
    } as never);

    const t = cmp.tree();
    expect(t).toHaveLength(1); // 'a' kökten çıktı
    const body = (t[0] as { lanes: Record<string, unknown[]> }).lanes['body'];
    expect(body).toHaveLength(2);
  });

  it('drags a selected group below the last container within the same list', () => {
    // [a, b, c, if] → a,b,c seçili, if'in ALTINA sürükleniyor.
    // CDK aynı liste içinde currentIndex'i "sürüklenen öğe çıkarılmış" listeye göre verir:
    // [b, c, if] içinde sona bırakma → currentIndex 3.
    const wf = treeToWorkflow([
      step(n('a')), step(n('b')), step(n('c')),
      container('if', { condition: '${x}' }, { true: [], false: [] }),
    ], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;

    const [a, b, c] = cmp.tree();
    cmp.onSelect(a); cmp.onSelect(b, true); cmp.onSelect(c, true);

    // CDK aynı-liste sürüklemede previousContainer ile container için AYNI nesneyi verir.
    const list = { data: cmp.tree() };
    cmp.onDrop({
      previousContainer: list, container: list,
      previousIndex: 0, currentIndex: 3, item: { data: a },
    } as never);

    expect(cmp.tree().map((i) => (i as { node?: { id: string } }).node?.id ?? 'IF'))
      .toEqual(['IF', 'a', 'b', 'c']);
  });

  it('drags a selected group INTO an empty container lane', () => {
    const wf = treeToWorkflow([
      step(n('a')), step(n('b')),
      container('if', { condition: '${x}' }, { true: [], false: [] }),
    ], { idGen: ids() });
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    const cmp = f.componentInstance;

    const [a, b, box] = cmp.tree();
    cmp.onSelect(a); cmp.onSelect(b, true);

    const trueLane = (box as { lanes: Record<string, unknown[]> }).lanes['true'];
    cmp.onDrop({
      previousContainer: { data: cmp.tree() },
      container: { data: trueLane },
      previousIndex: 0, currentIndex: 0, item: { data: a },
    } as never);

    const lanes = (cmp.tree()[0] as { lanes: Record<string, unknown[]> }).lanes;
    expect(lanes['true'].map((i) => (i as { node: { id: string } }).node.id)).toEqual(['a', 'b']);
    expect(cmp.tree()).toHaveLength(1);
  });

  it('undoes a palette add', () => {
    const cmp = seededView().componentInstance;
    cmp.addFromPalette(newStep('Web.Click'));
    expect(cmp.tree()).toHaveLength(3);
    cmp.undo();
    expect(cmp.tree()).toHaveLength(2);
  });
});
