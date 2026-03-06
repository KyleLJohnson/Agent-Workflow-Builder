import { useEffect, useRef, useState, useCallback } from "react";
import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
  HubConnectionState,
} from "@microsoft/signalr";
import type { ExecutionEvent } from "@/types";
import { acquireAccessToken } from "@/authUtils";
import { mapStructuredEvent } from "@/utils/eventHelpers";

export function useSignalR(
  onRegisterHandlers?: (connection: HubConnection) => void
) {
  const connectionRef = useRef<HubConnection | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [events, setEvents] = useState<ExecutionEvent[]>([]);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/workflow", {
        accessTokenFactory: acquireAccessToken,
      })
      .withAutomaticReconnect()
      .withServerTimeout(300_000)
      .withKeepAliveInterval(15_000)
      .configureLogging(LogLevel.Information)
      .build();

    connectionRef.current = connection;

    // ── Simple string-payload events ──────────────────────
    connection.on("ExecutionStarted", (workflowId: string) => {
      setEvents((prev) => [
        ...prev,
        {
          type: "ExecutionStarted",
          workflowId,
          message: `Workflow ${workflowId} started`,
          timestamp: new Date().toISOString(),
        },
      ]);
    });

    connection.on("ExecutionCompleted", (workflowId: string) => {
      setEvents((prev) => [
        ...prev,
        {
          type: "ExecutionCompleted",
          workflowId,
          message: "Workflow completed",
          timestamp: new Date().toISOString(),
        },
      ]);
    });

    connection.on("Error", (errorMessage: string) => {
      setEvents((prev) => [
        ...prev,
        {
          type: "Error",
          error: errorMessage,
          timestamp: new Date().toISOString(),
        },
      ]);
    });

    // ── Structured event-payload events ────────────────────
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
    ];

    for (const eventType of structuredEvents) {
      connection.on(eventType, (payload: Record<string, unknown>) => {
        setEvents((prev) => [...prev, mapStructuredEvent(eventType, payload)]);
      });
    }

    // Register additional handlers (e.g., from useExecutions)
    onRegisterHandlers?.(connection);

    connection.onreconnecting(() => setIsConnected(false));
    connection.onreconnected(() => setIsConnected(true));
    connection.onclose(() => setIsConnected(false));

    connection
      .start()
      .then(() => setIsConnected(true))
      .catch((err) => console.error("SignalR connection failed:", err));

    return () => {
      if (connection.state !== HubConnectionState.Disconnected) {
        connection.stop();
      }
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const executeWorkflow = useCallback(
    async (workflowId: string, inputMessage: string): Promise<string> => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        console.error("SignalR not connected");
        return "";
      }
      try {
        const executionId = await connection.invoke<string>(
          "ExecuteWorkflow",
          workflowId,
          inputMessage
        );
        return executionId ?? "";
      } catch (err) {
        console.error("ExecuteWorkflow invocation failed:", err);
        return "";
      }
    },
    []
  );

  const answerClarification = useCallback(
    async (executionId: string, answer: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected)
        return;
      await connection.invoke("AnswerClarification", executionId, answer);
    },
    []
  );

  const approveGate = useCallback(
    async (executionId: string, editedOutput?: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected)
        return;
      await connection.invoke("ApproveGate", executionId, editedOutput ?? null);
    },
    []
  );

  const rejectGate = useCallback(
    async (executionId: string, reason?: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected)
        return;
      await connection.invoke("RejectGate", executionId, reason ?? null);
    },
    []
  );

  const sendBackGate = useCallback(
    async (executionId: string, feedback?: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected)
        return;
      await connection.invoke("SendBackGate", executionId, feedback ?? null);
    },
    []
  );

  const cancelExecution = useCallback(async (executionId: string) => {
    const connection = connectionRef.current;
    if (!connection || connection.state !== HubConnectionState.Connected)
      return;
    await connection.invoke("CancelExecution", executionId);
  }, []);

  const clearEvents = useCallback(() => setEvents([]), []);

  return {
    connectionRef,
    isConnected,
    events,
    executeWorkflow,
    clearEvents,
    answerClarification,
    approveGate,
    rejectGate,
    sendBackGate,
    cancelExecution,
  };
}
