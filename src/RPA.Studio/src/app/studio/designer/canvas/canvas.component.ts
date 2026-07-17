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
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
  ViewChild,
  createComponent,
  inject,
} from '@angular/core';
import { ClassicPreset, GetSchemes, NodeEditor } from 'rete';
import { AreaExtensions, AreaPlugin } from 'rete-area-plugin';
import { ConnectionPlugin, Presets as ConnectionPresets } from 'rete-connection-plugin';
import { HistoryExtensions, HistoryPlugin, Presets as HistoryPresets } from 'rete-history-plugin';
import {
  ConnectionPort,
  ConnectionTargetPort,
  NodePosition,
  WorkflowConnection,
  WorkflowNode,
  WorkflowNodeType,
  WorkflowVersion,
  emptyWorkflow,
} from '../../../shared/models/workflow.model';
import { TranslatePipe } from '../../../core/translate.pipe';
import { CanvasNodeSelectEvent, CanvasNodeView, NodeComponent } from './node.component';
import { ConnectionComponent } from './connection.component';

const CONTROL_ACTIVITY_TO_NODE: Partial<Record<string, WorkflowNodeType>> = {
  'Logic.Assign': 'assign',
  'Logic.If': 'if',
  'Logic.ForEach': 'forEach',
  'Logic.For': 'for',
  'Logic.While': 'while',
  'Logic.TryCatch': 'tryCatch',
  'Logic.Delay': 'delay',
  'Logic.Log': 'log',
  'Logic.Checkpoint': 'checkpoint',
  'Logic.UserPrompt': 'userPrompt',
  'Logic.Terminate': 'terminate',
};

const NODE_TYPE_TO_CONTROL_ACTIVITY: Partial<Record<WorkflowNodeType, string>> = {
  assign: 'Logic.Assign',
  if: 'Logic.If',
  forEach: 'Logic.ForEach',
  for: 'Logic.For',
  while: 'Logic.While',
  tryCatch: 'Logic.TryCatch',
  delay: 'Logic.Delay',
  log: 'Logic.Log',
  checkpoint: 'Logic.Checkpoint',
  userPrompt: 'Logic.UserPrompt',
  terminate: 'Logic.Terminate',
};

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
    for (const input of getInputPorts(nodeType)) {
      this.addInput(input, new ClassicPreset.Input(socket, input, true));
    }
    for (const output of getOutputPorts(nodeType)) {
      this.addOutput(output.port, new ClassicPreset.Output(socket, output.label, true));
    }
  }
}

type Schemes = GetSchemes<
  FlowNode,
  ClassicPreset.Connection<ClassicPreset.Node, ClassicPreset.Node>
>;
type AreaExtra = never;
type FlowConnection = ClassicPreset.Connection<ClassicPreset.Node, ClassicPreset.Node>;
interface CanvasClipboard {
  nodes: WorkflowNode[];
  connections: WorkflowConnection[];
}

function getOutputPorts(nodeType: WorkflowNodeType): Array<{
  port: ConnectionPort;
  label: string;
  tone?: 'default' | 'positive' | 'negative' | 'neutral';
}> {
  switch (nodeType) {
    case 'while':
    case 'for':
    case 'forEach':
      return [
        { port: 'body', label: 'Body', tone: 'positive' },
        { port: 'exit', label: 'Exit', tone: 'negative' },
      ];
    case 'if':
      return [
        { port: 'true', label: 'True', tone: 'positive' },
        { port: 'false', label: 'False', tone: 'negative' },
      ];
    case 'tryCatch':
      return [
        { port: 'success', label: 'Try', tone: 'positive' },
        { port: 'failure', label: 'Catch', tone: 'negative' },
        { port: 'out', label: 'Finally', tone: 'neutral' },
      ];
    default:
      return [{ port: 'out', label: 'Next', tone: 'default' }];
  }
}

function getInputPorts(nodeType: WorkflowNodeType): ConnectionTargetPort[] {
  return ['while', 'for', 'forEach'].includes(nodeType) ? ['in', 'loop-back'] : ['in'];
}
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
export class CanvasComponent implements AfterViewInit, OnChanges, OnDestroy {
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
  private readonly selectedNodeIds = new Set<string>();
  private selectedNodeOrder: string[] = [];
  private ready = false;
  private suppressEvents = false;
  private pendingConnectionFrom: { nodeId: string; port: ConnectionPort } | null = null;
  private pendingPath?: SVGPathElement;
  private selectedConnectionId: string | null = null;

  /** Resolves once the editor and plugins are wired up (awaited by tests). */
  initialized: Promise<void> = Promise.resolve();
  /** Resolves after the latest workflow input has been loaded into the editor. */
  workflowLoaded: Promise<void> = Promise.resolve();

  private readonly nodeRefs = new Map<string, ComponentRef<NodeComponent>>();
  private connectionSvg?: SVGSVGElement;
  private connectionGroup?: SVGGElement;
  private loadedWorkflowKey: string | null = null;
  private clipboard?: CanvasClipboard;

  private readonly appRef = inject(ApplicationRef);
  private readonly envInjector = inject(EnvironmentInjector);
  private readonly host = inject(ElementRef<HTMLElement>);

  ngAfterViewInit(): void {
    this.initialized = (async () => {
      await this.setup();
      await this.loadWorkflowInput();
    })();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ('workflow' in changes && this.ready) {
      this.workflowLoaded = this.initialized.then(() => this.loadWorkflowInput());
    }
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
    container.addEventListener('wheel', () => {
      requestAnimationFrame(() => this.redrawConnections());
    }, { passive: true });
    container.addEventListener('keydown', (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        this.cancelConnection();
        return;
      }
      if ((e.ctrlKey || e.metaKey) && !e.shiftKey && e.key.toLowerCase() === 'c') {
        e.preventDefault();
        this.copySelection();
        return;
      }
      if ((e.ctrlKey || e.metaKey) && !e.shiftKey && e.key.toLowerCase() === 'v') {
        e.preventDefault();
        void this.pasteClipboard();
      }
    });
    container.addEventListener('pointerdown', (e: PointerEvent) => {
      const target = e.target as HTMLElement | SVGElement | null;
      if (!target) {
        return;
      }

      const clickedConnection = target.getAttribute?.('data-connection-id');
      if (clickedConnection) {
        return;
      }

      const clickedNode = target.closest?.('[data-node-id]');
      if (!clickedNode) {
        if (this.selectedConnectionId) {
          this.selectConnection(null);
        }
        if (this.selectedNodeId) {
          this.setSelected(null);
        }
      }
    });
    // Bağlantı path'ine tıklayınca seç (event delegation — path'ler her çizimde yenilenir).
    this.connectionSvg?.addEventListener('click', (e: MouseEvent) => {
      const target = e.target as SVGElement;
      if (target?.getAttribute?.('data-connection-delete') === 'true') {
        e.preventDefault();
        e.stopPropagation();
        void this.deleteSelectedConnection();
        return;
      }
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
        const data = ctx.data as { element: HTMLElement; type: string; payload: unknown };
        if (data?.type === 'connection') {
          data.element.style.display = 'none';
          return context;
        }
        this.mountNode(data);
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
      ref.instance.nodeSelect.subscribe((event: CanvasNodeSelectEvent) =>
        this.select(event.nodeId, event.additive));
      ref.instance.nodeDelete.subscribe((id: string) => void this.deleteNode(id));
      ref.instance.connectStart.subscribe(({ nodeId, port }) => this.beginConnection(nodeId, port as ConnectionPort));
      ref.instance.connectDrop.subscribe(({ nodeId, port }) =>
        void this.completeConnection(nodeId, port as ConnectionTargetPort));
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
      outputs: getOutputPorts(node.nodeType),
      inputs: getInputPorts(node.nodeType).map((port) => ({
        port,
        label: port === 'loop-back' ? 'Repeat' : 'In',
      })),
      selected: this.selectedNodeIds.has(node.id),
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
    group.removeAttribute('transform');
    group.replaceChildren();
    for (const conn of this.editor.getConnections()) {
      const from = this.socketPosition(
        conn.source,
        ((conn as unknown as { sourceOutput?: ConnectionPort }).sourceOutput ?? 'out'),
      );
      const to = this.socketPosition(
        conn.target,
        ((conn as unknown as { targetInput?: ConnectionTargetPort }).targetInput ?? 'in'),
      );
      if (!from || !to) {
        continue;
      }
      const isSelected = conn.id === this.selectedConnectionId;
      const d = ConnectionComponent.buildPath(from, to);
      const hitPath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      hitPath.setAttribute('d', d);
      hitPath.setAttribute('class', 'canvas-connections__hit');
      hitPath.setAttribute('pointer-events', 'stroke');
      hitPath.setAttribute('data-connection-id', conn.id);
      hitPath.setAttribute('fill', 'none');
      hitPath.style.stroke = 'transparent';
      hitPath.style.strokeWidth = '16px';
      group.appendChild(hitPath);

      const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      path.setAttribute('d', d);
      path.setAttribute('class', isSelected ? 'canvas-connections__path canvas-connections__path--selected' : 'canvas-connections__path');
      path.setAttribute('pointer-events', 'none');
      path.setAttribute('data-connection-id', conn.id);
      path.setAttribute('data-testid', 'canvas-connection-path');
      path.setAttribute('fill', 'none');
      path.style.stroke = isSelected ? '#f59e0b' : '#1f6feb';
      path.style.strokeWidth = isSelected ? '4.5px' : '2.25px';
      path.style.filter = isSelected
        ? 'drop-shadow(0 0 7px rgba(245, 158, 11, 0.9)) drop-shadow(0 0 14px rgba(245, 158, 11, 0.45))'
        : 'drop-shadow(0 1px 1px rgba(31, 111, 235, 0.25))';
      group.appendChild(path);

      if (isSelected) {
        const midpoint = this.connectionMidpoint(from, to);
        const deleteGroup = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        deleteGroup.setAttribute('class', 'canvas-connections__delete');
        deleteGroup.setAttribute('data-connection-id', conn.id);
        deleteGroup.setAttribute('data-connection-delete', 'true');
        deleteGroup.setAttribute('pointer-events', 'all');
        deleteGroup.setAttribute('transform', `translate(${midpoint.x} ${midpoint.y})`);

        const circle = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        circle.setAttribute('r', '12');
        circle.setAttribute('cx', '0');
        circle.setAttribute('cy', '0');
        circle.setAttribute('fill', '#ffffff');
        circle.setAttribute('stroke', '#f59e0b');
        circle.setAttribute('stroke-width', '2.5');
        circle.setAttribute('filter', 'drop-shadow(0 2px 6px rgba(15, 23, 42, 0.22))');
        circle.setAttribute('data-connection-id', conn.id);
        circle.setAttribute('data-connection-delete', 'true');

        const text = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        text.setAttribute('x', '0');
        text.setAttribute('y', '1');
        text.setAttribute('text-anchor', 'middle');
        text.setAttribute('dominant-baseline', 'middle');
        text.setAttribute('font-size', '14');
        text.setAttribute('font-weight', '700');
        text.setAttribute('fill', '#b42318');
        text.setAttribute('data-connection-id', conn.id);
        text.setAttribute('data-connection-delete', 'true');
        text.textContent = '×';

        deleteGroup.appendChild(circle);
        deleteGroup.appendChild(text);
        group.appendChild(deleteGroup);
      }
    }
  }

  private socketPosition(nodeId: string, port: string): NodePosition | null {
    const containerRect = this.reteContainer.nativeElement.getBoundingClientRect();
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
    const socket = this.nodeRefs
      .get(nodeId)
      ?.location.nativeElement.querySelector(`[data-port="${port}"]`) as HTMLElement | null;
    if (socket) {
      const socketRect = socket.getBoundingClientRect();
      return {
        x: socketRect.left - containerRect.left + socketRect.width / 2,
        y: socketRect.top - containerRect.top + socketRect.height / 2,
      };
    }

    const cardRect = card?.getBoundingClientRect();
    if (cardRect) {
      return {
        x: cardRect.left - containerRect.left + cardRect.width / 2,
        y: cardRect.top - containerRect.top + (port === 'in' ? 0 : cardRect.height),
      };
    }

    const transform = this.area.area.transform;
    const width = card?.offsetWidth || node.width;
    const height = card?.offsetHeight || node.height;
    return {
      x: transform.x + (view.position.x + width / 2) * transform.k,
      y: transform.y + (view.position.y + (port === 'in' ? 0 : height)) * transform.k,
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
    const type = options.type ?? (CONTROL_ACTIVITY_TO_NODE[activityId] ?? 'activity');
    const label = options.label ?? activityId;
    const node = new FlowNode(
      label,
      type,
      activityId,
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
  async connectNodes(
    fromId: string,
    toId: string,
    fromPort: ConnectionPort = 'out',
    toPort: ConnectionTargetPort = 'in',
  ): Promise<string | null> {
    this.assertWritable();
    const source = this.editor.getNode(fromId);
    const target = this.editor.getNode(toId);
    if (!source || !target || fromId === toId) {
      return null;
    }
    if (!(fromPort in source.outputs)) {
      return null;
    }
    if (!(toPort in target.inputs)) {
      return null;
    }
    const existingConnections = this.editor.getConnections();
    if (toPort === 'loop-back') {
      const bodyConnection = existingConnections.find((connection) =>
        connection.source === toId &&
        ((connection as unknown as { sourceOutput?: ConnectionPort }).sourceOutput ?? 'out') === 'body');
      if (!bodyConnection || !this.allPathsLeadTo(bodyConnection.target, fromId, existingConnections)) {
        return null;
      }
    } else if (this.isReachable(toId, fromId, existingConnections)) {
      return null;
    }
    if (
      (fromPort === 'body' || fromPort === 'exit') &&
      existingConnections.some((connection) =>
        connection.source === fromId &&
        ((connection as unknown as { sourceOutput?: ConnectionPort }).sourceOutput ?? 'out') === fromPort)
    ) {
      return null;
    }
    if (
      toPort === 'loop-back' &&
      existingConnections.some((connection) =>
        connection.target === toId &&
        ((connection as unknown as { targetInput?: ConnectionTargetPort }).targetInput ?? 'in') === 'loop-back')
    ) {
      return null;
    }
    const duplicate = existingConnections.some(
      (c) =>
        c.source === fromId &&
        c.target === toId &&
        ((c as unknown as { sourceOutput?: ConnectionPort }).sourceOutput ?? 'out') === fromPort,
    );
    if (duplicate) {
      return null;
    }
    const connection: FlowConnection = new ClassicPreset.Connection<
      ClassicPreset.Node,
      ClassicPreset.Node
    >(source, fromPort, target, toPort);
    await this.editor.addConnection(connection);
    return connection.id;
  }

  private isReachable(startId: string, targetId: string, connections: FlowConnection[]): boolean {
    const pending = [startId];
    const visited = new Set<string>();
    while (pending.length) {
      const current = pending.pop()!;
      if (current === targetId) {
        return true;
      }
      if (visited.has(current)) {
        continue;
      }
      visited.add(current);
      for (const connection of connections) {
        const targetPort = ((connection as unknown as { targetInput?: ConnectionTargetPort }).targetInput ?? 'in');
        if (connection.source === current && targetPort !== 'loop-back') {
          pending.push(connection.target);
        }
      }
    }
    return false;
  }

  private allPathsLeadTo(startId: string, targetId: string, connections: FlowConnection[]): boolean {
    const visit = (current: string, visiting: Set<string>, memo: Map<string, boolean>): boolean => {
      if (current === targetId) return true;
      if (memo.has(current)) return memo.get(current)!;
      if (visiting.has(current)) return false;
      visiting.add(current);
      const outgoing = connections.filter((connection) => {
        const targetPort = ((connection as unknown as { targetInput?: ConnectionTargetPort }).targetInput ?? 'in');
        return connection.source === current && targetPort !== 'loop-back';
      });
      const result = outgoing.length > 0 && outgoing.every((connection) => visit(connection.target, visiting, memo));
      visiting.delete(current);
      memo.set(current, result);
      return result;
    };
    return visit(startId, new Set<string>(), new Map<string, boolean>());
  }

  /** Out soketinden bağlantı sürüklemesi başlatır (geçici kesikli çizgi). */
  beginConnection(nodeId: string, fromPort: ConnectionPort = 'out'): void {
    if (this.readOnly) {
      return;
    }
    if (!this.editor.getNode(nodeId)) {
      return;
    }
    this.pendingConnectionFrom = { nodeId, port: fromPort };
    this.ensurePendingPath();
    // Basıldığı anda görsel geri bildirim: imleç henüz oynamadan soketten
    // kısa bir başlangıç çizgisi göster (pointermove gelince gerçek uca uzar).
    const from = this.socketPosition(nodeId, fromPort);
    if (from && this.pendingPath) {
      this.pendingPath.setAttribute(
        'd',
        ConnectionComponent.buildPath(from, { x: from.x, y: from.y + 24 }),
      );
    }
  }

  /** Sürüklemeyi hedef node üzerinde tamamlar; kural ihlalinde null döner. */
  async completeConnection(
    targetNodeId: string,
    targetPort: ConnectionTargetPort = 'in',
  ): Promise<string | null> {
    if (this.readOnly) {
      this.cancelConnection();
      return null;
    }
    const from = this.pendingConnectionFrom;
    this.cancelConnection();
    if (!from) {
      return null;
    }
    return this.connectNodes(from.nodeId, targetNodeId, from.port, targetPort);
  }

  /** Bekleyen bağlantı sürüklemesini iptal eder ve geçici çizgiyi kaldırır. */
  cancelConnection(): void {
    this.pendingConnectionFrom = null;
    this.pendingPath?.remove();
    this.pendingPath = undefined;
  }

  selectConnection(connectionId: string | null): void {
    if (connectionId && this.selectedConnectionId === connectionId) {
      this.selectedConnectionId = null;
      this.redrawConnections();
      return;
    }
    if (connectionId) {
      this.setSelected(null);
      this.reteContainer.nativeElement.focus();
    }
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
    if (this.selectedNodeIds.has(nodeId)) {
      this.selectedNodeIds.delete(nodeId);
      this.selectedNodeOrder = this.selectedNodeOrder.filter((id) => id !== nodeId);
      if (this.selectedNodeId === nodeId) {
        this.selectedNodeId = this.selectedNodeIds.size === 1 ? this.selectedNodeOrder[0] ?? null : null;
      }
      this.refreshViews();
      this.nodeSelect.emit(this.selectedNodeIds.size === 1 ? this.selectedNodeId : null);
    }
    return removed;
  }

  async deleteSelectedNodes(): Promise<boolean> {
    const ids = [...this.selectedNodeIds];
    if (ids.length === 0) {
      return false;
    }
    for (const id of ids) {
      await this.deleteNode(id);
    }
    return true;
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

  containsClientPoint(clientX: number, clientY: number): boolean {
    const rect = this.reteContainer.nativeElement.getBoundingClientRect();
    return clientX >= rect.left && clientX <= rect.right && clientY >= rect.top && clientY <= rect.bottom;
  }

  clientToCanvasPosition(clientX: number, clientY: number): NodePosition {
    const rect = this.reteContainer.nativeElement.getBoundingClientRect();
    const transform = this.area.area.transform;
    return {
      x: (clientX - rect.left - transform.x) / transform.k,
      y: (clientY - rect.top - transform.y) / transform.k,
    };
  }

  zoomIn(): Promise<unknown> {
    return this.setZoom(this.getZoom() * ZOOM_STEP).then((result) => {
      this.redrawConnections();
      return result;
    });
  }

  zoomOut(): Promise<unknown> {
    return this.setZoom(this.getZoom() / ZOOM_STEP).then((result) => {
      this.redrawConnections();
      return result;
    });
  }

  setZoom(k: number): Promise<unknown> {
    const clamped = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, k));
    return Promise.resolve(this.area.area.zoom(clamped, 0, 0));
  }

  pan(x: number, y: number): Promise<unknown> {
    return Promise.resolve(this.area.area.translate(x, y)).then((result) => {
      this.redrawConnections();
      return result;
    });
  }

  async zoomToFit(): Promise<void> {
    await AreaExtensions.zoomAt(this.area, this.editor.getNodes());
    this.redrawConnections();
  }

  // --- selection -----------------------------------------------------------

  select(nodeId: string | null, additive = false): void {
    this.setSelected(nodeId, additive);
  }

  private setSelected(nodeId: string | null, additive = false): void {
    if (nodeId && this.selectedConnectionId) {
      this.selectedConnectionId = null;
      this.redrawConnections();
    }
    if (!nodeId) {
      if (this.selectedNodeIds.size === 0 && this.selectedNodeId === null) {
        return;
      }
      this.selectedNodeIds.clear();
      this.selectedNodeOrder = [];
      this.selectedNodeId = null;
      this.refreshViews();
      this.nodeSelect.emit(null);
      return;
    }

    if (additive) {
      if (this.selectedNodeIds.has(nodeId)) {
        this.selectedNodeIds.delete(nodeId);
        this.selectedNodeOrder = this.selectedNodeOrder.filter((id) => id !== nodeId);
        if (this.selectedNodeId === nodeId) {
          this.selectedNodeId = this.selectedNodeIds.size === 1 ? this.selectedNodeOrder[0] ?? null : null;
        }
      } else {
        this.selectedNodeIds.add(nodeId);
        this.selectedNodeOrder.push(nodeId);
        this.selectedNodeId = nodeId;
      }
      this.refreshViews();
      this.reteContainer.nativeElement.focus();
      this.nodeSelect.emit(this.selectedNodeIds.size === 1 ? this.selectedNodeId : null);
      return;
    }

    if (this.selectedNodeIds.size === 1 && this.selectedNodeId === nodeId) {
      this.selectedNodeIds.clear();
      this.selectedNodeOrder = [];
      this.selectedNodeId = null;
      this.refreshViews();
      this.nodeSelect.emit(null);
      return;
    }

    this.selectedNodeIds.clear();
    this.selectedNodeOrder = [];
    this.selectedNodeIds.add(nodeId);
    this.selectedNodeOrder.push(nodeId);
    this.selectedNodeId = nodeId;
    this.refreshViews();
    this.reteContainer.nativeElement.focus();
    this.nodeSelect.emit(nodeId);
  }

  get selected(): string | null {
    return this.selectedNodeIds.size === 1 ? this.selectedNodeId : null;
  }

  get selectedNodes(): string[] {
    return [...this.selectedNodeOrder];
  }

  private copySelection(): void {
    if (this.selectedNodeIds.size === 0) {
      return;
    }

    const graph = this.serialize();
    const selected = new Set(this.selectedNodeOrder);
    this.clipboard = {
      nodes: this.selectedNodeOrder
        .map((id) => graph.nodes.find((node) => node.id === id))
        .filter((node): node is WorkflowNode => !!node)
        .map((node) => JSON.parse(JSON.stringify(node)) as WorkflowNode),
      connections: graph.connections
        .filter((connection) => selected.has(connection.from) && selected.has(connection.to))
        .map((connection) => ({ ...connection })),
    };
  }

  private async pasteClipboard(): Promise<void> {
    if (!this.clipboard || this.clipboard.nodes.length === 0) {
      return;
    }

    const idMap = new Map<string, string>();
    const pastedIds: string[] = [];

    for (const wfNode of this.clipboard.nodes) {
      const activityId =
        wfNode.type === 'activity' ? wfNode.activity : NODE_TYPE_TO_CONTROL_ACTIVITY[wfNode.type];
      const nextId = await this.addNode(activityId ?? wfNode.type, {
        type: wfNode.type,
        label: (wfNode['label'] as string) ?? activityId ?? wfNode.type,
        position: wfNode.position
          ? { x: wfNode.position.x + 40, y: wfNode.position.y + 40 }
          : undefined,
        properties: this.extractNodeProperties(wfNode),
      });
      idMap.set(wfNode.id, nextId);
      pastedIds.push(nextId);
    }

    for (const connection of this.clipboard.connections) {
      const from = idMap.get(connection.from);
      const to = idMap.get(connection.to);
      if (from && to) {
        await this.connectNodes(from, to, connection.fromPort ?? 'out');
      }
    }

    this.selectedNodeIds.clear();
    this.selectedNodeOrder = [];
    pastedIds.forEach((id) => this.selectedNodeIds.add(id));
    this.selectedNodeOrder.push(...pastedIds);
    this.selectedNodeId = pastedIds.length === 1 ? pastedIds[0] : null;
    this.refreshViews();
    this.nodeSelect.emit(pastedIds.length === 1 ? pastedIds[0] : null);
  }

  /** Activity id of the given node, if any (used by the properties panel). */
  getNodeActivityId(nodeId: string): string | undefined {
    const node = this.editor?.getNode(nodeId);
    return node?.activityId ?? (node ? NODE_TYPE_TO_CONTROL_ACTIVITY[node.nodeType] : undefined);
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
      if (node.nodeType === 'activity' && node.activityId) {
        wf.activity = node.activityId;
      }
      if (
        (node.nodeType === 'activity' || node.nodeType === 'checkpoint') &&
        node.properties &&
        Object.keys(node.properties).length > 0
      ) {
        wf.properties = node.properties;
      }
      this.applyNodePropertiesToWorkflowNode(wf, node);
      if (view) {
        wf.position = { x: view.position.x, y: view.position.y };
      }
      return wf;
    });
    const nodesById = new Map(nodes.map((node) => [node.id, node]));
    const connections: WorkflowConnection[] = [];
    for (const conn of this.editor.getConnections()) {
      const fromPort = ((conn as unknown as { sourceOutput?: ConnectionPort }).sourceOutput ?? 'out');
      const toPort = ((conn as unknown as { targetInput?: ConnectionTargetPort }).targetInput ?? 'in');
      const source = nodesById.get(conn.source);
      if (source?.type === 'tryCatch') {
        if (fromPort === 'success') {
          source['tryNodeId'] = conn.target;
          continue;
        }
        if (fromPort === 'failure') {
          source['catchNodeId'] = conn.target;
          continue;
        }
        if (fromPort === 'out') {
          source['finallyNodeId'] = conn.target;
          continue;
        }
      }
      connections.push({
        from: conn.source,
        to: conn.target,
        fromPort,
        ...(toPort !== 'in' ? { toPort } : {}),
      });
    }
    return { ...base, nodes, connections };
  }

  async loadWorkflow(workflow: WorkflowVersion): Promise<void> {
    this.workflow = workflow;
    this.loadedWorkflowKey = this.workflowKey(workflow);
    await this.clear();
    this.suppressEvents = true;
    const idMap = new Map<string, string>();
    try {
      for (const wfNode of workflow.nodes) {
        const activityId =
          wfNode.type === 'activity' ? wfNode.activity : NODE_TYPE_TO_CONTROL_ACTIVITY[wfNode.type];
        const node = new FlowNode(
          (wfNode['label'] as string) ?? activityId ?? wfNode.type,
          wfNode.type,
          activityId,
          this.extractNodeProperties(wfNode),
        );
        await this.editor.addNode(node);
        idMap.set(wfNode.id, node.id);
        if (wfNode.position) {
          await this.area.translate(node.id, wfNode.position);
        }
      }
      const orderedConnections = [...workflow.connections].sort((left, right) =>
        Number(left.toPort === 'loop-back') - Number(right.toPort === 'loop-back'));
      for (const conn of orderedConnections) {
        const from = idMap.get(conn.from);
        const to = idMap.get(conn.to);
        if (!from || !to) {
          continue;
        }
        await this.connectNodes(from, to, conn.fromPort ?? 'out', conn.toPort ?? 'in');
      }
      for (const wfNode of workflow.nodes) {
        if (wfNode.type !== 'tryCatch') {
          continue;
        }
        const from = idMap.get(wfNode.id);
        if (!from) {
          continue;
        }
        if (typeof wfNode['tryNodeId'] === 'string') {
          const to = idMap.get(wfNode['tryNodeId']);
          if (to) {
            await this.connectNodes(from, to, 'success');
          }
        }
        if (typeof wfNode['catchNodeId'] === 'string') {
          const to = idMap.get(wfNode['catchNodeId']);
          if (to) {
            await this.connectNodes(from, to, 'failure');
          }
        }
        if (typeof wfNode['finallyNodeId'] === 'string') {
          const to = idMap.get(wfNode['finallyNodeId']);
          if (to) {
            await this.connectNodes(from, to, 'out');
          }
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

  private async loadWorkflowInput(): Promise<void> {
    if (!this.workflow) {
      return;
    }
    const key = this.workflowKey(this.workflow);
    if (key === this.loadedWorkflowKey) {
      return;
    }
    await this.loadWorkflow(this.workflow);
  }

  private workflowKey(workflow: WorkflowVersion): string {
    const nodeCount = workflow.nodes?.length ?? 0;
    const connectionCount = workflow.connections?.length ?? 0;
    return `${workflow.id}:${workflow.version}:${nodeCount}:${connectionCount}:${JSON.stringify(workflow.nodes)}:${JSON.stringify(workflow.connections)}`;
  }

  private applyNodePropertiesToWorkflowNode(target: WorkflowNode, node: FlowNode): void {
    const props = node.properties ?? {};
    const writable = target as Record<string, unknown>;
    switch (node.nodeType) {
      case 'assign':
        writable['variableName'] = props['variableName'] as string | undefined;
        writable['value'] = props['value'];
        break;
      case 'if':
      case 'while':
        writable['condition'] = props['condition'] as string | undefined;
        break;
      case 'forEach':
        writable['items'] = props['items'] as string | undefined;
        writable['itemVariable'] = props['itemVariable'] as string | undefined;
        if (Array.isArray(props['itemFields'])) {
          writable['itemFields'] = props['itemFields'];
        }
        break;
      case 'for':
        writable['start'] = props['start'] as number | undefined;
        writable['end'] = props['end'] as number | undefined;
        writable['step'] = props['step'] as number | undefined;
        writable['indexVariable'] = props['indexVariable'] as string | undefined;
        break;
      case 'tryCatch':
        writable['exceptionVariable'] = props['exceptionVariable'] as string | undefined;
        break;
      case 'delay':
        writable['durationMs'] = props['durationMs'] as number | undefined;
        break;
      case 'log':
        writable['message'] = props['message'] as string | undefined;
        writable['level'] = props['level'] as string | undefined;
        break;
      case 'userPrompt':
        writable['promptTitle'] = props['promptTitle'] as string | undefined;
        writable['promptInputVariable'] = props['promptInputVariable'] as string | undefined;
        writable['promptTimeoutSeconds'] = props['promptTimeoutSeconds'] as number | undefined;
        break;
      case 'terminate':
        writable['message'] = props['message'] as string | undefined;
        writable['exceptionType'] = props['exceptionType'] as string | undefined;
        break;
    }
  }

  private extractNodeProperties(node: WorkflowNode): Record<string, unknown> {
    switch (node.type) {
      case 'assign':
        return {
          ...(typeof node['variableName'] === 'string' ? { variableName: node['variableName'] } : {}),
          ...(node['value'] !== undefined ? { value: node['value'] } : {}),
        };
      case 'if':
      case 'while':
        return typeof node['condition'] === 'string' ? { condition: node['condition'] } : {};
      case 'forEach':
        return {
          ...(typeof node['items'] === 'string' ? { items: node['items'] } : {}),
          ...(typeof node['itemVariable'] === 'string' ? { itemVariable: node['itemVariable'] } : {}),
          ...(Array.isArray(node['itemFields']) ? { itemFields: node['itemFields'] } : {}),
        };
      case 'for':
        return {
          ...(node['start'] !== undefined ? { start: node['start'] } : {}),
          ...(node['end'] !== undefined ? { end: node['end'] } : {}),
          ...(node['step'] !== undefined ? { step: node['step'] } : {}),
          ...(typeof node['indexVariable'] === 'string' ? { indexVariable: node['indexVariable'] } : {}),
        };
      case 'tryCatch':
        return typeof node['exceptionVariable'] === 'string'
          ? { exceptionVariable: node['exceptionVariable'] }
          : {};
      case 'delay':
        return node['durationMs'] !== undefined ? { durationMs: node['durationMs'] } : {};
      case 'log':
        return {
          ...(typeof node['message'] === 'string' ? { message: node['message'] } : {}),
          ...(typeof node['level'] === 'string' ? { level: node['level'] } : {}),
        };
      case 'checkpoint':
        return (node.properties as Record<string, unknown>) ?? {};
      case 'userPrompt':
        return {
          ...(typeof node['promptTitle'] === 'string' ? { promptTitle: node['promptTitle'] } : {}),
          ...(typeof node['promptInputVariable'] === 'string'
            ? { promptInputVariable: node['promptInputVariable'] }
            : {}),
          ...(node['promptTimeoutSeconds'] !== undefined
            ? { promptTimeoutSeconds: node['promptTimeoutSeconds'] }
            : {}),
        };
      case 'terminate':
        return {
          ...(typeof node['message'] === 'string' ? { message: node['message'] } : {}),
          ...(typeof node['exceptionType'] === 'string' ? { exceptionType: node['exceptionType'] } : {}),
        };
      default:
        return (node.properties as Record<string, unknown>) ?? {};
    }
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
    const from = this.socketPosition(this.pendingConnectionFrom.nodeId, this.pendingConnectionFrom.port);
    if (!from) {
      return;
    }
    const rect = this.reteContainer.nativeElement.getBoundingClientRect();
    const to: NodePosition = {
      x: clientX - rect.left,
      y: clientY - rect.top,
    };
    this.pendingPath.setAttribute('d', ConnectionComponent.buildPath(from, to));
  }

  private connectionMidpoint(start: NodePosition, end: NodePosition): NodePosition {
    const dy = Math.max(Math.abs(end.y - start.y) / 2, 20);
    const c1 = { x: start.x, y: start.y + dy };
    const c2 = { x: end.x, y: end.y - dy };
    const t = 0.5;
    const mt = 1 - t;

    return {
      x:
        (mt ** 3) * start.x +
        3 * (mt ** 2) * t * c1.x +
        3 * mt * (t ** 2) * c2.x +
        (t ** 3) * end.x,
      y:
        (mt ** 3) * start.y +
        3 * (mt ** 2) * t * c1.y +
        3 * mt * (t ** 2) * c2.y +
        (t ** 3) * end.y,
    };
  }

  onDeleteKey(): void {
    if (this.selectedConnectionId) {
      void this.deleteSelectedConnection();
    } else if (this.selectedNodeIds.size > 0) {
      void this.deleteSelectedNodes();
    }
  }
}
