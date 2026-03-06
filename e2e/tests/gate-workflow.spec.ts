import { test, expect, type Page } from "@playwright/test";

/**
 * Helper to wait for the app shell.
 */
async function waitForAppReady(page: Page) {
  await expect(
    page.getByRole("heading", { name: "Agents" })
  ).toBeVisible({ timeout: 15_000 });
}

test.describe("Gate workflow interactions", () => {
  let workflowId: string;
  let agentId: string;

  test.beforeEach(async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);

    // Get an agent to use
    const agentsResp = await page.request.get("/api/agents");
    const agents = (await agentsResp.json()) as Array<{
      id: string;
      name: string;
    }>;
    agentId = agents[0].id;
  });

  test.afterEach(async ({ page }) => {
    if (workflowId) {
      await page.request.delete(`/api/workflows/${workflowId}`).catch(() => {});
    }
  });

  test("creates workflow with gate node via API", async ({ page }) => {
    const wfName = `Gate Workflow ${Date.now()}`;

    const createResp = await page.request.post("/api/workflows", {
      data: {
        name: wfName,
        description: "Gate workflow for E2E test",
        nodes: [
          {
            nodeId: "agent-1",
            agentId: agentId,
            label: "First Agent",
            positionX: 200,
            positionY: 200,
          },
          {
            nodeId: "gate-1",
            nodeType: "gate",
            label: "Approval Gate",
            positionX: 500,
            positionY: 200,
            gateConfig: {
              gateType: "Approval",
              instructions: "Please approve this output",
            },
          },
          {
            nodeId: "agent-2",
            agentId: agentId,
            label: "Second Agent",
            positionX: 800,
            positionY: 200,
          },
        ],
        edges: [
          { sourceNodeId: "agent-1", targetNodeId: "gate-1" },
          { sourceNodeId: "gate-1", targetNodeId: "agent-2" },
        ],
      },
    });
    expect(createResp.ok()).toBeTruthy();
    const wf = (await createResp.json()) as { id: string };
    workflowId = wf.id;

    // Load the workflow
    await page.reload();
    await waitForAppReady(page);
    const loadBtn = page.locator("button", { hasText: "Load" });
    await loadBtn.click();
    await page.waitForTimeout(300);
    await page.locator("button", { hasText: wfName }).click();
    await page.waitForTimeout(500);

    // Verify gate node appears on canvas
    const gateNode = page.locator(".react-flow__node").filter({
      hasText: /Approval/i,
    });
    await expect(gateNode).toBeVisible({ timeout: 5_000 });

    // Verify there are 3 nodes total (2 agents + 1 gate)
    const allNodes = page.locator(".react-flow__node");
    await expect(allNodes).toHaveCount(3, { timeout: 5_000 });
  });

  test("gate node renders with correct visual style", async ({
    page,
  }) => {
    const wfName = `Gate Style ${Date.now()}`;

    const createResp = await page.request.post("/api/workflows", {
      data: {
        name: wfName,
        nodes: [
          {
            nodeId: "gate-review",
            nodeType: "gate",
            label: "Review Gate",
            positionX: 300,
            positionY: 200,
            gateConfig: {
              gateType: "ReviewAndEdit",
              instructions: "Review the output carefully",
            },
          },
        ],
        edges: [],
      },
    });
    expect(createResp.ok()).toBeTruthy();
    const wf = (await createResp.json()) as { id: string };
    workflowId = wf.id;

    await page.reload();
    await waitForAppReady(page);

    const loadBtn = page.locator("button", { hasText: "Load" });
    await loadBtn.click();
    await page.waitForTimeout(300);
    await page.locator("button", { hasText: wfName }).click();
    await page.waitForTimeout(500);

    // Verify the gate node shows "REVIEW & EDIT" badge
    const gateNode = page.locator(".react-flow__node").first();
    await expect(gateNode).toBeVisible({ timeout: 5_000 });
    await expect(
      gateNode.locator("text=REVIEW & EDIT")
    ).toBeVisible();
  });

  test("workflow with gate has correct edge count", async ({ page }) => {
    const wfName = `Gate Edges ${Date.now()}`;

    const createResp = await page.request.post("/api/workflows", {
      data: {
        name: wfName,
        nodes: [
          {
            nodeId: "n1",
            agentId: agentId,
            label: "Agent",
            positionX: 200,
            positionY: 200,
          },
          {
            nodeId: "g1",
            nodeType: "gate",
            label: "Gate",
            positionX: 500,
            positionY: 200,
            gateConfig: {
              gateType: "Approval",
              instructions: "Approve",
            },
          },
        ],
        edges: [{ sourceNodeId: "n1", targetNodeId: "g1" }],
      },
    });
    expect(createResp.ok()).toBeTruthy();
    const wf = (await createResp.json()) as { id: string };
    workflowId = wf.id;

    await page.reload();
    await waitForAppReady(page);
    const loadBtn = page.locator("button", { hasText: "Load" });
    await loadBtn.click();
    await page.waitForTimeout(300);
    await page.locator("button", { hasText: wfName }).click();
    await page.waitForTimeout(500);

    const edges = page.locator(".react-flow__edge");
    await expect(edges).toHaveCount(1, { timeout: 5_000 });
  });
});
