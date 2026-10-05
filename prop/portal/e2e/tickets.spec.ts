import { expect, test } from "@playwright/test";

import { acceptInvitation, admin, adminLink, firmName, logIn, startChallenge } from "./support";

// A screenshot of one pixel, as a trader would attach one.
const screenshot = { name: "screen.png", mimeType: "image/png", buffer: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==", "base64") };

test("a trader asks about an account in a ticket, and the firm answers and closes it", async ({ context, page }) => {
  const email = `support-${test.info().testId.slice(0, 8)}@e2e.example`;
  const subject = `Is my account ready? ${test.info().testId.slice(0, 8)}`;

  await logIn(page, "/admin/login", admin.email, admin.password);
  await startChallenge(page, email);
  await page.getByRole("button", { name: "Create invitation link" }).click();
  const invitation = await page.getByLabel(/Invitation link/).inputValue();
  const trader = await context.newPage();
  await acceptInvitation(trader, invitation);

  // The trader asks from the account's page, so the ticket is about that account, and adds a screenshot.
  await trader.getByRole("link", { name: /^Details of account/ }).click();
  const accountNumber = (await trader.getByText(/^Account #\d+/).first().textContent())!.match(/#(\d+)/)![1];
  await trader.getByRole("link", { name: "Ask about this account" }).click();
  await expect(trader.getByLabel("About").locator("option:checked")).toHaveText(new RegExp(`^Account #${accountNumber}`));
  await trader.getByLabel("Subject").fill(subject);
  await trader.getByLabel("Message").fill("Hi,\nthe terminal shows no prices for me.");
  await trader.getByLabel("Add files").setInputFiles(screenshot);
  await trader.getByRole("button", { name: "Send", exact: true }).click();
  await expect(trader).toHaveURL(/\/support\/[0-9a-f-]+$/);
  await expect(trader.getByRole("heading", { name: subject })).toBeVisible();
  await expect(trader.getByText(`Waiting for ${firmName}`)).toBeVisible();
  await expect(trader.getByRole("img", { name: "screen.png" })).toBeVisible();

  // The firm finds it waiting, sees what it is about, answers and closes it at once.
  await page.reload();
  await expect(adminLink(page, "Support")).toContainText(/\d+ waiting for you/);
  await adminLink(page, "Support").click();
  await page.getByRole("link", { name: new RegExp(subject.replace(/[?]/g, "\\?")) }).click();
  await expect(page.getByRole("link", { name: new RegExp(`^Account #${accountNumber}`) })).toBeVisible();
  await expect(page.getByText("the terminal shows no prices for me.")).toBeVisible();
  await page.getByLabel("Your answer").fill("Hi!\nChoose the server Demo Firm in the terminal, then the prices show.");
  await page.getByRole("button", { name: "Send and close" }).click();
  await expect(page.getByText(`by ${admin.email}. A new message opens it again.`)).toBeVisible();

  // The trader sees the unread answer in the menu and reads it. Writing again opens the ticket again.
  await trader.goto("/support");
  await expect(trader.getByRole("navigation", { name: "Main" }).getByRole("link", { name: /Support.*1 new answer/ })).toBeVisible();
  await trader.getByRole("link", { name: new RegExp(subject.replace(/[?]/g, "\\?")) }).click();
  await expect(trader.getByText("Choose the server Demo Firm in the terminal, then the prices show.")).toBeVisible();
  await expect(trader.getByText("Closed", { exact: true })).toBeVisible();
  await expect(trader.getByRole("navigation", { name: "Main" }).getByRole("link", { name: /Support.*new answer/ })).toHaveCount(0);
  await trader.getByLabel("Write to open the ticket again").fill("Thanks, it works now.");
  await trader.getByRole("button", { name: "Send", exact: true }).click();
  await expect(trader.getByText(`Waiting for ${firmName}`)).toBeVisible();
});
