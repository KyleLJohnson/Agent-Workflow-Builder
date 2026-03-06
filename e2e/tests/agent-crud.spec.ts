import { test, expect, type Page } from "@playwright/test";

/**
 * Helper to wait for the app shell to fully render.
 */
async function waitForAppReady(page: Page) {
  await expect(
    page.getByRole("heading", { name: "Agents" })
  ).toBeVisible({ timeout: 15_000 });
}

test.describe("Agent CRUD operations", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);
  });

  test("lists built-in agents on load", async ({ page }) => {
    const agentCards = page.locator(
      "aside div[draggable='true'] .text-sm.font-medium.text-slate-200"
    );
    await expect(agentCards.first()).toBeVisible({ timeout: 10_000 });
    const count = await agentCards.count();
    expect(count).toBeGreaterThanOrEqual(1);
  });

  test("search filters agent list", async ({ page }) => {
    const searchInput = page.locator(
      'aside input[placeholder="Search agents..."]'
    );
    await expect(searchInput).toBeVisible();

    // Count agents before search
    const allCards = page.locator("aside div[draggable='true']");
    const beforeCount = await allCards.count();
    expect(beforeCount).toBeGreaterThan(0);

    // Search for a non-existent agent
    await searchInput.fill("zzz_nonexistent_agent_xyz");
    await page.waitForTimeout(300);

    // Should show "No agents match your search" or empty list
    const afterCount = await allCards.count();
    expect(afterCount).toBe(0);

    // Clear search restores agents
    await searchInput.clear();
    await page.waitForTimeout(300);
    const restoredCount = await allCards.count();
    expect(restoredCount).toBe(beforeCount);
  });

  test("opens agent editor in create mode", async ({ page }) => {
    const createBtn = page.locator("aside button", { hasText: "Create" });
    await expect(createBtn).toBeVisible();
    await createBtn.click();

    // Modal should appear with "Create Agent" title
    const modal = page.locator("div.fixed.inset-0.z-50");
    await expect(modal).toBeVisible();

    const title = modal.locator("h2", { hasText: "Create Agent" });
    await expect(title).toBeVisible();

    // Verify key form fields are present
    await expect(
      modal.locator('input[placeholder="My Custom Agent"]')
    ).toBeVisible();
    await expect(
      modal.locator(
        'textarea[placeholder="You are a helpful agent that..."]'
      )
    ).toBeVisible();
  });

  test("creates a custom agent", async ({ page }) => {
    const agentName = `E2E Test Agent ${Date.now()}`;

    // Open create dialog
    await page.locator("aside button", { hasText: "Create" }).click();
    const modal = page.locator("div.fixed.inset-0.z-50");
    await expect(modal).toBeVisible();

    // Fill form
    await modal
      .locator('input[placeholder="My Custom Agent"]')
      .fill(agentName);
    await modal
      .locator('input[placeholder="What does this agent do?"]')
      .fill("An agent created by E2E tests");
    await modal
      .locator(
        'textarea[placeholder="You are a helpful agent that..."]'
      )
      .fill("You are a test agent. Respond with 'hello' to any input.");

    // Save
    const saveBtn = modal.locator("button", { hasText: "Create" });
    await saveBtn.click();

    // Modal should close
    await expect(modal).not.toBeVisible({ timeout: 5_000 });

    // New agent should appear in list
    const newAgent = page.locator("aside div[draggable='true']", {
      hasText: agentName,
    });
    await expect(newAgent).toBeVisible({ timeout: 5_000 });

    // Clean up via API
    const resp = await page.request.get("/api/agents");
    const agents = (await resp.json()) as Array<{
      id: string;
      name: string;
    }>;
    const created = agents.find((a) => a.name === agentName);
    if (created) {
      await page.request.delete(`/api/agents/${created.id}`);
    }
  });

  test("opens built-in agent in view mode", async ({ page }) => {
    // Double-click first built-in agent
    const firstAgent = page
      .locator("aside div[draggable='true']")
      .first();
    await expect(firstAgent).toBeVisible();
    await firstAgent.dblclick();

    const modal = page.locator("div.fixed.inset-0.z-50");
    await expect(modal).toBeVisible();

    // Built-in agents should show "View Agent"
    const viewTitle = modal.locator("h2", { hasText: "View Agent" });
    await expect(viewTitle).toBeVisible();

    // Close button should be "Close" (not "Cancel")
    const closeBtn = modal.locator("button", { hasText: "Close" });
    await expect(closeBtn).toBeVisible();
    await closeBtn.click();
    await expect(modal).not.toBeVisible({ timeout: 3_000 });
  });

  test("edits and updates a custom agent", async ({ page }) => {
    const agentName = `E2E Edit Agent ${Date.now()}`;

    // Create agent via API
    const createResp = await page.request.post("/api/agents", {
      data: {
        name: agentName,
        description: "Original description",
        systemInstructions: "Original instructions",
        category: "custom",
      },
    });
    expect(createResp.ok()).toBeTruthy();
    const agent = (await createResp.json()) as { id: string };

    // Reload to pick up the new agent
    await page.reload();
    await waitForAppReady(page);

    // Find and double-click the custom agent
    const agentCard = page.locator("aside div[draggable='true']", {
      hasText: agentName,
    });
    await expect(agentCard).toBeVisible({ timeout: 5_000 });
    await agentCard.dblclick();

    const modal = page.locator("div.fixed.inset-0.z-50");
    await expect(modal).toBeVisible();

    // Should show "Edit Agent"
    const editTitle = modal.locator("h2", { hasText: "Edit Agent" });
    await expect(editTitle).toBeVisible();

    // Update description
    const descInput = modal.locator(
      'input[placeholder="What does this agent do?"]'
    );
    await descInput.clear();
    await descInput.fill("Updated description via Playwright");

    const updateBtn = modal.locator("button", { hasText: "Update" });
    await updateBtn.click();
    await expect(modal).not.toBeVisible({ timeout: 5_000 });

    // Verify updated description appears
    const updatedCard = page.locator("aside div[draggable='true']", {
      hasText: "Updated description via Playwright",
    });
    await expect(updatedCard).toBeVisible({ timeout: 5_000 });

    // Clean up
    await page.request.delete(`/api/agents/${agent.id}`);
  });

  test("deletes a custom agent via API", async ({ page }) => {
    const agentName = `E2E Delete Agent ${Date.now()}`;

    // Create agent via API
    const createResp = await page.request.post("/api/agents", {
      data: {
        name: agentName,
        description: "Will be deleted",
        systemInstructions: "Test",
        category: "custom",
      },
    });
    expect(createResp.ok()).toBeTruthy();
    const agent = (await createResp.json()) as { id: string };

    // Reload
    await page.reload();
    await waitForAppReady(page);

    // Verify it exists
    const agentCard = page.locator("aside div[draggable='true']", {
      hasText: agentName,
    });
    await expect(agentCard).toBeVisible({ timeout: 5_000 });

    // Delete via API
    const delResp = await page.request.delete(
      `/api/agents/${agent.id}`
    );
    expect(delResp.ok()).toBeTruthy();

    // Reload and verify gone
    await page.reload();
    await waitForAppReady(page);
    await expect(agentCard).not.toBeVisible({ timeout: 5_000 });
  });
});
