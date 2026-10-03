import { expect, type Page } from "@playwright/test";

// The development firm and its administrator, from Prop.Api's appsettings.Development.json.
export const firmName = "Demo Firm";
export const admin = { email: "admin@test.com", password: "admin" };

export const traderPassword = "e2e-trader-password";

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
