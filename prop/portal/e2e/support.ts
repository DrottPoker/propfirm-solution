import { expect, type Page } from "@playwright/test";

import { opsUrl, platformUrl, portalPort } from "../playwright.config";

// The development firm and its administrator, and our staff member, from Prop.Api's appsettings.Development.json.
export const firmName = "Demo Firm";
export const admin = { email: "admin@test.com", password: "admin" };
export const staff = { email: "ops@test.com", password: "ops" };

export const traderPassword = "e2e-trader-password";

const ownerPassword = "e2e-owner-password";

/**
 * A firm signs up on the platform and lands in the guide of its own admin panel, on its own address, and skips it to the
 * overview. Development needs no email confirmation.
 */
export async function signUp(page: Page, firmName: string, shortName: string) {
  await page.goto(`${platformUrl}/signup`);
  await page.getByLabel("Firm name").fill(firmName);
  await expect(page.getByLabel("Short name")).toHaveValue(shortName);
  await expect(page.getByText("Available")).toBeVisible();
  await page.getByLabel("Your email").fill(`owner@${shortName}.e2e.example`);
  await page.getByLabel("Password", { exact: true }).fill(ownerPassword);
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Create my firm" }).click();
  await expect(page).toHaveURL(`http://${shortName}.localhost:${portalPort}/admin/get-started`);
  await expect(page.getByRole("heading", { name: "Make it yours" })).toBeVisible();
  await page.getByRole("link", { name: "Skip the guide" }).click();
  await expect(page).toHaveURL(`http://${shortName}.localhost:${portalPort}/admin`);
}

/** A new firm's trading server is created in the background, and its overview shows the steps to live once it is ready. */
export async function waitForSandbox(page: Page) {
  await expect(page.getByRole("heading", { name: /^Get .* live$/ })).toBeVisible({ timeout: 20_000 });
}

/** A link in the admin panel's menu. Links with something waiting have its count after the name. */
export function adminLink(page: Page, name: string) {
  return page.getByRole("navigation", { name: "Admin" }).getByRole("link", { name: new RegExp(`^${name}`) });
}

/** One of the firm's settings, which are together under Settings in the admin panel's menu. */
export async function openSetting(page: Page, name: string) {
  await adminLink(page, "Settings").click();
  await page.getByRole("main").getByRole("link", { name: new RegExp(`^${name}`) }).click();
}

/**
 * The firm's administrator starts a challenge from the admin panel, the first one by name unless another is chosen,
 * and lands on the new account. The trader is not emailed, since the tests have no mail server.
 */
export async function startChallenge(page: Page, email: string, challenge?: RegExp) {
  // Before the first account, the empty list offers the same button as the page.
  await page.getByRole("button", { name: "Start a challenge" }).first().click();
  const panel = page.getByRole("dialog", { name: "Start a challenge" });
  await panel.getByLabel("Trader's email").fill(email);
  if (challenge) {
    await panel.getByRole("radio", { name: challenge }).check();
  }

  await panel.getByRole("checkbox", { name: /^Email the trader/ }).uncheck();
  await panel.getByRole("button", { name: "Start challenge" }).click();
  await expect(page).toHaveURL(/\/admin\/accounts\/[0-9a-f-]+$/);
}

export async function logIn(page: Page, path: "/login" | "/admin/login", email: string, password: string) {
  await page.goto(path);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Log in" }).click();
}

/** The trader chooses a password with the invitation, in a new page of the same browser, and sees the dashboard. */
export async function acceptInvitation(page: Page, invitation: string) {
  await page.goto(invitation);
  await page.getByLabel("Password", { exact: true }).fill(traderPassword);
  await page.getByLabel("Repeat password").fill(traderPassword);
  await page.getByRole("button", { name: "Save password and continue" }).click();
  await expect(page).toHaveURL(/\/$/);
}

/** The firm chooses our built-in KYC before the first payout, which nothing chooses for it and going live waits for. */
export async function chooseBuiltInKyc(page: Page) {
  await openSetting(page, "KYC");
  await page.getByRole("radio", { name: /^Our built-in KYC/ }).check();
  await page.getByRole("radio", { name: /^Before the first payout/ }).check();
  await page.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText("Saved.")).toBeVisible();
}

/** The firm fills in its company's details, the first step on the Go live page. */
export async function fillApplication(page: Page, companyName: string) {
  await page.getByRole("navigation", { name: "Admin" }).getByRole("link", { name: "Go live" }).click();
  await expect(page.getByRole("button", { name: /^Company details/ })).toHaveAttribute("aria-current", "step");
  await page.getByLabel("Legal name").fill(companyName);
  await page.getByLabel("Registration number").fill("559000-1234");
  await page.getByLabel("Country of registration").selectOption("SE");
  await page.getByLabel("VAT number", { exact: true }).fill("SE 5590 0012 3401");
  await page.getByLabel("Registered address").fill("Storgatan 1\n111 22 Stockholm");
  await page.getByLabel("Contact person").fill("Anna Andersson");
  await page.getByRole("button", { name: "Add owner" }).click();
  await page.getByLabel("Name of owner 1").fill("Anna Andersson");
  await page.getByLabel("Share of owner 1").fill("100");
  await page.getByLabel("Your terms for traders").fill("https://example.com/terms");
}

/** The firm saves its details and goes on to the deposit, which it pays on the test payment page to send the application. */
export async function sendApplication(page: Page) {
  await page.getByRole("button", { name: "Save and continue" }).click();
  await payDeposit(page);
}

/** On the deposit step, the firm pays the deposit on the test payment page, which sends the application. */
export async function payDeposit(page: Page) {
  await expect(page).toHaveURL(/\/admin\/go-live\?step=deposit$/);
  await page.getByRole("button", { name: /^Pay .* and send for review$/ }).click();
  await expect(page).toHaveURL(/\/admin\/billing\/checkout\/test_[0-9a-f]+$/);
  await page.getByRole("button", { name: /^Pay / }).click();
  await expect(page).toHaveURL(/\/admin\/go-live\?checkout=done$/);
  await expect(page.getByText("Thank you. The deposit is paid and your application is sent.")).toBeVisible();
}

/** Our staff member opens the firm's review in our admin view, in a page of its own, logging in first when needed. */
export async function openAsStaff(page: Page, shortName: string): Promise<Page> {
  const ops = await page.context().newPage();
  await ops.goto(`${opsUrl}/ops/firms/${shortName}?tab=review`);
  const login = ops.getByRole("heading", { name: "Staff login" });
  const review = ops.getByRole("heading", { name: "Our checks" });
  await expect(login.or(review)).toBeVisible();
  if (await login.isVisible()) {
    await ops.getByLabel("Email").fill(staff.email);
    await ops.getByLabel("Password", { exact: true }).fill(staff.password);
    await ops.getByRole("button", { name: "Log in" }).click();
    await expect(ops).toHaveURL(`${opsUrl}/ops`);
    await ops.goto(`${opsUrl}/ops/firms/${shortName}?tab=review`);
  }

  await expect(review).toBeVisible();
  return ops;
}

/** Our staff approve the firm's application in its review, confirming in the dialog that asks first. */
export async function approveAsStaff(ops: Page) {
  await ops.getByRole("button", { name: "Approve", exact: true }).click();
  await ops.getByRole("dialog").getByRole("button", { name: /^Approve / }).click();
  await expect(ops.getByText("Approved", { exact: true })).toBeVisible();
  await expect(ops.getByRole("button", { name: "Approve", exact: true })).toHaveCount(0);
}

/** The firm sends a complete application with the deposit, our staff approve it, and the firm sets up its KYC, so it can go live. */
export async function getApproved(page: Page, shortName: string) {
  await fillApplication(page, `${shortName} Ltd`);
  await sendApplication(page);
  const ops = await openAsStaff(page, shortName);
  await approveAsStaff(ops);
  await ops.close();
  await chooseBuiltInKyc(page);
}
