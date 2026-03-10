# Agent Workflow Builder

A visual, low-code platform for designing and executing multi-agent AI workflows. Drag-and-drop agent nodes onto a canvas, wire them into directed acyclic graphs (DAGs), and watch them execute in real time — with built-in support for human-in-the-loop gates, clarification questions, feedback loops, and external tool integration via the Model Context Protocol (MCP).

---

## Demo

A full end-to-end demo showing a **Developer → Approval Gate → Code Reviewer** workflow:

1. Create a new workflow and drag-and-drop agents and a gate onto the canvas
2. Connect the nodes into a pipeline
3. Send a prompt ("Write C# code to add two numbers")
4. Watch the Developer agent generate code
5. Approve the gate
6. Watch the Code Reviewer agent review the output

https://github.com/user-attachments/assets/demo-gate-workflow.webm

> The video is available locally at [`docs/demo-gate-workflow.webm`](docs/demo-gate-workflow.webm).

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
| **Workflow list view** | A landing page for browsing, creating, and managing saved workflows. |
| **Auto-approve gates** | Toggle automatic gate approval for testing and development scenarios. |
| **Custom agents** | Create your own agents with custom system prompts, input/output schemas, and categories. |
| **Infrastructure as Code** | Full Bicep templates for one-command Azure deployment with optional private networking. |
| **Execution recovery** | Interrupted executions are automatically recovered on server restart. |
| **Distributed scale-out** | Azure SignalR Service + Service Bus queues for multi-instance deployments. |

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
| [Azure CLI](https://learn.microsoft.com/cli/azure/) | Deploying infrastructure via Bicep |

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
│  │  CosmosSessionStore (change feed signaling)       │   │
│  │  CosmosConcurrencyCounter (distributed limits)    │   │
│  │  ServiceBusExecutionQueue (distributed queuing)   │   │
│  └───────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────┘
                             │
                    External Services
                             │
   ┌────────┬────────┬────────┼────────┬────────┬────────┐
   │        │        │        │        │        │        │
┌──▼───┐ ┌──▼───┐ ┌──▼───┐ ┌──▼───┐ ┌──▼───┐ ┌──▼───┐ ┌──▼───┐
│Azure │ │ Your │ │ MCP  │ │Azure │ │Azure │ │Azure │ │ App  │
│OpenAI│ │ LLM  │ │Server│ │Cosmos│ │Signal│ │Svc   │ │Insig-│
│      │ │(BYOK)│ │(stdio│ │  DB  │ │  R   │ │ Bus  │ │ hts  │
│      │ │      │ │/SSE) │ │      │ │      │ │      │ │      │
└──────┘ └──────┘ └──────┘ └──────┘ └──────┘ └──────┘ └──────┘
```

### Project Structure

| Project | Role |
|---|---|
| **AgentWorkflowBuilder.Core** | Domain models, interfaces, workflow engine, session management. Zero infrastructure dependencies. |
| **AgentWorkflowBuilder.Persistence** | Storage implementations — JSON file stores (default) and Azure Cosmos DB stores (optional). |
| **AgentWorkflowBuilder.Agents** | Built-in agent definitions and seeding logic. |
| **AgentWorkflowBuilder.Api** | ASP.NET Core host — minimal API endpoints, SignalR hub, background services (execution worker, recovery, blob polling), DI composition root. |
| **client/** | React 19 SPA — workflow list view, visual canvas editor, agent management, real-time execution panel. |
| **data/** | Local data directory — built-in/custom agent definitions, workflow JSON files, MCP server config. |
| **e2e/** | Playwright E2E tests — agent CRUD, workflow design, gate workflows, execution panel, MCP settings. |
| **infra/** | Bicep IaC templates — modular Azure deployment (App Service, Cosmos DB, SignalR, Service Bus, OpenAI, networking). |
| **test/** | Unit test projects for Core and Persistence layers. |

### Built-in Agents

| Agent | Category | Purpose |
|---|---|---|
| Summarizer | Summarization | Condenses text into key points |
| Content Writer | Writing | Generates written content from prompts |
| Code Reviewer | Development | Reviews code for issues and improvements |
| Sentiment Analyzer | Analysis | Analyzes text sentiment |
| Data Extractor | Extraction | Extracts structured data from unstructured text |
| Translator | Translation | Translates text between languages |
| Planner | Planning | Generates multi-step execution plans (`<<<PLAN>>>` format) |

Additional agents can be created through the UI (e.g., a **Developer** agent for writing application code). Custom agents are stored in `data/agents/custom/`.

### Key Configuration

| Setting | Default | Description |
|---|---|---|
| `CopilotSdk:DefaultModel` | `gpt-4.1-mini` | LLM model for agent sessions |
| `Workflow:ClarificationTimeoutMinutes` | `10` | How long to wait for user clarification |
| `Workflow:MaxLoopIterations` | `3` | Max iterations for feedback loops |
| `Workflow:MaxConcurrentExecutionsPerUser` | `5` | Per-user concurrency limit |
| `Workflow:SignalingPollingIntervalMs` | `2000` | Polling interval for gate/clarification signaling |
| `Data:BasePath` | `../../../data` | Path to local data directory |
| `ServiceBus:ConnectionString` | — | Azure Service Bus connection (enables distributed execution queue) |
| `ServiceBus:ExecutionQueueName` | `workflow-executions` | Queue name for execution requests |
| `ServiceBus:CancellationQueueName` | `execution-cancellations` | Queue name for cancellation requests |
| `Azure:SignalR:ConnectionString` | — | Azure SignalR Service connection (enables multi-instance scale-out) |
| `CosmosDb:DedicatedGatewayEndpoint` | — | Cosmos DB dedicated gateway for integrated caching |

---

## E2E Tests

End-to-end tests are in `e2e/` using [Playwright](https://playwright.dev/) with Chromium and Firefox.

```bash
# Install browsers (one-time)
cd e2e
npm install
npx playwright install

# Run all tests (requires app running on localhost:5173)
npx playwright test

# Run a specific test
npx playwright test demo-gate-workflow --project=chromium
```

| Test | Coverage |
|---|---|
| `agent-crud.spec.ts` | Agent create, read, update, delete |
| `workflow-design.spec.ts` | Workflow canvas — new, rename, drag-drop nodes |
| `gate-workflow.spec.ts` | Gate node approval/rejection mechanics |
| `workflow-flow.spec.ts` | End-to-end workflow execution flow |
| `execution-panel.spec.ts` | Execution panel interactions |
| `mcp-settings.spec.ts` | MCP server configuration UI |
| `demo-gate-workflow.spec.ts` | Full demo: Developer → Gate → Code Reviewer with DnD and output scrolling |

---

## Deployment

### Local Development

No Azure services required. The app runs entirely locally using JSON file storage and a direct Azure OpenAI API key.

### Azure Deployment (Bicep IaC)

The `infra/` directory contains Bicep templates for a full Azure deployment. A single `az deployment` command provisions all resources:

```bash
az deployment sub create \
  --location <region> \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam
```

#### Provisioned Resources

| Resource | Module | Purpose |
|---|---|---|
| **App Service** | `modules/app-service.bicep` | .NET backend host with system-assigned managed identity |
| **Azure OpenAI** | `modules/openai.bicep` | LLM endpoint (conditionally deployed) |
| **Cosmos DB** | `modules/cosmos-db.bicep` | Workflow, execution, and session persistence |
| **Azure SignalR** | `modules/signalr.bicep` | Real-time SignalR scale-out for multi-instance |
| **Service Bus** | `modules/service-bus.bicep` | Execution and cancellation queues |
| **Storage Account** | `modules/storage.bicep` | Blob plan ingestion |
| **Application Insights** | `modules/app-insights.bicep` | Monitoring and diagnostics |
| **VNet + Private Endpoints** | `modules/networking.bicep` | Optional private networking |

#### Key Deployment Parameters

| Parameter | Description |
|---|---|
| `enablePrivateNetworking` | Enable VNet integration and private endpoints for all services |
| `cosmosEnableDedicatedGateway` | Enable Cosmos DB dedicated gateway for integrated caching |
| `openAiModelName` / `openAiModelVersion` | Model to deploy in Azure OpenAI |

#### Managed Identity

When deployed to Azure, the App Service uses a **system-assigned managed identity** to authenticate with Cosmos DB, eliminating connection string secrets. The backend detects `CosmosDb:Endpoint` and uses `DefaultAzureCredential()` automatically.

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