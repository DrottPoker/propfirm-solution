import { expect, test, type APIRequestContext } from "@playwright/test";

import { terminalUrl, tradingPort } from "../playwright.config";
import { acceptInvitation, admin, adminLink, logIn, startChallenge } from "./support";

const tradingUrl = `http://localhost:${tradingPort}`;

// The development firm's key for the trading platform's admin API, from Trading.Service's appsettings.Development.json.
const tradingAdminHeaders = { "X-Api-Key": "dev-admin-key" };

/** The firm adds money on the trading platform, so the trader's next close is a profit without waiting for prices. */
async function deposit(request: APIRequestContext, accountId: string, amount: number) {
  const response = await request.post(`${tradingUrl}/api/admin/v1/accounts/${accountId}/balance-operations`, {
    headers: tradingAdminHeaders,
    data: { operationId: `e2e-deposit-${accountId}`, amount },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
}

/** Opens and closes the smallest position. The rule engine measures the balance when a position closes. */
async function tradeOnce(request: APIRequestContext, accountId: string) {
  const orderId = crypto.randomUUID();
  const opened = await request.post(`${tradingUrl}/api/accounts/${accountId}/orders`, {
    data: { orderId, symbol: "EURUSD", side: "Buy", type: "Market", volume: 0.01 },
  });
  expect(opened.ok(), await opened.text()).toBeTruthy();
  const closed = await request.post(`${tradingUrl}/api/accounts/${accountId}/positions/${orderId}/close`);
  expect(closed.ok(), await closed.text()).toBeTruthy();
}

// The whole chain with the quick test challenge: 0.1 % targets, no minimum trading days and an 80 % profit split.
test("a funded trader asks for a payout, and the firm approves and pays it", async ({ context, page, request }) => {
  test.setTimeout(120_000);
  const email = `payout-${test.info().testId.slice(0, 8)}@e2e.example`;

  // The firm starts the challenge and invites the trader.
  await logIn(page, "/admin/login", admin.email, admin.password);
  await startChallenge(page, email, /^Quick test 100K/);
  const phaseOne = page.getByText(/Trading account demo-firm-\d+-1/);
  await expect(phaseOne).toBeVisible({ timeout: 20_000 });
  const accountBase = (await phaseOne.textContent())!.match(/demo-firm-\d+/)![0];
  await page.getByRole("button", { name: "Create invitation link" }).click();
  const trader = await context.newPage();
  await acceptInvitation(trader, await page.getByLabel(/Invitation link/).inputValue());

  // The trader trades through the trading platform's API, logged in with the portal's one-time link.
  await trader.route(`${terminalUrl}/**`, (route) => route.fulfill({ contentType: "text/html", body: "<p>Terminal</p>" }));
  await trader.getByRole("button", { name: "Open terminal" }).click();
  await trader.waitForURL(`${terminalUrl}/login/link?token=*`);
  const login = await request.post(`${tradingUrl}/api/auth/link`, { data: { token: new URL(trader.url()).searchParams.get("token") } });
  expect(login.ok()).toBeTruthy();
  await trader.goto("/");

  // Both evaluation stages are passed, and the firm approves the funded account.
  for (const stage of [1, 2]) {
    const accountId = `${accountBase}-${stage}`;
    await expect(page.getByText(`Trading account ${accountId}`)).toBeVisible({ timeout: 20_000 });
    await deposit(request, accountId, 200);
    await tradeOnce(request, accountId);
  }

  await page.getByRole("button", { name: "Approve funded account" }).click({ timeout: 20_000 });
  await page.getByRole("dialog").getByRole("button", { name: "Approve funded account" }).click();
  const funded = `${accountBase}-3`;
  await expect(page.getByText(`Trading account ${funded}`)).toBeVisible({ timeout: 20_000 });
  await deposit(request, funded, 8_000);
  await tradeOnce(request, funded);

  // The trader says where the money goes, under Payouts, before asking for one.
  await trader.goto("/payouts");
  await trader.getByLabel("Account holder").fill("E2E Trader");
  await trader.getByLabel("IBAN or account number").fill("SE45 5000 0000 0583 9825 7466");
  await trader.getByRole("button", { name: "Save" }).click();
  await expect(trader.getByText("SE45 5000 0000 0583 9825 7466")).toBeVisible();
  await trader.goto("/");

  // The trader opens the account from the start page, sees the funded stage's trade and asks for the payout. The
  // profit comes off the trading account at once.
  await trader.getByRole("link", { name: /^Details of account/ }).click();
  await expect(trader.getByRole("heading", { name: "History · Funded" })).toBeVisible({ timeout: 20_000 });
  await expect(trader.getByRole("cell", { name: "EURUSD" }).first()).toBeVisible({ timeout: 20_000 });
  const requestPayout = trader.getByRole("button", { name: "Request payout" });
  await expect(requestPayout).toBeEnabled({ timeout: 20_000 });
  await requestPayout.click();
  await trader.getByRole("dialog").getByRole("button", { name: "Request payout" }).click();
  await expect(trader.getByRole("heading", { name: "Payout on its way" })).toBeVisible({ timeout: 20_000 });
  await expect(trader.getByText("Waiting for approval").first()).toBeVisible({ timeout: 20_000 });
  const account = await request.get(`${tradingUrl}/api/admin/v1/accounts/${funded}`, { headers: tradingAdminHeaders });
  expect((await account.json()).balance).toBe(100_000);

  // The funded stage's trades can be downloaded as a file.
  const accountId = trader.url().split("/").at(-1);
  const file = await trader.request.get(`/api/portal/accounts/${accountId}/trades.csv?stage=2`);
  expect(file.ok()).toBeTruthy();
  const lines = (await file.text()).trim().split("\r\n");
  expect(lines[0]).toBe("Position,Symbol,Side,Volume,Opened (UTC),Open price,Closed (UTC),Close price,Profit,Commission,Result,Close reason");
  expect(lines.slice(1).every((line) => line.includes(",EURUSD,Buy,0.01,"))).toBeTruthy();

  // The firm sees where to send the money, approves the payout, sends the money and marks the payout as paid.
  await adminLink(page, "Payouts").click();
  const row = page.getByRole("row").filter({ hasText: email });
  await expect(row.getByText("SE45 5000 0000 0583 9825 7466")).toBeVisible();
  // The firm has not ticked its checks of the trader, so approving asks first.
  await row.getByRole("button", { name: "Approve" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Approve anyway" }).click();
  await expect(row).toHaveCount(0);
  await page.getByRole("button", { name: /^To pay/ }).click();
  await row.getByRole("button", { name: "Mark as paid" }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel(/^Reference/).fill("wire-e2e");
  await dialog.getByRole("button", { name: "Mark as paid" }).click();
  await expect(row).toHaveCount(0);
  await page.getByRole("button", { name: "All", exact: true }).click();
  await expect(row.getByText("Paid. Reference wire-e2e.")).toBeVisible();

  await expect(trader.getByText("Paid. Reference wire-e2e.")).toBeVisible({ timeout: 20_000 });

  // The trader's payouts page has it too, with what has been paid.
  await trader.getByRole("navigation", { name: "Main" }).getByRole("link", { name: "Payouts" }).click();
  await expect(trader.getByRole("heading", { name: "Payouts", level: 1 })).toBeVisible();
  await expect(trader.getByText("Paid. Reference wire-e2e.")).toBeVisible();
});
