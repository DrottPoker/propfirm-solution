import { type APIRequestContext, type BrowserContext, expect, type Page, test } from "@playwright/test";

import { opsUrl, terminalUrl, tradingPort } from "../playwright.config";
import { acceptInvitation, admin, logIn, staff, startChallenge } from "./support";

// Incidents and proof (ADR 0053): our staff publish an outage, the firm sees it, writes to its traders and reinstates a
// phase the outage ended, the status page shows it until it is resolved, and every closed trade's details show the
// feed's price behind it.

/** Opens a page of our admin view in a new tab, logged in as our staff, once its heading shows. */
async function openOps(page: Page, path: string, heading: string): Promise<Page> {
  const ops = await page.context().newPage();
  await ops.goto(`${opsUrl}${path}`);
  const login = ops.getByRole("heading", { name: "Staff login" });
  const opened = ops.getByRole("heading", { name: heading, exact: true });
  await expect(login.or(opened)).toBeVisible();
  if (await login.isVisible()) {
    await ops.getByLabel("Email").fill(staff.email);
    await ops.getByLabel("Password", { exact: true }).fill(staff.password);
    await ops.getByRole("button", { name: "Log in" }).click();
    await expect(ops).toHaveURL(`${opsUrl}/ops`);
    await ops.goto(`${opsUrl}${path}`);
  }

  await expect(opened).toBeVisible();
  return ops;
}

test("our staff publish an incident, the firm adds its note, and the status page shows it until it is resolved", async ({ page }) => {
  const title = `Prices stopped ${test.info().testId.slice(0, 8)}`;

  const ops = await openOps(page, "/ops/incidents", "Incidents");
  await ops.getByRole("link", { name: "Declare an incident" }).click();
  await expect(ops.getByRole("heading", { name: "Declare an incident" })).toBeVisible();
  await ops.getByRole("radio", { name: "Chosen firms" }).check();
  await ops.getByRole("checkbox", { name: /Demo Firm/ }).check();
  await ops.getByLabel("Title for the status pages").fill(title);
  await ops.getByRole("button", { name: "Publish to 1 firm" }).click();
  await ops.getByRole("dialog").getByRole("button", { name: "Publish" }).click();
  await expect(ops.getByRole("heading", { name: title, exact: true })).toBeVisible();
  await expect(ops.getByText("Ongoing", { exact: true }).first()).toBeVisible();

  // Anyone sees it on the firm's status page.
  await page.goto("/status");
  await expect(page.getByRole("heading", { name: "Status", exact: true })).toBeVisible();
  await expect(page.getByText("An incident affects trading and prices")).toBeVisible();
  await expect(page.getByRole("heading", { name: title })).toBeVisible();

  // The firm finds it in its admin panel and adds its own words for its traders.
  await logIn(page, "/admin/login", admin.email, admin.password);
  await page.getByRole("navigation", { name: "Admin" }).getByRole("link", { name: /^Incidents/ }).click();
  await page.getByRole("link", { name: new RegExp(title) }).click();
  await expect(page.getByRole("heading", { name: title })).toBeVisible();
  await page.getByLabel(/Shown under our text on your status page/).fill("Accounts that broke a limit because prices were missing are reinstated.");
  await page.getByRole("button", { name: "Publish the note" }).click();
  await expect(page.getByText("Your note is published.")).toBeVisible();

  await page.goto("/status");
  await expect(page.getByText("From Demo Firm")).toBeVisible();
  await expect(page.getByText("Accounts that broke a limit because prices were missing are reinstated.")).toBeVisible();

  // We resolve it, and the status page says everything runs again.
  await ops.getByRole("radio", { name: "Resolved" }).check();
  await ops.getByLabel("The update, as traders and firms read it").fill("Prices are back.");
  await ops.getByRole("button", { name: "Resolve the incident" }).click();
  await expect(ops.getByText("The incident is resolved. Terminals no longer show it.")).toBeVisible();
  await ops.close();

  await page.reload();
  await expect(page.getByText("Everything is running")).toBeVisible();
  await expect(page.getByText("Prices are back.")).toBeVisible();
});

/**
 * The firm starts a challenge, the trader accepts the invitation and opens a position on a market that is open now,
 * through the trading platform as the terminal would. The trader's page is left on the account.
 */
async function traderWithPosition(context: BrowserContext, page: Page, request: APIRequestContext, name: string) {
  await logIn(page, "/admin/login", admin.email, admin.password);
  await startChallenge(page, `${name}-${test.info().testId.slice(0, 8)}@e2e.example`);
  await expect(page.getByText(/trading account demo-firm-\d+-1/)).toBeVisible({ timeout: 20_000 });
  const accountId = (await page.getByText(/trading account demo-firm-\d+-1/).textContent())!.match(/demo-firm-\d+-1/)![0];
  const adminAccountUrl = page.url();
  await page.getByRole("button", { name: "Create invitation link" }).click();
  const invitation = await page.getByLabel(/Invitation link/).inputValue();

  const trader = await context.newPage();
  await acceptInvitation(trader, invitation);
  await trader.getByRole("link", { name: /^Details of account/ }).click();
  await expect(trader).toHaveURL(/\/accounts\/[0-9a-f-]+$/);
  const traderAccountUrl = trader.url();

  await trader.route(`${terminalUrl}/**`, (route) => route.fulfill({ contentType: "text/html", body: "<p>Terminal</p>" }));
  await trader.getByRole("button", { name: "Open terminal" }).click();
  await trader.waitForURL(`${terminalUrl}/login/link?token=*`);
  const token = new URL(trader.url()).searchParams.get("token");
  const api = `http://localhost:${tradingPort}/api/accounts/${accountId}`;
  expect((await request.post(`http://localhost:${tradingPort}/api/auth/link`, { data: { token } })).ok()).toBeTruthy();
  const hours: { symbol: string; isOpen: boolean }[] = await (await request.get(`${api}/market-hours`)).json();
  const instruments: { symbol: string; volumeMin: number }[] = await (await request.get(`${api}/instruments`)).json();
  const open = instruments.find((i) => hours.some((h) => h.symbol === i.symbol && h.isOpen));
  expect(open, "a market that is open").toBeDefined();
  await expect
    .poll(async () => (await request.post(`${api}/orders`, { data: { orderId: `e2e-${Date.now()}`, symbol: open!.symbol, side: "Buy", type: "Market", volume: open!.volumeMin } })).status(), {
      message: "the order to be filled once a price has come",
      timeout: 20_000,
    })
    .toBeLessThan(300);
  await trader.goto(traderAccountUrl);
  return { trader, accountId, api, symbol: open!.symbol, adminAccountUrl, traderAccountUrl };
}

test("a closed trade's details show the feed's price behind it", async ({ context, page, request }) => {
  const { trader, api, symbol } = await traderWithPosition(context, page, request, "details");
  const positions: { positionId: string }[] = (await (await request.get(api)).json()).positions;
  expect((await request.post(`${api}/positions/${positions[0].positionId}/close`)).ok()).toBeTruthy();

  // The portal lists the closed trade, and its details open in a panel.
  const open = trader.getByRole("button", { name: new RegExp(`^Details of Buy .* ${symbol}$`) });
  await expect(async () => {
    await trader.reload();
    await expect(open).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });
  await open.click();
  const details = trader.getByRole("dialog", { name: new RegExp(`^Buy .* ${symbol}$`) });
  await expect(details.getByRole("region", { name: "Opened" })).toContainText("Price from the feed");
  await expect(details.getByRole("region", { name: "Closed" })).toContainText(/Demo Firm's markup: \d+ points? on the bid\./);
  await expect(details).toContainText(/Result after commission\s*-?[\d,]+\.\d{2} USD/);
  await expect(details).toContainText(/journal, entries [\d,]+ and [\d,]+/);
  await expect(details.getByRole("button", { name: "Print or save as PDF" })).toBeVisible();

  await trader.keyboard.press("Escape");
  await expect(details).toBeHidden();
});

test("a phase that a broken limit ended during an incident is reinstated on the same trading account", async ({ context, page, request }) => {
  const title = `Outage ${test.info().testId.slice(0, 8)}`;
  const { trader, accountId, api, traderAccountUrl } = await traderWithPosition(context, page, request, "reinstate");

  // We publish an incident that goes on.
  const ops = await openOps(page, "/ops/incidents/new", "Declare an incident");
  await ops.getByRole("radio", { name: "Chosen firms" }).check();
  await ops.getByRole("checkbox", { name: /Demo Firm/ }).check();
  await ops.getByLabel("Title for the status pages").fill(title);
  await ops.getByRole("button", { name: "Publish to 1 firm" }).click();
  await ops.getByRole("dialog").getByRole("button", { name: "Publish" }).click();
  await expect(ops.getByRole("heading", { name: title, exact: true })).toBeVisible();

  // The account's daily limit is set above its equity, so the next price breaks it, as the outage's first price would.
  const floor = await request.put(`http://localhost:${tradingPort}/api/admin/v1/accounts/${accountId}/floors/daily`, {
    headers: { "X-Api-Key": "dev-admin-key" },
    data: { rule: { kind: "FixedFloor", level: 100_001 } },
  });
  expect(floor.ok()).toBeTruthy();
  await expect.poll(async () => (await (await request.get(api)).json()).status, { message: "the trading account to end", timeout: 20_000 }).toBe("Disabled");

  // The firm sees the broken limit in the incident and reinstates the phase with the starting balance.
  await page.goto("/admin/incidents");
  await page.getByRole("link", { name: new RegExp(title) }).click();
  await expect(page.getByRole("heading", { name: title })).toBeVisible();
  const reinstate = page.getByRole("button", { name: "Reinstate" });
  await expect(async () => {
    await page.reload();
    await expect(reinstate).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 30_000 });
  await expect(page.getByText(/^Broke the daily loss limit at equity/)).toBeVisible();
  await reinstate.click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByRole("radio", { name: /Balance when the incident began/ })).toBeVisible();
  await dialog.getByRole("textbox", { name: "Another amount" }).fill("100,000");
  await dialog.getByRole("button", { name: "Reinstate the account" }).click();
  await expect(page.getByText(/is reinstated with 100,000.00 USD/)).toBeVisible();
  await expect(page.getByText(/^Reinstated #\d+ with 100,000.00$/)).toBeVisible();

  // The same trading account trades again, with its limits set again.
  await expect
    .poll(async () => {
      const account = await (await request.get(api)).json();
      return [account.status, account.balance, account.floors.length > 0];
    }, { message: "the trading account to open again", timeout: 20_000 })
    .toEqual(["Active", 100_000, true]);

  // The trader's breach report says the phase was reinstated.
  await trader.goto(`${traderAccountUrl}/breach-report`);
  await expect(trader.getByRole("heading", { name: "The daily loss limit was broken" })).toBeVisible();
  await expect(trader.getByText(/has reinstated this phase/)).toBeVisible();

  await ops.getByRole("radio", { name: "Resolved" }).check();
  await ops.getByLabel("The update, as traders and firms read it").fill("Prices are back.");
  await ops.getByRole("button", { name: "Resolve the incident" }).click();
  await expect(ops.getByText("The incident is resolved. Terminals no longer show it.")).toBeVisible();
  await ops.close();
});
