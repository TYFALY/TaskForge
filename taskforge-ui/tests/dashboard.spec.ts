import { test, expect } from "@playwright/test";

test.describe("TaskForge Dashboard Smoke Tests", () => {
  test("dashboard loads and displays header", async ({ page }) => {
    await page.goto("/");
    await expect(page.locator("text=TaskForge")).toBeVisible({ timeout: 15000 });
  });

  test("job history table is visible", async ({ page }) => {
    await page.goto("/");
    await expect(page.locator("table")).toBeVisible({ timeout: 15000 });
  });

  test("enqueue job via API succeeds", async ({ page }) => {
    const response = await page.request.post("/api/v1/jobs/enqueue", {
      headers: {
        "Content-Type": "application/json",
        "X-TaskForge-Key": "taskforge-dev-key-12345"
      },
      data: JSON.stringify({
        queueName: "default",
        payload: JSON.stringify({ test: "playwright-smoke-test" }),
        maxRetries: 3
      })
    });

    expect(response.status()).toBe(202);
    const jobData = await response.json();
    expect(jobData.jobId).toBeDefined();
  });

  test("job status transitions to Completed", async ({ page }) => {
    const response = await page.request.post("/api/v1/jobs/enqueue", {
      headers: {
        "Content-Type": "application/json",
        "X-TaskForge-Key": "taskforge-dev-key-12345"
      },
      data: JSON.stringify({
        queueName: "default",
        payload: JSON.stringify({ test: "status-transition-test" }),
        maxRetries: 3
      })
    });

    expect(response.status()).toBe(202);
    const jobData = await response.json();
    expect(jobData.jobId).toBeDefined();

    // Poll for job completion (max 30 seconds)
    let completed = false;
    for (let i = 0; i < 30; i++) {
      const statusResponse = await page.request.get(`/api/v1/jobs/${jobData.jobId}`);
      const statusData = await statusResponse.json();
      if (statusData.status === "Completed") {
        completed = true;
        break;
      }
      if (statusData.status === "Failed") {
        break;
      }
      await page.waitForTimeout(1000);
    }

    expect(completed).toBe(true);
  });
});