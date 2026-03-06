import { createContext, useContext, useState, useEffect, useCallback } from "react";
import * as api from "@/api";
import type { AgentDefinition, CreateAgentRequest } from "@/types";

interface AgentContextValue {
  agents: AgentDefinition[];
  editingAgent: AgentDefinition | null;
  showAgentEditor: boolean;
  agentError: string | null;
  isSavingAgent: boolean;
  handleCreateAgent: () => void;
  handleEditAgent: (agent: AgentDefinition) => void;
  handleSaveAgent: (data: CreateAgentRequest) => Promise<void>;
  closeAgentEditor: () => void;
  loadAgents: () => Promise<void>;
}

const AgentContext = createContext<AgentContextValue | null>(null);

export function useAgentContext(): AgentContextValue {
  const ctx = useContext(AgentContext);
  if (!ctx) throw new Error("useAgentContext must be used within AgentProvider");
  return ctx;
}

export function AgentProvider({ children }: { children: React.ReactNode }) {
  const [agents, setAgents] = useState<AgentDefinition[]>([]);
  const [editingAgent, setEditingAgent] = useState<AgentDefinition | null>(null);
  const [showAgentEditor, setShowAgentEditor] = useState(false);
  const [agentError, setAgentError] = useState<string | null>(null);
  const [isSavingAgent, setIsSavingAgent] = useState(false);

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

  const handleCreateAgent = useCallback(() => {
    setEditingAgent(null);
    setShowAgentEditor(true);
  }, []);

  const handleEditAgent = useCallback((agent: AgentDefinition) => {
    setEditingAgent(agent);
    setShowAgentEditor(true);
  }, []);

  const handleSaveAgent = useCallback(
    async (data: CreateAgentRequest) => {
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
    },
    [editingAgent, loadAgents]
  );

  const closeAgentEditor = useCallback(() => {
    setShowAgentEditor(false);
    setAgentError(null);
  }, []);

  return (
    <AgentContext.Provider
      value={{
        agents,
        editingAgent,
        showAgentEditor,
        agentError,
        isSavingAgent,
        handleCreateAgent,
        handleEditAgent,
        handleSaveAgent,
        closeAgentEditor,
        loadAgents,
      }}
    >
      {children}
    </AgentContext.Provider>
  );
}
