import { expect, test, type APIRequestContext, type Locator, type Page } from "@playwright/test";

import { servicePort } from "../playwright.config";

const serviceUrl = `http://localhost:${servicePort}`;

// The development firm's admin key and server, from appsettings.Development.json.
const adminHeaders = { "X-Api-Key": "dev-admin-key" };
const server = { id: "demo-firm", name: "Demo Firm" };
const productName = "Kronant Trader";

const password = "e2e-password";

// An error the page does not catch fails the test, also when what the test looks at still works. So does anything the
// page's security policy refuses (ADR 0060), since a rule that is too tight breaks the terminal only where it is used.
const pageErrors = new WeakMap<Page, string[]>();
test.beforeEach(({ page }) => {
  const errors: string[] = [];
  pageErrors.set(page, errors);
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error" && message.text().includes("Content Security Policy")) {
      errors.push(message.text());
    }
  });
});
test.afterEach(({ page }) => {
  expect(pageErrors.get(page) ?? []).toEqual([]);
});

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

// The common login finds the firm from the email and the password (ADR 0058).
async function submitLogin(page: Page, email: string, withPassword: string) {
  await page.goto("/login");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password", { exact: true }).fill(withPassword);
  await page.getByRole("button", { name: "Log in" }).click();
}

/** Logs in and waits until the terminal is shown. A first login has the tour, which the tests skip. */
async function logIn(page: Page, email: string) {
  await submitLogin(page, email, password);
  await expect(page.getByRole("button", { name: "User menu" })).toBeVisible();
  await page
    .getByRole("button", { name: "Skip the tour" })
    .click({ timeout: 5_000 })
    .catch(() => undefined);
}

/** The account bar has the account's name twice, under the firm for a phone and beside the figures, one of them hidden. */
const shown = (locator: Locator) => locator.filter({ visible: true });

const orderPanel = (page: Page) => page.getByRole("complementary", { name: "Order panel" });
const buyButton = (page: Page) => orderPanel(page).getByRole("button", { name: /^Buy/ });
const watchlistOf = (page: Page) => page.getByRole("complementary", { name: "Watchlist" });
const positionsTab = (page: Page, count?: number) => page.getByRole("tab", { name: count === undefined ? /^Positions$/ : new RegExp(`^Positions\\s*${count}$`) });

/** A ticket starts at the smallest volume (ADR 0058), so tests that trade a lot set it. */
async function setVolume(page: Page, volume: string) {
  await orderPanel(page).getByLabel("Volume", { exact: true }).fill(volume);
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
async function openSettings(page: Page, tab: "Display" | "Chart" | "Sounds" | "Trading") {
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

/** The open position's row in the table, by its Close button. */
const positionRow = (page: Page) => page.getByRole("row").filter({ has: page.getByRole("button", { name: "Close", exact: true }) });

/** The stop loss cell of the only open position: its price and the estimated result there. */
async function stopLossCell(page: Page) {
  const text = (await positionRow(page).getByRole("cell").nth(6).innerText()).trim();
  const match = /^(\d+\.\d{5})\s*([+-][\d,]+\.\d{2})$/.exec(text);
  expect(match, `stop loss cell "${text}"`).not.toBeNull();
  return { price: Number(match![1]), amount: Number(match![2].replace(",", "")) };
}

const chartCanvas = (page: Page) => page.locator("section").filter({ has: page.getByRole("button", { name: "Full screen" }) }).locator("canvas").first();

/**
 * Finds a stop line on the chart the way a trader does: by moving the mouse until the cursor shows the line can be
 * dragged. Searches from the bottom, where a buy's stop loss is.
 */
async function findStopLine(page: Page) {
  const box = await chartCanvas(page).boundingBox();
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

/**
 * Lets the price scale take in every line, since the scale follows the candles and a stop far away can be beyond the
 * view. Such a line is told by a tag at the chart's edge, a buy's stop loss at the bottom right, and a press on the tag
 * shows them all. Without a tag the press does nothing.
 */
async function showAllLines(page: Page) {
  const box = (await chartCanvas(page).boundingBox())!;
  await page.mouse.click(box.x + box.width - 14, box.y + box.height - 18);
}

test("visitors who are not logged in are sent to the login page, which lists no firms", async ({ page }) => {
  await page.goto("/");

  await expect(page).toHaveURL(/\/login$/);
  await expect(page).toHaveTitle(productName);
  await expect(page.getByRole("heading", { name: productName })).toBeVisible();
  await expect(page.getByLabel("Email")).toBeVisible();

  // A firm is found by its name, never from a list of every firm.
  await page.getByRole("button", { name: "Find your firm" }).click();
  await page.getByLabel("The firm's name").fill("demo");
  await page.getByRole("button", { name: /^Demo Firm/ }).click();
  await expect(page.getByText(server.name, { exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: `Log in on ${server.name}'s website instead` })).toHaveAttribute("href", "http://localhost:3002/terminal");
});

test("a firm's own login page shows the firm and its website", async ({ page }) => {
  await page.goto(`/login?server=${server.id}`);

  await expect(page.getByRole("heading", { name: "Log in to the trading terminal" })).toBeVisible();
  await expect(page.getByRole("link", { name: `Log in on ${server.name}'s website instead` })).toHaveAttribute("href", "http://localhost:3002/terminal");
  await page.getByRole("button", { name: `Not with ${server.name}? Log in elsewhere` }).click();
  await expect(page.getByRole("button", { name: "Find your firm" })).toBeVisible();
});

test("a wrong password is refused", async ({ page, request }) => {
  const trader = await createTrader(request, "refused");

  await submitLogin(page, trader.email, "not-the-password");

  // Next.js has an alert of its own for route announcements, so look inside the form.
  await expect(page.locator("form").getByRole("alert")).toHaveText("Wrong email or password.");
  await expect(page).toHaveURL(/\/login$/);
});

test("a trader logs in, buys, closes and sees the history", async ({ page, request }) => {
  const trader = await createTrader(request, "trader");

  await logIn(page, trader.email);
  await expect(shown(page.getByRole("banner").getByText(trader.accountId, { exact: true }))).toBeVisible();
  await expect(page.getByRole("banner").getByText(server.name, { exact: true })).toBeVisible();
  await expect(page.getByRole("contentinfo").getByText("Live", { exact: true })).toBeVisible();

  // The buttons are enabled once prices have arrived. A ticket starts at the smallest volume.
  const buy = buyButton(page);
  await expect(buy).toBeEnabled();
  const volume = orderPanel(page).getByLabel("Volume", { exact: true });
  await expect(volume).toHaveValue("0.01");

  // The steppers set volume and stop loss without typing. A stop loss starts from the bid and moves a pip a step.
  await page.getByRole("button", { name: "Raise volume" }).click();
  await page.getByRole("button", { name: "Lower volume" }).click();
  await expect(volume).toHaveValue("0.01");
  await setVolume(page, "1.00");
  await lowerStopLoss(page);
  await expect(page.getByLabel("Stop loss", { exact: true })).toHaveValue(/^\d+\.\d{5}$/);

  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  await expect(positionsTab(page, 1)).toBeVisible();
  // The position's id is no column; it is under For support in a trade's details.
  await expect(page.getByRole("columnheader", { name: "Position" })).toHaveCount(0);
  await expect(page.getByRole("contentinfo").getByText("Live", { exact: true })).toBeVisible();

  await positionRow(page).getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByText(/^Buy 1\.00 EURUSD closed at \d\.\d{5}$/)).toBeVisible();
  await expect(positionsTab(page)).toBeVisible();

  // The history says what the trades made today, under a heading for the day, and exports them.
  await page.getByRole("tab", { name: "History" }).click();
  await expect(page.getByRole("cell", { name: "Manual" })).toBeVisible();
  await expect(page.getByRole("columnheader", { name: "Today" })).toBeVisible();
  await page.getByRole("button", { name: "Today", exact: true }).click();
  await expect(page.getByText(/^1 trade, [+-][\d,]+\.\d{2} USD after commission$/)).toBeVisible();
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export CSV" }).click();
  expect((await download).suggestedFilename()).toBe(`history-${trader.accountId}-today.csv`);

  await page.getByRole("tab", { name: "Events" }).click();
  await expect(page.getByText(/^Closed Buy 1\.00 EURUSD/)).toBeVisible();
  await page.getByRole("button", { name: "Warnings", exact: true }).click();
  await expect(page.getByText(/^Closed Buy 1\.00 EURUSD/)).toHaveCount(0);

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

test("stops are set as amounts, and the stop loss is dragged on the chart", async ({ page, request }) => {
  const trader = await createTrader(request, "stops");

  await logIn(page, trader.email);
  const buy = buyButton(page);
  await expect(buy).toBeEnabled();
  await setVolume(page, "1.00");

  // A buy's stop loss must be below the price, which the ticket says before anything is sent.
  await page.getByLabel("Stop loss", { exact: true }).fill("9.00000");
  await expect(orderPanel(page).getByRole("status")).toContainText("stop loss");
  await expect(buy).toBeDisabled();

  // 100 USD of loss and 200 USD of profit become prices for each side before the order is sent.
  await page.getByLabel("Stops in").selectOption("money");
  await page.getByLabel("Stop loss", { exact: true }).fill("100");
  await page.getByLabel("Take profit", { exact: true }).fill("200");
  await expect(buy).toBeEnabled();

  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  // The order panel is cleared for the next order, except the volume.
  await expect(page.getByLabel("Stop loss", { exact: true })).toHaveValue("");
  await expect(page.getByLabel("Take profit", { exact: true })).toHaveValue("");
  await expect(orderPanel(page).getByLabel("Volume", { exact: true })).toHaveValue("1.00");
  // One point of EURUSD is worth 1 USD per lot. The price can move a little between the click and the fill.
  const before = await stopLossCell(page);
  expect(before.amount).toBeGreaterThan(-110);
  expect(before.amount).toBeLessThan(-90);

  // Dragging the line down moves the stop loss further away when the mouse is released.
  await showAllLines(page);
  const line = await findStopLine(page);
  await page.mouse.down();
  await page.mouse.move(line.x, line.y + 40, { steps: 8 });
  await page.mouse.up();

  await expect.poll(async () => (await stopLossCell(page)).price).toBeLessThan(before.price);
  const after = await stopLossCell(page);
  expect(after.amount).toBeLessThan(before.amount);

  // The stops are also changed in a box by the row, in pips from the open price.
  await page.getByRole("button", { name: /^More for Buy 1\.00 EURUSD/ }).click();
  await page.getByRole("menuitem", { name: /^Stop loss and take profit/ }).click();
  const box = page.getByRole("dialog", { name: "Stops of Buy 1.00 EURUSD" });
  await box.getByRole("button", { name: "Pips", exact: true }).click();
  await box.getByLabel("Stop loss").fill("30");
  await expect(box).toContainText("-300.00 USD");
  await box.getByRole("button", { name: "Save stops" }).click();
  await expect(box).toBeHidden();
  await expect.poll(async () => (await stopLossCell(page)).amount).toBe(-300);

  await page.getByRole("tab", { name: "Events" }).click();
  await expect(page.getByText(/^Changed stops on /).first()).toBeVisible();
});

test("a right click on the chart places orders and sets stops where the mouse is", async ({ page, request }) => {
  const trader = await createTrader(request, "menu");

  await logIn(page, trader.email);
  const buy = buyButton(page);
  await expect(buy).toBeEnabled();
  await setVolume(page, "1.00");
  const box = (await chartCanvas(page).boundingBox())!;
  const menu = page.getByRole("menu");

  // A sell limit above the price, at the order panel's volume. The top of the chart is above the candles, and the
  // bottom is kept for the stop loss further down.
  await page.mouse.click(box.x + box.width / 2, box.y + 20, { button: "right" });
  await expect(menu).toHaveAccessibleName(/^At \d\.\d{5}$/);
  await menu.getByRole("group", { name: "New order" }).getByRole("menuitem", { name: /^Sell limit/ }).click();
  await expect(page.getByText(/^Sell limit 1\.00 EURUSD placed at \d\.\d{5}$/)).toBeVisible();
  await expect(page.getByRole("tab", { name: /^Orders\s*1$/ })).toBeVisible();

  // The order panel gets the level as a price.
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
  await menu.getByRole("group", { name: "Order panel" }).getByRole("menuitem", { name: "Use as take profit" }).click();
  await expect(menu).toHaveCount(0);
  await expect(page.getByLabel("Take profit", { exact: true })).toHaveValue(/^\d\.\d{5}$/);

  // The take profit's ghost line is now where the mouse is, and a right click on it removes it. New prices can rescale
  // the chart between the clicks and move the line away from the mouse, so a missed line is put back under it first.
  const orderGroup = menu.getByRole("group", { name: "Order panel" });
  await expect(async () => {
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
    await expect(menu).toBeVisible();
    const remove = orderGroup.getByRole("menuitem", { name: "Remove take profit" });
    if ((await remove.count()) === 0) {
      await orderGroup.getByRole("menuitem", { name: "Use as take profit" }).click();
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
  await showAllLines(page);
  const line = await findStopLine(page);
  await page.mouse.click(line.x, line.y + 20, { button: "right" });
  await menu.getByRole("group", { name: /^Buy 1\.00/ }).getByRole("menuitem", { name: /^Move stop loss here/ }).click();
  await expect.poll(async () => (await stopLossCell(page)).price).toBeLessThan(before.price);

  // A right click on the stop loss line itself removes it.
  const moved = await findStopLine(page);
  await page.mouse.click(moved.x, moved.y, { button: "right" });
  await menu.getByRole("group", { name: /^Buy 1\.00/ }).getByRole("menuitem", { name: "Remove stop loss" }).click();
  await expect(positionRow(page).getByRole("cell").nth(6)).toHaveText("-");

  // Reset chart closes the menu like any choice. Escape closes it without one.
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
  await menu.getByRole("menuitem", { name: /^Reset chart/ }).click();
  await expect(menu).toHaveCount(0);
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "right" });
  await expect(menu).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(menu).toHaveCount(0);
});

test("orders from the chart's buttons ask first when the trader chose so", async ({ page, request }) => {
  const trader = await createTrader(request, "quick");
  await logIn(page, trader.email);
  await expect(buyButton(page)).toBeEnabled();

  const trading = await openSettings(page, "Trading");
  await trading.getByRole("switch", { name: "Buy and sell on the chart" }).click();
  await trading.getByRole("switch", { name: "Ask before placing an order" }).click();
  await trading.getByRole("button", { name: "Done" }).click();

  // The chart's buttons use the order panel's volume.
  const quick = page.getByRole("group", { name: "Quick trade" });
  await quick.getByLabel("Volume in lots").fill("1.00");
  await expect(orderPanel(page).getByLabel("Volume", { exact: true })).toHaveValue("1.00");
  await quick.getByRole("button", { name: /^Buy/ }).click();
  const question = page.getByRole("alertdialog", { name: "Buy 1.00 EURUSD at market?" });
  await question.getByRole("button", { name: "Confirm buy" }).click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  await expect(positionsTab(page, 1)).toBeVisible();
});

test("the order ticket says what the order means and remembers the volume per symbol", async ({ page, request }) => {
  const trader = await createTrader(request, "summary");
  await setFloor(request, trader.accountId, "daily", 95_000);

  await logIn(page, trader.email);
  await expect(buyButton(page)).toBeEnabled();
  const summary = page.getByRole("region", { name: "What this order means" });
  await expect(summary).toContainText(/Margin\s*[\d,]+\.\d{2} USD/);
  await expect(summary).toContainText(/1 pip\s*0\.10 USD/);
  await expect(summary).toContainText(/No stop loss: today's limit lasts [\d,]+ pips at this volume\./);

  // Half a lot with a stop loss a pip below the bid risks the spread and a pip, a tiny share of the 5,000.00 left.
  await setVolume(page, "0.50");
  await expect(summary).toContainText(/1 pip\s*5\.00 USD/);
  await page.getByRole("button", { name: "Lower stop loss" }).click();
  await expect(summary).toContainText(/Risks \d+\.\d{2} USD, (under 0\.1|\d\.\d)% of today's limit/);

  // The spread stands between the buttons, in pips.
  await expect(orderPanel(page).getByTitle(/^Spread: \d+(\.\d)? pips?$/)).toBeVisible();

  // Gold starts at the smallest volume, and EURUSD from the volume chosen for it, also after a reload.
  const watchlist = watchlistOf(page);
  const volume = orderPanel(page).getByLabel("Volume", { exact: true });
  await watchlist.getByRole("button", { name: "XAUUSD", exact: true }).click();
  await expect(volume).toHaveValue("0.01");
  await watchlist.getByRole("button", { name: "EURUSD", exact: true }).click();
  await expect(volume).toHaveValue("0.50");

  // The volume under the candles can be turned off, and stays off. The synthetic history has no tick volume, so the
  // chart's own button for it is disabled here and the choice is made in Settings.
  const volumeSwitch = (await openSettings(page, "Chart")).getByRole("switch", { name: "Volume under the candles" });
  await expect(volumeSwitch).toHaveAttribute("aria-checked", "true");
  await volumeSwitch.click();
  await expect(volumeSwitch).toHaveAttribute("aria-checked", "false");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Volume", exact: true })).toHaveAttribute("aria-pressed", "false");

  await page.reload();
  await expect(orderPanel(page).getByLabel("Volume", { exact: true })).toHaveValue("0.50");
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
  await expect(shown(banner.getByText("#1001 Two-step 100K, Phase 1", { exact: true }))).toBeVisible();
  // Equity, today's result and what is left today in the bar, and the rest under More.
  await expect(banner).toContainText("Left today");
  await expect(banner).toContainText("5,000.00");
  await banner.getByRole("button", { name: "More", exact: true }).click();
  const figures = page.getByRole("dialog", { name: "The account's figures" });
  await expect(figures).toContainText("10,000.00 to go");
  await expect(figures).toContainText("Daily loss limit");
  await expect(figures).toContainText("5,000.00 left");
  await page.keyboard.press("Escape");
  await expect(banner.getByRole("link", { name: `Back to ${server.name}` })).toHaveAttribute("href", detailsUrl);
  // Every time is in the zone of the account's trading day, and says so.
  await expect(page.getByRole("contentinfo")).toContainText("Stockholm time");

  // A broken limit ends trading, which the terminal says plainly, without a negative amount left.
  await setFloor(request, trader.accountId, "max-loss", 100_001);
  const notice = page.getByTestId("account-ended");
  await expect(notice).toContainText("Trading on this account has ended");
  await expect(notice).toContainText(/The max loss limit was broken today at \d\d:\d\d:\d\d Stockholm time: equity 100,000\.00 fell below 100,001\.00\./);
  await expect(notice.getByRole("link", { name: `See the account at ${server.name}` })).toHaveAttribute("href", detailsUrl);
  await expect(shown(banner.getByText("Ended", { exact: true }))).toBeVisible();
  await banner.getByRole("button", { name: "More", exact: true }).click();
  await expect(figures.getByText("Broken")).toBeVisible();
  await expect(figures.getByText(/-[\d,.]+ left/)).toHaveCount(0);
  await page.keyboard.press("Escape");

  await page.getByRole("tab", { name: "Events" }).click();
  await expect(page.getByText("Trading ended: a loss limit was broken")).toBeVisible();
});

test("the watchlist is searched by name, filtered and kept in lists of the trader's own", async ({ page, request }) => {
  const trader = await createTrader(request, "watchlist");

  await logIn(page, trader.email);
  const watchlist = watchlistOf(page);
  await expect(watchlist.getByRole("button", { name: "EURUSD", exact: true })).toBeVisible();

  // The search finds an instrument by its name as well as its symbol.
  await watchlist.getByLabel("Search name or symbol").fill("gold");
  await expect(watchlist.getByRole("button", { name: "XAUUSD", exact: true })).toBeVisible();
  await expect(watchlist.getByRole("button", { name: "EURUSD", exact: true })).toHaveCount(0);

  // A starred symbol stays under Favorites.
  await watchlist.getByRole("button", { name: "Clear the search" }).click();
  await watchlist.getByRole("button", { name: "Add GBPUSD to favorites" }).click();
  await watchlist.getByRole("button", { name: /^Watchlist:/ }).click();
  await page.getByRole("menuitemradio", { name: /^Favorites/ }).click();
  await expect(watchlist.getByRole("button", { name: "GBPUSD", exact: true })).toBeVisible();
  await expect(watchlist.getByRole("button", { name: "EURUSD", exact: true })).toHaveCount(0);

  // A list of the trader's own, named and filled from any list.
  await watchlist.getByRole("button", { name: /^Watchlist:/ }).click();
  await page.getByRole("menuitem", { name: "New list" }).click();
  await watchlist.getByLabel("Name of the list").fill("My metals");
  await watchlist.getByRole("button", { name: "Create" }).click();
  await expect(watchlist.getByRole("button", { name: "Watchlist: My metals" })).toBeVisible();
  await watchlist.getByRole("button", { name: /^Watchlist:/ }).click();
  await page.getByRole("menuitemradio", { name: "All instruments" }).click();
  await watchlist.getByRole("button", { name: "XAGUSD in your lists" }).click();
  await page.getByRole("menuitem", { name: "Add to My metals" }).click();
  await watchlist.getByRole("button", { name: /^Watchlist:/ }).click();
  await page.getByRole("menuitemradio", { name: /^My metals/ }).click();
  await expect(watchlist.getByRole("button", { name: "XAGUSD", exact: true })).toBeVisible();
  await expect(watchlist.getByRole("button", { name: "GBPUSD", exact: true })).toHaveCount(0);

  // Choosing a symbol shows it in the order panel.
  await watchlist.getByRole("button", { name: "XAGUSD", exact: true }).click();
  await expect(orderPanel(page).getByRole("heading")).toContainText("XAGUSD");
});

test("a trader only sees their own account", async ({ page, request }) => {
  const mine = await createTrader(request, "mine");
  const theirs = await createTrader(request, "theirs");

  await logIn(page, mine.email);
  await page.goto(`/?account=${theirs.accountId}`);

  // An account the trader does not own is ignored, and their own is shown instead.
  await expect(shown(page.getByRole("banner").getByText(mine.accountId, { exact: true }))).toBeVisible();
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
  await page.getByRole("banner").getByRole("button", { name: /^Account/ }).click();
  // Every account with its state and balance.
  const accounts = page.getByRole("dialog", { name: "Your accounts" });
  await expect(accounts.getByRole("button", { name: new RegExp(`^${second}`) })).toContainText("50,000.00");
  await accounts.getByRole("button", { name: new RegExp(`^${second}`) }).click();

  await expect(page).toHaveURL(new RegExp(`account=${second}$`));
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
  await expect(shown(page.getByRole("banner").getByText(trader.accountId, { exact: true }))).toBeVisible();
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
  const buy = buyButton(page);
  await expect(buy).toBeEnabled();
  await setVolume(page, "1.00");

  // A trailing stop follows a stop loss, so it needs one.
  const trailing = orderPanel(page).getByRole("switch", { name: "Trailing stop" });
  await trailing.click();
  await expect(orderPanel(page).getByText("Set a stop loss for the trailing stop to follow.")).toBeVisible();
  await expect(buy).toBeDisabled();

  await lowerStopLoss(page);
  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  await expect(positionRow(page).getByText("Trailing", { exact: true })).toBeVisible();

  // A part starts from half the position, and the rest stays open.
  await page.getByRole("button", { name: /^More for Buy 1\.00 EURUSD/ }).click();
  await page.getByRole("menuitem", { name: /^Close part/ }).click();
  const part = page.getByRole("dialog", { name: "Close part of Buy 1.00 EURUSD" });
  await expect(part.getByLabel(/^Lots to close/)).toHaveValue("0.50");
  await part.getByRole("button", { name: "Close part" }).click();
  await expect(page.getByText(/^0\.50 of Buy EURUSD closed at \d\.\d{5}, 0\.50 still open$/)).toBeVisible();
  await expect(positionRow(page).getByRole("cell").nth(2)).toHaveText("0.50");

  // A second position, and both close at once on a second click.
  await trailing.click();
  await buy.click();
  await expect(positionsTab(page, 2)).toBeVisible();
  await page.getByRole("button", { name: "Close all" }).click();
  await page.getByRole("button", { name: "Close all 2?" }).click();
  await expect(positionsTab(page)).toBeVisible();

  await page.getByRole("tab", { name: "History" }).click();
  await expect(page.getByRole("cell", { name: "Part closed" })).toBeVisible();
});

test("a limit and a stop wait on their side of the market, and an order is moved in the orders tab", async ({ page, request }) => {
  const trader = await createTrader(request, "pending");
  await logIn(page, trader.email);
  await expect(buyButton(page)).toBeEnabled();
  await setVolume(page, "1.00");

  // 20 pips below the bid, a limit can only be a buy: a sell limit waits above the market, which its button says on
  // hover, and nothing is said under the buttons about the side not meant.
  await orderPanel(page).getByRole("button", { name: "Limit", exact: true }).click();
  const lower = page.getByRole("button", { name: "Lower price" });
  for (let pip = 0; pip < 20; pip++) {
    await lower.click();
  }
  const buyLimit = orderPanel(page).getByRole("button", { name: /^Buy limit/ });
  const sellLimit = orderPanel(page).getByRole("button", { name: /^Sell limit/ });
  await expect(buyLimit).toBeEnabled();
  await expect(sellLimit).toBeDisabled();
  await expect(sellLimit).toHaveAttribute("title", /^A sell limit waits above the market price, now \d\.\d{5}\.$/);
  await expect(orderPanel(page).getByRole("status")).toHaveCount(0);

  // At the same price a stop can only be a sell.
  await orderPanel(page).getByRole("button", { name: "Stop", exact: true }).click();
  await expect(orderPanel(page).getByRole("button", { name: /^Sell stop/ })).toBeEnabled();
  await expect(orderPanel(page).getByRole("button", { name: /^Buy stop/ })).toBeDisabled();

  await orderPanel(page).getByRole("button", { name: "Limit", exact: true }).click();
  await buyLimit.click();
  await expect(page.getByText(/^Buy limit 1\.00 EURUSD placed at \d\.\d{5}$/)).toBeVisible();

  await page.getByRole("tab", { name: /^Orders\s*1$/ }).click();
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  const edit = page.getByRole("dialog", { name: "Buy limit 1.00 EURUSD" });
  const price = edit.getByLabel("Price", { exact: true });
  const moved = (Number(await price.inputValue()) - 0.001).toFixed(5);
  await price.fill(moved);
  await edit.getByRole("button", { name: "Save order" }).click();

  await expect(page.getByText(`EURUSD order moved to ${moved}`)).toBeVisible();
  await expect(page.getByRole("cell", { name: moved, exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Cancel order" }).click();
  await expect(page.getByRole("tab", { name: /^Orders$/ })).toBeVisible();
});

test("indicators and drawings stay on the chart, and the digits choose the timeframe", async ({ page, request }) => {
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
  const box = (await chartCanvas(page).boundingBox())!;
  await page.getByRole("button", { name: "Drawing tools" }).click();
  await page.getByRole("menuitemradio", { name: "Horizontal line" }).click();
  await expect(page.getByText(/^Horizontal line: Click where the line goes\./)).toBeVisible();
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 3);
  await expect(page.getByRole("button", { name: "Remove the selected drawing" })).toBeEnabled();

  // Both are kept on the login.
  await page.reload();
  await expect(page.getByRole("button", { name: "Indicators (2)" })).toBeVisible();
  const removeAll = page.getByRole("button", { name: "Remove all drawings" });
  await expect(removeAll).toBeEnabled();

  // Removing every drawing takes a second click.
  await removeAll.click();
  await removeAll.click();
  await expect(removeAll).toBeDisabled();

  // 5 is the fifth timeframe, H1, and "?" lists the shortcuts.
  await page.keyboard.press("5");
  await expect(page.getByRole("button", { name: "H1", exact: true })).toHaveAttribute("aria-pressed", "true");
  await page.keyboard.press("?");
  await expect(page.getByRole("dialog", { name: "Keyboard shortcuts" })).toContainText("No shortcut places an order");
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
  await page.getByRole("button", { name: /Rules$/ }).click();
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
  const buy = buyButton(page);
  await expect(buy).toBeEnabled();
  await setVolume(page, "1.00");

  await page.getByRole("button", { name: /Rules$/ }).click();
  await page.getByRole("dialog", { name: "Rules" }).getByRole("button", { name: "Set your limits" }).click();
  const sheet = page.getByRole("dialog", { name: /Your own limits$/ });
  await sheet.getByLabel("Daily loss limit in USD").fill("1500");
  await sheet.getByLabel("Most trades a day").fill("6");
  // Nothing is looser yet, so nothing waits for tomorrow.
  await expect(sheet.getByTestId("limits-later")).toHaveCount(0);
  await sheet.getByRole("button", { name: "Save limits" }).click();
  await expect(page.locator("[data-sonner-toast]").filter({ hasText: "Your limits are saved" })).toContainText("They apply now.");
  // The trader's own limit is now the nearest, and what is left of it shows in the bar.
  await expect(page.getByRole("banner")).toContainText("1,500.00");

  // A looser limit waits for the next trading day.
  await page.getByRole("button", { name: /Rules$/ }).click();
  const rulebook = page.getByRole("dialog", { name: "Rules" });
  await expect(rulebook.locator("[data-own-limit='loss']")).toContainText("Daily loss limit, 1,500.00");
  await expect(rulebook.locator("[data-own-limit='trades']")).toContainText("0 of 6");
  await rulebook.getByRole("button", { name: "Change your limits" }).click();
  await sheet.getByLabel("Daily loss limit in USD").fill("2000");
  await expect(sheet.getByTestId("limits-later")).toHaveText("From 00:00: daily loss limit 2,000.00");
  await sheet.getByRole("button", { name: "Save limits" }).click();
  await expect(page.locator("[data-sonner-toast]").filter({ hasText: "Your limits are saved" })).toContainText("The looser ones apply from 00:00.");
  await page.getByRole("banner").getByRole("button", { name: "More", exact: true }).click();
  await expect(page.getByRole("dialog", { name: "The account's figures" })).toContainText(/Your daily limit\s*98,500\.00/);
  await page.keyboard.press("Escape");

  // Locking the rest of the day closes the open position, and new orders are not taken.
  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  await page.getByRole("button", { name: /Rules$/ }).click();
  await page.getByRole("dialog", { name: "Rules" }).getByRole("button", { name: "Lock the rest of the day" }).click();
  const lock = page.getByRole("dialog", { name: "Lock trading until 00:00?" });
  await expect(lock).toContainText("Your open position");
  await lock.getByRole("button", { name: "Close and lock until 00:00" }).click();
  await expect(page.getByTestId("own-lock")).toContainText(/^You locked the rest of the day at \d\d:\d\d:\d\d/);
  await expect(positionsTab(page)).toBeVisible();
  await expect(page.getByRole("note").filter({ hasText: "You locked the rest of the day, until 00:00 Stockholm time." })).toBeVisible();
  await expect(buy).toBeDisabled();
});

test("an order is sized from what it risks at its stop loss", async ({ page, request }) => {
  const trader = await createTrader(request, "risk");
  await logIn(page, trader.email);
  await expect(buyButton(page)).toBeEnabled();

  await page.getByLabel("Size in").selectOption("money");
  await orderPanel(page).getByLabel("Risk", { exact: true }).fill("200");
  await expect(page.getByRole("status").filter({ hasText: "Set a stop loss to size the order from the risk." }).first()).toBeVisible();

  // 20 pips and the spread on EURUSD are worth about 210 USD on a lot, so 200 USD is a little less than a lot.
  await lowerStopLoss(page);
  await expect(page.getByRole("status").filter({ hasText: "lots, risks" })).toContainText(/^= 0\.\d\d lots, risks 1\d\d\.\d\d USD$/);
  await buyButton(page).click();
  await expect(page.getByText(/^Bought 0\.\d\d EURUSD at \d\.\d{5}$/)).toBeVisible();

  // The ticket keeps sizing from risk.
  await page.reload();
  await expect(orderPanel(page).getByLabel("Risk", { exact: true })).toHaveValue("200");
});

test("the trader's settings change the chart and the theme, and ask before an order goes", async ({ page, request }) => {
  const trader = await createTrader(request, "settings");
  await logIn(page, trader.email);

  const settings = await openSettings(page, "Chart");
  await settings.getByRole("radio", { name: "Blue and orange" }).click();
  await expect(settings.getByRole("radio", { name: "Blue and orange" })).toHaveAttribute("aria-checked", "true");
  await settings.getByRole("switch", { name: "Ask price line" }).click();
  await settings.getByRole("tab", { name: "Display" }).click();
  await settings.getByRole("radio", { name: "Light" }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
  await settings.getByRole("tab", { name: "Trading" }).click();
  await settings.getByRole("switch", { name: "Ask before placing an order" }).click();
  await settings.getByRole("button", { name: "Done" }).click();
  await expect(settings).toBeHidden();

  // The order panel asks first, and Cancel sends nothing.
  const buy = buyButton(page);
  await expect(buy).toBeEnabled();
  await setVolume(page, "1.00");
  await buy.click();
  const question = page.getByRole("alertdialog", { name: "Buy 1.00 EURUSD at market?" });
  await expect(question).toContainText("No stop loss or take profit.");
  await question.getByRole("button", { name: "Cancel" }).click();
  await expect(question).toBeHidden();
  await expect(positionsTab(page)).toBeVisible();

  await buyButton(page).click();
  await page.getByRole("button", { name: "Confirm buy" }).click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  await expect(positionsTab(page, 1)).toBeVisible();

  // The choices stay after a reload, the theme before the page is drawn, and a reset needs a second click.
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
  const again = await openSettings(page, "Chart");
  await expect(again.getByRole("radio", { name: "Blue and orange" })).toHaveAttribute("aria-checked", "true");
  await again.getByRole("button", { name: "Reset to defaults" }).click();
  await again.getByRole("button", { name: "Click again to reset everything" }).click();
  await expect(again.getByRole("radio", { name: "Green and red" })).toHaveAttribute("aria-checked", "true");
  await again.getByRole("tab", { name: "Trading" }).click();
  await expect(again.getByRole("switch", { name: "Ask before placing an order" })).toHaveAttribute("aria-checked", "false");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
});

test("the trader's choices follow them to another device", async ({ page, request, browser }) => {
  const trader = await createTrader(request, "devices");
  await logIn(page, trader.email);
  await watchlistOf(page).getByRole("button", { name: "Add XAUUSD to favorites" }).click();
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
  await expect(watchlistOf(device).getByRole("button", { name: "Remove XAUUSD from favorites" })).toBeAttached();
  await expect(device.getByRole("button", { name: "Indicators (1)" })).toBeVisible();
  await expect((await openSettings(device, "Sounds")).getByRole("switch", { name: "Sound on warnings" })).toHaveAttribute("aria-checked", "false");
  // The tour was seen on the first device.
  await expect(device.getByRole("button", { name: "Skip the tour" })).toHaveCount(0);
  await other.close();
});

test("a trade's details show the feed's price behind each fill", async ({ page, request }) => {
  const trader = await createTrader(request, "details");
  await logIn(page, trader.email);
  const buy = buyButton(page);
  await expect(buy).toBeEnabled();
  await setVolume(page, "1.00");
  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();
  await positionRow(page).getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByText(/^Buy 1\.00 EURUSD closed at \d\.\d{5}$/)).toBeVisible();

  await page.getByRole("tab", { name: "History" }).click();
  await page.getByRole("tabpanel", { name: "History" }).getByRole("button", { name: "Details" }).click();
  const details = page.getByRole("dialog", { name: "Trade details: Buy 1.00 EURUSD" });
  await expect(details.getByRole("region", { name: "Opened" })).toContainText("Price from the feed");
  // The standard group raises EURUSD's ask by a point and lowers its bid by one, a tenth of a pip.
  await expect(details.getByRole("region", { name: "Opened" })).toContainText("The firm's markup: 0.1 pips on the ask.");
  await expect(details.getByRole("region", { name: "Closed" })).toContainText("The firm's markup: 0.1 pips on the bid.");
  await expect(details).toContainText(/Result after commission\s*-?[\d,]+\.\d{2} USD/);
  // The ids are for the firm's support, under the trade.
  const support = details.getByRole("region", { name: "For support" });
  await expect(support).toContainText(/Position\s*[0-9a-f-]{36}/);
  await expect(support).toContainText(/Journal entries\s*[\d,]+, [\d,]+/);

  await page.keyboard.press("Escape");
  await expect(details).toBeHidden();
});

test("a broken loss limit has a report with the prices that broke it", async ({ page, request }) => {
  const trader = await createTrader(request, "breach");
  await logIn(page, trader.email);
  const buy = buyButton(page);
  await expect(buy).toBeEnabled();
  await setVolume(page, "1.00");
  await buy.click();
  await expect(page.getByText(boughtNote)).toBeVisible();

  await setFloor(request, trader.accountId, "max-loss", 100_001);
  await page.getByTestId("account-ended").getByRole("button", { name: "Breach report" }).click();
  const report = page.getByRole("dialog", { name: "Breach report: The max loss limit was broken" });
  await expect(report).toContainText("Equity, price by price");
  await expect(report.getByRole("region", { name: "The prices that broke it" })).toContainText(/EURUSD\s*\d\.\d{5} \/ \d\.\d{5}\s*0\.2 pips/);
  await expect(report.getByRole("region", { name: "Open positions at that moment" })).toContainText(/Buy 1\.00 EURUSD/);
  await expect(report.getByRole("region", { name: "Step by step" })).toContainText("Max loss limit broken: equity");
  await expect(report.getByRole("region", { name: "Step by step" })).toContainText("Trading ended: a loss limit was broken");
});

test("the firm's notice shows in one line at the top of its traders' terminals", async ({ page, request }) => {
  const trader = await createTrader(request, "notice");
  await logIn(page, trader.email);
  await expect(buyButton(page)).toBeEnabled();

  const set = await request.put(`${serviceUrl}/api/admin/v1/notice`, {
    headers: adminHeaders,
    data: { title: "Price feed outage", text: "No prices since 15:12.\n\nWe reinstate accounts that broke a limit because of it.", level: "Warning", url: "http://localhost:3002/status" },
  });
  expect(set.ok()).toBeTruthy();
  const notice = page.getByRole("alert").filter({ hasText: "Price feed outage" });
  await expect(notice).toContainText("No prices since 15:12.");
  // The rest, and the firm's status page, are behind Details.
  await notice.getByRole("button", { name: "Details" }).click();
  const more = page.getByRole("dialog", { name: "Details" });
  await expect(more).toContainText("We reinstate accounts that broke a limit because of it.");
  await expect(more.getByRole("link", { name: "Read more on the status page" })).toHaveAttribute("href", "http://localhost:3002/status");
  await page.keyboard.press("Escape");

  const removed = await request.delete(`${serviceUrl}/api/admin/v1/notice`, { headers: adminHeaders });
  expect(removed.ok()).toBeTruthy();
  await expect(notice).toBeHidden();
});

test("a lost connection is said plainly, and the figures that are not live are dimmed", async ({ page, request }) => {
  const trader = await createTrader(request, "offline");
  // The hub is let through until the test cuts it, and refused while it is cut.
  let allow = true;
  const open: { close: (options: { code: number; reason: string }) => Promise<void> }[] = [];
  await page.routeWebSocket(/\/hubs\/trading/, (ws) => {
    if (!allow) {
      void ws.close({ code: 1011, reason: "cut" });
      return;
    }
    ws.connectToServer();
    open.push(ws);
  });
  await page.route(/\/hubs\/trading/, (route) => (allow ? route.continue() : route.abort("connectionrefused")));

  await logIn(page, trader.email);
  await expect(buyButton(page)).toBeEnabled();

  allow = false;
  for (const ws of open.splice(0)) {
    await ws.close({ code: 1011, reason: "cut" });
  }
  const lost = page.getByRole("alert").filter({ hasText: `Connection to ${server.name} lost` });
  await expect(lost).toBeVisible();
  await expect(page.locator("[data-offline]")).toHaveCount(1);
  await expect(orderPanel(page).getByRole("note")).toContainText("Not connected to the trading service.");
  await expect(buyButton(page)).toBeDisabled();

  allow = true;
  await expect(page.getByText("Connected again")).toBeVisible({ timeout: 20_000 });
  await expect(page.locator("[data-offline]")).toHaveCount(0);
  await expect(buyButton(page)).toBeEnabled();
});
