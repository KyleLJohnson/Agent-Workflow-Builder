import { useState, useEffect, useCallback, useMemo } from "react";
import { useNodesState, useEdgesState, type Node, type Edge } from "@xyflow/react";
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
import ExecutionPanel from "./components/ExecutionPanel";
import { useSignalR } from "./hooks/useSignalR";
import * as api from "./api";
import type {
  AgentDefinition,
  WorkflowDefinition,
  CreateAgentRequest,
  McpServerConfig,
} from "./types";

export default function App() {
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
  const { isConnected, events, executeWorkflow, clearEvents } = useSignalR();
  const [isExecuting, setIsExecuting] = useState(false);

  // Track which nodes are currently executing (match by nodeId or agentName)
  const executingNodeIds = useMemo(() => {
    const running = new Set<string>();
    for (const evt of events) {
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
  }, [events]);

  // Detect execution end
  useEffect(() => {
    const last = events[events.length - 1];
    if (
      last &&
      (last.type === "ExecutionCompleted" || last.type === "Error")
    ) {
      setIsExecuting(false);
    }
  }, [events]);

  const handleExecute = useCallback(
    async (inputMessage: string) => {
      if (!currentWorkflow) return;
      setIsExecuting(true);
      clearEvents();
      try {
        await executeWorkflow(currentWorkflow.id, inputMessage);
      } catch (err) {
        console.error("Execution failed:", err);
        setIsExecuting(false);
      }
    },
    [currentWorkflow, executeWorkflow, clearEvents]
  );

  const handleToolbarExecute = () => {
    if (!currentWorkflow) return;
    // Open the execution panel input; just trigger with a default message
    handleExecute("Hello");
  };

  // ─── Render ──────────────────────────────────────────────
  return (
    <div className="flex h-screen w-screen overflow-hidden">
      {/* Left: Agent Panel */}
      <AgentPanel
        agents={agents}
        onCreateAgent={handleCreateAgent}
        onEditAgent={handleEditAgent}
      />

      {/* Center: Canvas + Toolbar + Execution */}
      <div className="flex-1 flex flex-col min-w-0">
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
          isExecuting={isExecuting}
          hasUnsavedChanges={hasUnsavedChanges}
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
          events={events}
          isConnected={isConnected}
          isExecuting={isExecuting}
          onExecute={handleExecute}
          onClear={clearEvents}
          workflowId={currentWorkflow?.id ?? null}
        />
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
}
