import { useState, useEffect, useCallback, useMemo, useRef } from "react";
import { useNodesState, useEdgesState, type Node, type Edge } from "@xyflow/react";
import { AuthenticatedTemplate, UnauthenticatedTemplate, useMsal } from "@azure/msal-react";
import AgentPanel from "./components/AgentPanel";
import AgentEditor from "./components/AgentEditor";
import McpSettingsPanel from "./components/McpSettingsPanel";
import WorkflowCanvas, {
  workflowNodesToFlow,
  workflowEdgesToFlow,
  flowToWorkflowNodes,
  flowToWorkflowEdges,
} from "./components/WorkflowCanvas";
import WorkflowToolbar from "./components/WorkflowToolbar";
import WorkflowListView from "./components/WorkflowListView";
import ExecutionPanel from "./components/ExecutionPanel";
import { useSignalR } from "./hooks/useSignalR";
import { useExecutions } from "./hooks/useExecutions";
import { isAuthConfigured, loginRequest } from "./authConfig";
import * as api from "./api";
import type {
  AgentDefinition,
  WorkflowDefinition,
  CreateAgentRequest,
  McpServerConfig,
} from "./types";

export default function App() {
  const { instance, accounts } = useMsal();
  const authEnabled = isAuthConfigured();
  const userName = accounts[0]?.name ?? accounts[0]?.username;

  const handleLogin = () => instance.loginRedirect(loginRequest);
  const handleLogout = () => instance.logoutRedirect();

  // ─── Agents ──────────────────────────────────────────────
  const [agents, setAgents] = useState<AgentDefinition[]>([]);
  const [editingAgent, setEditingAgent] = useState<AgentDefinition | null>(null);
  const [showAgentEditor, setShowAgentEditor] = useState(false);

  const loadAgents = useCallback(async () => {
    try {
      const data = await api.getAgents();
      setAgents(data);
    } catch (err) {
      console.error("Failed to load agents:", err);
    }
  }, []);

  useEffect(() => {
    loadAgents();
  }, [loadAgents]);

  const handleCreateAgent = () => {
    setEditingAgent(null);
    setShowAgentEditor(true);
  };

  const handleEditAgent = (agent: AgentDefinition) => {
    setEditingAgent(agent);
    setShowAgentEditor(true);
  };

  const [agentError, setAgentError] = useState<string | null>(null);
  const [isSavingAgent, setIsSavingAgent] = useState(false);

  const handleSaveAgent = async (data: CreateAgentRequest) => {
    setAgentError(null);
    setIsSavingAgent(true);
    try {
      if (editingAgent) {
        await api.updateAgent(editingAgent.id, data);
      } else {
        await api.createAgent(data);
      }
      await loadAgents();
      setShowAgentEditor(false);
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : "Failed to save agent";
      setAgentError(message);
      console.error("Failed to save agent:", err);
    } finally {
      setIsSavingAgent(false);
    }
  };

  // ─── MCP Servers ─────────────────────────────────────────
  const [mcpServers, setMcpServers] = useState<McpServerConfig[]>([]);
  const [showMcpSettings, setShowMcpSettings] = useState(false);

  const loadMcpServers = useCallback(async () => {
    try {
      const data = await api.getMcpServers();
      setMcpServers(data);
    } catch (err) {
      console.error("Failed to load MCP servers:", err);
    }
  }, []);

  useEffect(() => {
    loadMcpServers();
  }, [loadMcpServers]);

  const handleOpenMcpSettings = () => {
    setShowMcpSettings(true);
  };

  // ─── Workflows ───────────────────────────────────────────
  const [savedWorkflows, setSavedWorkflows] = useState<WorkflowDefinition[]>([]);
  const [currentWorkflow, setCurrentWorkflow] = useState<WorkflowDefinition | null>(null);
  const [workflowName, setWorkflowName] = useState("Untitled Workflow");
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const [isSaving, setIsSaving] = useState(false);

  // React Flow state
  const initialNodes: Node[] = [];
  const initialEdges: Edge[] = [];
  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);

  const loadWorkflows = useCallback(async () => {
    try {
      const data = await api.getWorkflows();
      setSavedWorkflows(data);
    } catch (err) {
      console.error("Failed to load workflows:", err);
    }
  }, []);

  useEffect(() => {
    loadWorkflows();
  }, [loadWorkflows]);

  // Mark unsaved changes when nodes/edges change
  useEffect(() => {
    setHasUnsavedChanges(true);
  }, [nodes, edges, workflowName]);

  const handleLoadWorkflow = useCallback(
    async (wf: WorkflowDefinition) => {
      try {
        const full = await api.getWorkflow(wf.id);
        setCurrentWorkflow(full);
        setWorkflowName(full.name);
        setNodes(workflowNodesToFlow(full.nodes, agents));
        setEdges(workflowEdgesToFlow(full.edges));
        setHasUnsavedChanges(false);
      } catch (err) {
        console.error("Failed to load workflow:", err);
      }
    },
    [agents, setNodes, setEdges]
  );

  const handleNewWorkflow = () => {
    setCurrentWorkflow(null);
    setWorkflowName("Untitled Workflow");
    setNodes([]);
    setEdges([]);
    setHasUnsavedChanges(false);
  };

  const handleSaveWorkflow = async () => {
    setIsSaving(true);
    try {
      const payload = {
        name: workflowName,
        description: "",
        nodes: flowToWorkflowNodes(nodes),
        edges: flowToWorkflowEdges(edges),
      };

      if (currentWorkflow) {
        const updated = await api.updateWorkflow(currentWorkflow.id, payload);
        setCurrentWorkflow(updated);
      } else {
        const created = await api.createWorkflow(payload);
        setCurrentWorkflow(created);
      }
      setHasUnsavedChanges(false);
      await loadWorkflows();
    } catch (err) {
      console.error("Failed to save workflow:", err);
    } finally {
      setIsSaving(false);
    }
  };

  const handleDeleteWorkflow = async () => {
    if (!currentWorkflow) return;
    if (!confirm("Delete this workflow?")) return;
    try {
      await api.deleteWorkflow(currentWorkflow.id);
      handleNewWorkflow();
      await loadWorkflows();
    } catch (err) {
      console.error("Failed to delete workflow:", err);
    }
  };

  // ─── Execution ───────────────────────────────────────────
  // Use a stable ref to break the circular dependency between useSignalR and useExecutions.
  // useSignalR creates the connection; useExecutions registers its own event handlers on it.
  const execsRef = useRef<ReturnType<typeof useExecutions> | null>(null);

  const {
    connectionRef,
    isConnected,
  } = useSignalR((conn) => execsRef.current?.registerEventHandlers(conn));

  const execs = useExecutions(connectionRef);
  execsRef.current = execs;

  // Track which nodes are currently executing for the selected execution
  const executingNodeIds = useMemo(() => {
    const running = new Set<string>();
    const selectedEvents = execs.selectedExecution?.events ?? [];
    for (const evt of selectedEvents) {
      const key = evt.nodeId ?? evt.agentName;
      if (evt.type === "AgentStepStarted" && key) {
        running.add(key);
      }
      if (evt.type === "AgentStepCompleted" && key) {
        running.delete(key);
      }
      if (evt.type === "ExecutionCompleted" || evt.type === "Error") {
        running.clear();
      }
    }
    return running;
  }, [execs.selectedExecution?.events]);

  const handleExecute = useCallback(
    async (inputMessage: string) => {
      if (!currentWorkflow) return;
      await execs.startExecution(currentWorkflow.id, currentWorkflow.name, inputMessage);
    },
    [currentWorkflow, execs]
  );

  const handleToolbarExecute = () => {
    if (!currentWorkflow) return;
    handleExecute("Hello");
  };

  // ─── Render ──────────────────────────────────────────────
  const mainContent = (
    <div className="flex h-screen w-screen overflow-hidden">
      {/* Left: Agent Panel */}
      <AgentPanel
        agents={agents}
        onCreateAgent={handleCreateAgent}
        onEditAgent={handleEditAgent}
      />

      {/* Center: Canvas + Toolbar + Execution OR Landing View */}
      <div className="flex-1 flex flex-col min-w-0">
        {currentWorkflow ? (
          <>
            <WorkflowToolbar
              workflowName={workflowName}
              onNameChange={setWorkflowName}
              onSave={handleSaveWorkflow}
              onNew={handleNewWorkflow}
              onDelete={handleDeleteWorkflow}
              onLoad={handleLoadWorkflow}
              onExecute={handleToolbarExecute}
              savedWorkflows={savedWorkflows}
              currentWorkflowId={currentWorkflow?.id ?? null}
              isSaving={isSaving}
              isExecuting={execs.runningCount > 0}
              hasUnsavedChanges={hasUnsavedChanges}
              runningExecutionCount={execs.runningCount}
              userName={userName}
              onLogout={authEnabled ? handleLogout : undefined}
            />

            <WorkflowCanvas
              nodes={nodes}
              edges={edges}
              onNodesChange={onNodesChange}
              onEdgesChange={onEdgesChange}
              setNodes={setNodes}
              setEdges={setEdges}
              executingNodeIds={executingNodeIds}
            />

            <ExecutionPanel
              events={execs.selectedExecution?.events ?? []}
              isConnected={isConnected}
              isExecuting={execs.selectedExecution?.status === "running"}
              onExecute={handleExecute}
              onClear={() => execs.selectedExecutionId && execs.closeExecution(execs.selectedExecutionId)}
              workflowId={currentWorkflow?.id ?? null}
              onAnswerClarification={(executionId, answer) =>
                execs.answerClarification(executionId, answer)
              }
              onApproveGate={(executionId, editedOutput) =>
                execs.approveGate(executionId, editedOutput)
              }
              onRejectGate={(executionId, reason) =>
                execs.rejectGate(executionId, reason)
              }
              onSendBackGate={(executionId, feedback) =>
                execs.sendBackGate(executionId, feedback)
              }
              onCancelExecution={(executionId) =>
                execs.cancelExecution(executionId)
              }
              activeExecutionId={execs.selectedExecutionId ?? undefined}
              executions={execs.executions}
              onSelectExecution={execs.setSelectedExecutionId}
              onCloseExecution={execs.closeExecution}
            />
          </>
        ) : (
          <WorkflowListView
            workflows={savedWorkflows}
            onSelect={handleLoadWorkflow}
            onCreateNew={handleNewWorkflow}
          />
        )}
      </div>

      {/* Agent Editor Modal */}
      {showAgentEditor && (
        <AgentEditor
          agent={editingAgent}
          onSave={handleSaveAgent}
          onClose={() => { setShowAgentEditor(false); setAgentError(null); }}
          error={agentError}
          isSaving={isSavingAgent}
          mcpServers={mcpServers}
          onOpenMcpSettings={handleOpenMcpSettings}
        />
      )}

      {/* MCP Settings Modal */}
      {showMcpSettings && (
        <McpSettingsPanel
          onClose={() => setShowMcpSettings(false)}
          onServersChanged={loadMcpServers}
        />
      )}
    </div>
  );

  if (!authEnabled) return mainContent;

  return (
    <>
      <AuthenticatedTemplate>{mainContent}</AuthenticatedTemplate>
      <UnauthenticatedTemplate>
        <div className="flex h-screen items-center justify-center bg-gray-900">
          <div className="text-center">
            <h1 className="text-2xl font-bold text-white mb-4">Agent Workflow Builder</h1>
            <p className="text-gray-400 mb-6">Sign in to get started</p>
            <button
              onClick={handleLogin}
              className="px-6 py-3 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
            >
              Sign in with Microsoft
            </button>
          </div>
        </div>
      </UnauthenticatedTemplate>
    </>
  );
}
