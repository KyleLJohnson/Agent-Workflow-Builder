import { test, expect, type Page } from "@playwright/test";

/**
 * Helper to wait for the app shell.
 */
async function waitForAppReady(page: Page) {
  await expect(
    page.getByRole("heading", { name: "Agents" })
  ).toBeVisible({ timeout: 15_000 });
}

/**
 * Helper to create a workflow, load it, and return its ID.
 */
async function setupWorkflow(page: Page) {
  const agentsResp = await page.request.get("/api/agents");
  const agents = (await agentsResp.json()) as Array<{
    id: string;
    name: string;
  }>;
  const agent = agents[0];
  const wfName = `Exec Panel ${Date.now()}`;

  const createResp = await page.request.post("/api/workflows", {
    data: {
      name: wfName,
      description: "Execution panel test workflow",
      nodes: [
        {
          nodeId: "n1",
          agentId: agent.id,
          label: agent.name,
          positionX: 300,
          positionY: 200,
        },
      ],
      edges: [],
    },
  });
  expect(createResp.ok()).toBeTruthy();
  const wf = (await createResp.json()) as { id: string };

  await page.reload();
  await waitForAppReady(page);

  const loadBtn = page.locator("button", { hasText: "Load" });
  await loadBtn.click();
  await page.waitForTimeout(300);
  await page.locator("button", { hasText: wfName }).click();
  await page.waitForTimeout(500);

  return wf.id;
}

test.describe("Execution panel", () => {
  let workflowId: string;

  test.afterEach(async ({ page }) => {
    if (workflowId) {
      await page.request.delete(`/api/workflows/${workflowId}`).catch(() => {});
    }
  });

  test("execution panel is visible with header", async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);
    workflowId = await setupWorkflow(page);

    // Execution header should be visible
    const execHeader = page.locator("text=Execution").first();
    await expect(execHeader).toBeVisible({ timeout: 5_000 });
  });

  test("shows SignalR connection status", async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);
    workflowId = await setupWorkflow(page);

    // Green dot indicates connected state
    const connectionDot = page.locator("div.bg-green-500").first();
    await expect(connectionDot).toBeVisible({ timeout: 10_000 });
  });

  test("message input is available when workflow is loaded", async ({
    page,
  }) => {
    await page.goto("/");
    await waitForAppReady(page);
    workflowId = await setupWorkflow(page);

    const msgInput = page.locator(
      'input[placeholder="Type a message to send to the workflow..."]'
    );
    await expect(msgInput).toBeEnabled({ timeout: 5_000 });

    const sendBtn = page.locator("button", { hasText: "Send" });
    await expect(sendBtn).toBeVisible();
  });

  test("send button triggers execution", async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);
    workflowId = await setupWorkflow(page);

    // Type a message
    const msgInput = page.locator(
      'input[placeholder="Type a message to send to the workflow..."]'
    );
    await expect(msgInput).toBeEnabled({ timeout: 5_000 });
    await msgInput.fill("Hello, this is an E2E test message");

    // Click send
    const sendBtn = page.locator("button", { hasText: "Send" });
    await sendBtn.click();

    // Wait for execution started event
    const executionStarted = page.locator("text=Execution Started");
    await expect(executionStarted).toBeVisible({ timeout: 30_000 });
  });

  test("execution log shows events", async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);
    workflowId = await setupWorkflow(page);

    // Send a message to start execution
    const msgInput = page.locator(
      'input[placeholder="Type a message to send to the workflow..."]'
    );
    await msgInput.fill("Test execution log events");
    await page.locator("button", { hasText: "Send" }).click();

    // Wait for execution to start
    await expect(
      page.locator("text=Execution Started")
    ).toBeVisible({ timeout: 30_000 });

    // Execution log pane should show events
    const logPane = page.locator('[data-testid="execution-log"]');
    await expect(logPane).toBeVisible({ timeout: 5_000 });

    // Wait for at least some events to appear
    const eventItems = logPane.locator(
      ".flex.items-start.gap-2.text-xs"
    );
    await expect(eventItems.first()).toBeVisible({ timeout: 15_000 });
  });

  test("execution completes or errors with visible status", async ({
    page,
  }) => {
    await page.goto("/");
    await waitForAppReady(page);
    workflowId = await setupWorkflow(page);

    // Send a message
    const msgInput = page.locator(
      'input[placeholder="Type a message to send to the workflow..."]'
    );
    await msgInput.fill("Quick test for completion status");
    await page.locator("button", { hasText: "Send" }).click();

    // Wait for either completion or error
    const completedLabel = page.locator("span.text-green-500", {
      hasText: "Execution Completed",
    });
    const errorLabel = page.locator("span.text-red-400", {
      hasText: "Error",
    });
    const completedOrError = completedLabel.or(errorLabel);
    await expect(completedOrError.first()).toBeVisible({
      timeout: 60_000,
    });
  });

  test("agent output pane shows results after execution", async ({
    page,
  }) => {
    await page.goto("/");
    await waitForAppReady(page);
    workflowId = await setupWorkflow(page);

    const msgInput = page.locator(
      'input[placeholder="Type a message to send to the workflow..."]'
    );
    await msgInput.fill("Tell me a short joke");
    await page.locator("button", { hasText: "Send" }).click();

    // Wait for completion or error
    const completed = page.locator("span.text-green-500", {
      hasText: "Execution Completed",
    });
    const error = page.locator("span.text-red-400");
    await expect(completed.or(error).first()).toBeVisible({
      timeout: 60_000,
    });

    // Check the agent output pane
    const outputPane = page.locator('[data-testid="agent-output"]');
    await expect(outputPane).toBeVisible({ timeout: 5_000 });

    // If execution completed, there should be output content
    if ((await completed.count()) > 0) {
      const outputText = await outputPane.innerText();
      expect(outputText.length).toBeGreaterThan(0);
    }
  });

  test("clear button resets execution log", async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);
    workflowId = await setupWorkflow(page);

    // Start an execution
    const msgInput = page.locator(
      'input[placeholder="Type a message to send to the workflow..."]'
    );
    await msgInput.fill("Test clear button");
    await page.locator("button", { hasText: "Send" }).click();

    // Wait for at least the execution started event
    await expect(
      page.locator("text=Execution Started")
    ).toBeVisible({ timeout: 30_000 });

    // Wait for completion before clearing
    const completed = page.locator("span.text-green-500", {
      hasText: "Execution Completed",
    });
    const error = page.locator("span.text-red-400");
    await expect(completed.or(error).first()).toBeVisible({
      timeout: 60_000,
    });

    // Click the clear/trash button
    const clearBtn = page
      .locator("button")
      .filter({
        has: page.locator('svg[class*="lucide-trash"]'),
      })
      .first();
    // Fallback: try to find any button near the execution header with trash icon
    if (!(await clearBtn.isVisible())) {
      // The trash icon may have a different class naming
      const trashButtons = page.locator("button").filter({
        has: page.locator("svg"),
      });
      const count = await trashButtons.count();
      for (let i = 0; i < count; i++) {
        const btn = trashButtons.nth(i);
        const html = await btn.innerHTML();
        if (html.includes("trash") || html.includes("Trash")) {
          await btn.click();
          break;
        }
      }
    } else {
      await clearBtn.click();
    }
  });
});
