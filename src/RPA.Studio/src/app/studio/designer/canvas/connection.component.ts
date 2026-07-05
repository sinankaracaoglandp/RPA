import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, Input, computed, signal } from '@angular/core';

export interface Point {
  x: number;
  y: number;
}

/**
 * Renders a single connection (edge) between two node sockets as a smooth
 * cubic Bézier SVG path. Pure/presentational — the canvas feeds it endpoints.
 */
@Component({
  selector: 'app-canvas-connection',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg class="canvas-connection" [attr.data-testid]="'canvas-connection'" [attr.data-connection-id]="id">
      <path
        class="canvas-connection__path"
        [attr.d]="path()"
        [attr.data-testid]="'canvas-connection-path'"
        fill="none"
      ></path>
    </svg>
  `,
  styles: [
    `
      .canvas-connection {
        position: absolute;
        inset: 0;
        overflow: visible;
        pointer-events: none;
      }
      .canvas-connection__path {
        stroke: #2563eb;
        stroke-width: 2;
      }
    `,
  ],
})
export class ConnectionComponent {
  @Input() id?: string;

  private readonly _start = signal<Point>({ x: 0, y: 0 });
  private readonly _end = signal<Point>({ x: 0, y: 0 });

  @Input() set start(value: Point) {
    this._start.set(value);
  }

  @Input() set end(value: Point) {
    this._end.set(value);
  }

  readonly path = computed(() => ConnectionComponent.buildPath(this._start(), this._end()));

  /** Builds a vertical-biased cubic Bézier path (top→bottom flow). */
  static buildPath(start: Point, end: Point): string {
    const dy = Math.max(Math.abs(end.y - start.y) / 2, 20);
    const c1 = { x: start.x, y: start.y + dy };
    const c2 = { x: end.x, y: end.y - dy };
    return `M ${start.x} ${start.y} C ${c1.x} ${c1.y}, ${c2.x} ${c2.y}, ${end.x} ${end.y}`;
  }
}
