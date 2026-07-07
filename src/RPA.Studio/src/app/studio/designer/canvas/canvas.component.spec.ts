import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WorkflowVersion } from '../../../shared/models/workflow.model';
import { CanvasComponent } from './canvas.component';

describe('CanvasComponent', () => {
  let fixture: ComponentFixture<CanvasComponent>;
  let component: CanvasComponent;

  beforeAll(() => {
    if (!document.elementsFromPoint) {
      document.elementsFromPoint = () => [];
    }
  });

  async function ready(): Promise<void> {
    fixture.detectChanges();
    await component.initialized;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CanvasComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(CanvasComponent);
    component = fixture.componentInstance;
  });

  it('creates a Rete.js editor and area', async () => {
    await ready();
    expect(component.editor).toBeDefined();
    expect(component.area).toBeDefined();
    expect(component.isReady()).toBe(true);
  });

  it('renders the rete canvas container and toolbar controls', async () => {
    await ready();
    const canvas = fixture.nativeElement.querySelector('[data-testid="rete-canvas"]');
    const zoomIn = fixture.nativeElement.querySelector('[data-testid="canvas-zoom-in"]');
    expect(canvas).toBeTruthy();
    expect(zoomIn).toBeTruthy();
  });

  it('adds a node and returns its id', async () => {
    await ready();
    const id = await component.addNode('Web.Click');
    expect(id).toBeTruthy();
    expect(component.editor.getNodes().length).toBe(1);
    expect(component.editor.getNode(id)?.activityId).toBe('Web.Click');
  });

  it('positions a new node at the requested coordinates', async () => {
    await ready();
    const id = await component.addNode('Web.Click', { position: { x: 300, y: 150 } });
    const view = component.area.nodeViews.get(id);
    expect(view?.position).toEqual({ x: 300, y: 150 });
  });

  it('connects two nodes and returns a connection id', async () => {
    await ready();
    const a = await component.addNode('A');
    const b = await component.addNode('B');
    const connId = await component.connectNodes(a, b);
    expect(connId).toBeTruthy();
    expect(component.editor.getConnections().length).toBe(1);
  });

  it('refuses to connect a node to itself', async () => {
    await ready();
    const a = await component.addNode('A');
    const connId = await component.connectNodes(a, a);
    expect(connId).toBeNull();
    expect(component.editor.getConnections().length).toBe(0);
  });

  it('deletes a node and its incident connections', async () => {
    await ready();
    const a = await component.addNode('A');
    const b = await component.addNode('B');
    await component.connectNodes(a, b);
    const removed = await component.deleteNode(a);
    expect(removed).toBe(true);
    expect(component.editor.getNodes().length).toBe(1);
    expect(component.editor.getConnections().length).toBe(0);
  });

  it('deletes a single connection', async () => {
    await ready();
    const a = await component.addNode('A');
    const b = await component.addNode('B');
    const connId = await component.connectNodes(a, b);
    const removed = await component.deleteConnection(connId!);
    expect(removed).toBe(true);
    expect(component.editor.getConnections().length).toBe(0);
  });

  it('zooms in and out within clamped bounds', async () => {
    await ready();
    const base = component.getZoom();
    await component.zoomIn();
    expect(component.getZoom()).toBeGreaterThan(base);
    await component.zoomOut();
    expect(component.getZoom()).toBeCloseTo(base, 5);
    await component.setZoom(99);
    expect(component.getZoom()).toBeLessThanOrEqual(3);
    await component.setZoom(0.001);
    expect(component.getZoom()).toBeGreaterThanOrEqual(0.2);
  });

  it('pans the viewport to the given coordinates', async () => {
    await ready();
    await component.pan(120, -40);
    expect(component.area.area.transform.x).toBe(120);
    expect(component.area.area.transform.y).toBe(-40);
  });

  it('undoes and redoes node creation', async () => {
    await ready();
    await component.addNode('A');
    expect(component.editor.getNodes().length).toBe(1);
    await component.undo();
    expect(component.editor.getNodes().length).toBe(0);
    await component.redo();
    expect(component.editor.getNodes().length).toBe(1);
  });

  it('emits nodeSelect when a node is selected', async () => {
    await ready();
    const id = await component.addNode('A');
    let emitted: string | null = 'unset';
    component.nodeSelect.subscribe((v) => (emitted = v));
    component.select(id);
    expect(emitted).toBe(id);
    expect(component.selected).toBe(id);
  });

  it('emits graphChanged with a serialisable workflow after mutation', async () => {
    await ready();
    let graph: WorkflowVersion | undefined;
    component.graphChanged.subscribe((g) => (graph = g));
    await component.addNode('Web.Click', { position: { x: 10, y: 20 } });
    expect(graph).toBeDefined();
    expect(graph!.nodes.length).toBe(1);
    expect(graph!.nodes[0].activity).toBe('Web.Click');
    expect(graph!.nodes[0].position).toEqual({ x: 10, y: 20 });
    expect(graph!.schemaVersion).toBe('1.0');
  });

  it('serializes nodes, positions and connections into workflow JSON', async () => {
    await ready();
    const a = await component.addNode('A', { position: { x: 0, y: 0 } });
    const b = await component.addNode('B', { position: { x: 200, y: 0 } });
    await component.connectNodes(a, b);
    const wf = component.serialize();
    expect(wf.nodes.length).toBe(2);
    expect(wf.connections.length).toBe(1);
    expect(wf.connections[0].from).toBe(a);
    expect(wf.connections[0].to).toBe(b);
    expect(wf.connections[0].fromPort).toBe('out');
  });

  it('loads a workflow and round-trips node/connection ids by position', async () => {
    await ready();
    const wf: WorkflowVersion = {
      schemaVersion: '1.0',
      id: '11111111-1111-1111-1111-111111111111',
      name: 'demo',
      version: '1.0.0',
      nodes: [
        { id: 'n1', type: 'activity', activity: 'Excel.Read', position: { x: 40, y: 40 } },
        { id: 'n2', type: 'log', position: { x: 40, y: 200 } },
      ],
      connections: [{ from: 'n1', to: 'n2', fromPort: 'out' }],
    };
    await component.loadWorkflow(wf);
    expect(component.editor.getNodes().length).toBe(2);
    expect(component.editor.getConnections().length).toBe(1);
    const serialized = component.serialize();
    expect(serialized.nodes.find((n) => n.activity === 'Excel.Read')?.position).toEqual({
      x: 40,
      y: 40,
    });
  });

  it('clears the graph', async () => {
    await ready();
    await component.addNode('A');
    await component.addNode('B');
    await component.clear();
    expect(component.editor.getNodes().length).toBe(0);
  });

  it('throws when mutating a read-only canvas', async () => {
    component.readOnly = true;
    await ready();
    await expect(component.addNode('A')).rejects.toThrow('read-only');
  });

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

    it('keeps the connection overlay and pending path non-hit-testable (drop must reach the node card)', async () => {
      await ready();
      const a = await component.addNode('A');
      component.beginConnection(a);

      const svg = fixture.nativeElement.querySelector('[data-testid="canvas-connections"]');
      expect(svg.getAttribute('pointer-events')).toBe('none');

      const pending = fixture.nativeElement.querySelector(
        '[data-testid="canvas-connection-pending"]',
      );
      expect(pending.getAttribute('pointer-events')).toBe('none');
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

    it('clears selectedConnection when the selected connection is removed via deleteConnection', async () => {
      await ready();
      const a = await component.addNode('A');
      const b = await component.addNode('B');
      const connId = await component.connectNodes(a, b);

      component.selectConnection(connId!);
      expect(component.selectedConnection).toBe(connId);

      await component.deleteConnection(connId!);
      expect(component.selectedConnection).toBeNull();
    });

    it('clears selectedConnection when deleteNode cascades removal of the selected connection', async () => {
      await ready();
      const a = await component.addNode('A');
      const b = await component.addNode('B');
      const connId = await component.connectNodes(a, b);

      component.selectConnection(connId!);
      expect(component.selectedConnection).toBe(connId);

      await component.deleteNode(a);
      expect(component.selectedConnection).toBeNull();
    });
  });

  describe('read-only connection drag guard', () => {
    it('beginConnection does not throw on a read-only canvas', async () => {
      await ready();
      const a = await component.addNode('A');
      component.readOnly = true;
      expect(() => component.beginConnection(a)).not.toThrow();
    });

    it('completeConnection resolves to null on a read-only canvas and creates no connections', async () => {
      await ready();
      const a = await component.addNode('A');
      const b = await component.addNode('B');
      component.readOnly = true;

      component.beginConnection(a);
      const connId = await component.completeConnection(b);

      expect(connId).toBeNull();
      expect(component.editor.getConnections().length).toBe(0);
    });
  });
});
