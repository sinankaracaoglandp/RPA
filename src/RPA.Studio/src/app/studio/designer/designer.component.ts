import { CommonModule } from '@angular/common';
import { Component, OnDestroy, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Router } from '@angular/router';
import { Subscription, timer, switchMap } from 'rxjs';
import { WorkflowVariable, WorkflowVersion } from '../../shared/models/workflow.model';
import { DebugService } from '../../shared/services/debug.service';
import { ModeService } from '../../shared/services/mode.service';
import { WorkflowDraftService } from '../../shared/services/workflow-draft.service';
import { OrchestratorService } from '../../orchestrator/orchestrator.service';
import { TranslatePipe } from '../../core/translate.pipe';
import { CanvasComponent } from './canvas/canvas.component';
import { ToolboxComponent } from './toolbox/toolbox.component';
import { DebugPanelComponent } from '../debug/debug-panel.component';
import { SimpleModeToggleComponent } from '../simple-mode/simple-mode-toggle.component';
import { SimplifiedToolboxComponent } from '../simple-mode/simplified-toolbox.component';
import { PropertiesPanelComponent } from './properties/properties-panel.component';
import { VariablesPanelComponent } from './variables/variables-panel.component';
import { BackHomeComponent } from '../../shared/back-home/back-home.component';
import { LogConsoleComponent } from './log-console/log-console.component';
import { ExecutionLogService } from '../../shared/services/execution-log.service';
import { RunLogService } from '../../shared/services/run-log.service';
import { injectedLoopVariables, enclosingForEachNodes } from './loop-item-schema';
import { StructuredViewComponent } from './structured/view/structured-view.component';
import { newContainer, newStep } from './structured/edit/tree-ops';
import { CONTAINER_OF_ACTIVITY } from './structured/edit/control-activity-map';

/**
 * Root layout of the workflow designer. Owns the canvas and mediates between it
 * and the surrounding panels (toolbox / debugger).
 *
 * In Simple mode (Faz 5, Task 5.5) the debug IDE and full toolbox are hidden
 * in favour of the curated SimplifiedToolboxComponent, and breakpoints cannot
 * be set from the canvas.
 */
@Component({
  selector: 'app-designer',
  standalone: true,
  imports: [
    CommonModule,
    TranslatePipe,
    CanvasComponent,
    ToolboxComponent,
    DebugPanelComponent,
    SimpleModeToggleComponent,
    SimplifiedToolboxComponent,
    PropertiesPanelComponent,
    VariablesPanelComponent,
    BackHomeComponent,
    LogConsoleComponent,
    StructuredViewComponent,
  ],
  templateUrl: './designer.component.html',
  styleUrls: ['./designer.component.scss'],
})
export class DesignerComponent implements OnDestroy {
  private static readonly TerminalRunStatuses = new Set(['successful', 'failed', 'businessexception', 'abandoned']);

  /**
   * Sinyal tabanlı viewChild: zoneless CD'de dekoratör @ViewChild referansının
   * template binding'e ilk change detection turunda işlenmemesi, ilk kullanıcı
   * etkileşiminin (örn. ilk aktivite ekleme) sessizce kaybolmasına yol açıyordu.
   * Sinyal, resolve olduğunda bağlamaları reaktif olarak günceller.
   */
  readonly canvas = viewChild(CanvasComponent);
  readonly structuredViewRef = viewChild(StructuredViewComponent);

  private readonly debug = inject(DebugService);
  private readonly modeService = inject(ModeService);
  private readonly draft = inject(WorkflowDraftService);
  private readonly orchestrator = inject(OrchestratorService);
  readonly log = inject(ExecutionLogService);
  private readonly runLog = inject(RunLogService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private runStatusPolling?: Subscription;

  /** Projeden açıldıysa (query param) taşınan proje kimliği — e-fatura profil seçici gibi
   *  proje-kapsamlı alanları otomatik doldurmak için. */
  readonly projectId = signal<string | null>(null);
  readonly workflow = signal<WorkflowVersion | undefined>(undefined);
  readonly selectedNodeId = signal<string | null>(null);
  readonly selectedActivityType = signal<string | undefined>(undefined);
  readonly selectedProperties = signal<Record<string, unknown>>({});
  readonly currentGraph = signal<WorkflowVersion | undefined>(undefined);
  readonly variables = signal<WorkflowVariable[]>([]);
  readonly debugMode = signal(false);

  /** Salt-okunur yapısal görünüm açık mı (serbest-graf canvas yerine iç içe kutular). */
  // Varsayılan görünüm: yapısal (serbest-graf canvas'a düğmeyle geçilir).
  readonly structuredView = signal(true);
  toggleStructuredView(): void { this.structuredView.update((v) => !v); }

  /**
   * Properties paneline geçen değişkenler: temel workflow değişkenleri + seçili node'u
   * saran ForEach döngülerinin türetilmiş `item` değişkenleri. Enjekte edilenler kalıcı
   * workflow'a yazılmaz; yalnız autocomplete/alan gösterimi içindir.
   */
  readonly panelVariables = computed<WorkflowVariable[]>(() => {
    const base = this.variables();
    // Yapısal modda enjeksiyon ağaç-yolundan gelir (structuredVars); serbest-graf'ta graf-tabanlı.
    if (this.structuredView()) {
      return [...base, ...this.structuredVars()];
    }
    const graph = this.currentGraph() ?? this.workflow();
    const nodeId = this.selectedNodeId();
    if (!graph) {
      return base;
    }
    return [...base, ...injectedLoopVariables(nodeId, graph, base)];
  });

  /**
   * Seçili node bir ForEach ise (veya bir ForEach'in gövdesindeyse) vurgulanacak
   * gövde node id'leri. Seçili ForEach'in kendi gövdesini önceler; gövde node'u
   * seçiliyse onu saran (en yakın) döngünün gövdesini vurgular.
   */
  readonly loopBodyHighlightIds = computed<string[]>(() => {
    const graph = this.currentGraph() ?? this.workflow();
    const nodeId = this.selectedNodeId();
    if (!graph || !nodeId) {
      return [];
    }
    const selected = graph.nodes.find((n) => n.id === nodeId);
    const loopId =
      selected?.type === 'forEach'
        ? nodeId
        : enclosingForEachNodes(nodeId, graph).at(-1)?.id;
    if (!loopId) {
      return [];
    }
    return graph.nodes
      .filter((n) => enclosingForEachNodes(n.id, graph).some((fe) => fe.id === loopId))
      .map((n) => n.id);
  });

  readonly workflowId = signal<string | null>(null);
  readonly dirty = signal(false);
  readonly saveState = signal<'idle' | 'saving' | 'error'>('idle');
  /** Son kaydetme hatasının backend mesajı (400 gövdesindeki { error }); yoksa null. */
  readonly saveErrorMessage = signal<string | null>(null);
  readonly runState = signal<'idle' | 'saving' | 'queued' | 'error'>('idle');
  readonly lastQueueItemId = signal<string | null>(null);
  readonly lastQueueId = signal<string | null>(null);
  readonly lastRunStatus = signal<string | null>(null);
  readonly lastQueueItemShortId = computed(() => this.lastQueueItemId()?.slice(0, 8) ?? null);

  readonly mode = this.modeService.mode;
  readonly isSimpleMode = computed(() => this.mode() === 'Simple');

  /** Node ids carrying a breakpoint, for canvas highlighting. Empty in Simple mode. */
  readonly breakpointNodeIds = computed(() =>
    this.isSimpleMode() ? [] : this.debug.breakpoints().map((b) => b.nodeId),
  );
  readonly debugCurrentNodeId = this.debug.currentNodeId;

  constructor() {
    this.projectId.set(this.route.snapshot.queryParamMap?.get('projectId') ?? null);
    const routedId = this.route.snapshot.paramMap.get('workflowId');
    if (routedId) {
      this.workflowId.set(routedId);
      this.draft.load(routedId).subscribe({
        next: (wf) => this.applyWorkflow(wf),
        error: () => this.saveState.set('error'),
      });
      return;
    }
    const pending = this.draft.consumePending();
    if (pending) {
      this.applyWorkflow(pending);
    }
  }

  ngOnDestroy(): void {
    this.runStatusPolling?.unsubscribe();
  }

  /** Toggles the debug panel, connecting to RobotHub on first open. Disabled in Simple mode. */
  async toggleDebug(): Promise<void> {
    if (this.isSimpleMode()) {
      return;
    }
    const next = !this.debugMode();
    this.debugMode.set(next);
    if (next) {
      await this.debug.connect().catch(() => undefined);
    }
  }

  /** Invoked by the toolbox to drop an activity onto the canvas. */
  async addActivity(activityId: string): Promise<void> {
    await this.canvas()?.addNode(activityId);
  }

  /**
   * Toolbox'ta çift tık / `+` ile eklenen aktivite. Toolbox kendi ekleme yolunu `canvas`
   * varlığına bağlar; yapısal görünümde `app-canvas` render edilmediğinden o yol ölüdür ve
   * eylem sessizce kaybolurdu. Yapısal görünümdeyken ekleme ağaca yönlendirilir (kural C:
   * seçilinin ardına).
   *
   * Kontrol-akışı aktiviteleri (Logic.If/ForEach/...) düz adım olarak eklenemez — yapısal
   * karşılıkları konteyner bloğudur. Toolbox bunları listesinden elemez (add-menu eler),
   * bu yüzden dönüşüm burada yapılır; aksi halde blok yerine düz aktivite kutusu eklenir.
   */
  onToolboxActivityAdded(event: { activityId: string }): void {
    if (!this.structuredView()) { return; }
    const containerType = CONTAINER_OF_ACTIVITY[event.activityId];
    const item = containerType ? newContainer(containerType) : newStep(event.activityId);
    this.structuredViewRef()?.addFromPalette(item);
  }

  onNodeSelect(nodeId: string | null): void {
    this.selectedNodeId.set(nodeId);
    const canvas = this.canvas();
    if (nodeId && canvas) {
      this.selectedActivityType.set(canvas.getNodeActivityId(nodeId));
      this.selectedProperties.set(canvas.getNodeProperties(nodeId));
    } else {
      this.selectedActivityType.set(undefined);
      this.selectedProperties.set({});
    }
    // In debug mode (Advanced only), clicking a node toggles its breakpoint.
    if (!this.isSimpleMode() && this.debugMode() && nodeId) {
      this.debug.toggleBreakpoint(nodeId);
    }
  }

  /** Yapısal moddaki seçili node'u saran döngülerin item değişkenleri (panel autocomplete'i için). */
  readonly structuredVars = signal<WorkflowVariable[]>([]);

  /** Yapısal görünümdeki node seçimi mevcut özellik panelini besler. */
  onStructuredSelect(sel: { activityType?: string; properties: Record<string, unknown>; variables?: WorkflowVariable[] } | null): void {
    this.selectedActivityType.set(sel?.activityType);
    this.selectedProperties.set(sel?.properties ?? {});
    this.structuredVars.set(sel?.variables ?? []);
  }

  onPropertiesChange(properties: Record<string, unknown>): void {
    if (this.structuredView()) {
      this.selectedProperties.set(properties);
      this.structuredViewRef()?.updateSelectedProps(properties);
      this.bindOutputVariableSchema(this.selectedActivityType(), properties);
      return;
    }
    const nodeId = this.selectedNodeId();
    if (nodeId) {
      this.canvas()?.updateNodeProperties(nodeId, properties);
      this.selectedProperties.set(properties);
      this.bindOutputVariableSchema(this.selectedActivityType(), properties);
    }
  }

  /**
   * Çıktı-şeması üreten aktivitelerin (`File.List`, `EInvoice.ReadProfile*`) seçilen çıktı
   * değişkenini şemalı bir workflow değişkenine bağlar. Hem klasik hem yapısal görünümde
   * çalışır (yapısal görünüm daha önce erken `return` ile bunu atlıyordu).
   */
  private bindOutputVariableSchema(activity: string | undefined, properties: Record<string, unknown>): void {
    if (activity === 'EInvoice.ReadProfile' || activity === 'EInvoice.ReadProfileBatch') {
      this.onProfileActivityPropertiesChange(activity, properties);
    }
    if (activity === 'File.List') {
      this.onFileListPropertiesChange(properties);
    }
    if (activity === 'Sap.Gui.GridRead') {
      this.onGridReadPropertiesChange(properties);
    }
  }

  /**
   * Sap.Gui.GridRead çıktısını (ALV satır listesi) seçilen `outputVariable` adında bir
   * `list<object>` workflow değişkenine bağlar; sonraki node'lar (ör. Logic.ForEach) listeyi
   * görebilsin diye.
   *
   * <p>File.List'ten farkı: ALV kolonları çalışma anında belirlenir (hangi transaction/layout
   * kullanıldığına bağlı), bu yüzden sabit bir alan şeması ÜRETİLEMEZ. Değişken şemasız
   * `list<object>` olarak tanımlanır; satır alanlarına ALV teknik kolon adıyla erişilir.</p>
   */
  onGridReadPropertiesChange(properties: Record<string, unknown>): void {
    const outputVariable = String(properties['outputVariable'] ?? '').trim();
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(outputVariable)) {
      return;
    }
    // Kolonlar 🎯 ile grid seçildiğinde SAP'tan okunup 'columns' alanına yazılır. Varsa satır
    // şeması bunlardan üretilir → sonraki node'larda {{satir.MATNR}} autocomplete çalışır.
    const columns = this.parseGridColumns(properties['columns']);
    const schema = columns.length
      ? {
          type: 'array',
          items: {
            type: 'object',
            properties: Object.fromEntries(columns.map((c) => [c, { type: 'string' }])),
          },
        }
      : undefined;
    const nextVariable: WorkflowVariable = {
      name: outputVariable,
      type: 'list<object>',
      scope: 'global',
      schema,
      description: columns.length
        ? `Sap.Gui.GridRead satır listesi (${columns.length} kolon)`
        : 'Sap.Gui.GridRead satır listesi (kolonlar çalışma anında belirlenir)',
    };
    const variables = this.variables();
    const next = variables.some((variable) => variable.name === outputVariable)
      ? variables.map((variable) => (variable.name === outputVariable ? { ...variable, ...nextVariable } : variable))
      : [...variables, nextVariable];
    this.onVariablesChange(next);
  }

  /**
   * File.List çıktısını (dosya listesi) seçilen `outputVariable` adında bir `list<object>`
   * workflow değişkenine bağlar; böylece sonraki node'lar (ör. Logic.ForEach) dosya alanlarına
   * ({{...name}}, {{...path}}) şema autocomplete ile erişebilir.
   */
  onFileListPropertiesChange(properties: Record<string, unknown>): void {
    const outputVariable = String(properties['outputVariable'] ?? '').trim();
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(outputVariable)) {
      return;
    }
    const schema = {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          name: { type: 'string' },
          path: { type: 'string' },
          size: { type: 'number' },
          createdAt: { type: 'string' },
          modifiedAt: { type: 'string' },
        },
      },
    };
    const nextVariable: WorkflowVariable = {
      name: outputVariable,
      type: 'list<object>',
      scope: 'global',
      schema,
      description: 'File.List dosya listesi',
    };
    const variables = this.variables();
    const next = variables.some((variable) => variable.name === outputVariable)
      ? variables.map((variable) => (variable.name === outputVariable ? { ...variable, ...nextVariable } : variable))
      : [...variables, nextVariable];
    this.onVariablesChange(next);
  }

  /** 'columns' alanındaki JSON kolon dizisini okur (boş/bozuk → boş liste). */
  private parseGridColumns(raw: unknown): string[] {
    if (Array.isArray(raw)) {
      return raw.filter((c): c is string => typeof c === 'string' && c.trim().length > 0);
    }
    if (typeof raw !== 'string' || raw.trim().length === 0) {
      return [];
    }
    try {
      const parsed = JSON.parse(raw);
      return Array.isArray(parsed)
        ? parsed.filter((c): c is string => typeof c === 'string' && c.trim().length > 0)
        : [];
    } catch {
      return [];
    }
  }

  onProfileActivityPropertiesChange(activityType: string, properties: Record<string, unknown>): void {
    const schema = this.readProfileSchema(properties['outputSchemaJson']);
    if (!schema) {
      return;
    }
    const fallback = activityType === 'EInvoice.ReadProfileBatch' ? 'faturalar' : 'fatura';
    const outputVariable = String(properties['outputVariable'] || fallback).trim();
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(outputVariable)) {
      return;
    }
    const type = activityType === 'EInvoice.ReadProfileBatch' ? 'list<object>' : 'object';
    const nextVariable: WorkflowVariable = {
      name: outputVariable,
      type,
      scope: 'global',
      schema,
      description: `E-Fatura profil ${properties['profileId'] ?? ''} v${properties['profileVersion'] ?? ''}`.trim(),
    };
    const variables = this.variables();
    const next = variables.some((variable) => variable.name === outputVariable)
      ? variables.map((variable) => variable.name === outputVariable ? { ...variable, ...nextVariable } : variable)
      : [...variables, nextVariable];
    this.onVariablesChange(next);
  }

  onVariablesChange(variables: WorkflowVariable[]): void {
    this.variables.set(variables);
    const workflow = this.workflow();
    if (workflow) {
      this.workflow.set({ ...workflow, variables });
    }
    const graph = this.currentGraph();
    if (graph) {
      this.currentGraph.set({ ...graph, variables });
    }
    this.dirty.set(true);
  }

  onGraphChanged(graph: WorkflowVersion): void {
    this.currentGraph.set(graph);
    this.dirty.set(true);
  }

  onDebugCurrentNode(_nodeId: string | null): void {
    // Current node is read from the DebugService signal and bound to the canvas;
    // this handler exists so the panel can notify without extra wiring.
  }

  /** Kaydeder (yalnız yönlendirilmiş bir workflowId varken). Yeni-taslak modunda no-op. */
  async save(): Promise<void> {
    const id = this.workflowId();
    if (!id) {
      return; // yeni-taslak modu: kalıcı hedef yok (Projelerim'den açılır)
    }
    // Yapısal görünümde canvas yoktur; henüz düzenleme yapılmadıysa currentGraph de boştur —
    // bu durumda yüklenen taslak grafı kaydedilir (aksi halde kaydet sessizce hiçbir şey yapmazdı).
    const serialized = this.canvas()?.serialize() ?? this.currentGraph() ?? this.workflow();
    const graph = serialized ? { ...serialized, variables: this.variables() } : undefined;
    if (!graph) {
      return;
    }
    this.saveState.set('saving');
    // Not: firstValueFrom + await burada kasıtlı kullanılmadı — .then devamı bir
    // mikro-görev turuna erteleniyor ve HttpTestingController.flush() hemen
    // ardından senkron assert eden testler bunu göremiyor. subscribe() next/error
    // callback'leri flush() sırasında senkron çalışır.
    return new Promise<void>((resolve) => {
      this.draft.save(id, graph).subscribe({
        next: () => {
          this.dirty.set(false);
          this.saveState.set('idle');
          this.saveErrorMessage.set(null);
          resolve();
        },
        error: (err: unknown) => {
          const message = this.extractSaveError(err);
          this.saveErrorMessage.set(message);
          this.log.error(`Kaydetme başarısız: ${message}`);
          this.saveState.set('error');
          resolve();
        },
      });
    });
  }

  /** HTTP hata gövdesinden ({ error: "..." }) okunabilir bir mesaj çıkarır. */
  private extractSaveError(err: unknown): string {
    const body = (err as { error?: unknown })?.error;
    if (typeof body === 'string' && body.trim()) {
      return body;
    }
    const inner = (body as { error?: unknown })?.error;
    if (typeof inner === 'string' && inner.trim()) {
      return inner;
    }
    const message = (err as { message?: unknown })?.message;
    return typeof message === 'string' && message.trim() ? message : 'Bilinmeyen hata';
  }

  /** Kaydedilmiş taslağı Agent kuyruğuna alır. */
  async run(): Promise<void> {
    const id = this.workflowId();
    if (!id) {
      return;
    }

    // Konsolu aç ve önceki çalıştırmanın günlüğünü temizle.
    this.log.clear();
    this.log.open();
    this.log.info(`Çalıştırma başlatıldı: ${this.workflow()?.name ?? id}`);

    // Canlı node olayları için StudioHub'a bağlan (best-effort, ateşle-unut; çalıştırmayı
    // geciktirmez — başarısızsa konsol yine çalıştırma-durumu satırlarını gösterir).
    void this.runLog.connect().catch(() => undefined);

    this.runState.set('saving');
    this.log.step('Taslak kaydediliyor…');
    await this.save();
    if (this.saveState() === 'error') {
      this.runState.set('error');
      this.log.error('Kaydetme başarısız — çalıştırma iptal edildi.');
      return;
    }
    this.log.success('Taslak kaydedildi.');

    return new Promise<void>((resolve) => {
      this.draft.run(id).subscribe({
        next: (result) => {
          this.lastQueueItemId.set(result.queueItemId);
          this.lastQueueId.set(result.queueId);
          this.lastRunStatus.set(result.status);
          this.runState.set('queued');
          // Canlı node loglarını bu çalıştırmaya (jobRunId = queue item id) göre süz.
          this.runLog.setActiveJobRun(result.queueItemId);
          this.log.success('Kuyruğa alındı.', `İş kalemi: ${result.queueItemId.slice(0, 8)} · durum: ${result.status}`);
          this.startRunStatusPolling(result.queueId, result.queueItemId);
          resolve();
        },
        error: () => {
          this.runState.set('error');
          this.log.error('Çalıştırma kuyruğa alınamadı.');
          resolve();
        },
      });
    });
  }

  refreshRunStatus(): void {
    const queueId = this.lastQueueId();
    const itemId = this.lastQueueItemId();
    if (!queueId || !itemId) {
      return;
    }

    this.orchestrator.getQueueItem(queueId, itemId).subscribe({
      next: (item) => this.applyRunStatus(item.status),
      error: () => this.runState.set('error'),
    });
  }

  private startRunStatusPolling(queueId: string, itemId: string): void {
    this.runStatusPolling?.unsubscribe();
    this.runStatusPolling = timer(3000, 3000)
      .pipe(switchMap(() => this.orchestrator.getQueueItem(queueId, itemId)))
      .subscribe({
        next: (item) => this.applyRunStatus(item.status),
        error: () => {
          this.runStatusPolling?.unsubscribe();
          this.runState.set('error');
        },
      });
  }

  private applyRunStatus(status: string): void {
    const changed = this.lastRunStatus() !== status;
    this.lastRunStatus.set(status);
    if (changed) {
      const lower = status.toLowerCase();
      if (lower === 'successful') {
        this.log.success(`Durum: ${status}`);
      } else if (lower === 'failed' || lower === 'businessexception' || lower === 'abandoned') {
        this.log.error(`Durum: ${status}`);
      } else {
        this.log.step(`Durum: ${status}`);
      }
    }
    if (DesignerComponent.TerminalRunStatuses.has(status.toLowerCase())) {
      this.runStatusPolling?.unsubscribe();
      if (changed) {
        this.log.info('Çalıştırma tamamlandı.');
      }
    }
  }

  private applyWorkflow(workflow: WorkflowVersion): void {
    const withVariables = { ...workflow, variables: workflow.variables ?? [] };
    this.workflow.set(withVariables);
    this.variables.set(withVariables.variables ?? []);
  }

  private readProfileSchema(value: unknown): unknown | null {
    if (!value) {
      return null;
    }
    if (typeof value === 'object') {
      return value;
    }
    if (typeof value !== 'string') {
      return null;
    }
    try {
      return JSON.parse(value);
    } catch {
      return null;
    }
  }

  onSaveShortcut(event: Event): void {
    event.preventDefault();
    void this.save();
  }

  backToProjects(): void {
    void this.router.navigate(['/projects']);
  }
}
