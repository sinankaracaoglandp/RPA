import { CommonModule } from '@angular/common';
import {
  AfterViewInit,
  ApplicationRef,
  ChangeDetectionStrategy,
  Component,
  ComponentRef,
  ElementRef,
  EnvironmentInjector,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  ViewChild,
  createComponent,
  inject,
} from '@angular/core';
import { ClassicPreset, GetSchemes, NodeEditor } from 'rete';
import { AreaExtensions, AreaPlugin } from 'rete-area-plugin';
import { ConnectionPlugin, Presets as ConnectionPresets } from 'rete-connection-plugin';
import { HistoryExtensions, HistoryPlugin, Presets as HistoryPresets } from 'rete-history-plugin';
import {
  NodePosition,
  WorkflowConnection,
  WorkflowNode,
  WorkflowNodeType,
  WorkflowVersion,
  emptyWorkflow,
} from '../../../shared/models/workflow.model';
import { TranslatePipe } from '../../../core/translate.pipe';
import { CanvasNodeView, NodeComponent } from './node.component';
import { ConnectionComponent } from './connection.component';

/** Rete node carrying the workflow-schema metadata for a single step. */
export class FlowNode extends ClassicPreset.Node {
  width = 180;
  height = 90;
  nodeType: WorkflowNodeType;
  activityId?: string;
  properties: Record<string, unknown>;

  constructor(
    label: string,
    nodeType: WorkflowNodeType,
    activityId?: string,
    properties: Record<string, unknown> = {},
  ) {
    super(label);
    this.nodeType = nodeType;
    this.activityId = activityId;
    this.properties = properties;
    const socket = new ClassicPreset.Socket('flow');
    this.addInput('in', new ClassicPreset.Input(socket, 'in', true));
    this.addOutput('out', new ClassicPreset.Output(socket, 'out', true));
  }
}

type Schemes = GetSchemes<
  FlowNode,
  ClassicPreset.Connection<ClassicPreset.Node, ClassicPreset.Node>
>;
type AreaExtra = never;
type FlowConnection = ClassicPreset.Connection<ClassicPreset.Node, ClassicPreset.Node>;
/** Shape of the render/mutation signals we intercept on the area pipeline. */
interface PipeContext {
  type: string;
  data?: unknown;
}

const ZOOM_STEP = 1.15;
const ZOOM_MIN = 0.2;
const ZOOM_MAX = 3;

/**
 * The workflow design surface. Wraps a Rete.js 2 editor (area / connection /
 * history plugins) and exposes a small, stable manipulation API that the rest
 * of the Studio (toolbox, properties panel, debugger) builds on.
 */
@Component({
  selector: 'app-canvas',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './canvas.component.html',
  styleUrls: ['./canvas.component.scss'],
})
export class CanvasComponent implements AfterViewInit, OnDestroy {
  @ViewChild('reteContainer', { static: true }) reteContainer!: ElementRef<HTMLElement>;

  /** Workflow to load once the editor is ready. */
  @Input() workflow?: WorkflowVersion;
  /** Disables mutation (used by the read-only debugger view). */
  @Input() readOnly = false;
  /** Node ids that carry a breakpoint (debug mode). */
  @Input()
  set breakpointNodeIds(ids: string[]) {
    this._breakpointNodeIds = new Set(ids ?? []);
    this.refreshViews();
  }
  get breakpointNodeIds(): string[] {
    return [...this._breakpointNodeIds];
  }
  /** Node currently paused on during execution (debug mode). */
  @Input()
  set currentNodeId(id: string | null) {
    this._currentNodeId = id;
    this.refreshViews();
  }
  get currentNodeId(): string | null {
    return this._currentNodeId;
  }

  private _breakpointNodeIds = new Set<string>();
  private _currentNodeId: string | null = null;

  @Output() readonly nodeSelect = new EventEmitter<string | null>();
  @Output() readonly graphChanged = new EventEmitter<WorkflowVersion>();

  editor!: NodeEditor<Schemes>;
  area!: AreaPlugin<Schemes, AreaExtra>;
  history!: HistoryPlugin<Schemes>;

  private connectionPlugin!: ConnectionPlugin<Schemes, AreaExtra>;
  private selectedNodeId: string | null = null;
  private ready = false;
  private suppressEvents = false;
  private pendingConnectionFrom: string | null = null;
  private pendingPath?: SVGPathElement;
  private selectedConnectionId: string | null = null;

  /** Resolves once the editor and plugins are wired up (awaited by tests). */
  initialized: Promise<void> = Promise.resolve();

  private readonly nodeRefs = new Map<string, ComponentRef<NodeComponent>>();
  private connectionSvg?: SVGSVGElement;
  private connectionGroup?: SVGGElement;

  private readonly appRef = inject(ApplicationRef);
  private readonly envInjector = inject(EnvironmentInjector);
  private readonly host = inject(ElementRef<HTMLElement>);

  ngAfterViewInit(): void {
    this.initialized = (async () => {
      await this.setup();
      if (this.workflow) {
        await this.loadWorkflow(this.workflow);
      }
    })();
  }

  ngOnDestroy(): void {
    this.nodeRefs.forEach((ref) => ref.destroy());
    this.nodeRefs.clear();
    this.area?.destroy();
  }

  // --- initialisation ------------------------------------------------------

  private async setup(): Promise<void> {
    this.ensureResizeObserver();
    const container = this.reteContainer.nativeElement;

    this.editor = new NodeEditor<Schemes>();
    this.area = new AreaPlugin<Schemes, AreaExtra>(container);
    this.connectionPlugin = new ConnectionPlugin<Schemes, AreaExtra>();
    this.history = new HistoryPlugin<Schemes>();

    this.connectionPlugin.addPreset(ConnectionPresets.classic.setup());
    HistoryExtensions.keyboard(this.history);
    this.history.addPreset(HistoryPresets.classic.setup());

    this.editor.use(this.area);
    // Rete's Scope typing requires a render-extra generic to line up the
    // connection plugin's pseudo-connection render signal with the area; we
    // render manually, so bridge the boundary with a cast.
    this.area.use(this.connectionPlugin as unknown as Parameters<typeof this.area.use>[0]);
    this.area.use(this.history);

    AreaExtensions.selectableNodes(this.area, AreaExtensions.selector(), {
      accumulating: AreaExtensions.accumulateOnCtrl(),
    });
    AreaExtensions.simpleNodesOrder(this.area);

    this.setupConnectionLayer(container);
    this.registerRenderPipe();
    this.registerChangePipes();

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

    this.ready = true;
  }

  /** jsdom (unit tests) lacks ResizeObserver, which AreaPlugin subscribes to. */
  private ensureResizeObserver(): void {
    const g = globalThis as unknown as { ResizeObserver?: unknown };
    if (typeof g.ResizeObserver === 'undefined') {
      g.ResizeObserver = class {
        observe(): void {}
        unobserve(): void {}
        disconnect(): void {}
      };
    }
  }

  /** Intercept Rete render events and mount our Angular node component. */
  private registerRenderPipe(): void {
    this.area.addPipe((context) => {
      const ctx = context as unknown as PipeContext;
      if (ctx.type === 'render') {
        this.mountNode(ctx.data as { element: HTMLElement; type: string; payload: unknown });
      } else if (ctx.type === 'unmount') {
        this.unmountNode(ctx.data as { element: HTMLElement });
      }
      return context;
    });
  }

  /** Redraw connections and surface selection/position changes. */
  private registerChangePipes(): void {
    this.area.addPipe((context) => {
      const ctx = context as unknown as PipeContext;
      switch (ctx.type) {
        case 'nodetranslated':
        case 'translated':
        case 'zoomed':
          this.redrawConnections();
          break;
        case 'nodepicked': {
          const id = (ctx.data as { id: string } | undefined)?.id ?? null;
          this.setSelected(id);
          break;
        }
      }
      return context;
    });

    this.editor.addPipe((context) => {
      const type = (context as { type?: string }).type;
      if (type === 'connectionremoved') {
        const removedId = (context as { data?: { id?: string } }).data?.id;
        if (removedId && this.selectedConnectionId === removedId) {
          this.selectedConnectionId = null;
        }
      }
      if (
        !this.suppressEvents &&
        (type === 'nodecreated' ||
          type === 'noderemoved' ||
          type === 'connectioncreated' ||
          type === 'connectionremoved')
      ) {
        this.redrawConnections();
        this.emitChange();
      }
      return context;
    });
  }

  // --- node rendering (Angular bridge) -------------------------------------

  private mountNode(data: { element: HTMLElement; type: string; payload: unknown }): void {
    if (data?.type !== 'node') {
      return;
    }
    const node = data.payload as FlowNode;
    try {
      // Aynı node yeniden render ediliyorsa önce eski görünümü kaldır —
      // yeni bileşen mount edildikten SONRA destroy etmek (eski kod) aynı
      // host element'i paylaşan yeni DOM'u da siliyordu (kart "kayboluyor").
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
      ref.instance.connectStart.subscribe((id: string) => this.beginConnection(id));
      ref.instance.connectDrop.subscribe((id: string) => void this.completeConnection(id));
      this.appRef.attachView(ref.hostView);
      ref.changeDetectorRef.detectChanges();
      this.nodeRefs.set(node.id, ref);
    } catch {
      // Headless/edge rendering failures must never corrupt the graph model.
    }
  }

  private unmountNode(data: { element: HTMLElement }): void {
    for (const [id, ref] of this.nodeRefs) {
      if (ref.location.nativeElement === data?.element) {
        ref.destroy();
        this.nodeRefs.delete(id);
        break;
      }
    }
  }

  private toView(node: FlowNode): CanvasNodeView {
    return {
      id: node.id,
      label: node.label,
      nodeType: node.nodeType,
      activityId: node.activityId,
      selected: node.id === this.selectedNodeId,
      breakpoint: this._breakpointNodeIds.has(node.id),
      current: node.id === this._currentNodeId,
    };
  }

  private refreshViews(): void {
    for (const [id, ref] of this.nodeRefs) {
      const node = this.editor.getNode(id);
      if (node) {
        ref.setInput('node', this.toView(node));
        ref.changeDetectorRef.detectChanges();
      }
    }
  }

  // --- connection overlay --------------------------------------------------

  private setupConnectionLayer(container: HTMLElement): void {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.classList.add('canvas-connections');
    svg.setAttribute('data-testid', 'canvas-connections');
    // Stil sayfası henüz uygulanmamış olsa bile (ör. FOUC penceresi) overlay'in
    // hit-testable olmaması garanti edilsin — asıl kural yine SCSS'te.
    svg.setAttribute('pointer-events', 'none');
    const group = document.createElementNS('http://www.w3.org/2000/svg', 'g');
    svg.appendChild(group);
    container.appendChild(svg);
    this.connectionSvg = svg;
    this.connectionGroup = group;
  }

  private redrawConnections(): void {
    const group = this.connectionGroup;
    if (!group) {
      return;
    }
    const transform = this.area.area.transform;
    group.setAttribute(
      'transform',
      `translate(${transform.x} ${transform.y}) scale(${transform.k})`,
    );
    group.replaceChildren();
    for (const conn of this.editor.getConnections()) {
      const from = this.socketPosition(conn.source, 'out');
      const to = this.socketPosition(conn.target, 'in');
      if (!from || !to) {
        continue;
      }
      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      path.setAttribute('d', ConnectionComponent.buildPath(from, to));
      path.setAttribute(
        'class',
        conn.id === this.selectedConnectionId
          ? 'canvas-connections__path canvas-connections__path--selected'
          : 'canvas-connections__path',
      );
      path.setAttribute('pointer-events', 'stroke');
      path.setAttribute('data-connection-id', conn.id);
      path.setAttribute('data-testid', 'canvas-connection-path');
      path.setAttribute('fill', 'none');
      group.appendChild(path);
    }
  }

  private socketPosition(nodeId: string, port: 'in' | 'out'): NodePosition | null {
    const view = this.area.nodeViews.get(nodeId);
    const node = this.editor.getNode(nodeId);
    if (!view || !node) {
      return null;
    }
    // Gerçek render boyutunu kullan: FlowNode.width/height sabitleri (180x90)
    // kartın CSS'ten gelen fiili boyutundan farklı — uçlar soketlere oturmalı.
    const card = this.nodeRefs
      .get(nodeId)
      ?.location.nativeElement.querySelector('[data-testid="canvas-node"]') as HTMLElement | null;
    const width = card?.offsetWidth || node.width;
    const height = card?.offsetHeight || node.height;
    return {
      x: view.position.x + width / 2,
      y: view.position.y + (port === 'out' ? height : 0),
    };
  }

  // --- public API ----------------------------------------------------------

  /** Adds a node (activity by default) and returns its generated id. */
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
    await this.editor.addNode(node);
    const position = options.position ?? this.nextPosition();
    await this.area.translate(node.id, position);
    // Re-emit so subscribers observe the final position (the nodecreated
    // signal fires before the node is translated into place).
    this.emitChange();
    return node.id;
  }

  /** Connects two nodes (source out → target in). Returns the connection id. */
  async connectNodes(fromId: string, toId: string): Promise<string | null> {
    this.assertWritable();
    const source = this.editor.getNode(fromId);
    const target = this.editor.getNode(toId);
    if (!source || !target || fromId === toId) {
      return null;
    }
    const duplicate = this.editor
      .getConnections()
      .some((c) => c.source === fromId && c.target === toId);
    if (duplicate) {
      return null;
    }
    const connection: FlowConnection = new ClassicPreset.Connection<
      ClassicPreset.Node,
      ClassicPreset.Node
    >(source, 'out', target, 'in');
    await this.editor.addConnection(connection);
    return connection.id;
  }

  /** Out soketinden bağlantı sürüklemesi başlatır (geçici kesikli çizgi). */
  beginConnection(nodeId: string): void {
    if (this.readOnly) {
      return;
    }
    if (!this.editor.getNode(nodeId)) {
      return;
    }
    this.pendingConnectionFrom = nodeId;
    this.ensurePendingPath();
    // Basıldığı anda görsel geri bildirim: imleç henüz oynamadan soketten
    // kısa bir başlangıç çizgisi göster (pointermove gelince gerçek uca uzar).
    const from = this.socketPosition(nodeId, 'out');
    if (from && this.pendingPath) {
      this.pendingPath.setAttribute(
        'd',
        ConnectionComponent.buildPath(from, { x: from.x, y: from.y + 24 }),
      );
    }
  }

  /** Sürüklemeyi hedef node üzerinde tamamlar; kural ihlalinde null döner. */
  async completeConnection(targetNodeId: string): Promise<string | null> {
    if (this.readOnly) {
      this.cancelConnection();
      return null;
    }
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

  async deleteNode(nodeId: string): Promise<boolean> {
    this.assertWritable();
    for (const conn of this.editor.getConnections()) {
      if (conn.source === nodeId || conn.target === nodeId) {
        await this.editor.removeConnection(conn.id);
      }
    }
    const removed = await this.editor.removeNode(nodeId);
    if (this.selectedNodeId === nodeId) {
      this.setSelected(null);
    }
    return removed;
  }

  async deleteConnection(connectionId: string): Promise<boolean> {
    this.assertWritable();
    return this.editor.removeConnection(connectionId);
  }

  undo(): Promise<void> {
    return this.history.undo();
  }

  redo(): Promise<void> {
    return this.history.redo();
  }

  // --- viewport ------------------------------------------------------------

  getZoom(): number {
    return this.area.area.transform.k;
  }

  zoomIn(): Promise<unknown> {
    return this.setZoom(this.getZoom() * ZOOM_STEP);
  }

  zoomOut(): Promise<unknown> {
    return this.setZoom(this.getZoom() / ZOOM_STEP);
  }

  setZoom(k: number): Promise<unknown> {
    const clamped = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, k));
    return Promise.resolve(this.area.area.zoom(clamped, 0, 0));
  }

  pan(x: number, y: number): Promise<unknown> {
    return Promise.resolve(this.area.area.translate(x, y));
  }

  async zoomToFit(): Promise<void> {
    await AreaExtensions.zoomAt(this.area, this.editor.getNodes());
  }

  // --- selection -----------------------------------------------------------

  select(nodeId: string | null): void {
    this.setSelected(nodeId);
  }

  private setSelected(nodeId: string | null): void {
    if (this.selectedNodeId === nodeId) {
      return;
    }
    this.selectedNodeId = nodeId;
    this.refreshViews();
    this.nodeSelect.emit(nodeId);
  }

  get selected(): string | null {
    return this.selectedNodeId;
  }

  /** Activity id of the given node, if any (used by the properties panel). */
  getNodeActivityId(nodeId: string): string | undefined {
    return this.editor?.getNode(nodeId)?.activityId;
  }

  /** Current properties bag of the given node (used by the properties panel). */
  getNodeProperties(nodeId: string): Record<string, unknown> {
    return this.editor?.getNode(nodeId)?.properties ?? {};
  }

  /** Replaces a node's properties bag (e.g. from a property editor form) and notifies subscribers. */
  updateNodeProperties(nodeId: string, properties: Record<string, unknown>): void {
    const node = this.editor?.getNode(nodeId);
    if (!node) {
      return;
    }
    node.properties = properties;
    this.emitChange();
  }

  // --- (de)serialisation ---------------------------------------------------

  serialize(): WorkflowVersion {
    const base = this.workflow ?? emptyWorkflow();
    const nodes: WorkflowNode[] = this.editor.getNodes().map((node) => {
      const view = this.area.nodeViews.get(node.id);
      const wf: WorkflowNode = { id: node.id, type: node.nodeType };
      if (node.activityId) {
        wf.activity = node.activityId;
      }
      if (node.properties && Object.keys(node.properties).length > 0) {
        wf.properties = node.properties;
      }
      if (view) {
        wf.position = { x: view.position.x, y: view.position.y };
      }
      return wf;
    });
    const connections: WorkflowConnection[] = this.editor.getConnections().map((conn) => ({
      from: conn.source,
      to: conn.target,
      fromPort: 'out',
    }));
    return { ...base, nodes, connections };
  }

  async loadWorkflow(workflow: WorkflowVersion): Promise<void> {
    this.workflow = workflow;
    await this.clear();
    this.suppressEvents = true;
    const idMap = new Map<string, string>();
    try {
      for (const wfNode of workflow.nodes) {
        const node = new FlowNode(
          (wfNode['label'] as string) ?? wfNode.activity ?? wfNode.type,
          wfNode.type,
          wfNode.activity,
          (wfNode.properties as Record<string, unknown>) ?? {},
        );
        await this.editor.addNode(node);
        idMap.set(wfNode.id, node.id);
        if (wfNode.position) {
          await this.area.translate(node.id, wfNode.position);
        }
      }
      for (const conn of workflow.connections) {
        const from = idMap.get(conn.from);
        const to = idMap.get(conn.to);
        if (!from || !to) {
          continue;
        }
        const source = this.editor.getNode(from);
        const target = this.editor.getNode(to);
        if (source && target) {
          const connection: FlowConnection = new ClassicPreset.Connection<
            ClassicPreset.Node,
            ClassicPreset.Node
          >(source, 'out', target, 'in');
          await this.editor.addConnection(connection);
        }
      }
    } finally {
      this.suppressEvents = false;
    }
    this.history.clear();
    this.redrawConnections();
  }

  async clear(): Promise<void> {
    this.suppressEvents = true;
    try {
      for (const conn of [...this.editor.getConnections()]) {
        await this.editor.removeConnection(conn.id);
      }
      for (const node of [...this.editor.getNodes()]) {
        await this.editor.removeNode(node.id);
      }
    } finally {
      this.suppressEvents = false;
    }
    this.setSelected(null);
    this.redrawConnections();
  }

  isReady(): boolean {
    return this.ready;
  }

  // --- helpers -------------------------------------------------------------

  private emitChange(): void {
    this.graphChanged.emit(this.serialize());
  }

  private assertWritable(): void {
    if (this.readOnly) {
      throw new Error('Canvas is read-only');
    }
  }

  private nextPosition(): NodePosition {
    const count = this.editor.getNodes().length;
    return { x: 80 + (count % 4) * 220, y: 80 + Math.floor(count / 4) * 140 };
  }

  private ensurePendingPath(): void {
    if (!this.connectionGroup || this.pendingPath) {
      return;
    }
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('class', 'canvas-connections__path canvas-connections__path--pending');
    path.setAttribute('data-testid', 'canvas-connection-pending');
    path.setAttribute('fill', 'none');
    // Bu çizgi imleci birebir takip eder; hit-testable olursa pointerup
    // hedef node yerine bu path'e düşer ve bağlantı tamamlanmaz.
    path.setAttribute('pointer-events', 'none');
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

  onDeleteKey(): void {
    if (this.selectedConnectionId) {
      void this.deleteSelectedConnection();
    } else if (this.selectedNodeId) {
      void this.deleteNode(this.selectedNodeId);
    }
  }
}
