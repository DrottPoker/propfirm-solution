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
  await page.getByLabel("Password", { exact: true }).fill(withPassword);
  await page.getByRole("button", { name: "Log in" }).click();
}

/** Logs in and waits until the terminal is shown. */
async function logIn(page: Page, email: string) {
  await submitLogin(page, email, password);
  await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
}

/** The note in the corner when a buy of 1 lot of EURUSD fills, for example "Bought 1.00 EURUSD at 1.08724". */
const boughtNote = /^Bought 1\.00 EURUSD at \d\.\d{5}$/;

/**
 * Steps a buy's stop loss 20 pips below the bid. The synthetic prices move a few points a second, so a stop loss one
 * pip away could close the position before the test is done with it.
 */
async function lowerStopLoss(page: Page) {
  const lower = page.getByRole("button", { name: "Lower stop loss" });
  for (let pip = 0; pip < 20; pip++) {
    await lower.click();
  }
}

/** Settings are in the menu behind the trader's initials, one tab at a time. */
async function openSettings(page: Page, tab: "Chart" | "Sounds" | "Trading") {
  await page.getByRole("button", { name: "User menu" }).click();
  await page.getByRole("button", { name: "Settings" }).click();
  const dialog = page.getByRole("dialog", { name: "Settings" });
  await dialog.getByRole("tab", { name: tab }).click();
  return dialog;
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
  await expect(page.getByLabel("Password", { exact: true })).toHaveCount(0);
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
  await expect(page.getByRole("contentinfo").getByText("Live", { exact: true })).toBeVisible();

  // The buttons are enabled once prices have arrived.
  const buy = page.getByRole("button", { name: /^buy/i });
  await expect(buy).toBeEnabled();

  // The steppers set volume and stop loss without typing. A stop loss starts from the bid and moves a pip a step.
  await page.getByRole("button", { name: "Raise volume" }).click();
  await page.getByRole("button", { name: "Lower volume" }).click();
  await expect(page.getByLabel("Volume (lots)", { exact: true })).toHaveValue("1.00");
  await lowerStopLoss(page);
  await expect(page.getByLabel("Stop loss", { exact: true })).toHaveValue(/^\d+\.\d{5}$/);

  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  await expect(page.getByRole("tab", { name: "Positions (1)" })).toBeVisible();
  // The position's id is no column of its own, only a tooltip on the symbol.
  await expect(page.getByRole("columnheader", { name: "Position" })).toHaveCount(0);
  await expect(page.getByRole("cell", { name: "EURUSD", exact: true })).toHaveAttribute("title", /^Position [0-9a-f-]{36}$/);

  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByText(/^Buy 1\.00 EURUSD closed at \d\.\d{5}$/)).toBeVisible();
  await expect(page.getByRole("tab", { name: "Positions", exact: true })).toBeVisible();

  await page.getByRole("tab", { name: "History" }).click();
  await expect(page.getByRole("cell", { name: "Manual" })).toBeVisible();

  await page.getByRole("tab", { name: "Events" }).click();
  await expect(page.getByText(/^Closed Buy 1\.00 EURUSD/)).toBeVisible();

  // The sound on fills is off until the trader turns it on in Settings, and stays as chosen.
  const sound = (await openSettings(page, "Sounds")).getByRole("switch", { name: "Sound on fills" });
  await expect(sound).toHaveAttribute("aria-checked", "false");
  await sound.click();
  await expect(sound).toHaveAttribute("aria-checked", "true");
  await page.keyboard.press("Escape");
  await page.reload();
  await expect((await openSettings(page, "Sounds")).getByRole("switch", { name: "Sound on fills" })).toHaveAttribute("aria-checked", "true");
  await page.keyboard.press("Escape");

  await logOut(page);
  await expect(page).toHaveURL(/\/login$/);
});

/** The stop loss cell of the only open position: its price and the estimated result there. */
async function stopLossCell(page: Page) {
  const row = page.getByRole("row").filter({ has: page.getByRole("button", { name: "Close", exact: true }) });
  const text = (await row.getByRole("cell").nth(5).innerText()).trim();
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
  await expect(page.getByText("Refused: the stop loss is on the wrong side of the price", { exact: true })).toBeVisible();
  await expect(page.getByLabel("Stop loss", { exact: true })).toHaveValue("9.00000");

  // 100 USD of loss and 200 USD of profit become prices for each side before the order is sent.
  await page.getByRole("group", { name: "Stops in" }).getByRole("button", { name: "USD", exact: true }).click();
  await page.getByLabel("Stop loss", { exact: true }).fill("100");
  await page.getByLabel("Take profit", { exact: true }).fill("200");
  await expect(page.getByText(/^SL \d\.\d{5}$/)).toHaveCount(2);
  await expect(page.getByText(/^TP \d\.\d{5}$/)).toHaveCount(2);

  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
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

  // The take profit's ghost line is now where the mouse is, and a right click on it removes it. New prices can rescale
  // the chart between the clicks and move the line away from the mouse, so a missed line is put back under it first.
  const newOrder = menu.getByRole("group", { name: "New order" });
  await expect(async () => {
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
    await expect(menu).toBeVisible();
    const remove = newOrder.getByRole("menuitem", { name: "Remove take profit" });
    if ((await remove.count()) === 0) {
      await newOrder.getByRole("menuitem", { name: "Take profit", exact: true }).click();
      throw new Error("The take profit moved away from the mouse.");
    }

    await remove.click();
  }).toPass({ timeout: 20_000 });
  await expect(page.getByLabel("Take profit", { exact: true })).toHaveValue("");

  // An open buy gets a stop loss below the price. The menu names the position.
  await lowerStopLoss(page);
  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
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
  await expect(positionRow.getByRole("cell").nth(5)).toHaveText("-");

  // Reset chart closes the menu like any choice. Escape closes it without one.
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
  await menu.getByRole("menuitem", { name: /^Reset chart/ }).click();
  await expect(menu).toHaveCount(0);
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
  await expect(menu).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(menu).toHaveCount(0);
});

test("the order ticket says what the order means and remembers the volume per symbol", async ({ page, request }) => {
  const trader = await createTrader(request, "summary");
  await setFloor(request, trader.accountId, "daily", 95_000);

  await logIn(page, trader.email);
  await expect(page.getByRole("button", { name: /^buy/i })).toBeEnabled();
  const summary = page.getByRole("region", { name: "What this order means" });
  await expect(summary).toContainText(/Margin\s*[\d,]+\.\d{2} USD/);
  await expect(summary).toContainText(/Pip value\s*10\.00 USD/);
  await expect(summary).toContainText("Set a stop loss to see what the order risks.");

  // Half a lot with a stop loss a pip below the bid risks the spread and a pip, a tiny share of the 5,000.00 left.
  const volume = page.getByLabel("Volume (lots)", { exact: true });
  await volume.fill("0.50");
  await expect(summary).toContainText(/Pip value\s*5\.00 USD/);
  await page.getByRole("button", { name: "Lower stop loss" }).click();
  await expect(summary).toContainText(/Risks \d+\.\d{2} USD, (under 0\.1|\d\.\d)% of today's room/);

  // Gold starts from the default, and EURUSD from the volume chosen for it, also after a reload.
  const watchlist = page.getByRole("complementary").filter({ has: page.getByRole("heading", { name: "Watchlist" }) });
  await watchlist.getByRole("button", { name: "XAUUSD", exact: true }).click();
  await expect(volume).toHaveValue("1.00");
  await watchlist.getByRole("button", { name: "EURUSD", exact: true }).click();
  await expect(volume).toHaveValue("0.50");

  // The volume under the chart can be hidden, and stays hidden.
  const volumeToggle = page.getByRole("button", { name: "Volume", exact: true });
  await expect(volumeToggle).toHaveAttribute("aria-pressed", "true");
  await volumeToggle.click();
  await expect(volumeToggle).toHaveAttribute("aria-pressed", "false");

  await page.reload();
  await expect(page.getByLabel("Volume (lots)", { exact: true })).toHaveValue("0.50");
  await expect(page.getByRole("button", { name: "Volume", exact: true })).toHaveAttribute("aria-pressed", "false");
});

test("the account is shown as the firm's portal names it, with its target and limits", async ({ page, request }) => {
  const trader = await createTrader(request, "described");
  const detailsUrl = "http://localhost:3002/accounts/described";
  const details = await request.put(`${serviceUrl}/api/admin/v1/accounts/${trader.accountId}/details`, {
    headers: adminHeaders,
    data: { label: "#1001 Two-step 100K, Phase 1", profitTarget: 110_000, timeZone: "Europe/Stockholm", detailsUrl },
  });
  expect(details.ok()).toBeTruthy();
  await setFloor(request, trader.accountId, "daily", 95_000);

  await logIn(page, trader.email);
  const banner = page.getByRole("banner");
  await expect(banner.getByText("#1001 Two-step 100K, Phase 1", { exact: true })).toBeVisible();
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

test("a position closes in parts with a trailing stop, and every position closes at once", async ({ page, request }) => {
  const trader = await createTrader(request, "parts");
  await logIn(page, trader.email);
  const buy = page.getByRole("button", { name: /^buy/i });
  await expect(buy).toBeEnabled();

  // A trailing stop follows a stop loss, so it needs one.
  await page.getByLabel("Trailing stop").check();
  await buy.click();
  await expect(page.getByText("Set a stop loss for the trailing stop to follow.")).toBeVisible();

  await lowerStopLoss(page);
  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  const position = page.getByRole("row").filter({ has: page.getByRole("button", { name: "Close", exact: true }) });
  await expect(position.getByText("Trail", { exact: true })).toBeVisible();

  // A part starts from half the position, and the rest stays open.
  await position.getByRole("button", { name: "Close part" }).click();
  await expect(page.getByLabel("Close", { exact: true })).toHaveValue("0.50");
  await page.getByRole("button", { name: "Close part" }).click();
  await expect(page.getByText(/^0\.50 of Buy EURUSD closed at \d\.\d{5}, 0\.50 still open$/)).toBeVisible();
  await expect(position.getByRole("cell").nth(2)).toHaveText("0.50");

  // A second position, and both close at once on a second click.
  await page.getByLabel("Trailing stop").uncheck();
  await buy.click();
  await expect(page.getByRole("tab", { name: "Positions (2)" })).toBeVisible();
  await page.getByRole("button", { name: "Close all" }).click();
  await page.getByRole("button", { name: "Close all 2?" }).click();
  await expect(page.getByRole("tab", { name: "Positions", exact: true })).toBeVisible();

  await page.getByRole("tab", { name: "History" }).click();
  await expect(page.getByRole("cell", { name: "Part closed" })).toBeVisible();
});

test("a pending order is moved in the orders tab", async ({ page, request }) => {
  const trader = await createTrader(request, "pending");
  await logIn(page, trader.email);
  await expect(page.getByRole("button", { name: /^buy/i })).toBeEnabled();

  // A buy limit 20 pips below the bid.
  await page.getByRole("button", { name: "Limit", exact: true }).click();
  const lower = page.getByRole("button", { name: "Lower limit price" });
  for (let pip = 0; pip < 20; pip++) {
    await lower.click();
  }
  await page.getByRole("button", { name: /^buy/i }).click();
  await expect(page.getByText(/^Buy limit 1\.00 EURUSD placed at \d\.\d{5}$/)).toBeVisible();

  await page.getByRole("tab", { name: "Orders (1)" }).click();
  await page.getByRole("button", { name: "Edit" }).click();
  const price = page.getByLabel("Limit price", { exact: true }).last();
  const moved = (Number(await price.inputValue()) - 0.001).toFixed(5);
  await price.fill(moved);
  await page.getByRole("button", { name: "Save" }).click();

  await expect(page.getByText(`EURUSD order moved to ${moved}`)).toBeVisible();
  await expect(page.getByRole("cell", { name: moved, exact: true })).toBeVisible();
});

test("indicators and drawings stay on the chart", async ({ page, request }) => {
  const trader = await createTrader(request, "studies");
  await logIn(page, trader.email);

  await page.getByRole("button", { name: "Indicators" }).click();
  const menu = page.getByRole("dialog", { name: "Indicators" });
  await menu.getByRole("button", { name: "Moving average", exact: true }).click();
  await menu.getByRole("button", { name: "RSI", exact: true }).click();
  await menu.getByLabel("Moving average period").fill("50");
  await menu.getByLabel("Moving average period").press("Enter");
  await expect(menu.getByText("SMA 50")).toBeVisible();
  await expect(menu.getByText("RSI 14")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Indicators (2)" })).toBeVisible();

  // A horizontal line where the trader clicks on the candles.
  const chart = page.locator("section").filter({ has: page.getByRole("button", { name: "Full screen" }) }).locator("canvas").first();
  const box = await chart.boundingBox();
  expect(box).not.toBeNull();
  await page.getByRole("button", { name: "Horizontal line" }).click();
  await expect(page.getByText(/^Horizontal line: Click where the line goes\./)).toBeVisible();
  await page.mouse.click(box!.x + box!.width / 2, box!.y + box!.height / 3);
  await expect(page.getByRole("button", { name: "Remove the selected drawing" })).toBeEnabled();

  // Both are kept on the device.
  await page.reload();
  await expect(page.getByRole("button", { name: "Indicators (2)" })).toBeVisible();
  const removeAll = page.getByRole("button", { name: "Remove all drawings" });
  await expect(removeAll).toBeEnabled();

  // Removing every drawing takes a second click.
  await removeAll.click();
  await removeAll.click();
  await expect(removeAll).toBeDisabled();
});

test("the rulebook shows the firm's rules and warns as a deadline comes close", async ({ page, request }) => {
  const trader = await createTrader(request, "rules");
  await setFloor(request, trader.accountId, "daily", 95_000);
  const hour = 3_600_000;
  const tellRules = async (passBy: number) => {
    const response = await request.put(`${serviceUrl}/api/admin/v1/accounts/${trader.accountId}/rules`, {
      headers: adminHeaders,
      data: { tradingDaysRequired: 4, tradingDaysCounted: 1, passBy: new Date(passBy).toISOString(), openPositionBy: new Date(Date.now() + 480 * hour).toISOString() },
    });
    expect(response.ok()).toBeTruthy();
  };
  await tellRules(Date.now() + 240 * hour);

  await logIn(page, trader.email);
  await page.getByRole("button", { name: "Rules" }).click();
  const rulebook = page.getByRole("dialog", { name: "Rules" });
  await expect(rulebook.locator("[data-rule='floor-daily']")).toContainText("5,000.00 left");
  await expect(rulebook.locator("[data-rule='trading-days']")).toContainText("1 of 4");
  await expect(rulebook.locator("[data-rule='pass-by']")).toContainText(/\d+ days left/);
  await expect(rulebook.locator("[data-rule='open-by']")).toContainText(/\d+ days left/);
  await expect(page.getByTestId("rules-attention")).toHaveCount(0);
  await page.keyboard.press("Escape");

  // The firm's system tells a deadline less than a day away, and the trader is warned at once.
  await tellRules(Date.now() + 20 * hour);
  await expect(page.locator("[data-sonner-toast]").filter({ hasText: "Pass the stage by" })).toContainText(/\d+ h left\. After that the stage fails\./);
  await expect(page.getByTestId("rules-attention")).toBeVisible();
});

test("the trader sets limits of their own, and locking the rest of the day takes no new orders", async ({ page, request }) => {
  const trader = await createTrader(request, "limits");
  const day = await request.put(`${serviceUrl}/api/admin/v1/accounts/${trader.accountId}/trading-day`, {
    headers: adminHeaders,
    data: { timeZone: "Europe/Stockholm", startsAt: "00:00:00" },
  });
  expect(day.ok()).toBeTruthy();
  await logIn(page, trader.email);
  const buy = page.getByRole("button", { name: /^buy/i });
  await expect(buy).toBeEnabled();

  await page.getByRole("button", { name: "Rules" }).click();
  await page.getByRole("dialog", { name: "Rules" }).getByRole("button", { name: "Set your limits" }).click();
  const sheet = page.getByRole("dialog", { name: /^Your limits/ });
  await sheet.getByLabel("Daily loss limit in USD").fill("1500");
  await sheet.getByLabel("Most trades a day").fill("6");
  await sheet.getByRole("button", { name: "Save limits" }).click();
  await expect(page.locator("[data-sonner-toast]").filter({ hasText: "Your limits are saved" })).toContainText("They apply now.");
  await expect(page.getByRole("banner")).toContainText("Your daily limit98,500.00");

  // A looser limit waits for the next trading day.
  await page.getByRole("button", { name: "Rules" }).click();
  const rulebook = page.getByRole("dialog", { name: "Rules" });
  await expect(rulebook.locator("[data-own-limit='loss']")).toContainText("Daily loss limit, 1,500.00");
  await expect(rulebook.locator("[data-own-limit='trades']")).toContainText("0 of 6");
  await rulebook.getByRole("button", { name: "Change your limits" }).click();
  await sheet.getByLabel("Daily loss limit in USD").fill("2000");
  await expect(sheet.getByTestId("limits-later")).toHaveText("From 00:00: daily loss limit 2,000.00");
  await sheet.getByRole("button", { name: "Save limits" }).click();
  await expect(page.locator("[data-sonner-toast]").filter({ hasText: "Your limits are saved" })).toContainText("The looser ones apply from 00:00.");
  await expect(page.getByRole("banner")).toContainText("Your daily limit98,500.00");

  // Locking the rest of the day closes the open position, and new orders are not taken.
  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  await page.getByRole("button", { name: "Rules" }).click();
  await page.getByRole("dialog", { name: "Rules" }).getByRole("button", { name: "Lock the rest of the day" }).click();
  const lock = page.getByRole("dialog", { name: "Lock trading until 00:00?" });
  await expect(lock).toContainText("Your open position");
  await lock.getByRole("button", { name: "Close and lock until 00:00" }).click();
  await expect(page.getByTestId("own-lock")).toContainText(/^You locked the rest of the day at \d\d:\d\d:\d\d/);
  await expect(page.getByRole("tab", { name: "Positions", exact: true })).toBeVisible();
  await expect(page.getByRole("note").filter({ hasText: "You locked the rest of the day, until 00:00 Stockholm time." })).toBeVisible();
  await expect(buy).toBeDisabled();
});

test("an order is sized from what it risks at its stop loss", async ({ page, request }) => {
  const trader = await createTrader(request, "risk");
  await logIn(page, trader.email);
  await expect(page.getByRole("button", { name: /^buy/i })).toBeEnabled();

  await page.getByRole("group", { name: "Size in" }).getByRole("button", { name: "USD" }).click();
  await page.getByLabel("Risk (USD)", { exact: true }).fill("200");
  await expect(page.getByRole("status").filter({ hasText: "Set a stop loss to size the order from the risk." })).toBeVisible();

  // 20 pips and the spread on EURUSD are worth about 210 USD on a lot, so 200 USD is a little less than a lot.
  await lowerStopLoss(page);
  await expect(page.getByRole("status").filter({ hasText: "lots, risks" })).toContainText(/^= 0\.\d\d lots, risks 1\d\d\.\d\d USD$/);
  await page.getByRole("button", { name: /^buy/i }).click();
  await expect(page.getByText(/^Bought 0\.\d\d EURUSD at \d\.\d{5}$/)).toBeVisible();

  // The ticket keeps sizing from risk.
  await page.reload();
  await expect(page.getByLabel("Risk (USD)", { exact: true })).toHaveValue("200");
});

test("the trader's settings change the chart and ask before an order goes", async ({ page, request }) => {
  const trader = await createTrader(request, "settings");
  await logIn(page, trader.email);

  const settings = await openSettings(page, "Chart");
  await settings.getByRole("radio", { name: "Blue and orange" }).click();
  await expect(settings.getByRole("radio", { name: "Blue and orange" })).toHaveAttribute("aria-checked", "true");
  await settings.getByRole("switch", { name: "Ask price line" }).click();
  await settings.getByRole("tab", { name: "Trading" }).click();
  await settings.getByRole("switch", { name: "Ask before placing an order" }).click();
  await settings.getByRole("button", { name: "Done" }).click();
  await expect(settings).toBeHidden();

  // The order panel asks first, and Cancel sends nothing.
  const buy = page.getByRole("button", { name: /^buy/i });
  await expect(buy).toBeEnabled();
  await buy.click();
  const question = page.getByRole("alertdialog", { name: "Buy 1.00 EURUSD at market?" });
  await expect(question).toContainText("No stop loss or take profit.");
  await question.getByRole("button", { name: "Cancel" }).click();
  await expect(question).toBeHidden();
  await expect(page.getByRole("tab", { name: "Positions", exact: true })).toBeVisible();

  await buy.click();
  await page.getByRole("button", { name: "Confirm buy" }).click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  await expect(page.getByRole("tab", { name: "Positions (1)" })).toBeVisible();

  // The choices stay after a reload, and a reset needs a second click.
  await page.reload();
  const again = await openSettings(page, "Chart");
  await expect(again.getByRole("radio", { name: "Blue and orange" })).toHaveAttribute("aria-checked", "true");
  await again.getByRole("button", { name: "Reset to defaults" }).click();
  await again.getByRole("button", { name: "Click again to reset everything" }).click();
  await expect(again.getByRole("radio", { name: "Green and red" })).toHaveAttribute("aria-checked", "true");
  await again.getByRole("tab", { name: "Trading" }).click();
  await expect(again.getByRole("switch", { name: "Ask before placing an order" })).toHaveAttribute("aria-checked", "false");
});

test("the trader's choices follow them to another device", async ({ page, request, browser }) => {
  const trader = await createTrader(request, "devices");
  await logIn(page, trader.email);
  const watchlist = page.getByRole("complementary").filter({ has: page.getByRole("heading", { name: "Watchlist" }) });
  await watchlist.getByRole("button", { name: "Add XAUUSD to favorites" }).first().click();
  await page.getByRole("button", { name: "Indicators" }).click();
  await page.getByRole("dialog", { name: "Indicators" }).getByRole("button", { name: "RSI", exact: true }).click();
  await page.keyboard.press("Escape");
  await (await openSettings(page, "Sounds")).getByRole("switch", { name: "Sound on warnings" }).click();
  await page.keyboard.press("Escape");
  // Logging out sends what is not sent yet.
  await logOut(page);
  await expect(page).toHaveURL(/\/login$/);

  const other = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  const device = await other.newPage();
  await logIn(device, trader.email);
  await expect(device.getByRole("button", { name: "Remove XAUUSD from favorites" }).first()).toBeVisible();
  await expect(device.getByRole("button", { name: "Indicators (1)" })).toBeVisible();
  await expect((await openSettings(device, "Sounds")).getByRole("switch", { name: "Sound on warnings" })).toHaveAttribute("aria-checked", "false");
  await other.close();
});

test("a trade's details show the feed's price behind each fill", async ({ page, request }) => {
  const trader = await createTrader(request, "details");
  await logIn(page, trader.email);
  const buy = page.getByRole("button", { name: /^buy/i });
  await expect(buy).toBeEnabled();
  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByText(/^Buy 1\.00 EURUSD closed at \d\.\d{5}$/)).toBeVisible();

  await page.getByRole("tab", { name: "History" }).click();
  await page.getByRole("button", { name: "Details" }).click();
  const details = page.getByRole("dialog", { name: "Trade details: Buy 1.00 EURUSD" });
  await expect(details.getByRole("region", { name: "Opened" })).toContainText("Price from the feed");
  // The standard group raises EURUSD's ask by a point and lowers its bid by one.
  await expect(details.getByRole("region", { name: "Opened" })).toContainText("The firm's markup: 1 point on the ask.");
  await expect(details.getByRole("region", { name: "Closed" })).toContainText("The firm's markup: 1 point on the bid.");
  await expect(details).toContainText(/Result after commission\s*-?[\d,]+\.\d{2} USD/);
  await expect(details).toContainText(/journal, entries [\d,]+ and [\d,]+/);

  await page.keyboard.press("Escape");
  await expect(details).toBeHidden();
});

test("a broken loss limit has a report with the prices that broke it", async ({ page, request }) => {
  const trader = await createTrader(request, "breach");
  await logIn(page, trader.email);
  const buy = page.getByRole("button", { name: /^buy/i });
  await expect(buy).toBeEnabled();
  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();

  await setFloor(request, trader.accountId, "max-loss", 100_001);
  await page.getByRole("button", { name: "See the breach report" }).click();
  const report = page.getByRole("dialog", { name: "Breach report: The max loss limit was broken" });
  await expect(report).toContainText("Equity, price by price");
  await expect(report.getByRole("region", { name: "The prices that broke it" })).toContainText(/EURUSD\s*\d\.\d{5} \/ \d\.\d{5}\s*2 pts/);
  await expect(report.getByRole("region", { name: "Open positions at that moment" })).toContainText(/Buy 1\.00 EURUSD/);
  await expect(report.getByRole("region", { name: "Step by step" })).toContainText("Max loss limit broken: equity");
  await expect(report.getByRole("region", { name: "Step by step" })).toContainText("Trading ended: a loss limit was broken");
});

test("the firm's notice shows at the top of its traders' terminals", async ({ page, request }) => {
  const trader = await createTrader(request, "notice");
  await logIn(page, trader.email);
  await expect(page.getByRole("button", { name: /^buy/i })).toBeEnabled();

  const set = await request.put(`${serviceUrl}/api/admin/v1/notice`, {
    headers: adminHeaders,
    data: { title: "Price feed outage", text: "No prices since 15:12. We reinstate accounts that broke a limit because of it.", level: "Warning", url: "http://localhost:3002/status" },
  });
  expect(set.ok()).toBeTruthy();
  const notice = page.getByRole("alert").filter({ hasText: "Price feed outage" });
  await expect(notice).toContainText("We reinstate accounts that broke a limit because of it.");
  await expect(notice.getByRole("link", { name: "Read more" })).toHaveAttribute("href", "http://localhost:3002/status");

  const removed = await request.delete(`${serviceUrl}/api/admin/v1/notice`, { headers: adminHeaders });
  expect(removed.ok()).toBeTruthy();
  await expect(notice).toBeHidden();
});
