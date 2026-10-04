import { expect, test } from "@playwright/test";

import { admin, adminLink, logIn, traderPassword } from "./support";

// The development firm sells its challenges with test payments (appsettings.Development.json), so a purchase
// runs the whole way without a payment provider.

test("a visitor buys a challenge with a test payment, and the firm sees the paid order", async ({ page }) => {
  const buyer = `buyer-${Date.now()}@e2e.example`;

  await page.goto("/login");
  await page.getByRole("link", { name: "Buy a challenge" }).click();
  await expect(page).toHaveURL(/\/buy$/);
  await expect(page.getByText("Test payments: you pay on a test page, and no money is taken.")).toBeVisible();

  // The challenges are price tables, sizes as columns. The buyer gives the email, name and country.
  await expect(page.getByRole("row", { name: /^Price/ }).first()).toBeVisible();
  await page.getByRole("button", { name: "Buy Quick test 100K" }).click();
  await page.getByLabel("Your email").fill(buyer);
  await page.getByLabel("Your name").fill("Ann Buyer");
  await page.getByLabel("Country").selectOption("SE");
  await page.getByRole("button", { name: "Pay 9.00 USD" }).click();

  await expect(page).toHaveURL(/\/checkout\/test\?order=/);
  await expect(page.getByRole("heading", { name: "Test payment" })).toBeVisible();
  await page.getByRole("button", { name: "Pay 9.00 USD" }).click();

  // The buyer chooses the password right on the order's page and opens the account, and confirms the email later.
  await expect(page).toHaveURL(/\/orders\/[0-9a-f-]+\?token=/);
  await expect(page.getByText("Payment received. Your challenge has started.")).toBeVisible();
  await page.getByLabel("Password", { exact: true }).fill(traderPassword);
  await page.getByLabel("Repeat password").fill(traderPassword);
  await page.getByRole("button", { name: "Open my account" }).click();
  await expect(page).toHaveURL(/\/accounts\/[0-9a-f-]+$/);
  await expect(page.getByRole("heading", { name: "Quick test 100K" })).toBeVisible();
  await page.goto("/");
  await expect(page.getByText("Confirm your email.")).toBeVisible();

  await logIn(page, "/admin/login", admin.email, admin.password);
  await adminLink(page, "Orders").click();
  const order = page.getByRole("row").filter({ hasText: buyer });
  await expect(order.getByText("Paid", { exact: true })).toBeVisible();
  await order.getByRole("link", { name: "Quick test 100K" }).click();
  await expect(page).toHaveURL(/\/admin\/accounts\/[0-9a-f-]+$/);
  await expect(page.getByText(buyer).first()).toBeVisible();
  await expect(page.getByRole("heading", { name: "Ann Buyer" })).toBeVisible();
});

test("the firm makes a discount code, and a buyer pays less with it", async ({ page, context }) => {
  const code = `E2E${Date.now()}`;
  const buyer = `discount-${Date.now()}@e2e.example`;

  await logIn(page, "/admin/login", admin.email, admin.password);
  await adminLink(page, "Discount codes").click();
  await page.getByLabel("Code", { exact: true }).fill(code);
  await page.getByLabel("Percent off").fill("50");
  await page.getByRole("button", { name: "Add code" }).click();
  const row = page.getByRole("row").filter({ hasText: code });
  await expect(row.getByText("50% off")).toBeVisible();
  await expect(row.getByText("Active")).toBeVisible();

  // The buyer types the code and sees the price with it before paying.
  const shop = await context.newPage();
  await shop.goto("/buy");
  await shop.getByRole("button", { name: "Buy Quick test 100K" }).click();
  await shop.getByLabel("Your email").fill(buyer);
  await shop.getByLabel("Your name").fill("Dee Count");
  await shop.getByLabel("Country").selectOption("SE");
  await shop.getByLabel(/^Discount code/).fill(code.toLowerCase());
  await shop.getByRole("button", { name: "Apply" }).click();
  await expect(shop.getByText("4.50 USD off, so you pay 4.50 instead of 9.00.")).toBeVisible();
  await shop.getByRole("button", { name: "Pay 4.50 USD" }).click();
  await expect(shop.getByRole("heading", { name: "Test payment" })).toBeVisible();
  await shop.getByRole("button", { name: "Pay 4.50 USD" }).click();
  await expect(shop.getByText("Payment received. Your challenge has started.")).toBeVisible();

  // The order says what the code took off, and the code was used once.
  await adminLink(page, "Orders").click();
  await expect(page.getByRole("row").filter({ hasText: buyer }).getByText(`Code ${code.toLowerCase()}, was 9.00`)).toBeVisible();
  await adminLink(page, "Discount codes").click();
  await expect(row.getByRole("cell", { name: "1", exact: true })).toBeVisible();
  await row.getByRole("button", { name: "Turn off" }).click();
  await expect(row.getByText("Off", { exact: true })).toBeVisible();
});

test("a visitor at the portal's front page lands in the shop, with a way to log in", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveURL(/\/buy$/);
  await page.getByRole("link", { name: "Log in" }).click();
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByRole("link", { name: "Admin login" })).toHaveCount(0);
});

test("a logged-in trader buys with their own email and goes straight to the new account", async ({ page }) => {
  await logIn(page, "/login", "test@test.com", "test");
  await expect(page.getByRole("heading", { name: "You have no active challenge" })).toBeVisible();
  await page.getByRole("main").getByRole("link", { name: "Buy a challenge" }).click();
  await expect(page).toHaveURL(/\/buy$/);

  await page.getByRole("button", { name: "Buy Quick test 100K" }).click();
  await expect(page.getByText("You buy as test@test.com")).toBeVisible();
  await page.getByLabel("Your name").fill("Test Trader");
  await page.getByLabel("Country").selectOption("SE");
  await page.getByRole("button", { name: "Pay 9.00 USD" }).click();
  await page.getByRole("button", { name: "Pay 9.00 USD" }).click();

  await page.getByRole("link", { name: "Go to your account" }).click();
  await expect(page).toHaveURL(/\/accounts\/[0-9a-f-]+$/);
  await expect(page.getByRole("heading", { name: "Quick test 100K" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Objectives for Phase 1" })).toBeVisible();
});
