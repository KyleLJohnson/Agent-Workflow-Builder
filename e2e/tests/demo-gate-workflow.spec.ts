import { test, expect, type Page, type Locator } from "@playwright/test";

// ─── Pacing constant: milliseconds to pause between visual steps ───
const STEP_PAUSE = 1200;

test.use({
  baseURL: "http://localhost:5173",
  video: "on",
  headless: false,
  viewport: { width: 1440, height: 900 },
});

async function waitForAppReady(page: Page) {
  await expect(
    page.getByRole("heading", { name: "Agents" })
  ).toBeVisible({ timeout: 15_000 });
}

/**
 * Simulate a visible drag-and-drop using the HTML5 DnD API.
 * Animates the mouse from source card to target position for the video,
 * then dispatches synthetic dragstart → dragover → drop events.
 */
async function visualDragDrop(
  page: Page,
  source: Locator,
  target: Locator,
  targetX: number,
  targetY: number,
  dataTransferItems: Array<{ type: string; data: string }>,
  steps = 20
) {
  const box = await source.boundingBox();
  expect(box).toBeTruthy();
  const startX = box!.x + box!.width / 2;
  const startY = box!.y + box!.height / 2;

  // Animate mouse from source to target for the video
  await page.mouse.move(startX, startY, { steps: 5 });
  await page.waitForTimeout(400);
  await page.mouse.move(targetX, targetY, { steps });
  await page.waitForTimeout(300);

  // Dispatch synthetic HTML5 DnD events on the target element
  await target.evaluate(
    (el, { tx, ty, items }) => {
      const dataTransfer = new DataTransfer();
      for (const item of items) {
        dataTransfer.setData(item.type, item.data);
      }
      dataTransfer.effectAllowed = "move";

      el.dispatchEvent(
        new DragEvent("dragover", {
          bubbles: true,
          cancelable: true,
          dataTransfer,
          clientX: tx,
          clientY: ty,
        })
      );
      el.dispatchEvent(
        new DragEvent("drop", {
          bubbles: true,
          cancelable: true,
          dataTransfer,
          clientX: tx,
          clientY: ty,
        })
      );
    },
    { tx: targetX, ty: targetY, items: dataTransferItems }
  );
  await page.waitForTimeout(500);
}

/**
 * Connect two React Flow nodes by dragging from the source handle to the target handle.
 * Temporarily enlarges handles so the mouse reliably hits them (elementFromPoint).
 * Falls back to programmatic connection if the drag doesn't produce an edge.
 */
async function connectNodes(page: Page, sourceNode: Locator, targetNode: Locator) {
  const sourceHandle = sourceNode.locator('.react-flow__handle.source').first();
  const targetHandle = targetNode.locator('.react-flow__handle.target').first();

  await expect(sourceHandle).toBeVisible({ timeout: 3_000 });
  await expect(targetHandle).toBeVisible({ timeout: 3_000 });

  // Enlarge handles temporarily so mouse events land on them reliably
  await page.evaluate(() => {
    document.querySelectorAll<HTMLElement>('.react-flow__handle').forEach((h) => {
      h.style.width = '24px';
      h.style.height = '24px';
      h.style.zIndex = '9999';
    });
  });
  await page.waitForTimeout(200);

  const srcBox = await sourceHandle.boundingBox();
  const tgtBox = await targetHandle.boundingBox();
  expect(srcBox).toBeTruthy();
  expect(tgtBox).toBeTruthy();

  const srcX = srcBox!.x + srcBox!.width / 2;
  const srcY = srcBox!.y + srcBox!.height / 2;
  const tgtX = tgtBox!.x + tgtBox!.width / 2;
  const tgtY = tgtBox!.y + tgtBox!.height / 2;

  const edgesBefore = await page.locator(".react-flow__edge").count();

  // Animate mouse from source handle to target handle for the video
  await page.mouse.move(srcX, srcY, { steps: 5 });
  await page.waitForTimeout(400);
  await page.mouse.down();
  await page.waitForTimeout(300);
  await page.mouse.move(tgtX, tgtY, { steps: 25 });
  await page.waitForTimeout(400);
  await page.mouse.up();
  await page.waitForTimeout(600);

  // Restore handles to normal size
  await page.evaluate(() => {
    document.querySelectorAll<HTMLElement>('.react-flow__handle').forEach((h) => {
      h.style.width = '';
      h.style.height = '';
      h.style.zIndex = '';
    });
  });
  await page.waitForTimeout(200);

  // If the drag didn't create an edge, fall back to programmatic connection
  const edgesAfter = await page.locator(".react-flow__edge").count();
  if (edgesAfter <= edgesBefore) {
    const sourceId = await sourceHandle.evaluate((el) => el.getAttribute("data-nodeid"));
    const targetId = await targetHandle.evaluate((el) => el.getAttribute("data-nodeid"));
    const sourceHandleId = await sourceHandle.evaluate((el) => el.getAttribute("data-handleid"));
    const targetHandleId = await targetHandle.evaluate((el) => el.getAttribute("data-handleid"));

    await page.evaluate(
      ({ src, tgt, srcH, tgtH }) => {
        // Dispatch a synthetic connection event via the React Flow store
        // React Flow stores its instance on the wrapper element
        const rfWrapper = document.querySelector(".react-flow");
        if (!rfWrapper) return;
        // Trigger onConnect by dispatching a custom event that React Flow
        // can pick up, or by directly invoking the internal store.
        // Instead, we simulate the full pointer sequence on the exact handle elements.
        const srcEl = document.querySelector(`[data-nodeid="${src}"].react-flow__handle.source`);
        const tgtEl = document.querySelector(`[data-nodeid="${tgt}"].react-flow__handle.target`);
        if (!srcEl || !tgtEl) return;

        const srcRect = srcEl.getBoundingClientRect();
        const tgtRect = tgtEl.getBoundingClientRect();

        // React Flow listens for mousedown on the handle
        srcEl.dispatchEvent(new MouseEvent("mousedown", {
          bubbles: true, cancelable: true,
          clientX: srcRect.x + srcRect.width / 2,
          clientY: srcRect.y + srcRect.height / 2,
        }));

        // Then mousemove on the document
        document.dispatchEvent(new MouseEvent("mousemove", {
          bubbles: true, cancelable: true,
          clientX: tgtRect.x + tgtRect.width / 2,
          clientY: tgtRect.y + tgtRect.height / 2,
        }));

        // Then mouseup on the document — React Flow uses elementFromPoint
        document.dispatchEvent(new MouseEvent("mouseup", {
          bubbles: true, cancelable: true,
          clientX: tgtRect.x + tgtRect.width / 2,
          clientY: tgtRect.y + tgtRect.height / 2,
        }));
      },
      { src: sourceId, tgt: targetId, srcH: sourceHandleId, tgtH: targetHandleId }
    );
    await page.waitForTimeout(600);
  }
}

/**
 * Scroll the agent-output panel so that the given section scrolls fully into
 * and through view. This targets the parent scrollable container
 * [data-testid="agent-output"] because individual sections have overflow:hidden.
 */
async function scrollThroughSection(
  page: Page,
  section: Locator,
  durationMs = 4000
) {
  const outputPanel = page.locator('[data-testid="agent-output"]');
  await expect(outputPanel).toBeVisible({ timeout: 3_000 });

  // Scroll so the section's top is at the top of the panel
  await section.evaluate((el) => el.scrollIntoView({ block: "start", behavior: "smooth" }));
  await page.waitForTimeout(600);

  // Now calculate how far we need to scroll within the panel to reveal
  // the full section content
  const sectionHeight = await section.evaluate((el) => el.scrollHeight);
  const panelClientHeight = await outputPanel.evaluate((el) => el.clientHeight);
  const startScrollTop = await outputPanel.evaluate((el) => el.scrollTop);

  // We want to scroll enough that the bottom of the section is visible
  const scrollDistance = Math.max(0, sectionHeight - panelClientHeight + 40);
  if (scrollDistance <= 0) {
    // Section fits entirely — just pause to let viewer read
    await page.waitForTimeout(durationMs);
    return;
  }

  const scrollSteps = 20;
  const stepDelay = durationMs / scrollSteps;
  for (let i = 1; i <= scrollSteps; i++) {
    const pos = startScrollTop + (scrollDistance / scrollSteps) * i;
    await outputPanel.evaluate(
      (el, p) => el.scrollTo({ top: p, behavior: "smooth" }),
      pos
    );
    await page.waitForTimeout(stepDelay);
  }
  await page.waitForTimeout(500);
}

test.describe("Demo: Developer → Gate → Code Reviewer", () => {
  test.afterEach(async ({ page }) => {
    // Clean up: delete all "Demo Gate" workflows via API
    const resp = await page.request.get("/api/workflows").catch(() => null);
    if (resp?.ok()) {
      const workflows = (await resp.json()) as Array<{ id: string; name: string }>;
      for (const wf of workflows) {
        if (wf.name.startsWith("Demo Gate")) {
          await page.request.delete(`/api/workflows/${wf.id}`).catch(() => {});
        }
      }
    }
  });

  test("execute gated workflow and show both agent outputs", async ({ page }) => {
    test.setTimeout(300_000);

    // ═══════════════════════════════════════════════════════════
    // STEP 1: Open app and create a new workflow
    // ═══════════════════════════════════════════════════════════
    await page.goto("/");
    await waitForAppReady(page);
    await page.waitForTimeout(STEP_PAUSE);

    // Click "New Workflow" to get to the canvas
    const newWfBtn = page.locator("button", { hasText: "New Workflow" });
    await expect(newWfBtn).toBeVisible({ timeout: 5_000 });
    await newWfBtn.click();

    // Wait for the canvas to appear (React state transition to designer view)
    const canvas = page.locator(".react-flow");
    await expect(canvas).toBeVisible({ timeout: 10_000 });
    await page.waitForTimeout(STEP_PAUSE);

    // Name the workflow
    const nameInput = page.locator('input[placeholder="Untitled Workflow"]');
    await expect(nameInput).toBeVisible({ timeout: 5_000 });
    await nameInput.click();
    await nameInput.fill("");
    await page.keyboard.type("Demo Gate Workflow", { delay: 60 });
    await page.waitForTimeout(STEP_PAUSE);

    console.log("Canvas ready — starting drag-and-drop");

    // Fetch agent definitions from API for dataTransfer payloads
    const agentsResp = await page.request.get("/api/agents");
    const agents = (await agentsResp.json()) as Array<Record<string, unknown>>;
    const developerAgent = agents.find((a) => a.name === "Developer");
    const codeReviewerAgent = agents.find((a) => a.name === "Code Reviewer");
    expect(developerAgent).toBeTruthy();
    expect(codeReviewerAgent).toBeTruthy();

    // Get the canvas bounding box for drop target coordinates
    const canvasBox = await canvas.boundingBox();
    expect(canvasBox).toBeTruthy();

    // ═══════════════════════════════════════════════════════════
    // STEP 2: Drag Developer agent onto the canvas
    // ═══════════════════════════════════════════════════════════
    const developerCard = page
      .locator("aside")
      .locator("div[draggable='true']", { hasText: "Developer" });
    await expect(developerCard).toBeVisible({ timeout: 10_000 });

    // Scroll the agent panel to make Developer visible if needed
    await developerCard.scrollIntoViewIfNeeded();
    await page.waitForTimeout(500);

    // Place nodes at 1/4, 1/2, 3/4 across the canvas, all at the same Y
    const centerY = canvasBox!.y + canvasBox!.height * 0.4;
    const dropX1 = canvasBox!.x + canvasBox!.width * 0.2;
    const dropY1 = centerY;
    await visualDragDrop(page, developerCard, canvas, dropX1, dropY1, [
      { type: "application/agentworkflow", data: JSON.stringify(developerAgent) },
    ]);
    await page.waitForTimeout(STEP_PAUSE);

    // Verify Developer node appeared
    const canvasNodes = page.locator(".react-flow__node");
    await expect(canvasNodes).toHaveCount(1, { timeout: 5_000 });
    console.log("Developer node dropped on canvas");

    // ═══════════════════════════════════════════════════════════
    // STEP 3: Drag Approval Gate onto the canvas
    // ═══════════════════════════════════════════════════════════
    const gateToolbarItem = page.locator("div[draggable]", { hasText: "Gate" })
      .filter({ has: page.locator("svg") });
    await expect(gateToolbarItem).toBeVisible({ timeout: 5_000 });

    const dropX2 = canvasBox!.x + canvasBox!.width * 0.5;
    const dropY2 = centerY;
    await visualDragDrop(page, gateToolbarItem, canvas, dropX2, dropY2, [
      { type: "application/gate-node", data: "Approval" },
    ]);
    await page.waitForTimeout(STEP_PAUSE);

    // Verify: 2 nodes
    await expect(canvasNodes).toHaveCount(2, { timeout: 5_000 });
    console.log("Approval Gate dropped on canvas");

    // ═══════════════════════════════════════════════════════════
    // STEP 4: Drag Code Reviewer agent onto the canvas
    // ═══════════════════════════════════════════════════════════
    const codeReviewerCard = page
      .locator("aside")
      .locator("div[draggable='true']", { hasText: "Code Reviewer" });
    await expect(codeReviewerCard).toBeVisible({ timeout: 10_000 });
    await codeReviewerCard.scrollIntoViewIfNeeded();
    await page.waitForTimeout(500);

    const dropX3 = canvasBox!.x + canvasBox!.width * 0.8;
    const dropY3 = centerY;
    await visualDragDrop(page, codeReviewerCard, canvas, dropX3, dropY3, [
      { type: "application/agentworkflow", data: JSON.stringify(codeReviewerAgent) },
    ]);
    await page.waitForTimeout(STEP_PAUSE);

    // Verify: 3 nodes
    await expect(canvasNodes).toHaveCount(3, { timeout: 5_000 });
    console.log("Code Reviewer node dropped on canvas");

    // ═══════════════════════════════════════════════════════════
    // STEP 5: Connect Developer → Gate
    // ═══════════════════════════════════════════════════════════
    const devNode = page.locator(".react-flow__node").filter({ hasText: "Developer" });
    const gateNode = page.locator(".react-flow__node").filter({ hasText: /Approval/i });
    const crNode = page.locator(".react-flow__node").filter({ hasText: "Code Reviewer" });

    await connectNodes(page, devNode, gateNode);
    await page.waitForTimeout(STEP_PAUSE);

    await expect(page.locator(".react-flow__edge")).toHaveCount(1, { timeout: 5_000 });
    console.log("Edge 1 created: Developer → Gate");

    // ═══════════════════════════════════════════════════════════
    // STEP 6: Connect Gate → Code Reviewer
    // ═══════════════════════════════════════════════════════════
    await connectNodes(page, gateNode, crNode);
    await page.waitForTimeout(STEP_PAUSE);

    await expect(page.locator(".react-flow__edge")).toHaveCount(2, { timeout: 5_000 });
    console.log("Edge 2 created: Gate → Code Reviewer");

    // ═══════════════════════════════════════════════════════════
    // STEP 7: Save the workflow
    // ═══════════════════════════════════════════════════════════
    const saveBtn = page.locator("button", { hasText: "Save" });
    await expect(saveBtn).toBeVisible();
    await saveBtn.click();
    await page.waitForTimeout(STEP_PAUSE);
    console.log("Workflow saved");

    // ═══════════════════════════════════════════════════════════
    // STEP 7b: Straighten the workflow layout via API
    // (fitView causes screen-to-flow coordinate drift between drops)
    // ═══════════════════════════════════════════════════════════
    const wfListResp = await page.request.get("/api/workflows");
    const wfList = (await wfListResp.json()) as Array<{ id: string; name: string }>;
    const savedWf = wfList.find((w) => w.name === "Demo Gate Workflow");
    expect(savedWf).toBeTruthy();

    const wfResp = await page.request.get(`/api/workflows/${savedWf!.id}`);
    const wfData = await wfResp.json();
    const wfNodes = wfData.nodes as Array<{
      nodeId: string; label: string; positionX: number; positionY: number;
      [k: string]: unknown;
    }>;

    // Sort nodes into pipeline order: Developer → Gate → Code Reviewer
    const nodeOrder = ["Developer", "Approval Gate", "Code Reviewer"];
    const spacing = 350;
    const startX = 50;
    const lineY = 200;
    for (const n of wfNodes) {
      const idx = nodeOrder.indexOf(n.label);
      if (idx >= 0) {
        n.positionX = startX + idx * spacing;
        n.positionY = lineY;
      }
    }

    await page.request.put(`/api/workflows/${savedWf!.id}`, {
      data: { ...wfData, nodes: wfNodes },
    });
    console.log("Node positions straightened via API");

    // Reload the workflow so the canvas reflects the new positions
    const loadBtn = page.locator("button", { hasText: "Load" });
    await loadBtn.click();
    await page.waitForTimeout(500);
    const wfMenuItem = page.locator("button", { hasText: "Demo Gate Workflow" });
    await expect(wfMenuItem).toBeVisible({ timeout: 3_000 });
    await wfMenuItem.click();
    await page.waitForTimeout(STEP_PAUSE);

    // Click "Fit View" to ensure all three nodes are visible on screen
    const fitViewBtn = page.locator('button[title="Fit View"]');
    await expect(fitViewBtn).toBeVisible({ timeout: 3_000 });
    await fitViewBtn.click();
    await page.waitForTimeout(STEP_PAUSE);
    console.log("Workflow reloaded with straightened layout — all nodes in view");

    // ═══════════════════════════════════════════════════════════
    // STEP 8: Type and send the message
    // ═══════════════════════════════════════════════════════════
    const msgInput = page.locator('input[placeholder="Type a message to send to the workflow..."]');
    await expect(msgInput).toBeEnabled({ timeout: 5_000 });
    await msgInput.click();
    await page.keyboard.type("Write C# code to add two numbers", { delay: 50 });
    await page.waitForTimeout(800);

    await page.locator("button", { hasText: "Send" }).click();
    console.log("Sent: Write C# code to add two numbers");
    await page.waitForTimeout(STEP_PAUSE);

    // ═══════════════════════════════════════════════════════════
    // STEP 9: Wait for execution to start
    // ═══════════════════════════════════════════════════════════
    const executionStarted = page.locator("text=Execution Started");
    const agentStepStarted = page.locator("text=Agent Step Started");
    await expect(executionStarted.or(agentStepStarted).first()).toBeVisible({ timeout: 30_000 });
    console.log("Execution started — Developer agent running...");
    await page.waitForTimeout(STEP_PAUSE);

    // ═══════════════════════════════════════════════════════════
    // STEP 10: Wait for gate card (Developer must finish first)
    // ═══════════════════════════════════════════════════════════
    const gateCard = page.locator('[data-testid="agent-output"]')
      .locator("div.border-l-blue-500")
      .filter({ hasText: /Approval/i });
    await expect(gateCard).toBeVisible({ timeout: 90_000 });
    console.log("Gate card appeared — Developer agent has finished");
    await page.waitForTimeout(STEP_PAUSE);

    // ═══════════════════════════════════════════════════════════
    // STEP 11: Enlarge panel, expand Developer output, scroll through it
    // ═══════════════════════════════════════════════════════════
    // Make the execution panel taller so scrolling is visible in the video
    await page.evaluate(() => {
      const panel = document.querySelector('[data-testid="agent-output"]')?.closest('.bg-slate-900');
      if (panel) {
        (panel as HTMLElement).style.height = '500px';
        panel.classList.remove('h-80');
      }
    });
    await page.waitForTimeout(400);

    const devSection = page.locator('[data-testid="agent-section-Developer"]');
    await expect(devSection).toBeVisible({ timeout: 5_000 });
    await devSection.locator("button").first().click();
    await page.waitForTimeout(800);

    // Scroll the agent-output panel through the Developer section
    await scrollThroughSection(page, devSection, 5000);

    const devOutput = await devSection.locator("p.text-slate-200").allInnerTexts();
    const devText = devOutput.join("\n");
    console.log("\n" + "=".repeat(60));
    console.log("  DEVELOPER AGENT OUTPUT");
    console.log("=".repeat(60));
    console.log(devText);
    console.log("=".repeat(60) + "\n");
    await page.waitForTimeout(STEP_PAUSE);

    // ═══════════════════════════════════════════════════════════
    // STEP 12: Approve the gate
    // ═══════════════════════════════════════════════════════════
    // Scroll gate card into view first
    await gateCard.scrollIntoViewIfNeeded();
    await page.waitForTimeout(800);

    const approveBtn = gateCard.locator("button", { hasText: "Approve" });
    await expect(approveBtn).toBeVisible();
    // Hover over approve button before clicking for visual effect
    await approveBtn.hover();
    await page.waitForTimeout(600);
    await approveBtn.click();
    console.log("Gate approved — Code Reviewer agent starting...");
    await page.waitForTimeout(STEP_PAUSE);

    // ═══════════════════════════════════════════════════════════
    // STEP 13: Wait for execution to complete
    // ═══════════════════════════════════════════════════════════
    const completedLabel = page.locator("span.text-green-500", { hasText: "Execution Completed" });
    const errorLabel = page.locator("span.text-red-400", { hasText: "Error" });
    await expect(completedLabel.or(errorLabel).first()).toBeVisible({ timeout: 120_000 });

    const didComplete = (await completedLabel.count()) > 0;
    console.log(`Execution finished — ${didComplete ? "completed" : "errored"}`);
    await page.waitForTimeout(STEP_PAUSE);

    // ═══════════════════════════════════════════════════════════
    // STEP 14: Expand Code Reviewer output and scroll through it
    // ═══════════════════════════════════════════════════════════
    const crSection = page.locator('[data-testid="agent-section-Code Reviewer"]');
    await expect(crSection).toBeVisible({ timeout: 10_000 });
    await crSection.locator("button").first().click();
    await page.waitForTimeout(800);

    // Scroll the agent-output panel through the Code Reviewer section
    await scrollThroughSection(page, crSection, 6000);

    const crOutput = await crSection.locator("p.text-slate-200").allInnerTexts();
    const crText = crOutput.join("\n");
    console.log("\n" + "=".repeat(60));
    console.log("  CODE REVIEWER AGENT OUTPUT");
    console.log("=".repeat(60));
    console.log(crText);
    console.log("=".repeat(60) + "\n");

    // ═══════════════════════════════════════════════════════════
    // STEP 15: Final pause to show completed state
    // ═══════════════════════════════════════════════════════════
    expect(devText.trim().length).toBeGreaterThan(0);
    expect(crText.trim().length).toBeGreaterThan(0);

    await page.waitForTimeout(3000);
    console.log("Demo complete!");
  });
});
