/**
 * Workflow template metadata (Faz 5, Task 5.5 — Template Gallery).
 * Backend: GET /api/templates → TemplateMetadata[]; GET /api/templates/{id} → TemplateMetadata.
 */

export type TemplateCategory = 'SAP' | 'Web' | 'Mail' | 'Data' | 'Other';

export interface TemplateMetadata {
  id: string;
  name: string;
  description?: string;
  /** Emoji or icon URL. */
  icon?: string;
  category: TemplateCategory | string;
  /** The workflow definition (WorkflowVersion) serialised as a JSON string. */
  workflowJson: string;
}
