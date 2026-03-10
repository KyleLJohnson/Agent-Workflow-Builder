import { useState, useEffect, useCallback, useMemo, useRef, lazy, Suspense } from "react";
import { AuthenticatedTemplate, UnauthenticatedTemplate, useMsal } from "@azure/msal-react";
import AgentPanel from "@/components/AgentPanel";
import WorkflowCanvas from "@/components/WorkflowCanvas";
import WorkflowToolbar from "@/components/WorkflowToolbar";
import WorkflowListView from "@/components/WorkflowListView";
import ExecutionPanel from "@/components/ExecutionPanel";
import { useSignalR } from "@/hooks/useSignalR";
import { useExecutions } from "@/hooks/useExecutions";
import { isAuthConfigured, loginRequest } from "@/authConfig";
import { AgentProvider, useAgentContext } from "@/contexts/AgentContext";
import { WorkflowProvider, useWorkflowContext } from "@/contexts/WorkflowContext";
import type { McpServerConfig } from "@/types";
import * as api from "@/api";

const AgentEditor = lazy(() => import("@/components/AgentEditor"));
const McpSettingsPanel = lazy(() => import("@/components/McpSettingsPanel"));

export default function App() {
  return (
    <AgentProvider>
      <AppWithWorkflow />
    </AgentProvider>
  );
}

function AppWithWorkflow() {
  const { agents } = useAgentContext();
  return (
    <WorkflowProvider agents={agents}>
      <AppContent />
    </WorkflowProvider>
  );
}

function AppContent() {
  const { instance, accounts } = useMsal();
  const authEnabled = isAuthConfigured();
  const userName = accounts[0]?.name ?? accounts[0]?.username;

  const handleLogin = useCallback(() => instance.loginRedirect(loginRequest), [instance]);
  const handleLogout = useCallback(() => instance.logoutRedirect(), [instance]);

  const agentCtx = useAgentContext();
  const wfCtx = useWorkflowContext();

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

  const handleOpenMcpSettings = useCallback(() => {
    setShowMcpSettings(true);
  }, []);

  // ─── Execution ───────────────────────────────────────────
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
      if ((evt.type === "AgentStepStarted" || evt.type === "GateAwaitingApproval") && key) {
        running.add(key);
      }
      if ((evt.type === "AgentStepCompleted" || evt.type === "GateApproved" || evt.type === "GateRejected" || evt.type === "GateAutoApproved") && key) {
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
      if (!wfCtx.currentWorkflow) return;
      await execs.startExecution(wfCtx.currentWorkflow.id, wfCtx.currentWorkflow.name, inputMessage, wfCtx.autoApproveGates);
    },
    [wfCtx.currentWorkflow, wfCtx.autoApproveGates, execs]
  );

  const handleToolbarExecute = useCallback(() => {
    if (!wfCtx.currentWorkflow) return;
    handleExecute("Hello");
  }, [wfCtx.currentWorkflow, handleExecute]);

  // ─── Render ──────────────────────────────────────────────
  const mainContent = (
    <div className="flex h-screen w-screen overflow-hidden">
      {/* Left: Agent Panel */}
      <AgentPanel
        agents={agentCtx.agents}
        onCreateAgent={agentCtx.handleCreateAgent}
        onEditAgent={agentCtx.handleEditAgent}
      />

      {/* Center: Canvas + Toolbar + Execution OR Landing View */}
      <div className="flex-1 flex flex-col min-w-0">
        {wfCtx.currentWorkflow ? (
          <>
            <WorkflowToolbar
              workflowName={wfCtx.workflowName}
              onNameChange={wfCtx.setWorkflowName}
              onSave={wfCtx.handleSaveWorkflow}
              onNew={wfCtx.handleNewWorkflow}
              onDelete={wfCtx.handleDeleteWorkflow}
              onLoad={wfCtx.handleLoadWorkflow}
              onExecute={handleToolbarExecute}
              savedWorkflows={wfCtx.savedWorkflows}
              currentWorkflowId={wfCtx.currentWorkflow?.id ?? null}
              isSaving={wfCtx.isSaving}
              isExecuting={execs.runningCount > 0}
              hasUnsavedChanges={wfCtx.hasUnsavedChanges}
              runningExecutionCount={execs.runningCount}
              autoApproveGates={wfCtx.autoApproveGates}
              onAutoApproveChange={wfCtx.setAutoApproveGates}
              userName={userName}
              onLogout={authEnabled ? handleLogout : undefined}
            />

            <WorkflowCanvas
              nodes={wfCtx.nodes}
              edges={wfCtx.edges}
              onNodesChange={wfCtx.onNodesChange}
              onEdgesChange={wfCtx.onEdgesChange}
              setNodes={wfCtx.setNodes}
              setEdges={wfCtx.setEdges}
              executingNodeIds={executingNodeIds}
            />

            <ExecutionPanel
              events={execs.selectedExecution?.events ?? []}
              isConnected={isConnected}
              isExecuting={execs.selectedExecution?.status === "running"}
              onExecute={handleExecute}
              onClear={() => execs.selectedExecutionId && execs.closeExecution(execs.selectedExecutionId)}
              workflowId={wfCtx.currentWorkflow?.id ?? null}
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
            workflows={wfCtx.savedWorkflows}
            onSelect={wfCtx.handleLoadWorkflow}
            onCreateNew={wfCtx.handleNewWorkflow}
            onDelete={wfCtx.deleteWorkflowById}
          />
        )}
      </div>

      {/* Agent Editor Modal */}
      {agentCtx.showAgentEditor && (
        <Suspense>
          <AgentEditor
            agent={agentCtx.editingAgent}
            onSave={agentCtx.handleSaveAgent}
            onClose={agentCtx.closeAgentEditor}
            error={agentCtx.agentError}
            isSaving={agentCtx.isSavingAgent}
            mcpServers={mcpServers}
            onOpenMcpSettings={handleOpenMcpSettings}
          />
        </Suspense>
      )}

      {/* MCP Settings Modal */}
      {showMcpSettings && (
        <Suspense>
          <McpSettingsPanel
            onClose={() => setShowMcpSettings(false)}
            onServersChanged={loadMcpServers}
          />
        </Suspense>
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
