import { useState, useEffect } from "react";
import { X, Save, Server, Settings } from "lucide-react";
import type { AgentDefinition, CreateAgentRequest, McpServerConfig } from "../types";

interface AgentEditorProps {
  agent: AgentDefinition | null; // null = create mode
  onSave: (data: CreateAgentRequest) => void;
  onClose: () => void;
  error?: string | null;
  isSaving?: boolean;
  mcpServers?: McpServerConfig[];
  onOpenMcpSettings?: () => void;
}

const CATEGORIES = [
  "General",
  "Writing",
  "Analysis",
  "Code",
  "Data",
  "Research",
  "Creative",
  "Communication",
  "Other",
];

const EMPTY_FORM: CreateAgentRequest = {
  name: "",
  description: "",
  systemInstructions: "",
  category: "General",
  icon: "🤖",
  inputDescription: "",
  outputDescription: "",
  modelOverride: null,
  temperature: null,
  mcpServerIds: [],
};

export default function AgentEditor({
  agent,
  onSave,
  onClose,
  error,
  isSaving,
  mcpServers = [],
  onOpenMcpSettings,
}: AgentEditorProps) {
  const isBuiltIn = agent?.isBuiltIn ?? false;
  const isEditMode = agent !== null;

  const [form, setForm] = useState<CreateAgentRequest>(EMPTY_FORM);

  useEffect(() => {
    if (agent) {
      setForm({
        name: agent.name,
        description: agent.description,
        systemInstructions: agent.systemInstructions,
        category: agent.category,
        icon: agent.icon,
        inputDescription: agent.inputDescription,
        outputDescription: agent.outputDescription,
        modelOverride: agent.modelOverride ?? null,
        temperature: agent.temperature ?? null,
        mcpServerIds: agent.mcpServerIds ?? [],
      });
    } else {
      setForm(EMPTY_FORM);
    }
  }, [agent]);

  const set = <K extends keyof CreateAgentRequest>(
    key: K,
    value: CreateAgentRequest[K]
  ) => setForm((prev) => ({ ...prev, [key]: value }));

  const toggleMcpServer = (serverId: string) => {
    setForm((prev) => {
      const current = prev.mcpServerIds ?? [];
      const next = current.includes(serverId)
        ? current.filter((id) => id !== serverId)
        : [...current, serverId];
      return { ...prev, mcpServerIds: next };
    });
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSave(form);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 animate-fade-in">
      <div className="bg-slate-800 border border-slate-600 rounded-2xl shadow-2xl w-full max-w-lg max-h-[90vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-700">
          <h2 className="text-lg font-semibold text-slate-100">
            {isBuiltIn
              ? "View Agent"
              : isEditMode
              ? "Edit Agent"
              : "Create Agent"}
          </h2>
          <button
            onClick={onClose}
            className="p-1.5 rounded-lg hover:bg-slate-700 text-slate-400
                       hover:text-slate-200 transition-colors cursor-pointer"
          >
            <X size={18} />
          </button>
        </div>

        {/* Form */}
        <form
          onSubmit={handleSubmit}
          className="flex-1 overflow-y-auto px-6 py-4 space-y-4"
        >
          {/* Icon + Name row */}
          <div className="flex gap-3">
            <div className="shrink-0">
              <label className="block text-xs font-medium text-slate-400 mb-1">
                Icon
              </label>
              <input
                type="text"
                value={form.icon}
                onChange={(e) => set("icon", e.target.value)}
                readOnly={isBuiltIn}
                maxLength={4}
                className="w-14 h-10 text-center text-xl bg-slate-700 border border-slate-600
                           rounded-lg outline-none focus:border-blue-500 transition-colors"
              />
            </div>
            <div className="flex-1">
              <label className="block text-xs font-medium text-slate-400 mb-1">
                Name
              </label>
              <input
                type="text"
                value={form.name}
                onChange={(e) => set("name", e.target.value)}
                readOnly={isBuiltIn}
                required
                placeholder="My Custom Agent"
                className="w-full h-10 bg-slate-700 border border-slate-600 rounded-lg
                           text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                           transition-colors placeholder-slate-500"
              />
            </div>
          </div>

          {/* Category */}
          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">
              Category
            </label>
            <select
              value={form.category}
              onChange={(e) => set("category", e.target.value)}
              disabled={isBuiltIn}
              className="w-full h-10 bg-slate-700 border border-slate-600 rounded-lg
                         text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                         transition-colors"
            >
              {CATEGORIES.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </select>
          </div>

          {/* Description */}
          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">
              Description
            </label>
            <input
              type="text"
              value={form.description}
              onChange={(e) => set("description", e.target.value)}
              readOnly={isBuiltIn}
              placeholder="What does this agent do?"
              className="w-full h-10 bg-slate-700 border border-slate-600 rounded-lg
                         text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                         transition-colors placeholder-slate-500"
            />
          </div>

          {/* System Instructions */}
          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">
              System Instructions
            </label>
            <textarea
              value={form.systemInstructions}
              onChange={(e) => set("systemInstructions", e.target.value)}
              readOnly={isBuiltIn}
              rows={5}
              placeholder="You are a helpful agent that..."
              className="w-full bg-slate-700 border border-slate-600 rounded-lg
                         text-sm text-slate-200 px-3 py-2 outline-none focus:border-blue-500
                         transition-colors resize-y placeholder-slate-500"
            />
          </div>

          {/* Input / Output descriptions */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-400 mb-1">
                Input Description
              </label>
              <input
                type="text"
                value={form.inputDescription}
                onChange={(e) => set("inputDescription", e.target.value)}
                readOnly={isBuiltIn}
                placeholder="Accepts text..."
                className="w-full h-10 bg-slate-700 border border-slate-600 rounded-lg
                           text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                           transition-colors placeholder-slate-500"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-400 mb-1">
                Output Description
              </label>
              <input
                type="text"
                value={form.outputDescription}
                onChange={(e) => set("outputDescription", e.target.value)}
                readOnly={isBuiltIn}
                placeholder="Returns text..."
                className="w-full h-10 bg-slate-700 border border-slate-600 rounded-lg
                           text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                           transition-colors placeholder-slate-500"
              />
            </div>
          </div>

          {/* Model Override + Temperature */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-400 mb-1">
                Model Override{" "}
                <span className="text-slate-500">(optional)</span>
              </label>
              <input
                type="text"
                value={form.modelOverride ?? ""}
                onChange={(e) =>
                  set("modelOverride", e.target.value || null)
                }
                readOnly={isBuiltIn}
                placeholder="e.g. gpt-4o"
                className="w-full h-10 bg-slate-700 border border-slate-600 rounded-lg
                           text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                           transition-colors placeholder-slate-500"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-400 mb-1">
                Temperature{" "}
                <span className="text-slate-500">(optional)</span>
              </label>
              <input
                type="number"
                step="0.1"
                min="0"
                max="2"
                value={form.temperature ?? ""}
                onChange={(e) =>
                  set(
                    "temperature",
                    e.target.value ? parseFloat(e.target.value) : null
                  )
                }
                readOnly={isBuiltIn}
                placeholder="0.7"
                className="w-full h-10 bg-slate-700 border border-slate-600 rounded-lg
                           text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                           transition-colors placeholder-slate-500"
              />
            </div>
          </div>

          {/* MCP Servers */}
          <div>
            <div className="flex items-center justify-between mb-1">
              <label className="block text-xs font-medium text-slate-400">
                MCP Servers{" "}
                <span className="text-slate-500">(optional)</span>
              </label>
              {onOpenMcpSettings && (
                <button
                  type="button"
                  onClick={onOpenMcpSettings}
                  className="flex items-center gap-1 text-xs text-blue-400
                             hover:text-blue-300 transition-colors cursor-pointer"
                >
                  <Settings size={12} />
                  Manage
                </button>
              )}
            </div>
            {mcpServers.length === 0 ? (
              <div className="px-3 py-3 text-sm text-slate-500 bg-slate-700/50
                              border border-slate-600 rounded-lg text-center">
                No MCP servers configured.{" "}
                {onOpenMcpSettings && (
                  <button
                    type="button"
                    onClick={onOpenMcpSettings}
                    className="text-blue-400 hover:text-blue-300 underline cursor-pointer"
                  >
                    Configure MCP servers first
                  </button>
                )}
              </div>
            ) : (
              <div className="space-y-1.5 max-h-32 overflow-y-auto">
                {mcpServers
                  .filter((s) => s.enabled)
                  .map((server) => {
                    const isSelected = (form.mcpServerIds ?? []).includes(
                      server.id
                    );
                    return (
                      <label
                        key={server.id}
                        className={`flex items-center gap-2.5 p-2 rounded-lg border
                                    cursor-pointer transition-all ${
                                      isSelected
                                        ? "bg-blue-600/15 border-blue-500/40"
                                        : "bg-slate-700/50 border-slate-600 hover:border-slate-500"
                                    } ${isBuiltIn ? "opacity-60 pointer-events-none" : ""}`}
                      >
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={() => toggleMcpServer(server.id)}
                          disabled={isBuiltIn}
                          className="accent-blue-500 shrink-0"
                        />
                        <Server size={14} className="text-slate-400 shrink-0" />
                        <div className="min-w-0 flex-1">
                          <div className="text-sm text-slate-200 truncate">
                            {server.name}
                          </div>
                          {server.description && (
                            <div className="text-xs text-slate-500 truncate">
                              {server.description}
                            </div>
                          )}
                        </div>
                      </label>
                    );
                  })}
              </div>
            )}
          </div>
        </form>

        {/* Footer */}
        <div className="px-6 py-4 border-t border-slate-700 space-y-3">
          {error && (
            <div className="px-3 py-2 text-sm text-red-300 bg-red-900/40 border border-red-700 rounded-lg">
              {error}
            </div>
          )}
          <div className="flex items-center justify-end gap-3">
            <button
              type="button"
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-slate-300 rounded-lg
                         hover:bg-slate-700 transition-colors cursor-pointer"
            >
              {isBuiltIn ? "Close" : "Cancel"}
            </button>
            {!isBuiltIn && (
              <button
                type="submit"
                onClick={handleSubmit}
                disabled={isSaving}
                className="flex items-center gap-2 px-4 py-2 text-sm font-medium
                           bg-blue-600 hover:bg-blue-500 text-white rounded-lg
                           transition-colors cursor-pointer disabled:opacity-50
                           disabled:cursor-not-allowed"
              >
                <Save size={14} />
                {isSaving ? "Saving..." : isEditMode ? "Update" : "Create"}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
