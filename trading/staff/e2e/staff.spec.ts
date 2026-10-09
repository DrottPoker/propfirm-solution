import { expect, request as playwrightRequest, test, type Page } from "@playwright/test";

import { servicePort } from "../playwright.config";

const serviceUrl = `http://localhost:${servicePort}`;

// Our staff member, the development firm's admin key and the partner key, from appsettings.Development.json.
const staff = { email: "ops@test.com", password: "ops" };
const adminHeaders = { "X-Api-Key": "dev-admin-key" };
const partnerHeaders = { "X-Api-Key": "dev-partner-key" };
const password = "e2e-password";

// A page that throws, or that the browser's policy stops (ADR 0060), fails the test it is in.
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

// With STAFF_SCREENSHOTS set to a folder, every page is saved there too, to look at by eye.
async function snap(page: Page, name: string) {
  const folder = process.env.STAFF_SCREENSHOTS;
  if (folder) {
    await page.screenshot({ path: `${folder}/${name}.png`, fullPage: true });
  }
}

async function logIn(page: Page) {
  await page.goto("/login");
  await page.getByLabel("Email").fill(staff.email);
  await page.getByLabel("Password").fill(staff.password);
  await page.getByRole("button", { name: "Log in" }).click();
  await expect(page.getByRole("heading", { name: "Overview" })).toBeVisible();
}

/** A trader with an account and an open position, through the admin API and the trader's own API, as a firm would. */
async function traderWithPosition(name: string, side: "Buy" | "Sell", volume: number) {
  const accountId = `${name}-${test.info().testId.slice(0, 6)}`;
  const email = `${accountId}@e2e.example`;
  const api = await playwrightRequest.newContext();
  const user = await api.post(`${serviceUrl}/api/admin/v1/users`, { headers: adminHeaders, data: { email, password } });
  expect(user.ok()).toBeTruthy();
  const account = await api.post(`${serviceUrl}/api/admin/v1/accounts`, {
    headers: adminHeaders,
    data: { accountId, groupId: "standard", initialBalance: 100_000, ownerUserId: (await user.json()).userId },
  });
  expect(account.ok()).toBeTruthy();
  expect((await api.post(`${serviceUrl}/api/auth/login`, { data: { server: "demo-firm", email, password } })).ok()).toBeTruthy();

  // The synthetic feed needs a moment for its first prices after a start.
  await expect(async () => {
    const order = await api.post(`${serviceUrl}/api/accounts/${accountId}/orders`, {
      data: { orderId: `O-${Date.now()}`, symbol: "EURUSD", side, type: "Market", volume },
    });
    expect(order.ok()).toBeTruthy();
  }).toPass({ timeout: 20_000 });
  await api.dispose();
  return accountId;
}

test("staff log in, and a wrong password is refused", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveURL(/\/login$/);

  await page.getByLabel("Email").fill(staff.email);
  await page.getByLabel("Password").fill("wrong");
  await page.getByRole("button", { name: "Log in" }).click();
  await expect(page.locator("form").getByRole("alert")).toHaveText("Wrong email or password.");

  await page.getByLabel("Password").fill(staff.password);
  await page.getByRole("button", { name: "Log in" }).click();
  await expect(page.getByRole("heading", { name: "Overview" })).toBeVisible();
  await expect(page.getByText("End-to-end", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Log out" }).click();
  await expect(page).toHaveURL(/\/login$/);
});

test("the overview shows the platform in figures, the feed, the engine and what traders hold", async ({ page }) => {
  await traderWithPosition("over", "Buy", 1);
  await logIn(page);

  const figures = page.getByRole("region", { name: "Key figures" });
  await expect(figures.getByText("Open positions")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Needs us" })).toBeVisible();
  await expect(page.getByRole("heading", { name: /Price feed/ })).toBeVisible();
  await expect(page.getByText("Prices arriving")).toBeVisible();
  await expect(page.getByText("Healthy")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Largest net exposure" })).toBeVisible();
  await expect(page.getByRole("rowheader", { name: "EURUSD" })).toBeVisible();
  await expect(page.getByText(/The service started in/)).toBeVisible();
  await snap(page, "overview");
});

test("staff make a server of a type of business, see its key once, change its type and stop the key", async ({ page }) => {
  const id = `e2e-${test.info().testId.slice(0, 6)}`;
  await logIn(page);
  await page.getByRole("link", { name: "Servers" }).first().click();
  await expect(page.getByRole("heading", { name: "Servers", exact: true })).toBeVisible();
  await expect(page.getByRole("link", { name: /Demo Firm/ })).toBeVisible();
  await snap(page, "servers");

  await page.getByRole("button", { name: "New server" }).click();
  const dialog = page.getByRole("dialog", { name: "New server" });
  await dialog.getByLabel("Server").fill(id);
  await dialog.getByLabel("Firm name").fill("Helix Markets");
  await dialog.getByLabel("Account currency").selectOption("GBP");
  // Nothing is chosen for the firm, so the type is always a decision.
  await expect(dialog.getByRole("button", { name: "Make the server" })).toBeDisabled();
  await dialog.getByRole("radio", { name: /^Practice/ }).check();
  await dialog.getByRole("button", { name: "Make the server" }).click();

  const made = page.getByRole("dialog", { name: `Server ${id} is made` });
  await expect(made.getByRole("button", { name: "Copy the admin key" })).toBeVisible();
  await made.getByRole("button", { name: "Done" }).click();
  await expect(page.getByRole("heading", { name: "Helix Markets" })).toBeVisible();
  await expect(page.getByText(`made by ${staff.email}`, { exact: false })).toBeVisible();

  // A practice terminal shows no rules or limits. As a broker, its orders ask first.
  const terminal = page.getByRole("region", { name: "Terminal", exact: true });
  await expect(terminal).toContainText("Practice");
  await expect(terminal).toContainText("Sizing from risk and trade details");
  await terminal.getByRole("button", { name: "Change the type" }).click();
  const type = page.getByRole("dialog", { name: `Type of ${id}` });
  await type.getByRole("radio", { name: /^Broker/ }).check();
  await type.getByRole("button", { name: "Change the type" }).click();
  await expect(type).toBeHidden();
  await expect(terminal).toContainText("Ask before they are sent");
  await expect(page.getByText(`${staff.email} changed the type of ${id} from Practice to Broker`)).toBeVisible();

  await page.getByRole("button", { name: "New admin key" }).click();
  const key = page.getByRole("dialog", { name: `New admin key for ${id}` });
  await key.getByLabel("Why").fill("Rotated in a test");
  await expect(key.getByRole("button", { name: "Stop the old key" })).toBeDisabled();
  await key.getByLabel(`Type ${id} to confirm`).fill(id);
  await key.getByRole("button", { name: "Stop the old key" }).click();
  const stopped = page.getByRole("dialog", { name: "The old key has stopped" });
  await expect(stopped.getByRole("button", { name: "Copy the admin key" })).toBeVisible();
  await stopped.getByRole("button", { name: "Done" }).click();
  await expect(page.getByText(`${staff.email} stopped the admin key of ${id}: Rotated in a test`)).toBeVisible();
});

test("a partner's server can be listed by staff, and its key is never shown", async ({ page }) => {
  const id = `e2e-p-${test.info().testId.slice(0, 6)}`;
  const api = await playwrightRequest.newContext();
  expect((await api.post(`${serviceUrl}/api/partner/v1/tenants`, { headers: partnerHeaders, data: { id, name: "Partner Firm" } })).ok()).toBeTruthy();
  await api.dispose();

  await logIn(page);
  await page.goto(`/servers/${id}`);
  await expect(page.getByRole("heading", { name: "Partner Firm" })).toBeVisible();
  // Kronant Prop keeps its firms' terminals as prop firms.
  await expect(page.getByRole("region", { name: "Terminal", exact: true })).toContainText("Kronant Prop sets the type for its firms.");
  await expect(page.getByRole("button", { name: "Change the type" })).toHaveCount(0);
  await page.getByRole("button", { name: "Put on the list" }).click();
  await page.getByRole("dialog", { name: `Put ${id} on the list?` }).getByRole("button", { name: "Put on the list" }).click();
  await expect(page.getByRole("button", { name: "Stop listing" })).toBeVisible();

  await page.getByRole("button", { name: "New admin key" }).click();
  const key = page.getByRole("dialog", { name: `New admin key for ${id}` });
  await expect(key.getByText("Kronant Prop made this server.")).toBeVisible();
  await key.getByLabel("Why").fill("May have leaked");
  await key.getByLabel(`Type ${id} to confirm`).fill(id);
  await key.getByRole("button", { name: "Stop the old key" }).click();
  const stopped = page.getByRole("dialog", { name: "The old key has stopped" });
  await expect(stopped.getByText(/Kronant Prop asks for a new key by itself/)).toBeVisible();
  await expect(stopped.getByRole("button", { name: "Copy the admin key" })).toHaveCount(0);
});

test("a server shows its group, its figures and its events, and an account is found by search", async ({ page }) => {
  const accountId = await traderWithPosition("srv", "Sell", 2);
  await logIn(page);

  await page.keyboard.press("Control+k");
  await page.getByPlaceholder("A server, or an account number").fill(accountId);
  await page.getByRole("option", { name: new RegExp(`#${accountId}`) }).click();

  await expect(page.getByRole("heading", { name: "Demo Firm" })).toBeVisible();
  await expect(page.getByRole("heading", { name: `Account #${accountId}` })).toBeVisible();
  await expect(page.getByRole("heading", { name: `Latest events of #${accountId}` })).toBeVisible();
  await expect(page.getByRole("cell", { name: "Position opened" })).toBeVisible();
  await expect(page.getByRole("heading", { name: /Group standard/ })).toBeVisible();
  await snap(page, "server");
});

test("the price feed, the instruments, the exposure and the engine show their figures", async ({ page }) => {
  await traderWithPosition("exp", "Buy", 3);
  await logIn(page);

  await page.getByRole("link", { name: "Price feed" }).first().click();
  await expect(page.getByRole("heading", { name: "Price feed" })).toBeVisible();
  const eurusd = page.getByRole("row", { name: /EURUSD/ });
  await expect(eurusd.getByText("Live")).toBeVisible();
  await expect(page.getByText("Made-up prices have no history, so their gaps stay.")).toBeVisible();
  await snap(page, "price-feed");

  await page.getByRole("link", { name: "Instruments" }).first().click();
  await expect(page.getByRole("heading", { name: "Instruments" })).toBeVisible();
  await expect(page.getByRole("row", { name: /EURUSD/ }).getByText("Always open")).toBeVisible();
  await page.getByRole("button", { name: /^Crypto/ }).click();
  await expect(page.getByRole("rowheader", { name: /BTCUSD/ })).toBeVisible();
  await expect(page.getByRole("rowheader", { name: /EURUSD/ })).toHaveCount(0);
  await snap(page, "instruments");

  await page.getByRole("link", { name: "Exposure" }).first().click();
  await expect(page.getByRole("heading", { name: "Exposure" })).toBeVisible();
  await expect(page.getByRole("rowheader", { name: "EURUSD" })).toBeVisible();
  await page.getByLabel("Servers").selectOption("demo-firm");
  await expect(page).toHaveURL(/server=demo-firm/);
  await expect(page.getByRole("rowheader", { name: "EURUSD" })).toBeVisible();
  await snap(page, "exposure");

  await page.getByRole("link", { name: "Engine" }).first().click();
  await expect(page.getByRole("heading", { name: "Engine" })).toBeVisible();
  await expect(page.getByText("Healthy")).toBeVisible();
  await expect(page.getByText("prop-platform")).toBeVisible();
  await snap(page, "engine");
});

test("on a phone the menu folds into a button", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await logIn(page);
  await expect(page.getByRole("complementary", { name: "Staff menu" })).toBeHidden();
  await snap(page, "phone");

  await page.getByRole("button", { name: "Open the menu" }).click();
  await page.getByRole("link", { name: "Engine", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Engine" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Open the menu" })).toBeVisible();
});
