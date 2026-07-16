export interface EInvoiceProfile {
  id: string;
  projectId: string;
  name: string;
  description?: string | null;
  draftDefinitionJson: string;
  createdAt: string;
  updatedAt?: string | null;
}

export interface EInvoiceProfileVersion {
  id: string;
  profileId: string;
  version: number;
  definitionJson?: string | null;
  outputSchemaJson: string;
  publishedAt: string;
  publishedBy?: string | null;
}

export interface CreateEInvoiceProfileRequest {
  name: string;
  description?: string | null;
}
