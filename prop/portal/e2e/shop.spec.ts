import { expect, test } from "@playwright/test";

import { admin, logIn } from "./support";

// The development firm sells its challenges with test payments (appsettings.Development.json), so a purchase
// runs the whole way without a payment provider.

test("a visitor buys a challenge with a test payment, and the firm sees the paid order", async ({ page }) => {
  const buyer = `buyer-${Date.now()}@e2e.example`;

  await page.goto("/login");
  await page.getByRole("link", { name: "Buy a challenge" }).click();
  await expect(page).toHaveURL(/\/buy$/);
  await expect(page.getByText("Test payments: you pay on a test page, and no money is taken.")).toBeVisible();

  const challenge = page.getByRole("listitem").filter({ hasText: "Quick test 100000 USD" });
  await challenge.getByRole("button", { name: "Buy" }).click();
  await challenge.getByLabel("Your email").fill(buyer);
  await challenge.getByRole("button", { name: "Pay 9.00 USD" }).click();

  await expect(page).toHaveURL(/\/checkout\/test\?order=/);
  await expect(page.getByRole("heading", { name: "Test payment" })).toBeVisible();
  await page.getByRole("button", { name: "Pay 9.00 USD" }).click();

  await expect(page).toHaveURL(/\/orders\/[0-9a-f-]+\?token=/);
  await expect(page.getByText("Payment received. Your challenge is starting.")).toBeVisible();

  await logIn(page, "/admin/login", admin.email, admin.password);
  await page.getByRole("link", { name: "Orders" }).click();
  const order = page.getByRole("row").filter({ hasText: buyer });
  await expect(order.getByText("Paid", { exact: true })).toBeVisible();
  await order.getByRole("link", { name: "quick-test-100k" }).click();
  await expect(page).toHaveURL(/\/admin\/accounts\/[0-9a-f-]+$/);
  await expect(page.getByText(buyer).first()).toBeVisible();
});

test("a logged-in trader buys with their own email and goes straight to the new account", async ({ page }) => {
  await logIn(page, "/login", "test@test.com", "test");
  await expect(page.getByText("You have no challenge yet.")).toBeVisible();
  await page.getByRole("banner").getByRole("link", { name: "Buy a challenge" }).click();
  await expect(page).toHaveURL(/\/buy$/);

  const challenge = page.getByRole("listitem").filter({ hasText: "Quick test 100000 USD" });
  await challenge.getByRole("button", { name: "Buy" }).click();
  await expect(challenge.getByText("You buy as test@test.com")).toBeVisible();
  await challenge.getByRole("button", { name: "Pay 9.00 USD" }).click();
  await page.getByRole("button", { name: "Pay 9.00 USD" }).click();

  await page.getByRole("link", { name: "Go to your account" }).click();
  await expect(page).toHaveURL(/\/accounts\/[0-9a-f-]+$/);
  await expect(page.getByRole("heading", { name: "Quick test 100000 USD" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Objectives for Phase 1" })).toBeVisible();
});
