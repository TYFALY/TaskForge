export interface QueueMetrics {
  queueSize: number;
  activeWorkers: number;
  timestamp: string;
  mode?: string;
  processedCount?: number;
}

export type JobStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed' | 'DeadLettered';

export interface Job {
  id: string;
  queueName: string;
  payload: string;
  status: JobStatus;
  retryCount: number;
  maxRetries: number;
  deadLetterReason?: string;
  createdAt: string;
  updatedAt: string;
  jobType?: string;
}

export interface JobEvent {
  id: string;
  queueName?: string;
  payload?: string;
  status: string;
  jobType?: string;
  retryCount?: number;
  maxRetries?: number;
  createdAt?: string;
  updatedAt?: string;
}

export interface KpiData {
  totalProcessed: number;
  failedCount: number;
  deadLetteredCount: number;
  activeWorkers: number;
  avgLatencyMs: number;
}

export type WorkflowStatus = 'Draft' | 'Active' | 'Running' | 'Completed' | 'Failed' | 'Paused';
export type StepStatus = 'Pending' | 'Waiting' | 'Running' | 'Completed' | 'Failed' | 'Skipped';

export interface DagNode {
  id: string;
  name: string;
  description: string;
  x: number;
  y: number;
  queueName: string;
  payload: string;
  status: StepStatus;
  isParallel: boolean;
}

export interface DagEdge {
  id: string;
  sourceId: string;
  targetId: string;
  label?: string;
}

export interface WorkflowDag {
  workflowId: string;
  name: string;
  nodes: DagNode[];
  edges: DagEdge[];
}

export interface WorkflowDefinition {
  id: string;
  name: string;
  description: string;
  namespace: string;
  status: WorkflowStatus;
  createdAt: string;
  updatedAt?: string;
  steps: WorkflowStep[];
}

export interface WorkflowStep {
  id: string;
  name: string;
  description: string;
  order: number;
  queueName: string;
  payload: string;
  status: StepStatus;
  isParallel: boolean;
  dependsOnStepIds?: string[];
}

export interface WorkflowExecution {
  id: string;
  workflowId: string;
  workflowName?: string;
  status: WorkflowStatus;
  startedAt: string;
  completedAt?: string;
  currentStepId?: string;
  errorMessage?: string;
  stepExecutions: StepExecution[];
}

export interface StepExecution {
  id: string;
  stepId: string;
  stepName: string;
  status: StepStatus;
  startedAt?: string;
  completedAt?: string;
  result?: string;
  errorMessage?: string;
}