import { expect, test } from "@playwright/test";

import { getApproved, signUp, waitForSandbox } from "./support";

test("an approved firm in the sandbox goes live by paying, and buys more slots", async ({ page }) => {
  await signUp(page, "Billing E2E Firm", "billing-e2e-firm");
  await waitForSandbox(page);
  await getApproved(page, "billing-e2e-firm");

  // The firm comes back after our answer, and the page opens on the last step.
  await page.goto(new URL("/admin/go-live", page.url()).href);
  await expect(page.getByRole("heading", { name: "Go live" })).toBeVisible();
  await expect(page.getByRole("button", { name: /^Slots and payment/ })).toHaveAttribute("aria-current", "step");
  await expect(page.getByText("Package with 25 slots: 500.00 USD per month")).toBeVisible();
  await page.getByLabel("Slots", { exact: true }).fill("30");
  await expect(page.getByRole("cell", { name: "Startup fee, less the deposit of 200.00 USD" })).toBeVisible();
  await expect(page.getByRole("cell", { name: /^Package with 25 slots, / })).toBeVisible();
  await expect(page.getByRole("cell", { name: /^5 extra slots, / })).toBeVisible();
  await expect(page.getByRole("cell", { name: "VAT 25%" })).toBeVisible();

  // What one round of automatic expansion costs, and what to know before paying.
  await page.getByLabel("Buy more slots when the last is taken").check();
  await expect(page.getByText(/^Adds 10 slots for 50\.00 USD a month, and .* USD for the rest of /)).toBeVisible();
  await page.getByLabel("Buy more slots when the last is taken").uncheck();
  await expect(page.getByText("You have no test accounts that end.")).toBeVisible();
  await page.getByRole("button", { name: /and go live$/ }).click();

  // The test payment page: a card that declines first, then one that pays.
  await expect(page).toHaveURL(/\/admin\/billing\/checkout\/test_[0-9a-f]+$/);
  await page.getByRole("button", { name: "Try a card that declines" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "The test card was declined." })).toBeVisible();
  await page.getByRole("button", { name: /^Pay / }).click();

  await expect(page).toHaveURL(/\/admin\/go-live\?checkout=done$/);
  await expect(page.getByText("Thank you. The payment went through, and your firm is live.")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Your firm is live" })).toBeVisible();

  // Live now: the portal no longer says it is a test environment, and the menu has Plan and billing.
  await expect(page.getByRole("status").filter({ hasText: "Test environment" })).toHaveCount(0);
  await page.getByRole("link", { name: "Plan and billing" }).last().click();
  await expect(page).toHaveURL(/\/admin\/billing$/);
  await expect(page.getByRole("link", { name: "BILLING-E2E-FIRM-0002 (PDF)" })).toBeVisible();
  await expect(page.getByRole("link", { name: "BILLING-E2E-FIRM-0001 (PDF)" })).toBeVisible();
  await expect(page.getByText("0 of 30 slots taken. 30 free.")).toBeVisible();
  await expect(page.getByText("Test ending 4242, expires 12/2034")).toBeVisible();

  await page.getByLabel("New number of slots").fill("35");
  await page.getByRole("button", { name: /^Buy now for / }).click();
  await expect(page.getByText("0 of 35 slots taken. 35 free.")).toBeVisible();
  await expect(page.getByRole("cell", { name: /^More slots,/ })).toBeVisible();

  // The company's details, as we approved them, are a tab of their own.
  await page.getByRole("tab", { name: "Company details" }).click();
  await expect(page.getByText("billing-e2e-firm Ltd")).toBeVisible();
});
