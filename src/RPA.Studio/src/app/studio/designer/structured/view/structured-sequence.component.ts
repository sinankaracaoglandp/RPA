import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { StructuredSequence } from '../structured-model';
import { StructuredItemComponent } from './structured-item.component';

@Component({
  selector: 'app-structured-sequence',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, StructuredItemComponent],
  templateUrl: './structured-sequence.component.html',
})
export class StructuredSequenceComponent {
  @Input() items: StructuredSequence = [];
}
