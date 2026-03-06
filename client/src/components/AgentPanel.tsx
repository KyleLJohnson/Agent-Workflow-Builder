import { useState, useMemo } from "react";
import { Search, Plus } from "lucide-react";
import type { AgentDefinition } from "@/types";

interface AgentPanelProps {
  agents: AgentDefinition[];
  onCreateAgent: () => void;
  onEditAgent: (agent: AgentDefinition) => void;
}

export default function AgentPanel({
  agents,
  onCreateAgent,
  onEditAgent,
}: AgentPanelProps) {
  const [search, setSearch] = useState("");

  const filtered = useMemo(() => {
    if (!search.trim()) return agents;
    const q = search.toLowerCase();
    return agents.filter(
      (a) =>
        a.name.toLowerCase().includes(q) ||
        a.description.toLowerCase().includes(q) ||
        a.category.toLowerCase().includes(q)
    );
  }, [agents, search]);

  const grouped = useMemo(() => {
    const map = new Map<string, AgentDefinition[]>();
    for (const agent of filtered) {
      const cat = agent.category || "Uncategorized";
      if (!map.has(cat)) map.set(cat, []);
      map.get(cat)!.push(agent);
    }
    return map;
  }, [filtered]);

  const handleDragStart = (
    e: React.DragEvent<HTMLDivElement>,
    agent: AgentDefinition
  ) => {
    e.dataTransfer.setData(
      "application/agentworkflow",
      JSON.stringify(agent)
    );
    e.dataTransfer.effectAllowed = "move";
  };

  return (
    <aside className="w-[280px] shrink-0 h-full bg-slate-900 border-r border-slate-700 flex flex-col">
      {/* Header */}
      <div className="p-4 border-b border-slate-700">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold text-slate-200 uppercase tracking-wider">
            Agents
          </h2>
          <button
            onClick={onCreateAgent}
            className="flex items-center gap-1.5 text-xs font-medium px-2.5 py-1.5
                       rounded-lg bg-blue-600 hover:bg-blue-500 text-white
                       transition-colors cursor-pointer"
          >
            <Plus size={14} />
            Create
          </button>
        </div>

        {/* Search */}
        <div className="relative">
          <Search
            size={14}
            className="absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-500"
          />
          <input
            type="text"
            placeholder="Search agents..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full bg-slate-800 border border-slate-600 rounded-lg
                       text-sm text-slate-200 placeholder-slate-500
                       pl-8 pr-3 py-2 outline-none focus:border-blue-500
                       transition-colors"
          />
        </div>
      </div>

      {/* Agent list */}
      <div className="flex-1 overflow-y-auto p-3 space-y-4">
        {[...grouped.entries()].map(([category, catAgents]) => (
          <div key={category}>
            <h3 className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-2 px-1">
              {category}
            </h3>
            <div className="space-y-1.5">
              {catAgents.map((agent) => (
                <div
                  key={agent.id}
                  draggable
                  onDragStart={(e) => handleDragStart(e, agent)}
                  onDoubleClick={() => onEditAgent(agent)}
                  className="flex items-start gap-3 p-2.5 rounded-lg
                             bg-slate-800/60 hover:bg-slate-800 border border-transparent
                             hover:border-slate-600 cursor-grab active:cursor-grabbing
                             transition-all group"
                >
                  <span className="text-xl shrink-0 mt-0.5 select-none">
                    {agent.icon || "🤖"}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-medium text-slate-200 truncate">
                      {agent.name}
                    </div>
                    <div className="text-xs text-slate-500 mt-0.5 line-clamp-2 leading-snug">
                      {agent.description}
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        ))}
        {filtered.length === 0 && (
          <div className="text-center text-sm text-slate-500 py-8">
            {search ? "No agents match your search" : "No agents available"}
          </div>
        )}
      </div>
    </aside>
  );
}
