import { test, expect, type Page } from "@playwright/test";

/**
 * Helper to wait for the app shell.
 */
async function waitForAppReady(page: Page) {
  await expect(
    page.getByRole("heading", { name: "Agents" })
  ).toBeVisible({ timeout: 15_000 });
}

test.describe("MCP Settings panel", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
    await waitForAppReady(page);
  });

  test("opens MCP settings from agent editor", async ({ page }) => {
    // Open agent create dialog
    const createBtn = page.locator("aside button", { hasText: "Create" });
    await createBtn.click();

    const modal = page.locator("div.fixed.inset-0.z-50");
    await expect(modal).toBeVisible();

    // Look for "Manage" button in MCP Servers section
    const manageBtn = modal.locator("button", { hasText: "Manage" });
    await expect(manageBtn).toBeVisible();
    await manageBtn.click();

    // MCP Settings modal should appear
    const mcpTitle = page.locator("text=MCP Servers").first();
    await expect(mcpTitle).toBeVisible({ timeout: 5_000 });
  });

  test("shows Add Server button in MCP panel", async ({ page }) => {
    // Open agent create dialog, then MCP settings
    await page.locator("aside button", { hasText: "Create" }).click();
    const modal = page.locator("div.fixed.inset-0.z-50");
    await expect(modal).toBeVisible();

    const manageBtn = modal.locator("button", { hasText: "Manage" });
    await manageBtn.click();
    await page.waitForTimeout(500);

    // "Add Server" button should be visible
    const addBtn = page.locator("button", { hasText: "Add Server" });
    await expect(addBtn).toBeVisible({ timeout: 5_000 });
  });

  test("opens server form when clicking Add Server", async ({ page }) => {
    await page.locator("aside button", { hasText: "Create" }).click();
    const modal = page.locator("div.fixed.inset-0.z-50");
    await expect(modal).toBeVisible();

    await modal.locator("button", { hasText: "Manage" }).click();
    await page.waitForTimeout(500);

    await page.locator("button", { hasText: "Add Server" }).click();
    await page.waitForTimeout(300);

    // Server form fields should be visible
    const nameInput = page.locator(
      'input[placeholder="My MCP Server"]'
    );
    await expect(nameInput).toBeVisible({ timeout: 5_000 });

    // Transport select should be present
    const transportLabel = page.locator("label", {
      hasText: "Transport",
    });
    await expect(transportLabel).toBeVisible();
  });

  test("server form validates required name field", async ({ page }) => {
    await page.locator("aside button", { hasText: "Create" }).click();
    const modal = page.locator("div.fixed.inset-0.z-50");
    await expect(modal).toBeVisible();

    await modal.locator("button", { hasText: "Manage" }).click();
    await page.waitForTimeout(500);
    await page.locator("button", { hasText: "Add Server" }).click();
    await page.waitForTimeout(300);

    // Try to save without filling name
    const saveBtn = page.locator("button", { hasText: "Add" }).last();
    await saveBtn.click();

    // Should show an error or the form should not submit
    // The browser's built-in validation should prevent submission
    // or an error message should appear
    const nameInput = page.locator(
      'input[placeholder="My MCP Server"]'
    );
    const isRequired =
      (await nameInput.getAttribute("required")) !== null;
    expect(isRequired).toBeTruthy();
  });

  test("cancel button closes server form", async ({ page }) => {
    await page.locator("aside button", { hasText: "Create" }).click();
    const modal = page.locator("div.fixed.inset-0.z-50");
    await expect(modal).toBeVisible();

    await modal.locator("button", { hasText: "Manage" }).click();
    await page.waitForTimeout(500);
    await page.locator("button", { hasText: "Add Server" }).click();
    await page.waitForTimeout(300);

    // Verify form is open
    const nameInput = page.locator(
      'input[placeholder="My MCP Server"]'
    );
    await expect(nameInput).toBeVisible({ timeout: 3_000 });

    // Cancel
    const cancelBtn = page.locator("button", { hasText: "Cancel" });
    await cancelBtn.click();

    // Form should close (name input hidden)
    await expect(nameInput).not.toBeVisible({ timeout: 3_000 });
  });

  test("MCP servers from API are listed", async ({ page }) => {
    // Check if there are any existing MCP servers via API
    const mcpResp = await page.request.get("/api/mcp");
    const mcpServers = (await mcpResp.json()) as Array<{
      id: string;
      name: string;
    }>;

    await page.locator("aside button", { hasText: "Create" }).click();
    const modal = page.locator("div.fixed.inset-0.z-50");
    await expect(modal).toBeVisible();

    await modal.locator("button", { hasText: "Manage" }).click();
    await page.waitForTimeout(500);

    if (mcpServers.length > 0) {
      // Should see server cards
      const serverCards = page.locator(
        ".flex.items-center.gap-3.p-3.rounded-xl.border"
      );
      await expect(serverCards.first()).toBeVisible({ timeout: 5_000 });
    }
  });
});
