import { expect, test } from "@playwright/test";

import { terminalUrl, tradingPort } from "../playwright.config";
import { acceptInvitation, admin, firmName, logIn, startChallenge } from "./support";

test("a page for traders sends visitors who are not logged in to the login page of the firm", async ({ page }) => {
  await page.goto("/payouts");

  await expect(page).toHaveURL(/\/login\?next=%2Fpayouts$/);
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
  await expect(page.getByRole("heading", { name: "You have no active challenge" })).toBeVisible();
});

// The trader's own limits (ADR 0054): set and locked in the terminal, seen by the firm in the admin panel.
test("the firm sees the limits the trader set in the terminal, and the day they locked", async ({ context, page, request }) => {
  const email = `limits-${test.info().testId.slice(0, 8)}@e2e.example`;
  await logIn(page, "/admin/login", admin.email, admin.password);
  await startChallenge(page, email);
  await expect(page.getByText(/trading account demo-firm-\d+-1/)).toBeVisible({ timeout: 20_000 });
  const tradingAccountId = (await page.getByText(/trading account demo-firm-\d+-1/).textContent())!.match(/demo-firm-\d+-1/)![0];
  await page.getByRole("button", { name: "Create invitation link" }).click();
  const invitation = await page.getByLabel(/Invitation link/).inputValue();

  // The trader logs in to the trading platform with the portal's link, and sets limits and locks the day through its API,
  // as the terminal does.
  const trader = await context.newPage();
  await acceptInvitation(trader, invitation);
  await trader.route(`${terminalUrl}/**`, (route) => route.fulfill({ contentType: "text/html", body: "<p>Terminal</p>" }));
  await trader.getByRole("button", { name: "Open terminal" }).click();
  await trader.waitForURL(`${terminalUrl}/login/link?token=*`);
  const trading = `http://localhost:${tradingPort}/api`;
  expect((await request.post(`${trading}/auth/link`, { data: { token: new URL(trader.url()).searchParams.get("token") } })).ok()).toBeTruthy();
  const account = `${trading}/accounts/${tradingAccountId}`;
  expect((await request.put(`${account}/limits`, { data: { dailyLoss: 1500, dailyTarget: null, maxTrades: 6 } })).ok()).toBeTruthy();
  expect((await request.post(`${account}/lock`, { data: { closePositions: true } })).ok()).toBeTruthy();
  await trader.close();

  // The trading day is the challenge's, midnight in Stockholm, so the lock lasts until then.
  await page.getByRole("tab", { name: "Trading" }).click();
  const own = page.getByRole("region", { name: "The trader's own limits" });
  await expect(own.getByText("Locked by the trader until 00:00")).toBeVisible({ timeout: 20_000 });
  await expect(own).toContainText("Daily loss limit1,500.00 USD");
  await expect(own).toContainText("Trades a day60 opened today");
  await expect(own.getByRole("listitem")).toContainText("The trader locked the rest of the day.", { timeout: 20_000 });
});

test("the firm starts a challenge and invites the trader, who opens the terminal from the portal", async ({ context, page, request }) => {
  const email = `trader-${test.info().testId.slice(0, 8)}@e2e.example`;

  // The firm's administrator starts the challenge and waits for its trading account.
  await logIn(page, "/admin/login", admin.email, admin.password);
  await startChallenge(page, email);
  await expect(page.getByText(/trading account demo-firm-\d+-1/)).toBeVisible({ timeout: 20_000 });
  const tradingAccountId = (await page.getByText(/trading account demo-firm-\d+-1/).textContent())!.match(/demo-firm-\d+-1/)![0];

  await page.getByRole("button", { name: "Create invitation link" }).click();
  const invitation = await page.getByLabel(/Invitation link/).inputValue();
  expect(invitation).toMatch(/\/invite\?token=/);

  // The trader chooses a password with the invitation, in the same browser. Traders and administrators have
  // separate sessions, so the administrator stays logged in.
  const trader = await context.newPage();
  await acceptInvitation(trader, invitation);

  // The start page has a card for the account, and its page has the objectives and the rules.
  await expect(trader.getByText("100,000.00 USD")).toBeVisible();
  await expect(trader.getByText("Daily loss limit", { exact: true })).toBeVisible();
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

  // The firm cancels the account from the menu of seldom used actions, and its trading account is closed. The dialog asks first.
  await page.getByRole("button", { name: "More actions" }).click();
  await page.getByRole("menuitem", { name: "Cancel account" }).click();
  await page.getByRole("dialog").getByLabel(/^Reason/).fill("Refunded.");
  await page.getByRole("dialog").getByRole("button", { name: "Cancel account" }).click();
  await expect(page.getByText(/^Cancelled by the firm on /)).toBeVisible();
});
