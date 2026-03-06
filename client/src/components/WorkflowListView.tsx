import { Calendar, Layers, Plus, ArrowRight } from "lucide-react";
import type { WorkflowDefinition } from "../types";

interface WorkflowListViewProps {
  workflows: WorkflowDefinition[];
  onSelect: (workflow: WorkflowDefinition) => void;
  onCreateNew: () => void;
}

export default function WorkflowListView({
  workflows,
  onSelect,
  onCreateNew,
}: WorkflowListViewProps) {
  return (
    <div className="flex-1 flex flex-col items-center justify-start bg-gray-900 p-8 overflow-y-auto">
      <div className="max-w-4xl w-full">
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="text-2xl font-bold text-white">Your Workflows</h1>
            <p className="text-gray-400 mt-1">
              Select a workflow to edit or create a new one
            </p>
          </div>
          <button
            onClick={onCreateNew}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
          >
            <Plus size={18} />
            New Workflow
          </button>
        </div>

        {workflows.length === 0 ? (
          <div className="text-center py-16 text-gray-500">
            <Layers size={48} className="mx-auto mb-4 opacity-50" />
            <p className="text-lg">No workflows yet</p>
            <p className="text-sm mt-1">Create your first workflow to get started</p>
          </div>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {workflows.map((wf) => (
              <button
                key={wf.id}
                onClick={() => onSelect(wf)}
                className="bg-gray-800 border border-gray-700 rounded-lg p-5 text-left hover:border-blue-500 hover:bg-gray-750 transition-colors group"
              >
                <h3 className="text-white font-semibold truncate group-hover:text-blue-400 transition-colors">
                  {wf.name}
                </h3>
                <div className="flex items-center gap-4 mt-3 text-xs text-gray-500">
                  <span className="flex items-center gap-1">
                    <Layers size={12} />
                    {wf.nodes?.length ?? 0} node{(wf.nodes?.length ?? 0) !== 1 ? "s" : ""}
                  </span>
                  <span className="flex items-center gap-1">
                    <Calendar size={12} />
                    {new Date(wf.updatedAt ?? wf.createdAt).toLocaleDateString()}
                  </span>
                </div>
                <div className="flex items-center gap-1 mt-3 text-xs text-blue-400 opacity-0 group-hover:opacity-100 transition-opacity">
                  Open <ArrowRight size={12} />
                </div>
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
