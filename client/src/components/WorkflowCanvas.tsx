import { useCallback, useRef, useState } from "react";
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
import AgentNode from "@/components/AgentNode";
import GateNode from "@/components/GateNode";
import type { AgentNodeData } from "@/components/AgentNode";
import { wouldCreateCycle } from "@/utils/workflowConverters";
import type { AgentDefinition } from "@/types";

const nodeTypes = { agent: AgentNode, gate: GateNode };

interface WorkflowCanvasProps {
  nodes: Node[];
  edges: Edge[];
  onNodesChange: OnNodesChange;
  onEdgesChange: OnEdgesChange;
  setNodes: React.Dispatch<React.SetStateAction<Node[]>>;
  setEdges: React.Dispatch<React.SetStateAction<Edge[]>>;
  executingNodeIds?: Set<string>;
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
  const [edgeContextMenu, setEdgeContextMenu] = useState<{
    x: number;
    y: number;
    edgeId: string;
  } | null>(null);

  const onConnect: OnConnect = useCallback(
    (params: Connection) => {
      // Detect if this edge would create a cycle (back-edge)
      const isBackEdge = wouldCreateCycle(edges, params.source, params.target);
      setEdges((eds) =>
        addEdge(
          {
            ...params,
            type: "smoothstep",
            animated: false,
            ...(isBackEdge
              ? {
                  style: { strokeDasharray: "6 3", stroke: "#d97706" },
                  label: "loop",
                  labelStyle: { fill: "#d97706", fontSize: 10 },
                }
              : {}),
            data: { isBackEdge, maxIterations: isBackEdge ? 3 : undefined },
          },
          eds
        )
      );
    },
    [setEdges, nodes, edges]
  );

  const onEdgeContextMenu = useCallback(
    (event: React.MouseEvent, edge: Edge) => {
      event.preventDefault();
      setEdgeContextMenu({ x: event.clientX, y: event.clientY, edgeId: edge.id });
    },
    []
  );

  const insertGateOnEdge = useCallback(
    (gateType: "Approval" | "ReviewAndEdit") => {
      if (!edgeContextMenu) return;
      const edge = edges.find((e) => e.id === edgeContextMenu.edgeId);
      if (!edge) return;

      const sourceNode = nodes.find((n) => n.id === edge.source);
      const targetNode = nodes.find((n) => n.id === edge.target);
      if (!sourceNode || !targetNode) return;

      const midX = (sourceNode.position.x + targetNode.position.x) / 2;
      const midY = (sourceNode.position.y + targetNode.position.y) / 2;

      const gateId = getNodeId();
      const gateNode: Node = {
        id: gateId,
        type: "gate",
        position: { x: midX, y: midY },
        data: {
          label: gateType === "ReviewAndEdit" ? "Review Gate" : "Approval Gate",
          gateType,
          instructions: "",
        },
      };

      setNodes((nds) => [...nds, gateNode]);
      setEdges((eds) => {
        const filtered = eds.filter((e) => e.id !== edge.id);
        return [
          ...filtered,
          {
            id: `${edge.source}-${gateId}`,
            source: edge.source,
            target: gateId,
            type: "smoothstep",
            animated: false,
          },
          {
            id: `${gateId}-${edge.target}`,
            source: gateId,
            target: edge.target,
            type: "smoothstep",
            animated: false,
          },
        ];
      });
      setEdgeContextMenu(null);
    },
    [edgeContextMenu, edges, nodes, setNodes, setEdges]
  );

  const onDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
  }, []);

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      const rfInstance = reactFlowRef.current;
      if (!rfInstance) return;

      const bounds = wrapperRef.current?.getBoundingClientRect();
      const position = rfInstance.screenToFlowPosition({
        x: e.clientX - (bounds?.left ?? 0),
        y: e.clientY - (bounds?.top ?? 0),
      });

      // Check for gate node drop
      const gateType = e.dataTransfer.getData("application/gate-node");
      if (gateType) {
        const newNode: Node = {
          id: getNodeId(),
          type: "gate",
          position,
          data: {
            label: gateType === "ReviewAndEdit" ? "Review Gate" : "Approval Gate",
            gateType,
            instructions: "",
          },
        };
        setNodes((nds) => [...nds, newNode]);
        return;
      }

      const raw = e.dataTransfer.getData("application/agentworkflow");
      if (!raw) return;

      const agent: AgentDefinition = JSON.parse(raw);

      const newNode: Node = {
        id: getNodeId(),
        type: "agent",
        position,
        data: {
          label: agent.name,
          icon: agent.icon || "🤖",
          description: agent.description,
          agentId: agent.id,
          agentType: agent.agentType,
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
    <div ref={wrapperRef} className="flex-1 h-full" onClick={() => setEdgeContextMenu(null)}>
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
        onEdgeContextMenu={onEdgeContextMenu}
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

      {/* Edge context menu for inserting gates */}
      {edgeContextMenu && (
        <div
          className="fixed z-50 bg-slate-700 border border-slate-600 rounded-lg shadow-xl py-1 animate-fade-in"
          style={{ left: edgeContextMenu.x, top: edgeContextMenu.y }}
        >
          <button
            onClick={() => insertGateOnEdge("Approval")}
            className="w-full text-left px-4 py-2 text-sm text-slate-200 hover:bg-slate-600 transition-colors cursor-pointer"
          >
            Insert Approval Gate
          </button>
          <button
            onClick={() => insertGateOnEdge("ReviewAndEdit")}
            className="w-full text-left px-4 py-2 text-sm text-slate-200 hover:bg-slate-600 transition-colors cursor-pointer"
          >
            Insert Review Gate
          </button>
        </div>
      )}
    </div>
  );
}
