export interface AgentDefinition {
  id: string;
  name: string;
  description: string;
  systemInstructions: string;
  category: string;
  icon: string;
  isBuiltIn: boolean;
  inputDescription: string;
  outputDescription: string;
  modelOverride?: string | null;
  temperature?: number | null;
  mcpServerIds?: string[];
  allowClarification?: boolean;
  agentType?: string;
  createdAt: string;
  updatedAt: string;
}

export interface WorkflowNode {
  nodeId: string;
  agentId: string;
  label: string;
  positionX: number;
  positionY: number;
  configOverrides?: Record<string, unknown> | null;
  nodeType?: string;
  gateConfig?: GateConfiguration | null;
}

export interface GateConfiguration {
  gateType: "Approval" | "ReviewAndEdit";
  instructions?: string | null;
  sendBackTargetNodeId?: string | null;
}

export interface WorkflowEdge {
  id: string;
  sourceNodeId: string;
  targetNodeId: string;
  isBackEdge?: boolean;
  maxIterations?: number | null;
}

export interface WorkflowDefinition {
  id: string;
  name: string;
  description: string;
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
  userId?: string;
  blobContainerName?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface PlanStepInfo {
  stepNumber: number;
  title: string;
  instruction: string;
  agentHint: string;
  matchedAgentId?: string | null;
  matchedAgentName?: string | null;
}

export interface ExecutionEvent {
  type:
    | "ExecutionStarted"
    | "AgentStepStarted"
    | "AgentStepCompleted"
    | "WorkflowOutput"
    | "ExecutionCompleted"
    | "Error"
    | "ClarificationNeeded"
    | "GateAwaitingApproval"
    | "GateApproved"
    | "GateRejected"
    | "LoopIterationStarted"
    | "LoopIterationCompleted"
    | "PlanGenerated"
    | "PlanTriggered";
  workflowId?: string;
  executionId?: string;
  nodeId?: string;
  agentName?: string;
  message?: string;
  output?: string;
  error?: string;
  question?: string;
  previousAgentOutput?: string;
  gateType?: string;
  gateInstructions?: string;
  planSteps?: PlanStepInfo[];
  loopIteration?: number;
  maxIterations?: number;
  timestamp: string;
}

export interface ExecutionState {
  executionId: string;
  workflowId: string;
  workflowName: string;
  status: "running" | "paused" | "completed" | "failed" | "cancelled";
  events: ExecutionEvent[];
  startedAt: string;
}

export interface CreateAgentRequest {
  name: string;
  description: string;
  systemInstructions: string;
  category: string;
  icon: string;
  inputDescription: string;
  outputDescription: string;
  modelOverride?: string | null;
  temperature?: number | null;
  mcpServerIds?: string[];
  allowClarification?: boolean;
  agentType?: string;
}

export interface CreateWorkflowRequest {
  name: string;
  description: string;
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
}

// ─── MCP ──────────────────────────────────────────────────

export interface McpServerConfig {
  id: string;
  name: string;
  description: string;
  transportType: string;
  command?: string | null;
  args?: string[] | null;
  env?: Record<string, string> | null;
  url?: string | null;
  enabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateMcpServerRequest {
  name: string;
  description: string;
  transportType: string;
  command?: string | null;
  args?: string[] | null;
  env?: Record<string, string> | null;
  url?: string | null;
  enabled: boolean;
}

export interface McpToolInfo {
  serverId: string;
  serverName: string;
  name: string;
  description?: string | null;
  inputSchema?: unknown;
}

export interface UserInfo {
  userId: string;
  displayName: string;
  email: string;
}
