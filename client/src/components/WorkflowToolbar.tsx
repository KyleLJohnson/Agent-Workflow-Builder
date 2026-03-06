import { useState, useRef, useEffect } from "react";
import {
  Save,
  FolderOpen,
  FilePlus,
  Trash2,
  Play,
  ChevronDown,
  Check,
  Loader2,
  ShieldCheck,
  LogOut,
} from "lucide-react";
import type { WorkflowDefinition } from "@/types";

interface WorkflowToolbarProps {
  workflowName: string;
  onNameChange: (name: string) => void;
  onSave: () => void;
  onNew: () => void;
  onDelete: () => void;
  onLoad: (workflow: WorkflowDefinition) => void;
  onExecute: () => void;
  savedWorkflows: WorkflowDefinition[];
  currentWorkflowId: string | null;
  isSaving: boolean;
  isExecuting: boolean;
  hasUnsavedChanges: boolean;
  blobContainerName?: string | null;
  onBlobContainerNameChange?: (name: string) => void;
  runningExecutionCount?: number;
  autoApproveGates: boolean;
  onAutoApproveChange: (value: boolean) => void;
  userName?: string | null;
  onLogout?: () => void;
}

export default function WorkflowToolbar({
  workflowName,
  onNameChange,
  onSave,
  onNew,
  onDelete,
  onLoad,
  onExecute,
  savedWorkflows,
  currentWorkflowId,
  isSaving,
  isExecuting,
  hasUnsavedChanges,
  blobContainerName,
  onBlobContainerNameChange,
  runningExecutionCount,
  autoApproveGates,
  onAutoApproveChange,
  userName,
  onLogout,
}: WorkflowToolbarProps) {
  const [showLoadMenu, setShowLoadMenu] = useState(false);
  const loadMenuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (
        loadMenuRef.current &&
        !loadMenuRef.current.contains(e.target as HTMLElement)
      ) {
        setShowLoadMenu(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  return (
    <div className="h-14 shrink-0 bg-slate-800 border-b border-slate-700 flex items-center px-4 gap-3">
      {/* Workflow name input */}
      <input
        type="text"
        value={workflowName}
        onChange={(e) => onNameChange(e.target.value)}
        placeholder="Untitled Workflow"
        className="text-sm font-medium text-slate-200 bg-transparent border border-transparent
                   hover:border-slate-600 focus:border-blue-500 rounded-lg px-3 py-1.5
                   outline-none transition-colors w-56"
      />

      {/* Unsaved dot */}
      {hasUnsavedChanges && (
        <div className="w-2 h-2 rounded-full bg-amber-400 shrink-0" title="Unsaved changes" />
      )}

      {/* Gate node drag */}
      <div
        draggable
        onDragStart={(e) => {
          e.dataTransfer.setData("application/gate-node", "Approval");
          e.dataTransfer.effectAllowed = "move";
        }}
        className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium
                   text-slate-300 hover:text-slate-100 hover:bg-slate-700
                   rounded-lg transition-colors cursor-grab active:cursor-grabbing
                   border border-dashed border-slate-600"
        title="Drag to add an Approval Gate"
      >
        <ShieldCheck size={14} />
        Gate
      </div>

      {/* Blob container name */}
      {onBlobContainerNameChange && (
        <div className="flex items-center gap-1.5">
          {blobContainerName ? (
            <span className="text-[10px] text-blue-400" title="Watching blob container">
              📦
            </span>
          ) : null}
          <input
            type="text"
            value={blobContainerName || ""}
            onChange={(e) => onBlobContainerNameChange(e.target.value)}
            placeholder="Blob container"
            className="text-[11px] text-slate-300 bg-transparent border border-slate-600
                       hover:border-slate-500 focus:border-blue-500 rounded px-2 py-1
                       outline-none transition-colors w-28"
            title="Azure Blob Storage container name for plan ingestion"
          />
        </div>
      )}

      <div className="flex-1" />

      {/* New */}
      <button
        onClick={onNew}
        className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium
                   text-slate-300 hover:text-slate-100 hover:bg-slate-700
                   rounded-lg transition-colors cursor-pointer"
        title="New Workflow"
      >
        <FilePlus size={15} />
        New
      </button>

      {/* Load dropdown */}
      <div className="relative" ref={loadMenuRef}>
        <button
          onClick={() => setShowLoadMenu(!showLoadMenu)}
          className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium
                     text-slate-300 hover:text-slate-100 hover:bg-slate-700
                     rounded-lg transition-colors cursor-pointer"
          title="Load Workflow"
        >
          <FolderOpen size={15} />
          Load
          <ChevronDown size={12} />
        </button>
        {showLoadMenu && (
          <div
            className="absolute top-full right-0 mt-1 w-64 bg-slate-700 border border-slate-600
                        rounded-xl shadow-xl z-50 py-1 max-h-60 overflow-y-auto animate-fade-in"
          >
            {savedWorkflows.length === 0 ? (
              <div className="px-4 py-3 text-xs text-slate-400">
                No saved workflows
              </div>
            ) : (
              savedWorkflows.map((wf) => (
                <button
                  key={wf.id}
                  onClick={() => {
                    onLoad(wf);
                    setShowLoadMenu(false);
                  }}
                  className={`w-full text-left px-4 py-2 text-sm hover:bg-slate-600 
                             transition-colors flex items-center gap-2 cursor-pointer
                             ${wf.id === currentWorkflowId ? "text-blue-400" : "text-slate-200"}`}
                >
                  {wf.id === currentWorkflowId && <Check size={14} />}
                  <span className="truncate">{wf.name || "Untitled"}</span>
                </button>
              ))
            )}
          </div>
        )}
      </div>

      {/* Save */}
      <button
        onClick={onSave}
        disabled={isSaving}
        className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium
                   bg-slate-700 hover:bg-slate-600 text-slate-200 rounded-lg
                   transition-colors disabled:opacity-50 cursor-pointer"
        title="Save Workflow"
      >
        {isSaving ? (
          <Loader2 size={15} className="animate-spin" />
        ) : (
          <Save size={15} />
        )}
        Save
      </button>

      {/* Delete */}
      {currentWorkflowId && (
        <button
          onClick={onDelete}
          className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium
                     text-red-400 hover:bg-red-500/10 rounded-lg
                     transition-colors cursor-pointer"
          title="Delete Workflow"
        >
          <Trash2 size={15} />
        </button>
      )}

      {/* Separator */}
      <div className="w-px h-6 bg-slate-600 mx-1" />

      {/* Auto-approve toggle */}
      <label
        className="flex items-center gap-1.5 px-2 py-1.5 text-xs text-slate-300 cursor-pointer select-none"
        title="When enabled, gate nodes are automatically approved during execution"
      >
        <input
          type="checkbox"
          checked={autoApproveGates}
          onChange={(e) => onAutoApproveChange(e.target.checked)}
          className="accent-green-500 cursor-pointer"
        />
        <ShieldCheck size={14} />
        Auto-approve
      </label>

      {/* Execute */}
      <button
        onClick={onExecute}
        disabled={!currentWorkflowId}
        className="relative flex items-center gap-1.5 px-4 py-1.5 text-xs font-medium
                   bg-green-600 hover:bg-green-500 disabled:bg-green-800
                   disabled:opacity-50 text-white rounded-lg transition-colors cursor-pointer"
        title={
          runningExecutionCount
            ? `Start a new execution (${runningExecutionCount} currently running)`
            : "Execute Workflow"
        }
      >
        {isExecuting ? (
          <Loader2 size={15} className="animate-spin" />
        ) : (
          <Play size={15} />
        )}
        Execute
        {(runningExecutionCount ?? 0) > 0 && (
          <span className="absolute -top-1.5 -right-1.5 min-w-[18px] h-[18px] flex items-center justify-center
                           bg-blue-500 text-white text-[10px] font-bold rounded-full px-1">
            {runningExecutionCount}
          </span>
        )}
      </button>

      {/* User info + logout */}
      {userName && (
        <>
          <div className="w-px h-6 bg-slate-600 mx-1" />
          <div className="flex items-center gap-2">
            <span className="text-xs text-slate-400 truncate max-w-[120px]" title={userName}>
              {userName}
            </span>
            {onLogout && (
              <button
                onClick={onLogout}
                className="flex items-center gap-1 px-2 py-1 text-[11px] text-slate-400
                           hover:text-slate-200 hover:bg-slate-700 rounded transition-colors cursor-pointer"
                title="Sign out"
              >
                <LogOut size={13} />
              </button>
            )}
          </div>
        </>
      )}
    </div>
  );
}
