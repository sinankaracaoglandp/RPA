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

  readonly workflowId = signal<string | null>(null);
  readonly dirty = signal(false);
  readonly saveState = signal<'idle' | 'saving' | 'error'>('idle');
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

  onPropertiesChange(properties: Record<string, unknown>): void {
    const nodeId = this.selectedNodeId();
    if (nodeId) {
      this.canvas()?.updateNodeProperties(nodeId, properties);
      this.selectedProperties.set(properties);
      const activity = this.selectedActivityType();
      if (activity === 'EInvoice.ReadProfile' || activity === 'EInvoice.ReadProfileBatch') {
        this.onProfileActivityPropertiesChange(activity, properties);
      }
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
    const serialized = this.canvas()?.serialize() ?? this.currentGraph();
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
          resolve();
        },
        error: () => {
          this.saveState.set('error');
          resolve();
        },
      });
    });
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
