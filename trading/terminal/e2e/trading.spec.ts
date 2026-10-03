import { expect, test, type APIRequestContext, type Page } from "@playwright/test";

import { servicePort } from "../playwright.config";

const serviceUrl = `http://localhost:${servicePort}`;

// The development firm's admin key and server, from appsettings.Development.json.
const adminHeaders = { "X-Api-Key": "dev-admin-key" };
const server = { id: "demo-firm", name: "Demo Firm" };
const productName = "Trading terminal";

const password = "e2e-password";

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

async function submitLogin(page: Page, email: string, withPassword: string) {
  await page.goto("/login");
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(withPassword);
  await page.getByRole("button", { name: "Log in" }).click();
}

/** Logs in and waits until the terminal is shown. */
async function logIn(page: Page, email: string) {
  await submitLogin(page, email, password);
  await expect(page.getByRole("button", { name: "Log out" })).toBeVisible();
}

test("visitors who are not logged in are sent to the login page with the server chosen", async ({ page }) => {
  await page.goto("/");

  await expect(page).toHaveURL(/\/login$/);
  await expect(page).toHaveTitle(productName);
  await expect(page.getByRole("heading", { name: productName })).toBeVisible();
  // The only server is chosen for the trader.
  await expect(page.getByLabel("Server")).toHaveValue(server.id);
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
  await expect(page.getByText(server.name)).toBeVisible();
  await expect(page.getByText("Live")).toBeVisible();

  // The buttons are enabled once prices have arrived.
  const buy = page.getByRole("button", { name: /^buy/i });
  await expect(buy).toBeEnabled();
  await buy.click();
  await expect(page.getByRole("status")).toHaveText("Buy filled.");
  await expect(page.getByRole("tab", { name: "Positions (1)" })).toBeVisible();

  await page.getByRole("button", { name: "Close", exact: true }).click();
  await expect(page.getByRole("tab", { name: "Positions", exact: true })).toBeVisible();

  await page.getByRole("tab", { name: "History" }).click();
  await expect(page.getByRole("cell", { name: "Manual" })).toBeVisible();

  await page.getByRole("tab", { name: "Events" }).click();
  await expect(page.getByText(/^Closed Buy 1\.00 EURUSD/)).toBeVisible();

  await page.getByRole("button", { name: "Log out" }).click();
  await expect(page).toHaveURL(/\/login$/);
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
  await expect(page.getByText("50,000.00 USD")).toBeVisible();
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
  await expect(page.getByRole("button", { name: "Log out" })).toBeVisible();
  await expect(page.getByText(trader.accountId, { exact: true })).toBeVisible();
  // The token is gone from the address once it is used.
  expect(page.url()).not.toContain("token=");

  await page.getByRole("button", { name: "Log out" }).click();
  await page.goto(url);
  // Next.js has an alert of its own for route announcements, so look inside the page.
  await expect(page.locator("main").getByRole("alert")).toContainText("This link has expired or was already used.");
});
