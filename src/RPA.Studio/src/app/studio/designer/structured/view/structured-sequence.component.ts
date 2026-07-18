import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CdkDrag, CdkDropList, CdkDragDrop } from '@angular/cdk/drag-drop';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { StructuredSequence } from '../structured-model';
import { StructuredAction, StructuredItemComponent } from './structured-item.component';

@Component({
  selector: 'app-structured-sequence',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, StructuredItemComponent, CdkDrag, CdkDropList],
  templateUrl: './structured-sequence.component.html',
})
export class StructuredSequenceComponent {
  @Input() items: StructuredSequence = [];
  @Input() editable = false;
  @Output() readonly action = new EventEmitter<StructuredAction>();
  @Output() readonly drop = new EventEmitter<CdkDragDrop<StructuredSequence>>();
}
