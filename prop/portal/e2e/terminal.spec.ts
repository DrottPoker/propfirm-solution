import { expect, test } from "@playwright/test";

import { openSetting, signUp, waitForSandbox } from "./support";

// How orders start in the firm's terminal (ADR 0058), kept in the terminal's profile on the trading platform, so what
// the page shows after a reload is what the platform has.
test("the firm chooses how orders start in its traders' terminal", async ({ page }) => {
  await signUp(page, "Terminal E2E Firm", "terminal-e2e-firm");
  await waitForSandbox(page);

  // A prop firm's terminal starts with one-click orders at the smallest size.
  await openSetting(page, "Terminal");
  const ask = page.getByRole("switch", { name: "Ask before every order" });
  await expect(ask).not.toBeChecked();
  await expect(page.getByRole("radio", { name: /^The smallest each instrument allows/ })).toBeChecked();

  // A size the platform would not take is refused before it is saved.
  await page.getByRole("radio", { name: /^A number of lots/ }).check();
  await page.getByLabel("Lots", { exact: true }).fill("0");
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "Write the size in lots, above 0 and at most 1,000." })).toBeVisible();

  await page.getByLabel("Lots", { exact: true }).fill("0.50");
  await ask.click();
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Saved. Terminals start this way the next time they open.")).toBeVisible();

  await page.reload();
  await expect(page.getByRole("switch", { name: "Ask before every order" })).toBeChecked();
  await expect(page.getByRole("radio", { name: /^A number of lots/ })).toBeChecked();
  await expect(page.getByLabel("Lots", { exact: true })).toHaveValue("0.5");
});
