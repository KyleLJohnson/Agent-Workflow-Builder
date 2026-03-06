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
  MessageCircle,
  ShieldCheck,
  RefreshCw,
  Map as MapIcon,
  X,
  Square,
} from "lucide-react";
import type { ExecutionEvent, ExecutionState } from "../types";

interface ExecutionPanelProps {
  events: ExecutionEvent[];
  isConnected: boolean;
  isExecuting: boolean;
  onExecute: (input: string) => void;
  onClear: () => void;
  workflowId: string | null;
  onAnswerClarification?: (executionId: string, answer: string) => void;
  onApproveGate?: (executionId: string, editedOutput?: string) => void;
  onRejectGate?: (executionId: string, reason?: string) => void;
  onSendBackGate?: (executionId: string, feedback?: string) => void;
  onCancelExecution?: (executionId: string) => void;
  executions?: ExecutionState[];
  activeExecutionId?: string;
  onSelectExecution?: (executionId: string) => void;
  onCloseExecution?: (executionId: string) => void;
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
    case "ClarificationNeeded":
      return <MessageCircle size={14} className="text-amber-400" />;
    case "GateAwaitingApproval":
      return <ShieldCheck size={14} className="text-amber-400 animate-pulse" />;
    case "GateApproved":
      return <ShieldCheck size={14} className="text-green-400" />;
    case "GateRejected":
      return <ShieldCheck size={14} className="text-red-400" />;
    case "LoopIterationStarted":
      return <RefreshCw size={14} className="text-blue-400" />;
    case "LoopIterationCompleted":
      return <RefreshCw size={14} className="text-green-400" />;
    case "PlanGenerated":
      return <MapIcon size={14} className="text-purple-400" />;
    case "PlanTriggered":
      return <MapIcon size={14} className="text-blue-400" />;
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
    case "ClarificationNeeded":
      return <span className="text-amber-400 font-medium">Clarification Needed</span>;
    case "GateAwaitingApproval":
      return <span className="text-amber-400 font-medium">Gate Awaiting Approval</span>;
    case "GateApproved":
      return <span className="text-green-400 font-medium">Gate Approved</span>;
    case "GateRejected":
      return <span className="text-red-400 font-medium">Gate Rejected</span>;
    case "LoopIterationStarted":
      return <span className="text-blue-400 font-medium">Loop Iteration</span>;
    case "LoopIterationCompleted":
      return <span className="text-green-400 font-medium">Loop Iteration Done</span>;
    case "PlanGenerated":
      return <span className="text-purple-400 font-medium">Plan Generated</span>;
    case "PlanTriggered":
      return <span className="text-blue-400 font-medium">Plan Triggered</span>;
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
  onAnswerClarification,
  onApproveGate,
  onRejectGate,
  onSendBackGate,
  onCancelExecution,
  executions,
  activeExecutionId,
  onSelectExecution,
  onCloseExecution,
}: ExecutionPanelProps) {
  const [collapsed, setCollapsed] = useState(false);
  const [inputMessage, setInputMessage] = useState("");
  const [collapsedAgents, setCollapsedAgents] = useState<Set<string>>(new Set());
  const [clarificationAnswers, setClarificationAnswers] = useState<Record<string, string>>({});
  const [submittedClarifications, setSubmittedClarifications] = useState<Set<string>>(new Set());
  const [gateEditText, setGateEditText] = useState<Record<string, string>>({});
  const [gateRejectReason] = useState<Record<string, string>>({});
  const [submittedGates, setSubmittedGates] = useState<Set<string>>(new Set());
  const [sendBackFeedback, setSendBackFeedback] = useState<Record<string, string>>({});
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
    const indexMap: Map<string, number> = new Map();
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
                {groupedOutputs.length === 0 &&
                 events.filter((e) => e.type === "ClarificationNeeded" || e.type === "GateAwaitingApproval" || e.type === "PlanGenerated" || e.type === "LoopIterationStarted").length === 0 ? (
                  <div className="flex items-center justify-center h-full text-xs text-slate-500">
                    {isExecuting
                      ? "Waiting for agent output…"
                      : "Agent responses will appear here"}
                  </div>
                ) : (
                  <>
                    {events.map((evt, i) => {
                      // Loop iteration headers
                      if (evt.type === "LoopIterationStarted") {
                        return (
                          <div key={`loop-${i}`} className="flex items-center gap-2 py-1">
                            <RefreshCw size={12} className="text-blue-400" />
                            <span className="text-[11px] font-medium text-blue-400">
                              Loop iteration {evt.loopIteration}/{evt.maxIterations}
                            </span>
                            <div className="flex-1 h-px bg-slate-700" />
                          </div>
                        );
                      }

                      if (evt.type === "LoopIterationCompleted") {
                        return (
                          <div key={`loop-done-${i}`} className="flex items-center gap-2 py-1">
                            <CheckCircle2 size={12} className="text-green-400" />
                            <span className="text-[10px] text-slate-500">
                              {evt.message || `Iteration ${evt.loopIteration} completed`}
                            </span>
                          </div>
                        );
                      }

                      // Plan generated card
                      if (evt.type === "PlanGenerated" && evt.planSteps) {
                        return (
                          <div key={`plan-${i}`} className="rounded border border-purple-600/40 bg-purple-900/20 overflow-hidden animate-fade-in">
                            <div className="px-2.5 py-1.5 bg-purple-900/30 flex items-center gap-1.5">
                              <MapIcon size={12} className="text-purple-400" />
                              <span className="text-[11px] font-medium text-purple-300">
                                Execution Plan ({evt.planSteps.length} steps)
                              </span>
                            </div>
                            <div className="px-2.5 py-2 space-y-1.5">
                              {evt.planSteps.map((step) => (
                                <div key={step.stepNumber} className="flex items-start gap-2 text-xs">
                                  <span className="text-purple-400 font-mono shrink-0">{step.stepNumber}.</span>
                                  <div className="min-w-0">
                                    <span className="text-slate-200 font-medium">{step.title}</span>
                                    {step.matchedAgentName && (
                                      <span className="text-purple-400 ml-1.5">→ {step.matchedAgentName}</span>
                                    )}
                                    <p className="text-slate-400 text-[11px] mt-0.5">{step.instruction}</p>
                                  </div>
                                </div>
                              ))}
                            </div>
                          </div>
                        );
                      }

                      // Clarification card
                      if (evt.type === "ClarificationNeeded" && evt.executionId) {
                        const clarKey = `${evt.executionId}-${i}`;
                        const isSubmitted = submittedClarifications.has(clarKey);
                        return (
                          <div key={`clar-${i}`} className="rounded border-l-4 border-l-amber-500 border border-slate-700 bg-slate-800/70 p-3 animate-fade-in">
                            <div className="flex items-center gap-1.5 mb-2">
                              <MessageCircle size={12} className="text-amber-400" />
                              <span className="text-[11px] font-medium text-amber-300">
                                {evt.agentName || "Agent"} needs clarification
                              </span>
                            </div>
                            <p className="text-xs text-slate-200 mb-2">{evt.question || evt.message}</p>
                            {isSubmitted ? (
                              <div className="bg-slate-700/50 rounded p-2 text-xs text-slate-300">
                                ✓ Answered: {clarificationAnswers[clarKey]}
                              </div>
                            ) : (
                              <div className="flex gap-2">
                                <input
                                  type="text"
                                  value={clarificationAnswers[clarKey] || ""}
                                  onChange={(e) => setClarificationAnswers((prev) => ({ ...prev, [clarKey]: e.target.value }))}
                                  placeholder="Type your answer…"
                                  className="flex-1 h-7 bg-slate-700 border border-slate-600 rounded text-xs text-slate-200 px-2 outline-none focus:border-amber-500"
                                />
                                <button
                                  onClick={() => {
                                    const answer = clarificationAnswers[clarKey]?.trim();
                                    if (answer && evt.executionId) {
                                      onAnswerClarification?.(evt.executionId, answer);
                                      setSubmittedClarifications((prev) => new Set(prev).add(clarKey));
                                    }
                                  }}
                                  disabled={!clarificationAnswers[clarKey]?.trim()}
                                  className="h-7 px-3 bg-amber-600 hover:bg-amber-500 disabled:opacity-50 text-white rounded text-xs font-medium cursor-pointer"
                                >
                                  Submit
                                </button>
                              </div>
                            )}
                          </div>
                        );
                      }

                      // Gate awaiting approval card
                      if (evt.type === "GateAwaitingApproval" && evt.executionId) {
                        const gateKey = `${evt.executionId}-${i}`;
                        const isSubmitted = submittedGates.has(gateKey);
                        const isReviewEdit = evt.gateType === "ReviewAndEdit";
                        return (
                          <div key={`gate-${i}`} className="rounded border-l-4 border-l-blue-500 border border-slate-700 bg-slate-800/70 p-3 animate-fade-in">
                            <div className="flex items-center gap-1.5 mb-2">
                              <ShieldCheck size={12} className="text-blue-400" />
                              <span className="text-[11px] font-medium text-blue-300">
                                {evt.gateType === "ReviewAndEdit" ? "Review & Edit" : "Approval"} Gate
                              </span>
                            </div>
                            {evt.gateInstructions && (
                              <p className="text-[11px] text-slate-400 mb-2 italic">{evt.gateInstructions}</p>
                            )}
                            {isSubmitted ? (
                              <div className="bg-slate-700/50 rounded p-2 text-xs text-slate-300">
                                ✓ Response submitted
                              </div>
                            ) : (
                              <>
                                {isReviewEdit ? (
                                  <textarea
                                    value={gateEditText[gateKey] ?? evt.previousAgentOutput ?? ""}
                                    onChange={(e) => setGateEditText((prev) => ({ ...prev, [gateKey]: e.target.value }))}
                                    className="w-full h-20 bg-slate-700 border border-slate-600 rounded text-xs text-slate-200 p-2 outline-none focus:border-blue-500 mb-2 resize-y"
                                  />
                                ) : (
                                  <p className="text-xs text-slate-200 bg-slate-700/50 rounded p-2 mb-2 whitespace-pre-wrap">
                                    {evt.previousAgentOutput}
                                  </p>
                                )}
                                <div className="flex gap-2">
                                  <button
                                    onClick={() => {
                                      if (evt.executionId) {
                                        const editedOutput = isReviewEdit ? (gateEditText[gateKey] ?? evt.previousAgentOutput) : undefined;
                                        onApproveGate?.(evt.executionId, editedOutput);
                                        setSubmittedGates((prev) => new Set(prev).add(gateKey));
                                      }
                                    }}
                                    className="h-7 px-3 bg-green-600 hover:bg-green-500 text-white rounded text-xs font-medium cursor-pointer"
                                  >
                                    {isReviewEdit ? "Approve with Edits" : "Approve"}
                                  </button>
                                  <button
                                    onClick={() => {
                                      if (evt.executionId) {
                                        onRejectGate?.(evt.executionId, gateRejectReason[gateKey]);
                                        setSubmittedGates((prev) => new Set(prev).add(gateKey));
                                      }
                                    }}
                                    className="h-7 px-3 bg-red-600 hover:bg-red-500 text-white rounded text-xs font-medium cursor-pointer"
                                  >
                                    Reject
                                  </button>
                                  <button
                                    onClick={() => {
                                      if (evt.executionId) {
                                        onSendBackGate?.(evt.executionId, sendBackFeedback[gateKey]);
                                        setSubmittedGates((prev) => new Set(prev).add(gateKey));
                                      }
                                    }}
                                    className="h-7 px-3 bg-amber-600 hover:bg-amber-500 text-white rounded text-xs font-medium cursor-pointer"
                                  >
                                    Send Back
                                  </button>
                                </div>
                                <input
                                  type="text"
                                  value={sendBackFeedback[gateKey] || ""}
                                  onChange={(e) => setSendBackFeedback((prev) => ({ ...prev, [gateKey]: e.target.value }))}
                                  placeholder="Optional feedback for reject/send back…"
                                  className="w-full h-7 bg-slate-700 border border-slate-600 rounded text-xs text-slate-200 px-2 mt-2 outline-none focus:border-slate-500"
                                />
                              </>
                            )}
                          </div>
                        );
                      }

                      // Regular output events grouped below
                      return null;
                    })}

                    {/* Grouped agent outputs */}
                    {groupedOutputs.map((group) => {
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
                  })}
                  </>
                )}
              </div>
            </div>
          </div>

          {/* Execution tab bar (only shown when multiple executions exist) */}
          {executions && executions.length > 1 && (
            <div className="shrink-0 flex items-center gap-px px-2 py-1 border-t border-slate-700 bg-slate-800/50 overflow-x-auto">
              {executions.map((exec) => (
                <button
                  key={exec.executionId}
                  onClick={() => onSelectExecution?.(exec.executionId)}
                  className={`flex items-center gap-1.5 px-2.5 py-1 rounded text-[10px] font-medium shrink-0 cursor-pointer ${
                    exec.executionId === activeExecutionId
                      ? "bg-slate-700 text-slate-200"
                      : "text-slate-400 hover:bg-slate-700/50"
                  }`}
                >
                  {exec.status === "running" && <Loader2 size={10} className="animate-spin text-blue-400" />}
                  {exec.status === "paused" && <ShieldCheck size={10} className="text-amber-400" />}
                  {exec.status === "completed" && <CheckCircle2 size={10} className="text-green-400" />}
                  {exec.status === "failed" && <AlertCircle size={10} className="text-red-400" />}
                  {exec.status === "cancelled" && <Square size={10} className="text-slate-400" />}
                  <span className="max-w-[100px] truncate">{exec.workflowName}</span>
                  <span
                    onClick={(e) => {
                      e.stopPropagation();
                      if (exec.status === "running") {
                        if (confirm("Cancel this running execution?")) {
                          onCancelExecution?.(exec.executionId);
                          onCloseExecution?.(exec.executionId);
                        }
                      } else {
                        onCloseExecution?.(exec.executionId);
                      }
                    }}
                    className="hover:text-slate-200 cursor-pointer ml-1"
                  >
                    <X size={10} />
                  </span>
                </button>
              ))}
            </div>
          )}

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
