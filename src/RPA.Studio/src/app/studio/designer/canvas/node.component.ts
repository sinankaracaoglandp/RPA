import { CommonModule } from '@angular/common';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnDestroy,
  Output,
  inject,
} from '@angular/core';
import { TranslatePipe } from '../../../core/translate.pipe';

const OUT_SOCKET_SELECTOR = '[data-socket-direction="out"]';

export interface CanvasNodeSocketView {
  port: string;
  label: string;
  tone?: 'default' | 'positive' | 'negative' | 'neutral';
}

/**
 * View-model handed to the node renderer. Kept independent of the Rete
 * runtime so the component is trivially testable and reusable (e.g. toolbox
 * previews).
 */
export interface CanvasNodeView {
  id: string;
  /** Human-readable title (activity display name or node-type label). */
  label: string;
  /** Node type from the workflow schema (activity, if, forEach, ...). */
  nodeType: string;
  /** Optional activity id (e.g. 'Sap.Nco.CallBapi'). */
  activityId?: string;
  icon?: string;
  selected?: boolean;
  /** A breakpoint is set on this node (debug mode). */
  breakpoint?: boolean;
  /** Execution is currently paused on this node (debug mode). */
  current?: boolean;
  outputs?: CanvasNodeSocketView[];
  inputs?: CanvasNodeSocketView[];
}

export interface CanvasNodeSelectEvent {
  nodeId: string;
  additive: boolean;
}

/**
 * Renders a single activity node card on the canvas. Rendered both by the
 * Rete area (dynamically, via the canvas bridge) and directly in tests.
 */
@Component({
  selector: 'app-canvas-node',
  standalone: true,
  imports: [CommonModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './node.component.html',
  styleUrls: ['./node.component.scss'],
})
export class NodeComponent implements AfterViewInit, OnDestroy {
  @Input({ required: true }) node!: CanvasNodeView;

  @Output() readonly nodeSelect = new EventEmitter<CanvasNodeSelectEvent>();
  @Output() readonly nodeDelete = new EventEmitter<string>();
  @Output() readonly connectStart = new EventEmitter<{ nodeId: string; port: string }>();
  @Output() readonly connectDrop = new EventEmitter<{ nodeId: string; port: string }>();

  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private readonly captureSelectPointerDown = (event: PointerEvent): void => {
    const target = event.target as HTMLElement | null;
    if (!target) {
      return;
    }

    if (target.closest('[data-testid="canvas-node-delete"]')) {
      return;
    }

    if (target.closest(OUT_SOCKET_SELECTOR)) {
      return;
    }

    this.select(event.ctrlKey || event.metaKey);
  };
  private readonly capturePointerDown = (event: Event): void => {
    const target = event.target as HTMLElement | null;
    const socket = target?.closest?.(OUT_SOCKET_SELECTOR);
    if (!socket) {
      return;
    }
    // Rete'nin node-host elementine bağladığı sürükleme dinleyicisi de aynı
    // elementte (Angular hostElement mount modu) bubble aşamasında pointerdown
    // dinliyor. Gerçek tarayıcıda soket üzerindeki bırakma sırası garanti
    // edilemediğinden (bkz. rete-area-plugin Drag.down → stopPropagation),
    // capture aşamasında müdahale ederek bağlantı sürüklemesinin node
    // taşımaya "kaçırılmasını" kesin olarak engelliyoruz.
    event.stopPropagation();
    (event as { stopImmediatePropagation?: () => void }).stopImmediatePropagation?.();
    event.preventDefault();
    const port = (socket as HTMLElement).dataset['port'] || 'out';
    this.connectStart.emit({ nodeId: this.node.id, port });
  };

  ngAfterViewInit(): void {
    this.elementRef.nativeElement.addEventListener('pointerdown', this.captureSelectPointerDown, {
      capture: true,
    });
    this.elementRef.nativeElement.addEventListener('pointerdown', this.capturePointerDown, {
      capture: true,
    });
  }

  ngOnDestroy(): void {
    this.elementRef.nativeElement.removeEventListener('pointerdown', this.captureSelectPointerDown, {
      capture: true,
    });
    this.elementRef.nativeElement.removeEventListener('pointerdown', this.capturePointerDown, {
      capture: true,
    });
  }

  select(additive = false): void {
    this.nodeSelect.emit({ nodeId: this.node.id, additive });
  }

  remove(event: Event): void {
    event.stopPropagation();
    this.nodeDelete.emit(this.node.id);
  }

  get outputSockets(): CanvasNodeSocketView[] {
    return this.node.outputs?.length ? this.node.outputs : [{ port: 'out', label: 'Next', tone: 'default' }];
  }

  get inputSockets(): CanvasNodeSocketView[] {
    return this.node.inputs?.length ? this.node.inputs : [{ port: 'in', label: 'In' }];
  }

  get isBranchNode(): boolean {
    return ['if', 'tryCatch', 'forEach', 'for', 'while'].includes(this.node.nodeType);
  }

  onOutSocketDown(event: Event, port: string): void {
    // Rete'nin node-drag yakalamasını engelle; bağlantı sürüklemesi başlasın.
    event.stopPropagation();
    event.preventDefault();
    this.connectStart.emit({ nodeId: this.node.id, port });
  }

  onPointerUp(): void {
    this.connectDrop.emit({ nodeId: this.node.id, port: 'in' });
  }

  onInputPointerUp(event: Event, port: string): void {
    event.stopPropagation();
    this.connectDrop.emit({ nodeId: this.node.id, port });
  }
}
