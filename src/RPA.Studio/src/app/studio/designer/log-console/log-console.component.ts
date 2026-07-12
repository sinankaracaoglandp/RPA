import { CommonModule } from '@angular/common';
import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  viewChild,
} from '@angular/core';
import { ExecutionLogService } from '../../../shared/services/execution-log.service';

/**
 * Tasarım ekranının altına yerleşen canlı çalıştırma konsolu (footer).
 * <see cref="ExecutionLogService"/> girişlerini zaman damgalı, seviye renkli satırlar
 * halinde gösterir ve otomatik olarak en alta kaydırır.
 */
@Component({
  selector: 'app-log-console',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './log-console.component.html',
  styleUrls: ['./log-console.component.scss'],
})
export class LogConsoleComponent implements AfterViewChecked {
  readonly log = inject(ExecutionLogService);

  private readonly scrollBox = viewChild<ElementRef<HTMLDivElement>>('scrollBox');
  private lastCount = 0;

  ngAfterViewChecked(): void {
    const count = this.log.entries().length;
    if (count !== this.lastCount) {
      this.lastCount = count;
      const box = this.scrollBox()?.nativeElement;
      if (box) {
        box.scrollTop = box.scrollHeight;
      }
    }
  }

  clear(): void {
    this.log.clear();
  }

  close(): void {
    this.log.close();
  }
}
