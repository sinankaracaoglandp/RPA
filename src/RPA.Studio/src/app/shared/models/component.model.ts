/**
 * Component library models — mirrors RPA.Domain ComponentVersion (WP-4.5/5.3).
 * Backend: GET /api/components → ComponentVersion[]
 * Status: Draft, Test, Published, Deprecated
 */

export type ComponentStatusType = 'Draft' | 'Test' | 'Published' | 'Deprecated';

export interface ComponentVersion {
  id: string;
  componentId: string;
  version: string; // SemVer
  displayName: string; // e.g. "SAP Login"
  description?: string;
  author?: string;
  category?: string;
  status: ComponentStatusType;
  jsonDefinition: string; // JSON object as string
  inputOutputSchema?: string; // JSON schema
  createdAt?: string;
  updatedAt?: string;
}

export interface PublishComponentRequest {
  version: string;
  jsonDefinition: string;
  inputOutputSchema?: string;
}

export interface ApproveComponentRequest {
  // No body needed — version is in URL
}
