import { createContext, useContext, useState, useEffect, useCallback } from "react";
import { useNodesState, useEdgesState, type Node, type Edge } from "@xyflow/react";
import {
  workflowNodesToFlow,
  workflowEdgesToFlow,
  flowToWorkflowNodes,
  flowToWorkflowEdges,
} from "@/utils/workflowConverters";
import * as api from "@/api";
import type { AgentDefinition, WorkflowDefinition } from "@/types";

interface WorkflowContextValue {
  savedWorkflows: WorkflowDefinition[];
  currentWorkflow: WorkflowDefinition | null;
  workflowName: string;
  setWorkflowName: (name: string) => void;
  autoApproveGates: boolean;
  setAutoApproveGates: (v: boolean) => void;
  hasUnsavedChanges: boolean;
  isSaving: boolean;
  nodes: Node[];
  edges: Edge[];
  onNodesChange: ReturnType<typeof useNodesState>[2];
  onEdgesChange: ReturnType<typeof useEdgesState>[2];
  setNodes: React.Dispatch<React.SetStateAction<Node[]>>;
  setEdges: React.Dispatch<React.SetStateAction<Edge[]>>;
  handleLoadWorkflow: (wf: WorkflowDefinition) => Promise<void>;
  handleNewWorkflow: () => void;
  handleSaveWorkflow: () => Promise<void>;
  handleDeleteWorkflow: () => Promise<void>;
  loadWorkflows: () => Promise<void>;
}

const WorkflowContext = createContext<WorkflowContextValue | null>(null);

export function useWorkflowContext(): WorkflowContextValue {
  const ctx = useContext(WorkflowContext);
  if (!ctx) throw new Error("useWorkflowContext must be used within WorkflowProvider");
  return ctx;
}

export function WorkflowProvider({
  agents,
  children,
}: {
  agents: AgentDefinition[];
  children: React.ReactNode;
}) {
  const [savedWorkflows, setSavedWorkflows] = useState<WorkflowDefinition[]>([]);
  const [currentWorkflow, setCurrentWorkflow] = useState<WorkflowDefinition | null>(null);
  const [workflowName, setWorkflowName] = useState("Untitled Workflow");
  const [autoApproveGates, setAutoApproveGates] = useState(false);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const [isSaving, setIsSaving] = useState(false);

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
        setAutoApproveGates(full.autoApproveGates ?? false);
        setNodes(workflowNodesToFlow(full.nodes, agents));
        setEdges(workflowEdgesToFlow(full.edges));
        setHasUnsavedChanges(false);
      } catch (err) {
        console.error("Failed to load workflow:", err);
      }
    },
    [agents, setNodes, setEdges]
  );

  const handleNewWorkflow = useCallback(() => {
    setCurrentWorkflow(null);
    setWorkflowName("Untitled Workflow");
    setAutoApproveGates(false);
    setNodes([]);
    setEdges([]);
    setHasUnsavedChanges(false);
  }, [setNodes, setEdges]);

  const handleSaveWorkflow = useCallback(async () => {
    setIsSaving(true);
    try {
      const payload = {
        name: workflowName,
        description: "",
        nodes: flowToWorkflowNodes(nodes),
        edges: flowToWorkflowEdges(edges),
        autoApproveGates,
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
  }, [workflowName, nodes, edges, autoApproveGates, currentWorkflow, loadWorkflows]);

  const handleDeleteWorkflow = useCallback(async () => {
    if (!currentWorkflow) return;
    if (!confirm("Delete this workflow?")) return;
    try {
      await api.deleteWorkflow(currentWorkflow.id);
      handleNewWorkflow();
      await loadWorkflows();
    } catch (err) {
      console.error("Failed to delete workflow:", err);
    }
  }, [currentWorkflow, handleNewWorkflow, loadWorkflows]);

  return (
    <WorkflowContext.Provider
      value={{
        savedWorkflows,
        currentWorkflow,
        workflowName,
        setWorkflowName,
        autoApproveGates,
        setAutoApproveGates,
        hasUnsavedChanges,
        isSaving,
        nodes,
        edges,
        onNodesChange,
        onEdgesChange,
        setNodes,
        setEdges,
        handleLoadWorkflow,
        handleNewWorkflow,
        handleSaveWorkflow,
        handleDeleteWorkflow,
        loadWorkflows,
      }}
    >
      {children}
    </WorkflowContext.Provider>
  );
}
