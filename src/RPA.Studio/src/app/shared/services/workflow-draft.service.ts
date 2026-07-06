import { Injectable, signal } from '@angular/core';
import { WorkflowVersion } from '../models/workflow.model';

/**
 * Hand-off point for "create workflow from template" (Faz 5, Task 5.5).
 * The Template Wizard stores the newly created workflow here before
 * navigating to the designer; the designer consumes (and clears) it on load.
 */
@Injectable({ providedIn: 'root' })
export class WorkflowDraftService {
  private readonly _pending = signal<WorkflowVersion | null>(null);

  setPending(workflow: WorkflowVersion): void {
    this._pending.set(workflow);
  }

  /** Returns and clears the pending draft, if any. */
  consumePending(): WorkflowVersion | null {
    const pending = this._pending();
    this._pending.set(null);
    return pending;
  }
}
