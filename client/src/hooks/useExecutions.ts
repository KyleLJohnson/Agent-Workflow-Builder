import { useState, useCallback, useRef } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { HubConnectionState } from "@microsoft/signalr";
import type { ExecutionEvent, ExecutionState } from "@/types";
import { mapStructuredEvent } from "@/utils/eventHelpers";

/**
 * Manages multiple concurrent workflow executions, each with its own events array.
 * Replaces the single isExecuting/events pattern in App.tsx.
 */
export function useExecutions(connectionRef: React.RefObject<HubConnection | null>) {
  const [executions, setExecutions] = useState<Map<string, ExecutionState>>(new Map());
  const [selectedExecutionId, setSelectedExecutionId] = useState<string | null>(null);
  // Track the most recently started execution for fallback routing
  const lastStartedRef = useRef<string | null>(null);

  const routeEvent = useCallback(
    (executionId: string | undefined, event: ExecutionEvent) => {
      const targetId = executionId ?? lastStartedRef.current;
      if (!targetId) return;

      setExecutions((prev) => {
        const next = new Map(prev);
        const existing = next.get(targetId);
        if (!existing) return prev;

        let status = existing.status;
        if (
          event.type === "ClarificationNeeded" ||
          event.type === "GateAwaitingApproval"
        ) {
          status = "paused";
        } else if (event.type === "ExecutionCompleted") {
          status = "completed";
        } else if (event.type === "Error") {
          status = "failed";
        } else if (
          status === "paused" &&
          (event.type === "AgentStepStarted" || event.type === "GateApproved" || event.type === "GateAutoApproved")
        ) {
          status = "running";
        }

        next.set(targetId, {
          ...existing,
          status,
          events: [...existing.events, event],
        });
        return next;
      });
    },
    []
  );

  const registerEventHandlers = useCallback(
    (connection: HubConnection) => {
      // Simple string-payload events
      connection.on("ExecutionStarted", (workflowId: string) => {
        const event: ExecutionEvent = {
          type: "ExecutionStarted",
          workflowId,
          message: `Workflow ${workflowId} started`,
          timestamp: new Date().toISOString(),
        };
        routeEvent(lastStartedRef.current ?? undefined, event);
      });

      connection.on("ExecutionCompleted", (workflowId: string) => {
        const event: ExecutionEvent = {
          type: "ExecutionCompleted",
          workflowId,
          message: "Workflow completed",
          timestamp: new Date().toISOString(),
        };
        routeEvent(lastStartedRef.current ?? undefined, event);
      });

      connection.on("Error", (errorMessage: string) => {
        const event: ExecutionEvent = {
          type: "Error",
          error: errorMessage,
          timestamp: new Date().toISOString(),
        };
        routeEvent(lastStartedRef.current ?? undefined, event);
      });

      // Structured events — route by executionId in payload
      const structuredEvents: ExecutionEvent["type"][] = [
        "AgentStepStarted",
        "AgentStepCompleted",
        "WorkflowOutput",
        "ClarificationNeeded",
        "GateAwaitingApproval",
        "GateApproved",
        "GateRejected",
        "LoopIterationStarted",
        "LoopIterationCompleted",
        "PlanGenerated",
        "PlanTriggered",
        "GateAutoApproved",
      ];

      for (const eventType of structuredEvents) {
        connection.on(eventType, (payload: Record<string, unknown>) => {
          const event = mapStructuredEvent(eventType, payload);
          routeEvent(event.executionId, event);
        });
      }
    },
    [routeEvent]
  );

  const startExecution = useCallback(
    async (
      workflowId: string,
      workflowName: string,
      inputMessage: string,
      autoApproveGates?: boolean
    ): Promise<string> => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        console.error("SignalR not connected");
        return "";
      }

      try {
        const executionId = await connection.invoke<string>(
          "ExecuteWorkflow",
          workflowId,
          inputMessage,
          autoApproveGates ?? null
        );
        if (!executionId) return "";

        const newState: ExecutionState = {
          executionId,
          workflowId,
          workflowName,
          status: "running",
          events: [],
          startedAt: new Date().toISOString(),
        };

        lastStartedRef.current = executionId;
        setExecutions((prev) => {
          const next = new Map(prev);
          next.set(executionId, newState);
          return next;
        });
        setSelectedExecutionId(executionId);

        return executionId;
      } catch (err) {
        console.error("ExecuteWorkflow invocation failed:", err);
        return "";
      }
    },
    [connectionRef]
  );

  const cancelExecution = useCallback(
    async (executionId: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected)
        return;
      await connection.invoke("CancelExecution", executionId);
      setExecutions((prev) => {
        const next = new Map(prev);
        const existing = next.get(executionId);
        if (existing) {
          next.set(executionId, { ...existing, status: "cancelled" });
        }
        return next;
      });
    },
    [connectionRef]
  );

  const closeExecution = useCallback(
    (executionId: string) => {
      setExecutions((prev) => {
        const next = new Map(prev);
        next.delete(executionId);
        return next;
      });
      if (selectedExecutionId === executionId) {
        setSelectedExecutionId(null);
      }
    },
    [selectedExecutionId]
  );

  const answerClarification = useCallback(
    async (executionId: string, answer: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected)
        return;
      await connection.invoke("AnswerClarification", executionId, answer);
    },
    [connectionRef]
  );

  const approveGate = useCallback(
    async (executionId: string, editedOutput?: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected)
        return;
      await connection.invoke("ApproveGate", executionId, editedOutput ?? null);
    },
    [connectionRef]
  );

  const rejectGate = useCallback(
    async (executionId: string, reason?: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected)
        return;
      await connection.invoke("RejectGate", executionId, reason ?? null);
    },
    [connectionRef]
  );

  const sendBackGate = useCallback(
    async (executionId: string, feedback?: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected)
        return;
      await connection.invoke("SendBackGate", executionId, feedback ?? null);
    },
    [connectionRef]
  );

  const activeExecutions = Array.from(executions.values());

  const selectedExecution = selectedExecutionId
    ? executions.get(selectedExecutionId) ?? null
    : null;

  const runningCount = activeExecutions.filter(
    (e) => e.status === "running" || e.status === "paused"
  ).length;

  return {
    executions: activeExecutions,
    selectedExecution,
    selectedExecutionId,
    setSelectedExecutionId,
    startExecution,
    cancelExecution,
    closeExecution,
    answerClarification,
    approveGate,
    rejectGate,
    sendBackGate,
    registerEventHandlers,
    runningCount,
  };
}
