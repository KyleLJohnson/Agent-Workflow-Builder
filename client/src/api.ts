import axios from "axios";
import type { IPublicClientApplication } from "@azure/msal-browser";
import type {
  AgentDefinition,
  WorkflowDefinition,
  CreateAgentRequest,
  CreateWorkflowRequest,
  McpServerConfig,
  CreateMcpServerRequest,
  McpToolInfo,
} from "@/types";
import { tokenRequest, isAuthConfigured } from "@/authConfig";

let msalInstance: IPublicClientApplication | null = null;

/** Called from main.tsx to provide the MSAL instance for token acquisition. */
export function setMsalInstance(instance: IPublicClientApplication): void {
  msalInstance = instance;
}

const client = axios.create({
  baseURL: "/api",
  headers: { "Content-Type": "application/json" },
});

// Attach Bearer token when auth is configured
client.interceptors.request.use(async (config) => {
  if (!msalInstance || !isAuthConfigured()) return config;
  try {
    const accounts = msalInstance.getAllAccounts();
    if (accounts.length > 0) {
      const response = await msalInstance.acquireTokenSilent({
        ...tokenRequest,
        account: accounts[0],
      });
      config.headers.Authorization = `Bearer ${response.accessToken}`;
    }
  } catch {
    // Silent acquisition failed; let the request proceed without token — server will 401
  }
  return config;
});

// ─── Agents ───────────────────────────────────────────────

export async function getAgents(): Promise<AgentDefinition[]> {
  const { data } = await client.get<AgentDefinition[]>("/agents");
  return data;
}

export async function getAgent(id: string): Promise<AgentDefinition> {
  const { data } = await client.get<AgentDefinition>(`/agents/${id}`);
  return data;
}

export async function createAgent(
  agent: CreateAgentRequest
): Promise<AgentDefinition> {
  const { data } = await client.post<AgentDefinition>("/agents", agent);
  return data;
}

export async function updateAgent(
  id: string,
  agent: Partial<CreateAgentRequest>
): Promise<AgentDefinition> {
  const { data } = await client.put<AgentDefinition>(`/agents/${id}`, agent);
  return data;
}

export async function deleteAgent(id: string): Promise<void> {
  await client.delete(`/agents/${id}`);
}

// ─── Workflows ────────────────────────────────────────────

export async function getWorkflows(): Promise<WorkflowDefinition[]> {
  const { data } = await client.get<WorkflowDefinition[]>("/workflows");
  return data;
}

export async function getWorkflow(id: string): Promise<WorkflowDefinition> {
  const { data } = await client.get<WorkflowDefinition>(`/workflows/${id}`);
  return data;
}

export async function createWorkflow(
  workflow: CreateWorkflowRequest
): Promise<WorkflowDefinition> {
  const { data } = await client.post<WorkflowDefinition>(
    "/workflows",
    workflow
  );
  return data;
}

export async function updateWorkflow(
  id: string,
  workflow: Partial<CreateWorkflowRequest>
): Promise<WorkflowDefinition> {
  const { data } = await client.put<WorkflowDefinition>(
    `/workflows/${id}`,
    workflow
  );
  return data;
}

export async function deleteWorkflow(id: string): Promise<void> {
  await client.delete(`/workflows/${id}`);
}

export async function executeWorkflow(
  workflowId: string,
  inputMessage: string
): Promise<void> {
  await client.post(`/workflows/${workflowId}/execute`, { inputMessage });
}

// ─── Executions ───────────────────────────────────────────

export async function getExecutions(): Promise<unknown[]> {
  const { data } = await client.get<unknown[]>("/executions");
  return data;
}

export async function getExecution(id: string): Promise<unknown> {
  const { data } = await client.get<unknown>(`/executions/${id}`);
  return data;
}

export async function getWorkflowExecutions(workflowId: string): Promise<unknown[]> {
  const { data } = await client.get<unknown[]>(`/workflows/${workflowId}/executions`);
  return data;
}

export async function getPausedExecutions(): Promise<unknown[]> {
  const { data } = await client.get<unknown[]>("/executions/paused");
  return data;
}

// ─── MCP Servers ──────────────────────────────────────────

export async function getMcpServers(): Promise<McpServerConfig[]> {
  const { data } = await client.get<McpServerConfig[]>("/mcp/servers");
  return data;
}

export async function createMcpServer(
  server: CreateMcpServerRequest
): Promise<McpServerConfig> {
  const { data } = await client.post<McpServerConfig>("/mcp/servers", server);
  return data;
}

export async function updateMcpServer(
  id: string,
  server: Partial<CreateMcpServerRequest>
): Promise<McpServerConfig> {
  const { data } = await client.put<McpServerConfig>(
    `/mcp/servers/${id}`,
    server
  );
  return data;
}

export async function deleteMcpServer(id: string): Promise<void> {
  await client.delete(`/mcp/servers/${id}`);
}

export async function getMcpTools(): Promise<McpToolInfo[]> {
  const { data } = await client.get<McpToolInfo[]>("/mcp/tools");
  return data;
}
