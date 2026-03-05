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
}

export interface WorkflowEdge {
  id: string;
  sourceNodeId: string;
  targetNodeId: string;
}

export interface WorkflowDefinition {
  id: string;
  name: string;
  description: string;
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
  createdAt: string;
  updatedAt: string;
}

export interface ExecutionEvent {
  type:
    | "ExecutionStarted"
    | "AgentStepStarted"
    | "AgentStepCompleted"
    | "WorkflowOutput"
    | "ExecutionCompleted"
    | "Error";
  workflowId?: string;
  nodeId?: string;
  agentName?: string;
  message?: string;
  output?: string;
  error?: string;
  timestamp: string;
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
