import { memo } from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import { ShieldCheck } from "lucide-react";

interface GateNodeData {
  label: string;
  gateType?: string;
  instructions?: string;
  isExecuting?: boolean;
  [key: string]: unknown;
}

export default memo(function GateNode({ data, selected }: NodeProps) {
  const nodeData = data as GateNodeData;
  const gateType = nodeData.gateType || "Approval";
  const isReviewEdit = gateType === "ReviewAndEdit";
  const isExecuting = nodeData.isExecuting;

  return (
    <div
      className={`
        relative flex flex-col items-center gap-1 px-3 py-2 rounded-lg
        border-2 shadow-lg transition-all min-w-[120px]
        ${selected ? "border-blue-400 ring-2 ring-blue-400/30" : "border-amber-600/50"}
        ${isExecuting ? "animate-pulse ring-2 ring-amber-400/40" : ""}
        bg-slate-800/90 backdrop-blur
      `}
    >
      <Handle
        type="target"
        position={Position.Left}
        className="!w-2.5 !h-2.5 !bg-amber-400 !border-slate-700"
      />

      <ShieldCheck
        size={20}
        className={isReviewEdit ? "text-blue-400" : "text-amber-400"}
      />

      <span className="text-[10px] font-semibold text-slate-300 text-center leading-tight">
        {nodeData.label || (isReviewEdit ? "Review & Edit" : "Approval Gate")}
      </span>

      <span className="text-[8px] text-slate-500 uppercase tracking-wide">
        {isReviewEdit ? "Review & Edit" : "Approval"}
      </span>

      {nodeData.instructions && (
        <span className="text-[8px] text-slate-500 italic max-w-[120px] truncate">
          {nodeData.instructions}
        </span>
      )}

      <Handle
        type="source"
        position={Position.Right}
        className="!w-2.5 !h-2.5 !bg-amber-400 !border-slate-700"
      />
    </div>
  );
});
