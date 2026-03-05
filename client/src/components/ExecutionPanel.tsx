import { useState, useRef, useEffect, useMemo } from "react";
import {
  ChevronDown,
  ChevronRight,
  ChevronUp,
  Send,
  Loader2,
  CheckCircle2,
  AlertCircle,
  Play,
  Zap,
  Trash2,
  FileText,
} from "lucide-react";
import type { ExecutionEvent } from "../types";

interface ExecutionPanelProps {
  events: ExecutionEvent[];
  isConnected: boolean;
  isExecuting: boolean;
  onExecute: (input: string) => void;
  onClear: () => void;
  workflowId: string | null;
}

function EventIcon({ type }: { type: ExecutionEvent["type"] }) {
  switch (type) {
    case "ExecutionStarted":
      return <Play size={14} className="text-blue-400" />;
    case "AgentStepStarted":
      return <Loader2 size={14} className="text-amber-400 animate-spin" />;
    case "AgentStepCompleted":
      return <CheckCircle2 size={14} className="text-green-400" />;
    case "WorkflowOutput":
      return <Zap size={14} className="text-purple-400" />;
    case "ExecutionCompleted":
      return <CheckCircle2 size={14} className="text-green-500" />;
    case "Error":
      return <AlertCircle size={14} className="text-red-400" />;
    default:
      return null;
  }
}

function EventLabel({ type }: { type: ExecutionEvent["type"] }) {
  switch (type) {
    case "ExecutionStarted":
      return <span className="text-blue-400 font-medium">Execution Started</span>;
    case "AgentStepStarted":
      return <span className="text-amber-400 font-medium">Agent Step Started</span>;
    case "AgentStepCompleted":
      return <span className="text-green-400 font-medium">Agent Step Completed</span>;
    case "WorkflowOutput":
      return <span className="text-purple-400 font-medium">Output</span>;
    case "ExecutionCompleted":
      return <span className="text-green-500 font-medium">Execution Completed</span>;
    case "Error":
      return <span className="text-red-400 font-medium">Error</span>;
    default:
      return null;
  }
}

export default function ExecutionPanel({
  events,
  isConnected,
  isExecuting,
  onExecute,
  onClear,
  workflowId,
}: ExecutionPanelProps) {
  const [collapsed, setCollapsed] = useState(false);
  const [inputMessage, setInputMessage] = useState("");
  const [collapsedAgents, setCollapsedAgents] = useState<Set<string>>(new Set());
  const logScrollRef = useRef<HTMLDivElement>(null);
  const outputScrollRef = useRef<HTMLDivElement>(null);

  // Separate events into execution log (non-output) and output events
  const logEvents = useMemo(
    () => events.filter((e) => e.type !== "WorkflowOutput"),
    [events]
  );
  const outputEvents = useMemo(
    () =>
      events.filter(
        (e) => e.type === "WorkflowOutput" && e.output && e.output.trim() !== ""
      ),
    [events]
  );

  // Group output events by agent name, preserving first-seen order
  const groupedOutputs = useMemo(() => {
    const groups: { agentName: string; events: ExecutionEvent[] }[] = [];
    const indexMap = new Map<string, number>();
    for (const evt of outputEvents) {
      const name = evt.agentName || "Unknown Agent";
      if (indexMap.has(name)) {
        groups[indexMap.get(name)!].events.push(evt);
      } else {
        indexMap.set(name, groups.length);
        groups.push({ agentName: name, events: [evt] });
      }
    }
    return groups;
  }, [outputEvents]);

  const toggleAgentCollapsed = (agentName: string) => {
    setCollapsedAgents((prev) => {
      const next = new Set(prev);
      if (next.has(agentName)) next.delete(agentName);
      else next.add(agentName);
      return next;
    });
  };

  // Auto-scroll both panes on new events
  useEffect(() => {
    if (logScrollRef.current) {
      logScrollRef.current.scrollTop = logScrollRef.current.scrollHeight;
    }
  }, [logEvents]);

  useEffect(() => {
    if (outputScrollRef.current) {
      outputScrollRef.current.scrollTop = outputScrollRef.current.scrollHeight;
    }
  }, [outputEvents]);

  // Expand on new execution
  useEffect(() => {
    if (isExecuting) setCollapsed(false);
  }, [isExecuting]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputMessage.trim() || !workflowId) return;
    onExecute(inputMessage.trim());
    setInputMessage("");
  };

  return (
    <div
      className={`
        shrink-0 bg-slate-900 border-t border-slate-700 flex flex-col transition-all duration-300
        ${collapsed ? "h-10" : "h-80"}
      `}
    >
      {/* Header */}
      <button
        onClick={() => setCollapsed(!collapsed)}
        className="h-10 shrink-0 flex items-center justify-between px-4
                   hover:bg-slate-800 transition-colors cursor-pointer"
      >
        <div className="flex items-center gap-2">
          <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">
            Execution
          </span>
          <div
            className={`w-2 h-2 rounded-full ${
              isConnected ? "bg-green-500" : "bg-red-500"
            }`}
            title={isConnected ? "SignalR connected" : "SignalR disconnected"}
          />
          {events.length > 0 && (
            <span className="text-xs text-slate-500">
              {events.length} event{events.length !== 1 ? "s" : ""}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          {events.length > 0 && (
            <span
              onClick={(e) => {
                e.stopPropagation();
                onClear();
              }}
              className="text-xs text-slate-500 hover:text-slate-300 cursor-pointer px-1"
            >
              <Trash2 size={13} />
            </span>
          )}
          {collapsed ? (
            <ChevronUp size={16} className="text-slate-400" />
          ) : (
            <ChevronDown size={16} className="text-slate-400" />
          )}
        </div>
      </button>

      {!collapsed && (
        <>
          {/* Two-column layout: Execution Log | Agent Output */}
          <div className="flex-1 flex min-h-0">
            {/* Left pane — Execution log */}
            <div className="flex-1 flex flex-col min-w-0 border-r border-slate-700">
              <div className="shrink-0 px-3 py-1.5 border-b border-slate-700/50 flex items-center gap-1.5">
                <Play size={12} className="text-slate-500" />
                <span className="text-[10px] font-semibold text-slate-500 uppercase tracking-wider">
                  Execution Log
                </span>
              </div>
              <div
                ref={logScrollRef}
                className="flex-1 overflow-y-auto px-3 py-2 space-y-1.5"
                data-testid="execution-log"
              >
                {logEvents.length === 0 ? (
                  <div className="flex items-center justify-center h-full text-xs text-slate-500">
                    Execute a workflow to see events here
                  </div>
                ) : (
                  logEvents.map((evt, i) => (
                    <div
                      key={i}
                      className="flex items-start gap-2 text-xs animate-fade-in"
                    >
                      <span className="shrink-0 mt-0.5">
                        <EventIcon type={evt.type} />
                      </span>
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2">
                          <EventLabel type={evt.type} />
                          {evt.agentName && (
                            <span className="text-slate-500">
                              — {evt.agentName}
                            </span>
                          )}
                          <span className="text-slate-600 ml-auto whitespace-nowrap">
                            {new Date(evt.timestamp).toLocaleTimeString()}
                          </span>
                        </div>
                        {evt.message && (
                          <p className="text-slate-300 mt-0.5 whitespace-pre-wrap break-words">
                            {evt.message}
                          </p>
                        )}
                        {evt.error && (
                          <p className="text-red-400 mt-0.5 whitespace-pre-wrap break-words">
                            {evt.error}
                          </p>
                        )}
                      </div>
                    </div>
                  ))
                )}
              </div>
            </div>

            {/* Right pane — Agent Output */}
            <div className="flex-1 flex flex-col min-w-0">
              <div className="shrink-0 px-3 py-1.5 border-b border-slate-700/50 flex items-center gap-1.5">
                <FileText size={12} className="text-slate-500" />
                <span className="text-[10px] font-semibold text-slate-500 uppercase tracking-wider">
                  Agent Output
                </span>
              </div>
              <div
                ref={outputScrollRef}
                className="flex-1 overflow-y-auto px-3 py-2 space-y-2"
                data-testid="agent-output"
              >
                {groupedOutputs.length === 0 ? (
                  <div className="flex items-center justify-center h-full text-xs text-slate-500">
                    {isExecuting
                      ? "Waiting for agent output…"
                      : "Agent responses will appear here"}
                  </div>
                ) : (
                  groupedOutputs.map((group) => {
                    const isOpen = !collapsedAgents.has(group.agentName);
                    return (
                      <div
                        key={group.agentName}
                        className="rounded border border-slate-700/60 overflow-hidden animate-fade-in"
                        data-testid={`agent-section-${group.agentName}`}
                      >
                        {/* Collapsible header */}
                        <button
                          onClick={() => toggleAgentCollapsed(group.agentName)}
                          className="w-full flex items-center gap-1.5 px-2.5 py-1.5
                                     bg-slate-800/70 hover:bg-slate-800 transition-colors cursor-pointer"
                        >
                          {isOpen ? (
                            <ChevronDown size={12} className="text-slate-400 shrink-0" />
                          ) : (
                            <ChevronRight size={12} className="text-slate-400 shrink-0" />
                          )}
                          <Zap size={12} className="text-purple-400 shrink-0" />
                          <span className="text-[11px] font-medium text-purple-300">
                            {group.agentName}
                          </span>
                          <span className="text-slate-600 text-[10px] ml-auto">
                            {group.events.length} output{group.events.length !== 1 ? "s" : ""}
                          </span>
                        </button>
                        {/* Collapsible body */}
                        {isOpen && (
                          <div className="px-2.5 py-2 space-y-2">
                            {group.events.map((evt, j) => (
                              <div key={j}>
                                <span className="text-slate-600 text-[10px]">
                                  {new Date(evt.timestamp).toLocaleTimeString()}
                                </span>
                                <p className="text-slate-200 text-xs whitespace-pre-wrap break-words bg-slate-800/50 rounded p-2 mt-0.5">
                                  {evt.output}
                                </p>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    );
                  })
                )}
              </div>
            </div>
          </div>

          {/* Input */}
          <form
            onSubmit={handleSubmit}
            className="shrink-0 flex items-center gap-2 px-4 py-3 border-t border-slate-700"
          >
            <input
              type="text"
              value={inputMessage}
              onChange={(e) => setInputMessage(e.target.value)}
              placeholder={
                workflowId
                  ? "Type a message to send to the workflow..."
                  : "Save the workflow first to execute it"
              }
              disabled={!workflowId || isExecuting}
              className="flex-1 h-9 bg-slate-800 border border-slate-600 rounded-lg
                         text-sm text-slate-200 px-3 outline-none focus:border-blue-500
                         transition-colors placeholder-slate-500 disabled:opacity-50"
            />
            <button
              type="submit"
              disabled={!workflowId || isExecuting || !inputMessage.trim()}
              className="h-9 px-4 bg-blue-600 hover:bg-blue-500 disabled:bg-blue-800
                         disabled:opacity-50 text-white rounded-lg transition-colors
                         flex items-center gap-1.5 text-xs font-medium cursor-pointer"
            >
              {isExecuting ? (
                <Loader2 size={14} className="animate-spin" />
              ) : (
                <Send size={14} />
              )}
              Send
            </button>
          </form>
        </>
      )}
    </div>
  );
}
