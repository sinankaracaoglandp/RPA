import { ChangeDetectionStrategy, Component, Input, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '../../../../core/translate.pipe';
import { WorkflowVersion } from '../../../../shared/models/workflow.model';
import { StructuredSequence } from '../structured-model';
import { workflowToTree } from '../workflow-to-tree';
import { treeToWorkflow } from '../tree-to-workflow';
import { checkStructuralInvariants } from '../structural-invariants';
import { StructuredSequenceComponent } from './structured-sequence.component';

interface ViewState {
  kind: 'empty' | 'tree' | 'fallback';
  tree?: StructuredSequence;
}

@Component({
  selector: 'app-structured-view',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe, StructuredSequenceComponent],
  templateUrl: './structured-view.component.html',
  styleUrls: ['./structured-view.component.scss'],
})
export class StructuredViewComponent {
  private readonly _workflow = signal<WorkflowVersion | null>(null);
  @Input() set workflow(value: WorkflowVersion | null) { this._workflow.set(value); }

  readonly state = computed<ViewState>(() => this.convert(this._workflow()));

  private convert(workflow: WorkflowVersion | null): ViewState {
    if (!workflow || workflow.nodes.length === 0) {
      return { kind: 'empty' };
    }
    try {
      const tree = workflowToTree(workflow);
      // Güvence: graf yapısal alt-küme değilse (keyfi serbest-graf) fallback.
      // 1) Girişin kendisi yapısal değişmezleri ihlal ediyorsa (ör. eksik loop-back) → fallback.
      if (checkStructuralInvariants(workflow).length > 0) {
        return { kind: 'fallback' };
      }
      // 2) Ağacı geri çevir; node/bağlantı SAYILARI eşleşmiyorsa (ör. düşen dallar, ekstra entry)
      //    graf sadık biçimde temsil edilememiştir → fallback. (id'ler farklı olduğundan yalnız sayı.)
      let i = 0;
      const back = treeToWorkflow(tree, { idGen: () => `g${++i}` });
      if (back.nodes.length !== workflow.nodes.length
        || back.connections.length !== workflow.connections.length) {
        return { kind: 'fallback' };
      }
      return { kind: 'tree', tree };
    } catch {
      return { kind: 'fallback' };
    }
  }
}
