import { useCallback, useRef } from "react";
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  addEdge,
  BackgroundVariant,
  type Node,
  type Edge,
  type Connection,
  type OnConnect,
  type ReactFlowInstance,
  type OnNodesChange,
  type OnEdgesChange,
} from "@xyflow/react";
import AgentNode from "./AgentNode";
import type { AgentNodeData } from "./AgentNode";
import type { AgentDefinition, WorkflowNode, WorkflowEdge } from "../types";

const nodeTypes = { agent: AgentNode };

interface WorkflowCanvasProps {
  nodes: Node[];
  edges: Edge[];
  onNodesChange: OnNodesChange;
  onEdgesChange: OnEdgesChange;
  setNodes: React.Dispatch<React.SetStateAction<Node[]>>;
  setEdges: React.Dispatch<React.SetStateAction<Edge[]>>;
  executingNodeIds?: Set<string>;
}

export function workflowNodesToFlow(
  wfNodes: WorkflowNode[],
  agents: AgentDefinition[]
): Node[] {
  return wfNodes.map((n) => {
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
  }));
}

export function flowToWorkflowNodes(nodes: Node[]): WorkflowNode[] {
  return nodes.map((n) => ({
    nodeId: n.id,
    agentId: (n.data as AgentNodeData).agentId,
    label: (n.data as AgentNodeData).label,
    positionX: n.position.x,
    positionY: n.position.y,
    configOverrides: null,
  }));
}

export function flowToWorkflowEdges(edges: Edge[]): WorkflowEdge[] {
  return edges.map((e) => ({
    id: e.id,
    sourceNodeId: e.source,
    targetNodeId: e.target,
  }));
}

let nodeIdCounter = 0;
function getNodeId() {
  return `node_${Date.now()}_${nodeIdCounter++}`;
}

export default function WorkflowCanvas({
  nodes,
  edges,
  onNodesChange,
  onEdgesChange,
  setNodes,
  setEdges,
  executingNodeIds,
}: WorkflowCanvasProps) {
  const reactFlowRef = useRef<ReactFlowInstance | null>(null);
  const wrapperRef = useRef<HTMLDivElement>(null);

  const onConnect: OnConnect = useCallback(
    (params: Connection) => {
      setEdges((eds) =>
        addEdge(
          { ...params, type: "smoothstep", animated: false },
          eds
        )
      );
    },
    [setEdges]
  );

  const onDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
  }, []);

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      const raw = e.dataTransfer.getData("application/agentworkflow");
      if (!raw) return;

      const agent: AgentDefinition = JSON.parse(raw);
      const rfInstance = reactFlowRef.current;
      if (!rfInstance) return;

      const bounds = wrapperRef.current?.getBoundingClientRect();
      const position = rfInstance.screenToFlowPosition({
        x: e.clientX - (bounds?.left ?? 0),
        y: e.clientY - (bounds?.top ?? 0),
      });

      const newNode: Node = {
        id: getNodeId(),
        type: "agent",
        position,
        data: {
          label: agent.name,
          icon: agent.icon || "🤖",
          description: agent.description,
          agentId: agent.id,
        } satisfies AgentNodeData,
      };

      setNodes((nds) => [...nds, newNode]);
    },
    [setNodes]
  );

  // Update executing state on nodes
  const displayNodes = executingNodeIds
    ? nodes.map((n) => ({
        ...n,
        data: {
          ...n.data,
          isExecuting: executingNodeIds.has(n.id),
        },
      }))
    : nodes;

  return (
    <div ref={wrapperRef} className="flex-1 h-full">
      <ReactFlow
        nodes={displayNodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onDragOver={onDragOver}
        onDrop={onDrop}
        onInit={(instance) => {
          reactFlowRef.current = instance;
        }}
        nodeTypes={nodeTypes}
        fitView
        snapToGrid
        snapGrid={[16, 16]}
        deleteKeyCode={["Backspace", "Delete"]}
        defaultEdgeOptions={{ type: "smoothstep" }}
        proOptions={{ hideAttribution: true }}
      >
        <Background
          variant={BackgroundVariant.Dots}
          gap={20}
          size={1}
          color="#334155"
        />
        <Controls />
        <MiniMap
          nodeStrokeWidth={3}
          pannable
          zoomable
          style={{ height: 100, width: 140 }}
        />
      </ReactFlow>
    </div>
  );
}
