import type { Node, Edge } from "@xyflow/react";
import type { AgentNodeData } from "@/components/AgentNode";
import type { AgentDefinition, WorkflowNode, WorkflowEdge } from "@/types";

export function workflowNodesToFlow(
  wfNodes: WorkflowNode[],
  agents: AgentDefinition[]
): Node[] {
  return wfNodes.map((n) => {
    if (n.nodeType === "gate") {
      return {
        id: n.nodeId,
        type: "gate",
        position: { x: n.positionX, y: n.positionY },
        data: {
          label: n.label || "Gate",
          gateType: n.gateConfig?.gateType || "Approval",
          instructions: n.gateConfig?.instructions || "",
        },
      };
    }
    const agent = agents.find((a) => a.id === n.agentId);
    return {
      id: n.nodeId,
      type: "agent",
      position: { x: n.positionX, y: n.positionY },
      data: {
        label: n.label || agent?.name || "Agent",
        icon: agent?.icon || "🤖",
        description: agent?.description || "",
        agentId: n.agentId,
        agentType: agent?.agentType,
      } satisfies AgentNodeData,
    };
  });
}

export function workflowEdgesToFlow(wfEdges: WorkflowEdge[]): Edge[] {
  return wfEdges.map((e) => ({
    id: e.id,
    source: e.sourceNodeId,
    target: e.targetNodeId,
    type: "smoothstep",
    animated: false,
    ...(e.isBackEdge
      ? {
          style: { strokeDasharray: "6 3", stroke: "#d97706" },
          label: e.maxIterations ? `max ${e.maxIterations}×` : "loop",
          labelStyle: { fill: "#d97706", fontSize: 10 },
        }
      : {}),
    data: { isBackEdge: e.isBackEdge || false, maxIterations: e.maxIterations },
  }));
}

export function flowToWorkflowNodes(nodes: Node[]): WorkflowNode[] {
  return nodes.map((n) => {
    if (n.type === "gate") {
      const gateData = n.data as Record<string, unknown>;
      return {
        nodeId: n.id,
        agentId: "",
        label: (gateData.label as string) || "Gate",
        positionX: n.position.x,
        positionY: n.position.y,
        nodeType: "gate",
        gateConfig: {
          gateType: ((gateData.gateType as string) || "Approval") as "Approval" | "ReviewAndEdit",
          instructions: (gateData.instructions as string) || null,
          sendBackTargetNodeId: null,
        },
      };
    }
    return {
      nodeId: n.id,
      agentId: (n.data as AgentNodeData).agentId,
      label: (n.data as AgentNodeData).label,
      positionX: n.position.x,
      positionY: n.position.y,
      configOverrides: null,
    };
  });
}

export function flowToWorkflowEdges(edges: Edge[]): WorkflowEdge[] {
  return edges.map((e) => ({
    id: e.id,
    sourceNodeId: e.source,
    targetNodeId: e.target,
    isBackEdge: (e.data as Record<string, unknown>)?.isBackEdge as boolean || false,
    maxIterations: (e.data as Record<string, unknown>)?.maxIterations as number | undefined,
  }));
}

/** DFS check: can we reach `from` starting at `to` using existing forward edges? If yes, adding from→to creates a cycle. */
export function wouldCreateCycle(edges: Edge[], from: string, to: string): boolean {
  const adj = new Map<string, string[]>();
  for (const e of edges) {
    if ((e.data as Record<string, unknown>)?.isBackEdge) continue;
    const list = adj.get(e.source) || [];
    list.push(e.target);
    adj.set(e.source, list);
  }
  const visited = new Set<string>();
  const stack = [to];
  while (stack.length > 0) {
    const current = stack.pop()!;
    if (current === from) return true;
    if (visited.has(current)) continue;
    visited.add(current);
    for (const next of adj.get(current) || []) {
      stack.push(next);
    }
  }
  return false;
}
