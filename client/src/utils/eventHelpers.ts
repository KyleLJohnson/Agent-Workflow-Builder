import type { ExecutionEvent } from "@/types";

/** Strip trailing GUID suffix and replace underscores with spaces for display. */
export function cleanAgentName(raw: string | undefined | null): string | undefined {
  if (!raw) return undefined;
  const stripped = raw.replace(/_[0-9a-f]{32}$/i, "");
  return stripped.replace(/_/g, " ");
}

export function mapStructuredEvent(
  type: ExecutionEvent["type"],
  payload: Record<string, unknown>
): ExecutionEvent {
  return {
    type,
    executionId: (payload.executionId as string) || undefined,
    nodeId: (payload.nodeId as string) || undefined,
    agentName: cleanAgentName(
      (payload.executorName as string) || (payload.agentName as string)
    ),
    message: (payload.data as string) || undefined,
    output: (payload.data as string) || undefined,
    question: (payload.question as string) || undefined,
    previousAgentOutput: (payload.previousAgentOutput as string) || undefined,
    gateType: (payload.gateType as string) || undefined,
    gateInstructions: (payload.gateInstructions as string) || undefined,
    planSteps: (payload.planSteps as ExecutionEvent["planSteps"]) || undefined,
    loopIteration: (payload.loopIteration as number) || undefined,
    maxIterations: (payload.maxIterations as number) || undefined,
    timestamp: (payload.timestamp as string) ?? new Date().toISOString(),
  };
}
