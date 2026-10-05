import { expect, test, type APIRequestContext, type Page } from "@playwright/test";

import { servicePort } from "../playwright.config";

const serviceUrl = `http://localhost:${servicePort}`;

// The development firm's admin key and server, from appsettings.Development.json.
const adminHeaders = { "X-Api-Key": "dev-admin-key" };
const server = { id: "demo-firm", name: "Demo Firm" };
const productName = "Kronant Trader";

const password = "e2e-password";

/** Sets a loss limit at a fixed level through the admin API, like the prop platform does. */
async function setFloor(request: APIRequestContext, accountId: string, floorId: string, level: number) {
  const response = await request.put(`${serviceUrl}/api/admin/v1/accounts/${accountId}/floors/${floorId}`, {
    headers: adminHeaders,
    data: { rule: { kind: "FixedFloor", level } },
  });
  expect(response.ok()).toBeTruthy();
}

/** Creates a trader with an account through the admin API, like the prop platform will. */
async function createTrader(request: APIRequestContext, name: string) {
  const accountId = `${name}-${test.info().testId.slice(0, 8)}`;
  const email = `${accountId}@e2e.example`;

  const user = await request.post(`${serviceUrl}/api/admin/v1/users`, { headers: adminHeaders, data: { email, password } });
  expect(user.ok()).toBeTruthy();
  const account = await request.post(`${serviceUrl}/api/admin/v1/accounts`, {
    headers: adminHeaders,
    data: { accountId, groupId: "standard", initialBalance: 100_000, ownerUserId: (await user.json()).userId },
  });
  expect(account.ok()).toBeTruthy();

  return { accountId, email, userId: (await user.json()).userId as string };
}

// The development firm's traders log in through its portal, so the terminal's own password login is one click away.
async function submitLogin(page: Page, email: string, withPassword: string) {
  await page.goto("/login");
  await page.getByRole("button", { name: "I have a password for the terminal" }).click();
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(withPassword);
  await page.getByRole("button", { name: "Log in" }).click();
}

/** Logs in and waits until the terminal is shown. */
async function logIn(page: Page, email: string) {
  await submitLogin(page, email, password);
  await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
}

/** Log out is in the menu behind the trader's initials. */
async function logOut(page: Page) {
  await page.getByRole("button", { name: "User menu" }).click();
  await page.getByRole("button", { name: "Log out" }).click();
}

test("visitors who are not logged in are sent to the login page with the server chosen", async ({ page }) => {
  await page.goto("/");

  await expect(page).toHaveURL(/\/login$/);
  await expect(page).toHaveTitle(productName);
  await expect(page.getByRole("heading", { name: productName })).toBeVisible();
  // The only server is chosen for the trader.
  await expect(page.getByLabel("Server")).toHaveValue(server.id);
});

test("a firm with a portal has its traders log in there", async ({ page }) => {
  await page.goto("/login");

  const portal = page.getByRole("link", { name: `Log in through ${server.name}` });
  await expect(portal).toHaveAttribute("href", "http://localhost:3002/terminal");
  await expect(page.getByLabel("Password")).toHaveCount(0);
});

test("a wrong password is refused", async ({ page, request }) => {
  const trader = await createTrader(request, "refused");

  await submitLogin(page, trader.email, "not-the-password");

  // Next.js has an alert of its own for route announcements, so look inside the form.
  await expect(page.locator("form").getByRole("alert")).toHaveText("Wrong server, email or password.");
  await expect(page).toHaveURL(/\/login$/);
});

test("a trader logs in, buys, closes and sees the history", async ({ page, request }) => {
  const trader = await createTrader(request, "trader");

  await logIn(page, trader.email);
  await expect(page.getByText(trader.accountId, { exact: true })).toBeVisible();
  await expect(page.getByRole("banner").getByText(server.name, { exact: true })).toBeVisible();
  await expect(page.getByText("Live prices")).toBeVisible();

  // The buttons are enabled once prices have arrived.
  const buy = page.getByRole("button", { name: /^buy/i });
  await expect(buy).toBeEnabled();

  // The steppers set volume and stop loss without typing. A stop loss starts one pip from the bid.
  await page.getByRole("button", { name: "Raise volume" }).click();
  await page.getByRole("button", { name: "Lower volume" }).click();
  await expect(page.getByLabel("Volume (lots)", { exact: true })).toHaveValue("1.00");
  await page.getByRole("button", { name: "Lower stop loss" }).click();
  await expect(page.getByLabel("Stop loss", { exact: true })).toHaveValue(/^\d+\.\d{5}$/);

  await buy.click();
  await expect(page.getByRole("status")).toHaveText("Buy filled.");
  await expect(page.getByRole("tab", { name: "Positions (1)" })).toBeVisible();

  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByRole("tab", { name: "Positions", exact: true })).toBeVisible();

  await page.getByRole("tab", { name: "History" }).click();
  await expect(page.getByRole("cell", { name: "Manual" })).toBeVisible();

  await page.getByRole("tab", { name: "Events" }).click();
  await expect(page.getByText(/^Closed Buy 1\.00 EURUSD/)).toBeVisible();

  await logOut(page);
  await expect(page).toHaveURL(/\/login$/);
});

/** The stop loss cell of the only open position: its price and the estimated result there. */
async function stopLossCell(page: Page) {
  const row = page.getByRole("row").filter({ has: page.getByRole("button", { name: "Close", exact: true }) });
  const text = (await row.getByRole("cell").nth(6).innerText()).trim();
  const match = /^(\d+\.\d{5})\s+([+-][\d,]+\.\d{2})$/.exec(text);
  expect(match, `stop loss cell "${text}"`).not.toBeNull();
  return { price: Number(match![1]), amount: Number(match![2].replace(",", "")) };
}

/**
 * Finds a stop line on the chart the way a trader does: by moving the mouse until the cursor shows the line can be
 * dragged. Searches from the bottom, where a buy's stop loss is.
 */
async function findStopLine(page: Page) {
  const chart = page.locator("section").filter({ has: page.getByRole("button", { name: "Full screen" }) }).locator("canvas").first();
  const box = await chart.boundingBox();
  expect(box).not.toBeNull();
  const x = box!.x + box!.width / 2;
  for (let y = box!.y + box!.height - 2; y > box!.y; y -= 2) {
    await page.mouse.move(x, y);
    const cursor = await page.evaluate(([px, py]) => {
      const element = document.elementFromPoint(px, py);
      return element ? getComputedStyle(element).cursor : null;
    }, [x, y] as const);
    if (cursor === "ns-resize") {
      return { x, y };
    }
  }

  throw new Error("No stop line on the chart.");
}

test("stops are set as amounts, and the stop loss is dragged on the chart", async ({ page, request }) => {
  const trader = await createTrader(request, "stops");

  await logIn(page, trader.email);
  const buy = page.getByRole("button", { name: /^buy/i });
  await expect(buy).toBeEnabled();

  // A refused order keeps what was typed, so it can be corrected. A buy's stop loss must be below the price.
  await page.getByLabel("Stop loss", { exact: true }).fill("9.00000");
  await buy.click();
  await expect(page.getByRole("status")).toHaveText("Refused: the stop loss is on the wrong side of the price");
  await expect(page.getByLabel("Stop loss", { exact: true })).toHaveValue("9.00000");

  // 100 USD of loss and 200 USD of profit become prices for each side before the order is sent.
  await page.getByRole("button", { name: "USD", exact: true }).click();
  await page.getByLabel("Stop loss", { exact: true }).fill("100");
  await page.getByLabel("Take profit", { exact: true }).fill("200");
  await expect(page.getByText(/^SL \d\.\d{5}$/)).toHaveCount(2);
  await expect(page.getByText(/^TP \d\.\d{5}$/)).toHaveCount(2);

  await buy.click();
  await expect(page.getByRole("status")).toHaveText("Buy filled.");
  // The order panel is cleared for the next order, except the volume.
  await expect(page.getByLabel("Stop loss", { exact: true })).toHaveValue("");
  await expect(page.getByLabel("Take profit", { exact: true })).toHaveValue("");
  await expect(page.getByLabel("Volume (lots)", { exact: true })).toHaveValue("1.00");
  // One point of EURUSD is worth 1 USD per lot. The price can move a little between the click and the fill.
  const before = await stopLossCell(page);
  expect(before.amount).toBeGreaterThan(-110);
  expect(before.amount).toBeLessThan(-90);

  // Dragging the line down moves the stop loss further away when the mouse is released.
  const line = await findStopLine(page);
  await page.mouse.down();
  await page.mouse.move(line.x, line.y + 40, { steps: 8 });
  await page.mouse.up();

  await expect.poll(async () => (await stopLossCell(page)).price).toBeLessThan(before.price);
  const after = await stopLossCell(page);
  expect(after.amount).toBeLessThan(before.amount);
  await page.getByRole("tab", { name: "Events" }).click();
  await expect(page.getByText(/^Changed stops on /)).toBeVisible();
});

test("a right click on the chart sets stops where the mouse is", async ({ page, request }) => {
  const trader = await createTrader(request, "menu");

  await logIn(page, trader.email);
  const buy = page.getByRole("button", { name: /^buy/i });
  await expect(buy).toBeEnabled();
  const chart = page.locator("section").filter({ has: page.getByRole("button", { name: "Full screen" }) }).locator("canvas").first();
  const box = (await chart.boundingBox())!;

  // The new order gets the level as a price.
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
  const menu = page.getByRole("menu");
  await expect(menu).toHaveAccessibleName(/^At \d\.\d{5}$/);
  await menu.getByRole("group", { name: "New order" }).getByRole("menuitem", { name: "Take profit" }).click();
  await expect(menu).toHaveCount(0);
  await expect(page.getByLabel("Take profit", { exact: true })).toHaveValue(/^\d\.\d{5}$/);

  // The take profit's ghost line is now where the mouse is, and a right click on it removes it.
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
  await menu.getByRole("group", { name: "New order" }).getByRole("menuitem", { name: "Remove take profit" }).click();
  await expect(page.getByLabel("Take profit", { exact: true })).toHaveValue("");

  // An open buy gets a stop loss below the price. The menu names the position.
  await page.getByRole("button", { name: "Lower stop loss" }).click();
  await buy.click();
  await expect(page.getByRole("status")).toHaveText("Buy filled.");
  const before = await stopLossCell(page);
  const line = await findStopLine(page);
  await page.mouse.click(line.x, line.y + 20, { button: "right" });
  await menu.getByRole("group", { name: /^Buy 1\.00/ }).getByRole("menuitem", { name: /^Move stop loss here/ }).click();
  await expect.poll(async () => (await stopLossCell(page)).price).toBeLessThan(before.price);

  // A right click on the stop loss line itself removes it.
  const moved = await findStopLine(page);
  await page.mouse.click(moved.x, moved.y, { button: "right" });
  await menu.getByRole("group", { name: /^Buy 1\.00/ }).getByRole("menuitem", { name: "Remove stop loss" }).click();
  const positionRow = page.getByRole("row").filter({ has: page.getByRole("button", { name: "Close", exact: true }) });
  await expect(positionRow.getByRole("cell").nth(6)).toHaveText("-");

  // Reset chart closes the menu like any choice. Escape closes it without one.
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
  await menu.getByRole("menuitem", { name: /^Reset chart/ }).click();
  await expect(menu).toHaveCount(0);
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
  await expect(menu).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(menu).toHaveCount(0);
});

test("the account is shown as the firm's portal names it, with its target and limits", async ({ page, request }) => {
  const trader = await createTrader(request, "described");
  const detailsUrl = "http://localhost:3002/accounts/described";
  const details = await request.put(`${serviceUrl}/api/admin/v1/accounts/${trader.accountId}/details`, {
    headers: adminHeaders,
    data: { label: "#1001 Two-step 100K \u00b7 Phase 1", profitTarget: 110_000, timeZone: "Europe/Stockholm", detailsUrl },
  });
  expect(details.ok()).toBeTruthy();
  await setFloor(request, trader.accountId, "daily", 95_000);

  await logIn(page, trader.email);
  const banner = page.getByRole("banner");
  await expect(banner.getByText("#1001 Two-step 100K \u00b7 Phase 1", { exact: true })).toBeVisible();
  await expect(banner.getByText("10,000.00 to go")).toBeVisible();
  await expect(banner.getByText("Daily loss limit")).toBeVisible();
  await expect(banner.getByText("5,000.00 left")).toBeVisible();
  await expect(banner.getByRole("link", { name: `Back to ${server.name}` })).toHaveAttribute("href", detailsUrl);
  // Every time is in the zone of the account's trading day, and says so.
  await expect(page.getByRole("contentinfo")).toContainText("Stockholm time");

  // A broken limit ends trading, which the terminal says plainly, without a negative amount left.
  await setFloor(request, trader.accountId, "max-loss", 100_001);
  const notice = page.getByRole("alert").filter({ hasText: "Trading on this account has ended" });
  await expect(notice).toContainText(/The max loss limit was broken at \d\d:\d\d:\d\d Stockholm time: equity 100,000\.00 fell below 100,001\.00\./);
  await expect(notice.getByRole("link", { name: `See the account at ${server.name}` })).toHaveAttribute("href", detailsUrl);
  await expect(banner.getByText("Broken")).toBeVisible();
  await expect(banner.getByText(/-[\d,.]+ left/)).toHaveCount(0);

  await page.getByRole("tab", { name: "Events" }).click();
  await expect(page.getByText("Trading ended: a loss limit was broken")).toBeVisible();
});

test("the watchlist is searched and filtered", async ({ page, request }) => {
  const trader = await createTrader(request, "watchlist");

  await logIn(page, trader.email);
  const watchlist = page.getByRole("complementary").filter({ has: page.getByRole("heading", { name: "Watchlist" }) });
  await expect(watchlist.getByRole("button", { name: "EURUSD", exact: true })).toBeVisible();

  await watchlist.getByLabel("Search symbols").fill("xau");
  await expect(watchlist.getByRole("button", { name: "XAUUSD", exact: true })).toBeVisible();
  await expect(watchlist.getByRole("button", { name: "EURUSD", exact: true })).toHaveCount(0);

  // A starred symbol stays under Favorites.
  await watchlist.getByLabel("Search symbols").fill("");
  await watchlist.getByRole("button", { name: "Add GBPUSD to favorites" }).click();
  await watchlist.getByRole("button", { name: "Favorites", exact: true }).click();
  await expect(watchlist.getByRole("button", { name: "GBPUSD", exact: true })).toBeVisible();
  await expect(watchlist.getByRole("button", { name: "EURUSD", exact: true })).toHaveCount(0);

  // Choosing a symbol shows it in the order panel.
  await watchlist.getByRole("button", { name: "GBPUSD", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Trade GBPUSD" })).toBeVisible();
});

test("a trader only sees their own account", async ({ page, request }) => {
  const mine = await createTrader(request, "mine");
  const theirs = await createTrader(request, "theirs");

  await logIn(page, mine.email);
  await page.goto(`/?account=${theirs.accountId}`);

  // An account the trader does not own is ignored, and their own is shown instead.
  await expect(page.getByText(mine.accountId, { exact: true })).toBeVisible();
  await expect(page.getByText(theirs.accountId, { exact: true })).toHaveCount(0);
});

test("a trader with several accounts switches between them", async ({ page, request }) => {
  const trader = await createTrader(request, "several");
  const second = `${trader.accountId}-2`;
  const account = await request.post(`${serviceUrl}/api/admin/v1/accounts`, {
    headers: adminHeaders,
    data: { accountId: second, groupId: "standard", initialBalance: 50_000, ownerUserId: trader.userId },
  });
  expect(account.ok()).toBeTruthy();

  await logIn(page, trader.email);
  await page.getByLabel("Account").selectOption(second);

  await expect(page).toHaveURL(new RegExp(`account=${second}$`));
  // The status bar shows equity with the currency too, so look at the balance in the account bar.
  await expect(page.getByRole("banner").getByText("50,000.00 USD")).toBeVisible();
});

test("a link from the firm's portal logs the trader straight in, once", async ({ page, request }) => {
  const trader = await createTrader(request, "linked");
  const response = await request.post(`${serviceUrl}/api/admin/v1/users/${trader.userId}/login-links`, {
    headers: adminHeaders,
    data: { accountId: trader.accountId },
  });
  expect(response.ok()).toBeTruthy();
  const { url } = await response.json();

  await page.goto(url);
  await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
  await expect(page.getByText(trader.accountId, { exact: true })).toBeVisible();
  // The token is gone from the address once it is used.
  expect(page.url()).not.toContain("token=");

  await logOut(page);
  await expect(page).toHaveURL(/\/login$/);
  await page.goto(url);
  // Next.js has an alert of its own for route announcements, so look inside the page.
  await expect(page.locator("main").getByRole("alert")).toContainText("This link has expired or was already used.");
});
