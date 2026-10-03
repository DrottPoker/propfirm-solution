import { expect, type Page } from "@playwright/test";

import { platformUrl, portalPort } from "../playwright.config";

// The development firm and its administrator, from Prop.Api's appsettings.Development.json.
export const firmName = "Demo Firm";
export const admin = { email: "admin@test.com", password: "admin" };

export const traderPassword = "e2e-trader-password";

const ownerPassword = "e2e-owner-password";

/** A firm signs up on the platform and lands in its own admin panel, on its own address. Development needs no email confirmation. */
export async function signUp(page: Page, firmName: string, shortName: string) {
  await page.goto(`${platformUrl}/signup`);
  await page.getByLabel("Firm name").fill(firmName);
  await expect(page.getByLabel("Short name")).toHaveValue(shortName);
  await expect(page.getByText("Available")).toBeVisible();
  await page.getByLabel("Your email").fill(`owner@${shortName}.e2e.example`);
  await page.getByLabel("Password").fill(ownerPassword);
  await page.getByRole("checkbox").check();
  await page.getByRole("button", { name: "Create my firm" }).click();
  await expect(page).toHaveURL(`http://${shortName}.localhost:${portalPort}/admin`);
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
