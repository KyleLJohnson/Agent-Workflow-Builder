import { memo } from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";

export interface AgentNodeData {
  label: string;
  icon: string;
  description: string;
  agentId: string;
  agentType?: string;
  isExecuting?: boolean;
  [key: string]: unknown;
}

function AgentNodeComponent({ data, selected }: NodeProps) {
  const { label, icon, description, isExecuting, agentType } =
    data as unknown as AgentNodeData;

  return (
    <div
      className={`
        relative min-w-[180px] max-w-[220px] rounded-xl px-4 py-3
        border-2 transition-all duration-200 shadow-lg
        ${
          selected
            ? "border-blue-500 shadow-blue-500/20"
            : "border-slate-600 hover:border-slate-500"
        }
        ${isExecuting ? "animate-pulse-border" : ""}
        bg-slate-800
      `}
    >
      <Handle
        type="target"
        position={Position.Left}
        className="!-left-[6px]"
      />

      <div className="flex items-start gap-3">
        <span className="text-2xl shrink-0 mt-0.5 select-none">{icon || "🤖"}</span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5">
            <div className="text-sm font-semibold text-slate-100 truncate">
              {label}
            </div>
            {agentType === "planner" && (
              <span className="shrink-0 text-[9px] font-bold uppercase px-1.5 py-0.5 rounded bg-purple-600/30 text-purple-300 border border-purple-500/30">
                planner
              </span>
            )}
          </div>
          {description && (
            <div className="text-xs text-slate-400 mt-0.5 line-clamp-2 leading-snug">
              {description}
            </div>
          )}
        </div>
      </div>

      <Handle
        type="source"
        position={Position.Right}
        className="!-right-[6px]"
      />
    </div>
  );
}

export const AgentNode = memo(AgentNodeComponent);
export default AgentNode;
