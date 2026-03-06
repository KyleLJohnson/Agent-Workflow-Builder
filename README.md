# Agent Workflow Builder

A visual, low-code platform for designing and executing multi-agent AI workflows. Drag-and-drop agent nodes onto a canvas, wire them into directed acyclic graphs (DAGs), and watch them execute in real time — with built-in support for human-in-the-loop gates, clarification questions, feedback loops, and external tool integration via the Model Context Protocol (MCP).

---

## Problem → Solution

### The Problem

Building multi-agent AI pipelines today means writing bespoke orchestration code for every workflow. Teams face:

- **No visual tooling** — agent chains are defined in code, making iteration slow and opaque to non-developers.
- **No human oversight** — once a pipeline starts, there is no standard way to pause for approval, ask the user a clarifying question, or send work back for revision.
- **Fragile integrations** — connecting agents to external tools (databases, APIs, file systems) requires custom glue code for each integration.
- **No execution history** — when a multi-step workflow fails, there is no checkpoint or recovery mechanism.

### The Solution

Agent Workflow Builder provides a **visual canvas** for composing AI agents into executable workflows, backed by:

| Capability | How it works |
|---|---|
| **Visual DAG editor** | Drag agent and gate nodes onto a React Flow canvas; connect them with edges to define execution order. |
| **Human-in-the-loop gates** | Insert approval or review-and-edit gates between agents. Execution pauses until a human approves, rejects, or sends work back. |
| **Clarification questions** | Agents can ask the user mid-execution via the Copilot SDK `OnUserInputRequest` callback. |
| **Feedback loops** | Mark edges as back-edges to create iteration loops with configurable max iterations. |
| **MCP tool integration** | Attach MCP servers (stdio or SSE) to agents so they can call external tools during reasoning. |
| **Execution persistence** | Every run is checkpointed. Paused executions survive restarts and can be resumed. |
| **Concurrent execution** | Run multiple workflows simultaneously with per-user concurrency limits. |
| **Planner agents** | A special agent type that generates a `<<<PLAN>>>` and dynamically creates sub-steps. |

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | **10.0+** | Backend runtime |
| [Node.js](https://nodejs.org/) | **18+** | Frontend toolchain (npm) |
| **Azure OpenAI** (or compatible endpoint) | — | An API key and endpoint URL for an OpenAI-compatible LLM |

### Optional

| Requirement | When needed |
|---|---|
| [Azure Cosmos DB](https://learn.microsoft.com/azure/cosmos-db/) | Cloud-persistent workflow & execution storage (replaces default JSON files) |
| [Azure Blob Storage](https://learn.microsoft.com/azure/storage/blobs/) | Ingest plan files from blob containers |
| [Microsoft Entra ID](https://learn.microsoft.com/entra/identity/) | Multi-user authentication & authorization |
| [Playwright](https://playwright.dev/) | Running E2E tests |

---

## Setup

### 1. Clone & restore

```bash
git clone https://github.com/<your-org>/AgentWorkflowBuilder.git
cd AgentWorkflowBuilder

# Backend
dotnet restore

# Frontend
cd client
npm install
cd ..
```

### 2. Configure the LLM provider

The only **required** setting is your Azure OpenAI API key. Edit [src/AgentWorkflowBuilder.Api/appsettings.json](src/AgentWorkflowBuilder.Api/appsettings.json):

```jsonc
{
  "CopilotSdk": {
    "Provider": {
      "Type": "azure",
      "BaseUrl": "https://<your-resource>.openai.azure.com",
      "ApiKey": "<your-api-key>",          // or set COPILOT_PROVIDER_API_KEY env var
      "AzureApiVersion": "2024-10-21"
    },
    "DefaultModel": "gpt-4.1-mini"         // any deployed model name
  }
}
```

Or via environment variable:

```bash
# PowerShell
$env:COPILOT_PROVIDER_API_KEY = "<your-api-key>"

# Bash
export COPILOT_PROVIDER_API_KEY="<your-api-key>"
```

### 3. Run

```bash
# Terminal 1 — Backend (http://localhost:5275)
cd src/AgentWorkflowBuilder.Api
dotnet run

# Terminal 2 — Frontend (http://localhost:5173)
cd client
npm run dev
```

Open **http://localhost:5173** in your browser.

### 4. Optional configuration

All optional features are **disabled by default** when their config values are empty or set to placeholders.

<details>
<summary><strong>Microsoft Entra ID (authentication)</strong></summary>

Register an app in the Azure portal, then update both files:

**Backend** — `appsettings.json`:
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<your-tenant-id>",
    "ClientId": "<your-client-id>",
    "Audience": "api://<your-client-id>"
  }
}
```

**Frontend** — `client/src/authConfig.ts`:
```typescript
auth: {
  clientId: "<your-client-id>",
  authority: "https://login.microsoftonline.com/<your-tenant-id>",
}
```

When configured, workflows and executions are scoped per user.

</details>

<details>
<summary><strong>Azure Cosmos DB (cloud persistence)</strong></summary>

```json
{
  "CosmosDb": {
    "ConnectionString": "AccountEndpoint=https://...",
    "DatabaseName": "AgentWorkflowBuilder",
    "WorkflowContainerName": "workflows",
    "ExecutionContainerName": "executions"
  }
}
```

When `ConnectionString` is empty, the app falls back to local JSON file storage under the `data/` directory.

</details>

<details>
<summary><strong>Azure Blob Storage (plan ingestion)</strong></summary>

```json
{
  "AzureBlobPlans": {
    "ConnectionString": "<blob-connection-string>",
    "PollingIntervalSeconds": 30,
    "Enabled": true
  }
}
```

When enabled, a background service polls the configured container for plan files and ingests them as workflows.

</details>

---

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                     React Client                         │
│  React 19 · TypeScript · Tailwind · @xyflow/react        │
│                                                          │
│  ┌──────────┐ ┌──────────┐ ┌───────────┐ ┌───────────┐  │
│  │ Workflow  │ │  Agent   │ │ Execution │ │    MCP    │  │
│  │  Canvas   │ │  Editor  │ │   Panel   │ │ Settings  │  │
│  └────┬─────┘ └────┬─────┘ └─────┬─────┘ └─────┬─────┘  │
│       │             │             │              │        │
│       └─────────────┴──────┬──────┴──────────────┘        │
│                            │                              │
│              ┌─────────────┴─────────────┐                │
│              │   SignalR + REST (Axios)   │                │
│              └─────────────┬─────────────┘                │
└────────────────────────────┼──────────────────────────────┘
                             │
                     HTTP / WebSocket
                             │
┌────────────────────────────┼──────────────────────────────┐
│                    ASP.NET Core API                       │
│              .NET 10 · Minimal APIs · SignalR             │
│                                                          │
│  ┌──────────────────┐    ┌───────────────────────────┐   │
│  │  Minimal API     │    │     WorkflowHub           │   │
│  │  Endpoints       │    │  (real-time execution)    │   │
│  │  /api/agents     │    │  Execute · Clarify · Gate │   │
│  │  /api/workflows  │    │  Cancel · Approve         │   │
│  │  /api/mcp/*      │    └────────────┬──────────────┘   │
│  └────────┬─────────┘                 │                  │
│           └───────────┬───────────────┘                  │
│                       │                                  │
│  ┌────────────────────▼──────────────────────────────┐   │
│  │                 Core Engine                       │   │
│  │                                                   │   │
│  │  WorkflowEngine        ExecutionSessionManager    │   │
│  │  ├─ DAG planning       ├─ Pause / resume          │   │
│  │  ├─ Agent execution    ├─ Clarification wait      │   │
│  │  ├─ Gate handling      ├─ Gate response wait      │   │
│  │  ├─ Loop iteration     └─ Checkpoint persistence  │   │
│  │  └─ Plan parsing                                  │   │
│  │                                                   │   │
│  │  ┌─────────────────────────────────────────────┐  │   │
│  │  │  GitHub Copilot SDK (BYOK)                  │  │   │
│  │  │  CopilotClient (singleton)                  │  │   │
│  │  │  ├─ CopilotProviderFactory → ProviderConfig │  │   │
│  │  │  │   └─ Your Azure OpenAI key + endpoint    │  │   │
│  │  │  └─ CopilotSessionFactory → CopilotSession  │  │   │
│  │  │       ├─ Per-agent session with BYOK model   │  │   │
│  │  │       ├─ OnUserInputRequest (clarification)  │  │   │
│  │  │       └─ MCP server wiring (tools)           │  │   │
│  │  └─────────────────────────────────────────────┘  │   │
│  └───────────────────────────────────────────────────┘   │
│                       │                                  │
│  ┌────────────────────▼──────────────────────────────┐   │
│  │              Persistence Layer                    │   │
│  │                                                   │   │
│  │  JsonWorkflowStore ──or── CosmosWorkflowStore     │   │
│  │  JsonExecutionStore ─or── CosmosExecutionStore    │   │
│  │  JsonAgentRegistry                                │   │
│  │  McpClientManager (mcp.json)                      │   │
│  └───────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
                             │
                    External Services
                             │
     ┌───────────────────────┼───────────────────────┐
     │              │                │                │
┌────▼────────┐ ┌───▼──────────┐ ┌───▼──────┐ ┌──────▼──────┐
│ Azure       │ │ Your LLM     │ │ MCP      │ │ Azure       │
│ OpenAI      │ │ (BYOK)       │ │ Servers  │ │ Cosmos DB   │
│ gpt-4.1-*   │ │ Any OpenAI-  │ │ (stdio/  │ │ Blob Storage│
│             │ │ compatible   │ │  SSE)    │ │             │
└─────────────┘ └──────────────┘ └──────────┘ └─────────────┘
```

### Project Structure

| Project | Role |
|---|---|
| **AgentWorkflowBuilder.Core** | Domain models, interfaces, workflow engine, session management. Zero infrastructure dependencies. |
| **AgentWorkflowBuilder.Persistence** | Storage implementations — JSON file stores (default) and Azure Cosmos DB stores (optional). |
| **AgentWorkflowBuilder.Agents** | Built-in agent definitions and seeding logic. |
| **AgentWorkflowBuilder.Api** | ASP.NET Core host — minimal API endpoints, SignalR hub, background services, DI composition root. |
| **client/** | React 19 SPA — visual workflow editor, agent management, real-time execution panel. |
| **data/** | Local data directory — agent definitions, workflow JSON files, MCP server config. |
| **e2e/** | Playwright E2E test scaffold. |

### Built-in Agents

| Agent | Purpose |
|---|---|
| Summarizer | Condenses text into key points |
| Content Writer | Generates written content from prompts |
| Code Reviewer | Reviews code for issues and improvements |
| Sentiment Analyzer | Analyzes text sentiment |
| Data Extractor | Extracts structured data from unstructured text |
| Translator | Translates text between languages |
| Planner | Generates multi-step execution plans (`<<<PLAN>>>` format) |

### Key Configuration

| Setting | Default | Description |
|---|---|---|
| `CopilotSdk:DefaultModel` | `gpt-4.1-mini` | LLM model for agent sessions |
| `Workflow:ClarificationTimeoutMinutes` | `10` | How long to wait for user clarification |
| `Workflow:MaxLoopIterations` | `3` | Max iterations for feedback loops |
| `Workflow:MaxConcurrentExecutionsPerUser` | `5` | Per-user concurrency limit |
| `Data:BasePath` | `../../../data` | Path to local data directory |

---

## Deployment

### Local Development

No Azure services required. The app runs entirely locally using JSON file storage and a direct Azure OpenAI API key.

### Azure Deployment

For a production deployment on Azure:

1. **Azure OpenAI** — Deploy a model (e.g., `gpt-4.1-mini`) and note the endpoint + key.
2. **Azure App Service** or **Azure Container Apps** — Host the .NET backend. Set environment variables for all config values.
3. **Azure Static Web Apps** or **App Service** — Host the built React frontend (`npm run build` → `client/dist/`).
4. **Azure Cosmos DB** (optional) — Create a database and containers (`workflows`, `executions`). Set the connection string.
5. **Azure Blob Storage** (optional) — Create a storage account for plan ingestion.
6. **Microsoft Entra ID** (optional) — Register an app for authentication. Configure `AzureAd` and `authConfig.ts`.

#### Environment Variables (production)

```bash
COPILOT_PROVIDER_API_KEY=<azure-openai-key>
CopilotSdk__Provider__BaseUrl=https://<resource>.openai.azure.com
CosmosDb__ConnectionString=AccountEndpoint=https://...
AzureAd__TenantId=<tenant-id>
AzureAd__ClientId=<client-id>
```

> **Note:** .NET configuration maps `__` (double underscore) to `:` in hierarchical keys when using environment variables.

---

## Responsible AI (RAI) Notes

### Transparency

- **Human-in-the-loop by design.** Gate nodes let operators review, edit, or reject agent outputs before they propagate downstream. No automated action is taken without the option for human oversight.
- **Clarification support.** Agents can ask users for additional information rather than guessing, reducing hallucination risk.
- **Full execution visibility.** Every agent step, gate decision, and loop iteration is streamed to the UI in real time and persisted for audit.

### Fairness & Safety

- **No training on user data.** The application is a workflow orchestrator. It sends prompts to your own Azure OpenAI deployment and does not fine-tune or train models.
- **Configurable system prompts.** Each agent's behavior is governed by its `SystemInstructions`, which you author and control. Review these prompts for bias, safety, and appropriateness.
- **Model selection.** You choose which model to deploy. Evaluate the model's safety profile (content filtering, grounding) in the Azure OpenAI studio before use.

### Privacy & Data Handling

- **BYOK (Bring Your Own Key).** All LLM calls go to your own Azure OpenAI resource. No data is sent to third-party services unless you configure an MCP server that does so.
- **Local-first storage.** By default, all data stays on disk as JSON files. Cosmos DB is opt-in.
- **User scoping.** When Entra ID is enabled, workflows and execution history are isolated per user.

### Limitations

- **LLM outputs are non-deterministic.** Agent responses may vary between runs. Use gate nodes for critical decision points.
- **No content filtering built in.** Content safety relies on your Azure OpenAI deployment's content filtering configuration. Enable and configure filters in the Azure portal.
- **MCP tool risk.** MCP servers you attach can execute arbitrary actions (file I/O, API calls, database queries). Only connect trusted MCP servers and review their capabilities.
- **Not a replacement for review.** This tool augments human workflows — it does not replace the need for human judgment on consequential decisions.

### Recommendations

1. **Enable content filters** on your Azure OpenAI deployment.
2. **Use gate nodes** before any workflow step that produces externally visible output.
3. **Audit agent prompts** periodically for bias, safety, and alignment with your organization's policies.
4. **Restrict MCP servers** to trusted, well-scoped tool implementations.
5. **Enable Entra ID** in multi-user environments to ensure proper data isolation.

---

## License

See [LICENSE](LICENSE) for details.