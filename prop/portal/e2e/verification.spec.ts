import { expect, test } from "@playwright/test";

import { opsUrl, platformUrl } from "../playwright.config";

import { adminLink, fillApplication, openAsStaff, sendApplication, signUp, waitForSandbox } from "./support";

const pdf = Buffer.from("%PDF-1.7\nA certificate of registration\n%%EOF");

test("a new firm is reviewed by our staff, goes live, and can be suspended", async ({ page }) => {
  await signUp(page, "Review E2E Firm", "review-e2e-firm");
  await waitForSandbox(page);

  // The firm fills in its application, adds a document and sends it with the deposit.
  await fillApplication(page, "Review Trading Ltd");
  await expect(page.getByText("You pay a deposit of 200.00 USD when you send.")).toBeVisible();
  await page.getByLabel("Add a document").setInputFiles({ name: "certificate.pdf", mimeType: "application/pdf", buffer: pdf });
  await expect(page.getByRole("link", { name: "certificate.pdf" })).toBeVisible();
  await sendApplication(page);
  await expect(page.getByText("Waiting for review")).toBeVisible();

  // Our staff log in to our own admin view, find the firm waiting and ask for a change.
  const ops = await page.context().newPage();
  await ops.goto(`${opsUrl}/ops`);
  await expect(ops).toHaveURL(`${opsUrl}/ops/login`);
  await ops.close();
  const review = await openAsStaff(page, "review-e2e-firm");
  await expect(review.getByText("Review Trading Ltd")).toBeVisible();
  await expect(review.getByText("SE559000123401")).toBeVisible();
  await expect(review.getByRole("link", { name: "certificate.pdf" })).toBeVisible();
  await expect(review.getByText("200.00 USD paid")).toBeVisible();
  await review.getByLabel("Message to the firm").fill("Write your registered address as in the register.");
  await review.getByRole("button", { name: "Ask for changes" }).click();
  await expect(review.getByText("Changes needed", { exact: true })).toBeVisible();

  // The firm sees what we need, changes it and sends again without a new deposit.
  await page.reload();
  await expect(page.getByText("Write your registered address as in the register.")).toBeVisible();
  await page.getByLabel("Registered address").fill("Storgatan 1, 111 22 Stockholm, Sweden");
  await page.getByRole("button", { name: "Send for review", exact: true }).click();
  await expect(page.getByText("Waiting for review")).toBeVisible();

  // Our staff approve it.
  await review.reload();
  await review.getByRole("button", { name: "Approve" }).click();
  await expect(review.getByRole("button", { name: "Approve" })).toHaveCount(0);

  // The firm goes live, with the deposit taken off the startup fee.
  await adminLink(page, "Plan and billing").click();
  await page.getByLabel("Slots", { exact: true }).fill("30");
  await expect(page.getByRole("cell", { name: "Startup fee, less the deposit of 200.00 USD" })).toBeVisible();
  await page.getByRole("button", { name: /and go live$/ }).click();
  await page.getByRole("button", { name: /^Pay / }).click();
  await expect(page.getByText("Thank you. The payment went through.")).toBeVisible();

  // Our staff suspend the firm, which then sees why on every page, and lift it again.
  await review.reload();
  await review.getByLabel("Reason").fill("Traders report payouts that were never paid.");
  await review.getByRole("button", { name: "Suspend the firm" }).click();
  await expect(review.getByRole("button", { name: "Lift the suspension" })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("status").filter({ hasText: "Your firm is suspended: Traders report payouts that were never paid." })).toBeVisible();
  await review.getByRole("button", { name: "Lift the suspension" }).click();
  await expect(review.getByRole("button", { name: "Suspend the firm" })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("status").filter({ hasText: "Your firm is suspended" })).toHaveCount(0);
});

test("our admin view is only on its own address", async ({ page }) => {
  const onFirmPortal = await page.goto("/ops");
  expect(onFirmPortal?.status()).toBe(404);
  const onPlatform = await page.goto(`${platformUrl}/ops/login`);
  expect(onPlatform?.status()).toBe(404);

  await page.goto(`${opsUrl}/login`);
  await expect(page).toHaveURL(`${opsUrl}/ops/login`);
  await expect(page.getByRole("heading", { name: "Staff login" })).toBeVisible();
});
