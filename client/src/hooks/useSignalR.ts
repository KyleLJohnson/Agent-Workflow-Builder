import { useEffect, useRef, useState, useCallback } from "react";
import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
  HubConnectionState,
} from "@microsoft/signalr";
import type { ExecutionEvent } from "../types";

/** Strip trailing GUID suffix and replace underscores with spaces for display.
 *  e.g. "Code_Reviewer_00e65282ec4a426db9291bd6fde5eff7" → "Code Reviewer" */
function cleanAgentName(raw: string | undefined | null): string | undefined {
  if (!raw) return undefined;
  // Remove trailing _<32-hex-char> GUID
  const stripped = raw.replace(/_[0-9a-f]{32}$/i, "");
  // Replace underscores with spaces
  return stripped.replace(/_/g, " ");
}

export function useSignalR() {
  const connectionRef = useRef<HubConnection | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [events, setEvents] = useState<ExecutionEvent[]>([]);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/workflow")
      .withAutomaticReconnect()
      .withServerTimeout(300_000)   // 5 min — AI workflows can take 30-60+ sec
      .withKeepAliveInterval(15_000) // 15 sec keep-alive ping
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
    // Backend sends WorkflowExecutionEvent { eventType, executorName, data, timestamp }
    connection.on("AgentStepStarted", (payload: Record<string, unknown>) => {
      console.log("[SignalR] AgentStepStarted raw payload:", payload);
      setEvents((prev) => [
        ...prev,
        {
          type: "AgentStepStarted",
          nodeId: (payload.nodeId as string) || undefined,
          agentName: cleanAgentName((payload.executorName as string) || (payload.agentName as string)),
          message: (payload.data as string) || undefined,
          timestamp: (payload.timestamp as string) ?? new Date().toISOString(),
        },
      ]);
    });

    connection.on("AgentStepCompleted", (payload: Record<string, unknown>) => {
      console.log("[SignalR] AgentStepCompleted raw payload:", payload);
      setEvents((prev) => [
        ...prev,
        {
          type: "AgentStepCompleted",
          nodeId: (payload.nodeId as string) || undefined,
          agentName: cleanAgentName((payload.executorName as string) || (payload.agentName as string)),
          output: (payload.data as string) || undefined,
          timestamp: (payload.timestamp as string) ?? new Date().toISOString(),
        },
      ]);
    });

    connection.on("WorkflowOutput", (payload: Record<string, unknown>) => {
      console.log("[SignalR] WorkflowOutput raw payload:", payload);
      setEvents((prev) => [
        ...prev,
        {
          type: "WorkflowOutput",
          agentName: cleanAgentName((payload.executorName as string) || (payload.agentName as string)),
          output: (payload.data as string) || undefined,
          timestamp: (payload.timestamp as string) ?? new Date().toISOString(),
        },
      ]);
    });

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
  }, []);

  const executeWorkflow = useCallback(
    async (workflowId: string, inputMessage: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        console.error("SignalR not connected");
        return;
      }
      try {
        await connection.invoke(
          "ExecuteWorkflow",
          workflowId,
          inputMessage
        );
      } catch (err) {
        console.error("ExecuteWorkflow invocation failed:", err);
      }
    },
    []
  );

  const clearEvents = useCallback(() => setEvents([]), []);

  return { isConnected, events, executeWorkflow, clearEvents };
}
