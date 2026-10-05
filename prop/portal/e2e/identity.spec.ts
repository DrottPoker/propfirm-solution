import { expect, test } from "@playwright/test";

import { acceptInvitation, adminLink, signUp, startChallenge, waitForSandbox } from "./support";

test("a new firm chooses the built-in KYC, and a trader verifies with the test check", async ({ context, page }) => {
  await signUp(page, "Identity E2E Firm", "identity-e2e-firm");
  await waitForSandbox(page);

  // Nothing is chosen for the firm, and going live waits until it chooses.
  await adminLink(page, "KYC").click();
  await expect(page.getByRole("heading", { name: "Why KYC matters" })).toBeVisible();
  await expect(page.getByText("Choose how your traders are checked. You need it before you go live.")).toBeVisible();
  await expect(page.getByRole("radio", { name: /^Our built-in KYC/ })).not.toBeChecked();
  await expect(page.getByRole("radio", { name: /^Your own KYC service/ })).not.toBeChecked();
  await expect(page.getByRole("button", { name: "Save" })).toBeDisabled();

  // The firm chooses the built-in check before the first payout. In the sandbox every check is a test check.
  await page.getByRole("radio", { name: /^Our built-in KYC/ }).check();
  await page.getByRole("radio", { name: /^Before the first payout/ }).check();
  await expect(page.getByText(/These are test checks/)).toBeVisible();
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Saved.")).toBeVisible();

  await adminLink(page, "Accounts").click();
  await startChallenge(page, "trader@identity-e2e-firm.e2e.example");
  await page.getByRole("button", { name: "Create invitation link" }).click();
  const invitation = await page.getByLabel(/Invitation link/).inputValue();
  const trader = await context.newPage();
  await acceptInvitation(trader, invitation);

  // The trader finds the check under Payouts and approves the test check.
  await trader.goto(new URL("/payouts", trader.url()).href);
  await trader.getByRole("button", { name: "Verify your identity" }).click();
  await expect(trader).toHaveURL(/\/identity\/test\?session=/);
  await trader.getByRole("button", { name: "Approve" }).click();
  await expect(trader).toHaveURL(/\/identity\?returned=1$/);
  await expect(trader.getByRole("heading", { name: "Your identity is verified" })).toBeVisible();

  // The firm sees the outcome on the trader's card, with ID checked ticked by the check.
  await page.reload();
  const check = page.getByRole("region", { name: "KYC" });
  await expect(check.getByText("Verified", { exact: true })).toBeVisible();
  await expect(check.getByText("Test Trader")).toBeVisible();
  await expect(page.getByText(/by Test check/).first()).toBeVisible();
});
