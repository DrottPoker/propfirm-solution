import { expect, test } from "@playwright/test";

import { platformUrl } from "../playwright.config";

import { adminLink, signUp, startChallenge, waitForSandbox } from "./support";

// A PNG of one pixel, as a logo.
const logo = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==", "base64");

test("a firm signs up, gets its own portal and server, and starts a challenge in the sandbox", async ({ page }) => {
  await signUp(page, "Nordic E2E Prop", "nordic-e2e-prop");

  await expect(page).toHaveTitle("Nordic E2E Prop");
  await expect(page.getByRole("status").filter({ hasText: "Test environment" })).toBeVisible();

  // The trading server is created in the background, together with the firm's first challenge. Then the overview shows
  // the steps to live, with the server and the challenge done.
  await waitForSandbox(page);
  await expect(page.getByText("Two-step 100000 USD. Change the rules or add more whenever you like.")).toBeVisible();
  await startChallenge(page, "trader@nordic-e2e-prop.e2e.example");
  await expect(page.getByText("Trading account nordic-e2e-prop-1001-1")).toBeVisible({ timeout: 20_000 });
});

test("the firm changes its look and makes a challenge of its own", async ({ page }) => {
  await signUp(page, "Look E2E Firm", "look-e2e-firm");
  await waitForSandbox(page);

  await adminLink(page, "Portal design").click();
  await page.getByLabel("Logo file").setInputFiles({ name: "logo.png", mimeType: "image/png", buffer: logo });
  await expect(page.getByRole("img", { name: "Your logo" })).toBeVisible();
  await expect(page.getByRole("complementary", { name: "Admin menu" }).getByRole("img", { name: "Look E2E Firm" })).toBeVisible();
  await page.getByLabel("Brand color", { exact: true }).fill("#ff8800");
  await page.getByRole("button", { name: "Save design" }).click();
  await expect.poll(() => page.evaluate(() => document.documentElement.style.getPropertyValue("--accent"))).toBe("#ff8800");

  await adminLink(page, "Challenges").click();
  await page.getByRole("link", { name: "New challenge" }).click();
  await page.getByLabel("Id for your systems").fill("one-step-50k");
  await page.getByLabel("Name traders see").fill("One-step 50k");
  await page.getByLabel("Account size").fill("50000");
  // A one-step challenge: the second of the two evaluation stages goes.
  await page.getByRole("button", { name: "Remove Phase 2" }).click();
  await page.getByRole("button", { name: "Save challenge" }).click();

  await expect(page).toHaveURL(/\/admin\/challenges$/);
  await expect(page.getByRole("heading", { name: "One-step 50k" })).toBeVisible();
  await expect(page.getByText("50,000.00 USD account", { exact: false })).toBeVisible();
});

test("sign-up is only on the platform's address, and a firm's pages are not there", async ({ page }) => {
  const onFirmPortal = await page.goto("/signup");
  expect(onFirmPortal?.status()).toBe(404);

  await page.goto(`${platformUrl}/login`);
  await expect(page).toHaveURL(`${platformUrl}/signup`);
  await expect(page.getByRole("heading", { name: /Start your prop firm/ })).toBeVisible();
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
