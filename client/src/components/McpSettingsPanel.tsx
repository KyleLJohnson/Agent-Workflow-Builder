import { useState, useEffect } from "react";
import { X, Plus, Trash2, Save, Server, ToggleLeft, ToggleRight } from "lucide-react";
import type { McpServerConfig, CreateMcpServerRequest } from "../types";
import * as api from "../api";

interface McpSettingsPanelProps {
  onClose: () => void;
  onServersChanged?: () => void;
}

const EMPTY_SERVER: CreateMcpServerRequest = {
  name: "",
  description: "",
  transportType: "stdio",
  command: "",
  args: [],
  env: {},
  url: null,
  enabled: true,
};

export default function McpSettingsPanel({
  onClose,
  onServersChanged,
}: McpSettingsPanelProps) {
  const [servers, setServers] = useState<McpServerConfig[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Inline editing
  const [editingId, setEditingId] = useState<string | null>(null); // null = not editing, "new" = adding
  const [form, setForm] = useState<CreateMcpServerRequest>(EMPTY_SERVER);
  const [argsText, setArgsText] = useState("");
  const [envText, setEnvText] = useState("");
  const [saving, setSaving] = useState(false);

  const loadServers = async () => {
    setLoading(true);
    try {
      const data = await api.getMcpServers();
      setServers(data);
    } catch (err) {
      console.error("Failed to load MCP servers:", err);
      setError("Failed to load MCP servers");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadServers();
  }, []);

  const startAdd = () => {
    setEditingId("new");
    setForm(EMPTY_SERVER);
    setArgsText("");
    setEnvText("");
    setError(null);
  };

  const startEdit = (server: McpServerConfig) => {
    setEditingId(server.id);
    setForm({
      name: server.name,
      description: server.description,
      transportType: server.transportType,
      command: server.command ?? "",
      args: server.args ?? [],
      env: server.env ?? {},
      url: server.url ?? null,
      enabled: server.enabled,
    });
    setArgsText((server.args ?? []).join("\n"));
    setEnvText(
      Object.entries(server.env ?? {})
        .map(([k, v]) => `${k}=${v}`)
        .join("\n")
    );
    setError(null);
  };

  const cancelEdit = () => {
    setEditingId(null);
    setError(null);
  };

  const parseArgs = (text: string): string[] =>
    text
      .split("\n")
      .map((s) => s.trim())
      .filter(Boolean);

  const parseEnv = (text: string): Record<string, string> => {
    const result: Record<string, string> = {};
    for (const line of text.split("\n")) {
      const trimmed = line.trim();
      if (!trimmed) continue;
      const eqIdx = trimmed.indexOf("=");
      if (eqIdx > 0) {
        result[trimmed.slice(0, eqIdx).trim()] = trimmed.slice(eqIdx + 1).trim();
      }
    }
    return result;
  };

  const handleSave = async () => {
    if (!form.name.trim()) {
      setError("Name is required");
      return;
    }
    if (form.transportType === "stdio" && !form.command?.trim()) {
      setError("Command is required for stdio transport");
      return;
    }
    if (
      (form.transportType === "sse" || form.transportType === "http") &&
      !form.url?.trim()
    ) {
      setError("URL is required for SSE/HTTP transport");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      const payload: CreateMcpServerRequest = {
        ...form,
        args: parseArgs(argsText),
        env: parseEnv(envText),
      };

      if (editingId === "new") {
        await api.createMcpServer(payload);
      } else if (editingId) {
        await api.updateMcpServer(editingId, payload);
      }

      await loadServers();
      setEditingId(null);
      onServersChanged?.();
    } catch (err) {
      const message =
        err instanceof Error ? err.message : "Failed to save server";
      setError(message);
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm("Delete this MCP server?")) return;
    try {
      await api.deleteMcpServer(id);
      await loadServers();
      if (editingId === id) setEditingId(null);
      onServersChanged?.();
    } catch (err) {
      console.error("Failed to delete MCP server:", err);
    }
  };

  const handleToggle = async (server: McpServerConfig) => {
    try {
      await api.updateMcpServer(server.id, { enabled: !server.enabled });
      await loadServers();
      onServersChanged?.();
    } catch (err) {
      console.error("Failed to toggle MCP server:", err);
    }
  };

  const set = <K extends keyof CreateMcpServerRequest>(
    key: K,
    value: CreateMcpServerRequest[K]
  ) => setForm((prev) => ({ ...prev, [key]: value }));

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 animate-fade-in">
      <div className="bg-slate-800 border border-slate-600 rounded-2xl shadow-2xl w-full max-w-2xl max-h-[85vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-700">
          <div className="flex items-center gap-2">
            <Server size={18} className="text-blue-400" />
            <h2 className="text-lg font-semibold text-slate-100">
              MCP Servers
            </h2>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={startAdd}
              disabled={editingId !== null}
              className="flex items-center gap-1.5 text-xs font-medium px-2.5 py-1.5
                         rounded-lg bg-blue-600 hover:bg-blue-500 text-white
                         transition-colors cursor-pointer disabled:opacity-50
                         disabled:cursor-not-allowed"
            >
              <Plus size={14} />
              Add Server
            </button>
            <button
              onClick={onClose}
              className="p-1.5 rounded-lg hover:bg-slate-700 text-slate-400
                         hover:text-slate-200 transition-colors cursor-pointer"
            >
              <X size={18} />
            </button>
          </div>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-3">
          {loading && (
            <div className="text-center text-sm text-slate-500 py-8">
              Loading...
            </div>
          )}

          {!loading && servers.length === 0 && editingId !== "new" && (
            <div className="text-center text-sm text-slate-500 py-8">
              No MCP servers configured.{" "}
              <button
                onClick={startAdd}
                className="text-blue-400 hover:text-blue-300 underline cursor-pointer"
              >
                Add one now
              </button>
            </div>
          )}

          {/* Server list */}
          {servers.map((server) =>
            editingId === server.id ? (
              <ServerForm
                key={server.id}
                form={form}
                argsText={argsText}
                envText={envText}
                set={set}
                setArgsText={setArgsText}
                setEnvText={setEnvText}
                onSave={handleSave}
                onCancel={cancelEdit}
                saving={saving}
                error={error}
                isNew={false}
              />
            ) : (
              <ServerCard
                key={server.id}
                server={server}
                onEdit={() => startEdit(server)}
                onDelete={() => handleDelete(server.id)}
                onToggle={() => handleToggle(server)}
                disabled={editingId !== null}
              />
            )
          )}

          {/* New server form */}
          {editingId === "new" && (
            <ServerForm
              form={form}
              argsText={argsText}
              envText={envText}
              set={set}
              setArgsText={setArgsText}
              setEnvText={setEnvText}
              onSave={handleSave}
              onCancel={cancelEdit}
              saving={saving}
              error={error}
              isNew={true}
            />
          )}
        </div>
      </div>
    </div>
  );
}

// ─── Sub-components ────────────────────────────────────────

function ServerCard({
  server,
  onEdit,
  onDelete,
  onToggle,
  disabled,
}: {
  server: McpServerConfig;
  onEdit: () => void;
  onDelete: () => void;
  onToggle: () => void;
  disabled: boolean;
}) {
  return (
    <div
      className={`flex items-center gap-3 p-3 rounded-xl border transition-all ${
        server.enabled
          ? "bg-slate-700/50 border-slate-600"
          : "bg-slate-800/50 border-slate-700 opacity-60"
      }`}
    >
      <Server size={16} className="text-slate-400 shrink-0" />
      <div className="flex-1 min-w-0">
        <div className="text-sm font-medium text-slate-200 truncate">
          {server.name}
        </div>
        <div className="text-xs text-slate-500 mt-0.5">
          {server.transportType === "stdio"
            ? `${server.command} ${(server.args ?? []).join(" ")}`
            : server.url}
        </div>
      </div>
      <div className="flex items-center gap-1.5 shrink-0">
        <button
          onClick={onToggle}
          disabled={disabled}
          title={server.enabled ? "Disable" : "Enable"}
          className="p-1.5 rounded-lg hover:bg-slate-600 text-slate-400
                     hover:text-slate-200 transition-colors cursor-pointer
                     disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {server.enabled ? (
            <ToggleRight size={18} className="text-green-400" />
          ) : (
            <ToggleLeft size={18} />
          )}
        </button>
        <button
          onClick={onEdit}
          disabled={disabled}
          className="px-2 py-1 text-xs font-medium rounded-lg hover:bg-slate-600
                     text-slate-400 hover:text-slate-200 transition-colors cursor-pointer
                     disabled:opacity-50 disabled:cursor-not-allowed"
        >
          Edit
        </button>
        <button
          onClick={onDelete}
          disabled={disabled}
          className="p-1.5 rounded-lg hover:bg-red-900/40 text-slate-400
                     hover:text-red-400 transition-colors cursor-pointer
                     disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Trash2 size={14} />
        </button>
      </div>
    </div>
  );
}

function ServerForm({
  form,
  argsText,
  envText,
  set,
  setArgsText,
  setEnvText,
  onSave,
  onCancel,
  saving,
  error,
  isNew,
}: {
  form: CreateMcpServerRequest;
  argsText: string;
  envText: string;
  set: <K extends keyof CreateMcpServerRequest>(
    key: K,
    value: CreateMcpServerRequest[K]
  ) => void;
  setArgsText: (v: string) => void;
  setEnvText: (v: string) => void;
  onSave: () => void;
  onCancel: () => void;
  saving: boolean;
  error: string | null;
  isNew: boolean;
}) {
  return (
    <div className="p-4 rounded-xl border border-blue-500/40 bg-slate-700/40 space-y-3">
      <div className="text-xs font-semibold text-blue-400 uppercase tracking-wider">
        {isNew ? "New Server" : "Edit Server"}
      </div>

      {/* Name + Transport */}
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-medium text-slate-400 mb-1">
            Name
          </label>
          <input
            type="text"
            value={form.name}
            onChange={(e) => set("name", e.target.value)}
            placeholder="My MCP Server"
            className="w-full h-9 bg-slate-700 border border-slate-600 rounded-lg
                       text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                       transition-colors placeholder-slate-500"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-slate-400 mb-1">
            Transport
          </label>
          <select
            value={form.transportType}
            onChange={(e) => set("transportType", e.target.value)}
            className="w-full h-9 bg-slate-700 border border-slate-600 rounded-lg
                       text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                       transition-colors"
          >
            <option value="stdio">stdio</option>
            <option value="sse">SSE</option>
            <option value="http">HTTP</option>
          </select>
        </div>
      </div>

      {/* Description */}
      <div>
        <label className="block text-xs font-medium text-slate-400 mb-1">
          Description <span className="text-slate-500">(optional)</span>
        </label>
        <input
          type="text"
          value={form.description}
          onChange={(e) => set("description", e.target.value)}
          placeholder="What does this server provide?"
          className="w-full h-9 bg-slate-700 border border-slate-600 rounded-lg
                     text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                     transition-colors placeholder-slate-500"
        />
      </div>

      {/* stdio fields */}
      {form.transportType === "stdio" && (
        <>
          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">
              Command
            </label>
            <input
              type="text"
              value={form.command ?? ""}
              onChange={(e) => set("command", e.target.value)}
              placeholder="e.g. npx, python, node"
              className="w-full h-9 bg-slate-700 border border-slate-600 rounded-lg
                         text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                         transition-colors placeholder-slate-500"
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">
              Arguments <span className="text-slate-500">(one per line)</span>
            </label>
            <textarea
              value={argsText}
              onChange={(e) => setArgsText(e.target.value)}
              rows={2}
              placeholder={"-y\n@modelcontextprotocol/server-filesystem\n/path/to/dir"}
              className="w-full bg-slate-700 border border-slate-600 rounded-lg
                         text-sm text-slate-200 px-3 py-2 outline-none focus:border-blue-500
                         transition-colors resize-y placeholder-slate-500 font-mono"
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-slate-400 mb-1">
              Environment Variables{" "}
              <span className="text-slate-500">(KEY=VALUE, one per line)</span>
            </label>
            <textarea
              value={envText}
              onChange={(e) => setEnvText(e.target.value)}
              rows={2}
              placeholder={"API_KEY=abc123"}
              className="w-full bg-slate-700 border border-slate-600 rounded-lg
                         text-sm text-slate-200 px-3 py-2 outline-none focus:border-blue-500
                         transition-colors resize-y placeholder-slate-500 font-mono"
            />
          </div>
        </>
      )}

      {/* SSE / HTTP fields */}
      {(form.transportType === "sse" || form.transportType === "http") && (
        <div>
          <label className="block text-xs font-medium text-slate-400 mb-1">
            URL
          </label>
          <input
            type="url"
            value={form.url ?? ""}
            onChange={(e) => set("url", e.target.value)}
            placeholder="http://localhost:3000/mcp"
            className="w-full h-9 bg-slate-700 border border-slate-600 rounded-lg
                       text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                       transition-colors placeholder-slate-500"
          />
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="px-3 py-2 text-sm text-red-300 bg-red-900/40 border border-red-700 rounded-lg">
          {error}
        </div>
      )}

      {/* Actions */}
      <div className="flex items-center justify-end gap-2 pt-1">
        <button
          onClick={onCancel}
          className="px-3 py-1.5 text-xs font-medium text-slate-300 rounded-lg
                     hover:bg-slate-600 transition-colors cursor-pointer"
        >
          Cancel
        </button>
        <button
          onClick={onSave}
          disabled={saving}
          className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium
                     bg-blue-600 hover:bg-blue-500 text-white rounded-lg
                     transition-colors cursor-pointer disabled:opacity-50
                     disabled:cursor-not-allowed"
        >
          <Save size={12} />
          {saving ? "Saving..." : isNew ? "Add" : "Update"}
        </button>
      </div>
    </div>
  );
}
