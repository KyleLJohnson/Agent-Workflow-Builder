import { test, expect, type Page } from "@playwright/test";

/**
 * Helper to wait for the app shell to fully render.
 */
async function waitForAppReady(page: Page) {
  await expect(
    page.getByRole("heading", { name: "Agents" })
  ).toBeVisible({ timeout: 15_000 });
}

/**
 * Helper to create a workflow via API and load it.
 */
async function createAndLoadWorkflow(
  page: Page,
  name: string,
  nodes: Array<{
    nodeId: string;
    agentId: string;
    label: string;
    positionX: number;
    positionY: number;
  }>,
  edges: Array<{
    sourceNodeId: string;
    targetNodeId: string;
  }> = []
) {
  const resp = await page.request.post("/api/workflows", {
    data: { name, description: `Playwright test – ${name}`, nodes, edges },
  });
  expect(resp.ok()).toBeTruthy();
  const workflow = (await resp.json()) as { id: string };

  await page.reload();
  await waitForAppReady(page);

  // Load the workflow
  const loadBtn = page.locator("button", { hasText: "Load" });
  await loadBtn.click();
  await page.waitForTimeout(300);

  const entry = page.locator("button", { hasText: name });
  await expect(entry).toBeVisible({ timeout: 5_000 });
  await entry.click();
  await page.waitForTimeout(500);

  return workflow.id;
}

test.describe("Workflow design and management", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);
  });

  test("shows workflow list view initially", async ({ page }) => {
    // The landing view should show "Your Workflows"
    const heading = page.locator("h1", { hasText: "Your Workflows" });
    await expect(heading).toBeVisible({ timeout: 5_000 });
  });

  test("creates new workflow via New Workflow button", async ({ page }) => {
    const newBtn = page.locator("button", { hasText: "New Workflow" });
    await expect(newBtn).toBeVisible();
    await newBtn.click();
    await page.waitForTimeout(500);

    // Canvas should appear (ReactFlow container)
    const canvas = page.locator(".react-flow");
    await expect(canvas).toBeVisible({ timeout: 5_000 });

    // Workflow name input should be visible with placeholder
    const nameInput = page.locator(
      'input[placeholder="Untitled Workflow"]'
    );
    await expect(nameInput).toBeVisible();
  });

  test("renames workflow via toolbar input", async ({ page }) => {
    const newName = `Renamed WF ${Date.now()}`;

    // Create a workflow via API first
    const agentsResp = await page.request.get("/api/agents");
    const agents = (await agentsResp.json()) as Array<{
      id: string;
      name: string;
    }>;
    const firstAgent = agents[0];

    const workflowId = await createAndLoadWorkflow(
      page,
      `Original Name ${Date.now()}`,
      [
        {
          nodeId: "n1",
          agentId: firstAgent.id,
          label: firstAgent.name,
          positionX: 300,
          positionY: 200,
        },
      ]
    );

    // Update the name
    const nameInput = page.locator(
      'input[placeholder="Untitled Workflow"]'
    );
    await nameInput.clear();
    await nameInput.fill(newName);

    // Save
    const saveBtn = page.locator("button", { hasText: "Save" });
    await saveBtn.click();
    await page.waitForTimeout(500);

    // Verify the name persisted by reloading
    await page.reload();
    await waitForAppReady(page);
    const loadBtn = page.locator("button", { hasText: "Load" });
    await loadBtn.click();
    await page.waitForTimeout(300);

    const entry = page.locator("button", { hasText: newName });
    await expect(entry).toBeVisible({ timeout: 5_000 });

    // Clean up
    await page.request.delete(`/api/workflows/${workflowId}`);
  });

  test("loads existing workflow and shows nodes on canvas", async ({
    page,
  }) => {
    const agentsResp = await page.request.get("/api/agents");
    const agents = (await agentsResp.json()) as Array<{
      id: string;
      name: string;
    }>;
    const agent = agents[0];
    const wfName = `Canvas Nodes ${Date.now()}`;

    const workflowId = await createAndLoadWorkflow(
      page,
      wfName,
      [
        {
          nodeId: "n1",
          agentId: agent.id,
          label: agent.name,
          positionX: 200,
          positionY: 150,
        },
        {
          nodeId: "n2",
          agentId: agent.id,
          label: `${agent.name} 2`,
          positionX: 500,
          positionY: 150,
        },
      ],
      [{ sourceNodeId: "n1", targetNodeId: "n2" }]
    );

    // Verify nodes on canvas
    const nodes = page.locator(".react-flow__node");
    await expect(nodes).toHaveCount(2, { timeout: 5_000 });

    // Verify edge exists
    const edges = page.locator(".react-flow__edge");
    await expect(edges).toHaveCount(1, { timeout: 5_000 });

    // Clean up
    await page.request.delete(`/api/workflows/${workflowId}`);
  });

  test("saves workflow changes", async ({ page }) => {
    const agentsResp = await page.request.get("/api/agents");
    const agents = (await agentsResp.json()) as Array<{
      id: string;
      name: string;
    }>;
    const agent = agents[0];
    const wfName = `Save Test ${Date.now()}`;

    const workflowId = await createAndLoadWorkflow(
      page,
      wfName,
      [
        {
          nodeId: "n1",
          agentId: agent.id,
          label: agent.name,
          positionX: 300,
          positionY: 200,
        },
      ]
    );

    // Save button should be available
    const saveBtn = page.locator("button", { hasText: "Save" });
    await expect(saveBtn).toBeVisible();
    await saveBtn.click();

    // Wait for save to complete (loader should disappear)
    await page.waitForTimeout(1000);

    // Verify via API
    const wfResp = await page.request.get(
      `/api/workflows/${workflowId}`
    );
    expect(wfResp.ok()).toBeTruthy();
    const wf = (await wfResp.json()) as { nodes: unknown[] };
    expect(wf.nodes.length).toBe(1);

    // Clean up
    await page.request.delete(`/api/workflows/${workflowId}`);
  });

  test("deletes workflow via toolbar", async ({ page }) => {
    const agentsResp = await page.request.get("/api/agents");
    const agents = (await agentsResp.json()) as Array<{
      id: string;
      name: string;
    }>;
    const agent = agents[0];
    const wfName = `Delete Test ${Date.now()}`;

    await createAndLoadWorkflow(
      page,
      wfName,
      [
        {
          nodeId: "n1",
          agentId: agent.id,
          label: agent.name,
          positionX: 300,
          positionY: 200,
        },
      ]
    );

    // Click delete button
    const deleteBtn = page.locator("button", { hasText: "Delete" });
    // Some UIs show a confirm dialog; handle both cases
    page.on("dialog", (dialog) => dialog.accept());
    await deleteBtn.click();
    await page.waitForTimeout(1000);

    // Verify workflow is removed from load list
    const loadBtn = page.locator("button", { hasText: "Load" });
    if (await loadBtn.isVisible()) {
      await loadBtn.click();
      await page.waitForTimeout(300);
      const entry = page.locator("button", { hasText: wfName });
      await expect(entry).not.toBeVisible({ timeout: 3_000 });
    }
  });

  test("execute button is enabled when workflow has nodes", async ({
    page,
  }) => {
    const agentsResp = await page.request.get("/api/agents");
    const agents = (await agentsResp.json()) as Array<{
      id: string;
      name: string;
    }>;
    const agent = agents[0];
    const wfName = `Execute Ready ${Date.now()}`;

    const workflowId = await createAndLoadWorkflow(
      page,
      wfName,
      [
        {
          nodeId: "n1",
          agentId: agent.id,
          label: agent.name,
          positionX: 300,
          positionY: 200,
        },
      ]
    );

    const executeBtn = page.locator("button", { hasText: "Execute" });
    await expect(executeBtn).toBeEnabled({ timeout: 5_000 });

    // Clean up
    await page.request.delete(`/api/workflows/${workflowId}`);
  });
});
