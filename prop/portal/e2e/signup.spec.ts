import { expect, test } from "@playwright/test";

import { platformUrl } from "../playwright.config";

import { adminLink, openSetting, signUp, startChallenge, waitForSandbox } from "./support";

// A PNG of one pixel, as a logo.
const logo = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==", "base64");

test("a firm signs up, gets its own portal and server, and starts a challenge in the sandbox", async ({ page }) => {
  await signUp(page, "Nordic E2E Prop", "nordic-e2e-prop");

  await expect(page).toHaveTitle("Nordic E2E Prop");
  await expect(page.getByRole("status").filter({ hasText: "Test environment" })).toBeVisible();

  // The trading server is created in the background, together with the firm's first challenge. Then the overview shows
  // the steps to live, with the server and the challenge done.
  await waitForSandbox(page);
  // The steps already done are folded away, so the next one is at the top.
  await page.getByRole("button", { name: /^Show \d+ done$/ }).click();
  await expect(page.getByText("Two-step 100K. Change the rules or add more whenever you like.")).toBeVisible();

  // The guide prices the first challenge and turns on test payments, and the steps to live show it.
  await page.getByRole("link", { name: "Open the guide" }).click();
  await expect(page.getByRole("heading", { name: "Make it yours" })).toBeVisible();
  await page.getByRole("button", { name: "Next", exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/get-started\?step=price$/);
  await expect(page.getByRole("heading", { name: "Price your first challenge" })).toBeVisible();
  await page.getByLabel("Price of Two-step 100K").fill("499");
  await page.getByLabel("For sale in your shop").check();
  // Next saves the price.
  await page.getByRole("button", { name: "Next", exact: true }).click();
  await expect(page.getByText("Saved.")).toBeVisible();
  await expect(page).toHaveURL(/\/admin\/get-started\?step=payments$/);
  await page.getByRole("button", { name: "Use test payments and go on" }).click();
  await expect(page).toHaveURL(/\/admin\/get-started\?step=try$/);
  await expect(page.getByRole("link", { name: "Open your shop" })).toBeVisible();
  await expect(page.getByRole("list", { name: "Trying it as a trader" }).getByRole("listitem")).toHaveCount(3);
  await page.getByRole("link", { name: "Go to your overview" }).click();
  await page.getByRole("button", { name: /^Show \d+ done$/ }).click();
  await expect(page.getByText(/One challenge is for sale in your portal's shop/)).toBeVisible();

  // Until we have approved the firm, its emails go only to its administrators, so only the owner could be emailed.
  await page.getByRole("button", { name: "Start a challenge" }).click();
  const panel = page.getByRole("dialog", { name: "Start a challenge" });
  await panel.getByLabel("Trader's email").fill("trader@nordic-e2e-prop.e2e.example");
  await expect(panel.getByRole("checkbox", { name: /^Email the trader/ })).toBeDisabled();
  await expect(panel.getByText(/^Until we have approved your firm, it reaches only its administrators/)).toBeVisible();
  await panel.getByLabel("Trader's email").fill("owner@nordic-e2e-prop.e2e.example");
  await expect(panel.getByRole("checkbox", { name: /^Email the trader/ })).toBeEnabled();
  await panel.getByRole("button", { name: "Cancel" }).click();

  await startChallenge(page, "trader@nordic-e2e-prop.e2e.example");
  await expect(page.getByText("Trading account nordic-e2e-prop-1001-1")).toBeVisible({ timeout: 20_000 });
});

test("the firm changes its look and makes challenges of its own", async ({ page }) => {
  await signUp(page, "Look E2E Firm", "look-e2e-firm");
  await waitForSandbox(page);

  await openSetting(page, "Portal design");
  await page.getByLabel("Logo file").setInputFiles({ name: "logo.png", mimeType: "image/png", buffer: logo });
  await expect(page.getByRole("img", { name: "Your logo" })).toBeVisible();
  await expect(page.getByRole("complementary", { name: "Admin menu" }).getByRole("img", { name: "Look E2E Firm" })).toBeVisible();
  await page.getByLabel("Brand color", { exact: true }).fill("#ff8800");
  await page.getByRole("button", { name: "Save design" }).click();
  await expect.poll(() => page.evaluate(() => document.documentElement.style.getPropertyValue("--accent"))).toBe("#ff8800");

  // Two sizes of the one-step template at once, one of them with a price and for sale.
  await adminLink(page, "Challenges").click();
  await page.getByRole("link", { name: "New challenges" }).click();
  await page.getByRole("radio", { name: /^One-step/ }).click();
  await page.getByRole("checkbox", { name: /^One-step 50K/ }).check();
  await page.getByLabel("Price of One-step 50K").fill("199");
  await page.getByLabel("Put those with a price up for sale at once").check();
  await page.getByRole("button", { name: "Create 2 challenges" }).click();

  await expect(page).toHaveURL(/\/admin\/challenges$/);
  const fifty = page.getByRole("listitem").filter({ has: page.getByRole("heading", { name: "One-step 50K" }) });
  await expect(fifty.getByText("For sale", { exact: true })).toBeVisible();
  await expect(fifty.getByLabel("Price of One-step 50K")).toHaveValue("199");
  await expect(page.getByRole("heading", { name: "One-step 100K" })).toBeVisible();
  await expect(page.getByText("50,000.00 USD account", { exact: false })).toBeVisible();

  // The rules change in the editor: a one-step challenge without a time limit, from the list.
  await page.getByRole("link", { name: "Change the rules of One-step 50K" }).click();
  await expect(page.getByLabel("Time limit (days)")).toHaveValue("");
  await page.getByLabel("Name traders see").fill("One-step 50K Pro");
  await page.getByRole("button", { name: "Save challenge" }).click();
  await expect(page.getByRole("heading", { name: "One-step 50K Pro" })).toBeVisible();
});

test("sign-up is only on the platform's address, and a firm's pages are not there", async ({ page }) => {
  const onFirmPortal = await page.goto("/signup");
  expect(onFirmPortal?.status()).toBe(404);

  await page.goto(`${platformUrl}/admin/login`);
  await expect(page).toHaveURL(`${platformUrl}/`);
  await expect(page.getByRole("heading", { name: "Start your own prop firm" })).toBeVisible();
  await expect(page.getByText(/^Startup fee: \$700 once, of which \$200/)).toBeVisible();
  await page.getByRole("link", { name: "Start free sandbox" }).first().click();
  await expect(page).toHaveURL(`${platformUrl}/signup`);
  await expect(page.getByRole("heading", { name: /Start your prop firm/ })).toBeVisible();
});

// An administrator who does not remember the firm's address gets a login link by email.
test("the platform's login finds the firm from the email", async ({ page }) => {
  await page.goto(`${platformUrl}/login`);
  await expect(page.getByRole("heading", { name: "Log in to your firm" })).toBeVisible();
  await page.getByLabel("Email").fill("nobody@e2e.example");
  await page.getByRole("button", { name: "Email me a login link" }).click();
  await expect(page.getByText(/If nobody@e2e\.example administers a firm here/)).toBeVisible();
});

test("a short name that is taken or reserved is refused while it is typed", async ({ page }) => {
  await page.goto(`${platformUrl}/signup`);

  await page.getByLabel("Short name").fill("demo-firm");
  await expect(page.getByText("That name is taken.")).toBeVisible();
  await page.getByLabel("Short name").fill("www");
  await expect(page.getByText("That name is reserved.")).toBeVisible();
  await page.getByLabel("Short name").fill("Bad Name");
  await expect(page.getByText(/Use 2 to 40 lowercase letters/)).toBeVisible();
});
