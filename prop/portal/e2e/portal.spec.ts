import { expect, test } from "@playwright/test";

import { terminalUrl, tradingPort } from "../playwright.config";
import { acceptInvitation, admin, firmName, logIn } from "./support";

test("visitors who are not logged in are sent to the login page of the firm", async ({ page }) => {
  await page.goto("/");

  await expect(page).toHaveURL(/\/login$/);
  await expect(page).toHaveTitle(firmName);
  await expect(page.getByText(firmName)).toBeVisible();
});

test("a wrong password is refused", async ({ page }) => {
  await logIn(page, "/admin/login", admin.email, "not-the-password");

  // Next.js has an alert of its own for route announcements, so look inside the form.
  await expect(page.locator("form").getByRole("alert")).toHaveText("Wrong email or password.");
  await expect(page).toHaveURL(/\/admin\/login$/);
});

test("a trader from the development configuration logs in with its short password", async ({ page }) => {
  await logIn(page, "/login", "anna@test.com", "anna");

  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByText("You have no challenge yet.", { exact: false })).toBeVisible();
});

test("the firm starts a challenge and invites the trader, who opens the terminal from the portal", async ({ context, page, request }) => {
  const email = `trader-${test.info().testId.slice(0, 8)}@e2e.example`;

  // The firm's administrator starts the challenge and waits for its trading account.
  await logIn(page, "/admin/login", admin.email, admin.password);
  await page.getByLabel("Trader email").fill(email);
  await page.getByRole("button", { name: "Start challenge" }).click();
  await expect(page).toHaveURL(/\/admin\/accounts\/[0-9a-f-]+$/);
  await expect(page.getByText(/Trading account demo-firm-\d+-1/)).toBeVisible({ timeout: 20_000 });
  const tradingAccountId = (await page.getByText(/Trading account demo-firm-\d+-1/).textContent())!.match(/demo-firm-\d+-1/)![0];

  await page.getByRole("button", { name: "Create invitation link" }).click();
  const invitation = await page.getByLabel(/Invitation link/).inputValue();
  expect(invitation).toMatch(/\/invite\?token=/);

  // The trader chooses a password with the invitation, in the same browser. Traders and administrators have
  // separate sessions, so the administrator stays logged in.
  const trader = await context.newPage();
  await acceptInvitation(trader, invitation);

  // The start page has a card for the account, and its page has the objectives and the rules.
  await expect(trader.getByText("100,000.00 USD")).toBeVisible();
  await expect(trader.getByText("Daily loss room")).toBeVisible();
  await trader.getByRole("link", { name: /^Details of account/ }).click();
  await expect(trader).toHaveURL(/\/accounts\/[0-9a-f-]+$/);
  await expect(trader.getByRole("heading", { name: "Objectives for Phase 1" })).toBeVisible();
  await expect(trader.getByRole("rowheader", { name: "Daily loss limit" })).toBeVisible();
  await expect(trader.getByText("No open positions")).toBeVisible();

  // The terminal is not running in these tests, so the link is caught and checked against the trading platform.
  await trader.route(`${terminalUrl}/**`, (route) => route.fulfill({ contentType: "text/html", body: "<p>Terminal</p>" }));
  await trader.getByRole("button", { name: "Open terminal" }).click();
  await trader.waitForURL(`${terminalUrl}/login/link?token=*`);
  const token = new URL(trader.url()).searchParams.get("token");
  const login = await request.post(`http://localhost:${tradingPort}/api/auth/link`, { data: { token } });
  expect(login.ok()).toBeTruthy();
  expect((await login.json()).accounts).toContain(tradingAccountId);
  await trader.close();

  // The firm cancels the account, and its trading account is closed.
  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Cancel account" }).click();
  await expect(page.getByText("Cancelled by the firm.")).toBeVisible();
});
