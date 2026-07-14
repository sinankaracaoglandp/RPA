/** Orchestrator UI (WP-6.1) read-side modelleri — backend DTO'larıyla birebir. */

export interface DashboardSummary {
  total: number;
  running: number;
  successful: number;
  failed: number;
  businessException: number;
  abandoned: number;
  successRate: number;
}

export interface JobRun {
  id: string;
  workflowVersionId: string;
  status: string;
  triggeredBy: string;
  assignedRobotId: string | null;
  environmentId: string;
  startedAt: string;
  completedAt: string | null;
  correlationId: string;
}

export interface JobRunListResponse {
  totalCount: number;
  items: JobRun[];
}

export interface Robot {
  id: string;
  machineName: string;
  mode: string;
  status: string;
  tags?: string | null;
  agentVersion?: string | null;
  lastHeartbeat?: string | null;
}

export interface QueueSummary {
  id: string;
  name: string;
  maxRetries: number;
  slaSeconds: number | null;
  newCount: number;
  inProgressCount: number;
  failedCount: number;
  total: number;
}

export interface QueueItem {
  id: string;
  queueId: string;
  status: string;
  attemptCount: number;
  assignedRobotId: string | null;
  payload: string;
  errorDetail: string | null;
}

export interface QueueItemListResponse {
  totalCount: number;
  items: QueueItem[];
}

export interface ActionItem {
  id: string;
  type: string;
  status: string;
  jobRunId: string | null;
  queueItemId: string | null;
  assignedUserId: string | null;
  resolutionNote: string | null;
  resolvedAt: string | null;
  timeoutAt: string | null;
  createdAt: string;
}

export interface AlertRule {
  id: string;
  name: string;
  condition: string;
  channel: string;
  recipients: string;
  isActive: boolean;
}

export interface CreateAlertRuleRequest {
  name: string;
  condition: string;
  channel: string;
  recipients: string;
  isActive: boolean;
}

export interface JobRunQuery {
  status?: string;
  environmentId?: string;
  robotId?: string;
  skip?: number;
  take?: number;
}

// WP-6.4 — Ortam yönetimi + deployment governance
export interface Environment {
  id: string;
  name: string;
  description: string;
}

export interface WorkflowVersion {
  id: string;
  workflowId: string;
  version: string;
  status: string;
  environmentId: string;
  changeNotes?: string | null;
}

export interface CredentialReference {
  key: string;
  type?: string | null;
  environment?: string | null;
  description?: string | null;
  metadata: Record<string, string>;
}

export interface StoreCredentialRequest {
  key: string;
  secret: string;
  type?: string;
  environment?: string;
  description?: string;
  metadata?: Record<string, string>;
}

// WP-6.6 — Job tanımları (trigger) yönetimi
export interface TriggerScheduleDto {
  cronExpression: string;
  timeZone: string;
  overlapPolicy: string;
}

export interface TriggerDefinition {
  id: string;
  workflowVersionId: string;
  type: string;
  configuration: string;
  isActive: boolean;
  targetRobotTags: string;
  priority: number;
  schedule?: TriggerScheduleDto | null;
}

export interface CreateTriggerRequest {
  projectId: string;
  workflowVersionId: string;
  type: string;
  configuration?: string;
  environmentId: string;
  isActive: boolean;
  targetRobotTags: string;
  priority: number;
  schedule?: TriggerScheduleDto | null;
}

export interface UpdateTriggerRequest {
  isActive?: boolean;
  configuration?: string;
  targetRobotTags?: string;
  priority?: number;
  schedule?: TriggerScheduleDto | null;
}

export interface TriggerListQuery {
  projectId?: string;
  environmentId?: string;
  isActive?: boolean;
}
