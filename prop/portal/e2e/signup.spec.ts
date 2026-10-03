import { expect, test, type Page } from "@playwright/test";

import { platformUrl, portalPort } from "../playwright.config";

const ownerPassword = "e2e-owner-password";

/** A firm signs up on the platform and lands in its own admin panel, on its own address. Development needs no email confirmation. */
async function signUp(page: Page, firmName: string, shortName: string) {
  await page.goto(`${platformUrl}/signup`);
  await page.getByLabel("Firm name").fill(firmName);
  await expect(page.getByLabel("Short name")).toHaveValue(shortName);
  await expect(page.getByText("Available")).toBeVisible();
  await page.getByLabel("Your email").fill(`owner@${shortName}.e2e.example`);
  await page.getByLabel("Password").fill(ownerPassword);
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Create my firm" }).click();
  await expect(page).toHaveURL(`http://${shortName}.localhost:${portalPort}/admin`);
}

test("a firm signs up, gets its own portal and server, and starts a challenge in the sandbox", async ({ page }) => {
  await signUp(page, "Nordic E2E Prop", "nordic-e2e-prop");

  await expect(page).toHaveTitle("Nordic E2E Prop");
  await expect(page.getByRole("status").filter({ hasText: "Test environment" })).toBeVisible();

  // The trading server is created in the background, together with the firm's first challenge.
  await expect(page.getByRole("option", { name: "Two-step 100000 USD (two-step-100k)" })).toBeAttached({ timeout: 20_000 });
  await page.getByLabel("Trader email").fill("trader@nordic-e2e-prop.e2e.example");
  await page.getByRole("button", { name: "Start challenge" }).click();
  await expect(page).toHaveURL(/\/admin\/accounts\/[0-9a-f-]+$/);
  await expect(page.getByText("Trading account nordic-e2e-prop-1001-1")).toBeVisible({ timeout: 20_000 });
});

test("the firm changes its look and makes a challenge of its own", async ({ page }) => {
  await signUp(page, "Look E2E Firm", "look-e2e-firm");
  await expect(page.getByRole("option", { name: /two-step-100k/ })).toBeAttached({ timeout: 20_000 });

  await page.getByRole("link", { name: "Settings" }).click();
  await page.getByLabel("accent color").fill("#ff8800");
  await page.getByRole("button", { name: "Save look" }).click();
  await expect.poll(() => page.evaluate(() => document.documentElement.style.getPropertyValue("--accent"))).toBe("#ff8800");

  await page.getByRole("link", { name: "Challenges" }).click();
  await page.getByRole("button", { name: "New challenge" }).click();
  await page.getByLabel("Id, used by your systems").fill("one-step-50k");
  await page.getByLabel("Name, shown to traders").fill("One-step 50k");
  await page.getByLabel("Account size (USD)").fill("50000");
  // A one-step challenge: the second of the two evaluation stages goes.
  await page.getByRole("button", { name: "Remove" }).nth(1).click();
  await page.getByRole("button", { name: "Save challenge" }).click();

  await expect(page.getByText("One-step 50k (one-step-50k)")).toBeVisible();
  await expect(page.getByText("50,000.00 USD", { exact: false })).toBeVisible();
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
