import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
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

  it('shows the fallback when conversion throws (tryCatch)', () => {
    const wf = treeToWorkflow(
      [container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('fin'))] })],
      { idGen: ids() },
    );
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('[data-testid="structured-view-fallback"]')).toBeTruthy();
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
    const wf = treeToWorkflow(
      [container('tryCatch', {}, { success: [step(n('t'))], failure: [step(n('c'))], out: [step(n('fin'))] })],
      { idGen: ids() },
    );
    const f = TestBed.createComponent(StructuredViewComponent);
    f.componentRef.setInput('workflow', wf);
    f.detectChanges();
    expect(f.nativeElement.querySelector('[data-testid="item-delete"]')).toBeFalsy();
    expect(f.nativeElement.querySelector('[data-testid="structured-view-fallback"]')).toBeTruthy();
  });
});
