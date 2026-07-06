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
