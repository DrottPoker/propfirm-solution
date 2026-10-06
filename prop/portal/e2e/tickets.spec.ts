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
  await expect(trader.getByRole("navigation", { name: "Main" }).getByRole("link", { name: /Support.*1 unread message/ })).toBeVisible();
  await trader.getByRole("link", { name: new RegExp(subject.replace(/[?]/g, "\\?")) }).click();
  await expect(trader.getByText("Choose the server Demo Firm in the terminal, then the prices show.")).toBeVisible();
  await expect(trader.getByText("Closed", { exact: true })).toBeVisible();
  await expect(trader.getByRole("navigation", { name: "Main" }).getByRole("link", { name: /Support.*unread/ })).toHaveCount(0);
  await trader.getByLabel("Write to open the ticket again").fill("Thanks, it works now.");
  await trader.getByRole("button", { name: "Send", exact: true }).click();
  await expect(trader.getByText(`Waiting for ${firmName}`)).toBeVisible();
});

test("the firm writes to a trader first from the trader's card, and the trader answers", async ({ context, page }) => {
  const email = `notice-${test.info().testId.slice(0, 8)}@e2e.example`;
  const subject = `Your EURUSD trade ${test.info().testId.slice(0, 8)}`;

  await logIn(page, "/admin/login", admin.email, admin.password);
  await startChallenge(page, email);
  await page.getByRole("button", { name: "Create invitation link" }).click();
  const invitation = await page.getByLabel(/Invitation link/).inputValue();
  const trader = await context.newPage();
  await acceptInvitation(trader, invitation);

  // From the trader's card the form knows the trader and the account.
  await page.getByRole("link", { name: "Write to the trader" }).click();
  await expect(page).toHaveURL(/\/admin\/support\/new\?/);
  await expect(page.getByLabel("Trader's email")).toHaveValue(email);
  await expect(page.getByLabel("About").locator("option:checked")).toHaveText(/^Account #\d+/);
  await page.getByLabel("Subject").fill(subject);
  await page.getByLabel("Message").fill("Did the terminal show a gap before your EURUSD trade closed?");
  await page.getByRole("button", { name: "Send", exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/support\/[0-9a-f-]+$/);
  await expect(page.getByText("Waiting for the trader")).toBeVisible();

  // The trader sees the message unread, as a message from the firm, and answers it.
  await trader.goto("/support");
  await expect(trader.getByRole("navigation", { name: "Main" }).getByRole("link", { name: /Support.*1 unread message/ })).toBeVisible();
  await trader.getByRole("link", { name: new RegExp(subject) }).click();
  await expect(trader.getByText(`Message from ${firmName}`)).toBeVisible();
  await expect(trader.getByText("Did the terminal show a gap before your EURUSD trade closed?")).toBeVisible();
  await trader.getByLabel("Write back").fill("Yes, here is a screenshot.");
  await trader.getByLabel("Add files").setInputFiles(screenshot);
  await trader.getByRole("button", { name: "Send", exact: true }).click();
  await expect(trader.getByText(`Waiting for ${firmName}`)).toBeVisible();

  // The firm sees the answer waiting for it.
  await page.reload();
  await expect(page.getByText("Yes, here is a screenshot.")).toBeVisible();
  await expect(adminLink(page, "Support")).toContainText(/\d+ waiting for you/);
});

test("the firm saves a reply, answers with it filled in for the trader, and changes and deletes it", async ({ page }) => {
  const email = `replies-${test.info().testId.slice(0, 8)}@e2e.example`;
  const title = `Payout times ${test.info().testId.slice(0, 8)}`;

  // A ticket about the trader's account, which shows where the account is beside the ticket.
  await logIn(page, "/admin/login", admin.email, admin.password);
  await startChallenge(page, email);
  await page.getByRole("link", { name: "Write to the trader" }).click();
  await page.getByLabel("Subject").fill("About your payout");
  await page.getByLabel("Message").fill("We have a question about your account.");
  await page.getByRole("button", { name: "Send", exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/support\/[0-9a-f-]+$/);
  await expect(page.getByRole("link", { name: /^Account #\d+/ })).toBeVisible();
  await expect(page.getByRole("definition").filter({ hasText: /^Phase 1/ })).toBeVisible();

  // Without saved replies, the list says what they are and adds the first one.
  const savedReplies = page.getByRole("button", { name: "Saved replies", exact: true });
  const menu = page.getByRole("dialog", { name: "Choose a saved reply" });
  const panel = page.getByRole("dialog", { name: "Saved replies" });
  await savedReplies.click();
  await expect(menu.getByText("No saved replies yet")).toBeVisible();
  await menu.getByRole("button", { name: "Add a saved reply" }).click();
  await expect(panel.getByText("You can use {trader} and {firm}.")).toBeVisible();
  await panel.getByLabel("Title", { exact: true }).fill(title);
  await panel.getByLabel("Reply", { exact: true }).fill("Hi {trader},\npayouts from {firm} are paid within 2 days.");
  await panel.getByRole("button", { name: "Save reply" }).click();
  await expect(page.getByText("Saved.")).toBeVisible();
  await expect(panel.getByRole("list", { name: "Your saved replies" }).getByText(title)).toBeVisible();
  await panel.getByRole("button", { name: "Done" }).click();
  await expect(panel).toBeHidden();

  // The reply goes in at the cursor, after what is written, with the trader and the firm filled in, to edit before it is sent.
  await page.getByLabel("Your answer").fill("Thanks for asking.");
  await savedReplies.click();
  await menu.getByRole("button", { name: title }).click();
  await expect(menu).toBeHidden();
  await expect(page.getByLabel("Your answer")).toHaveValue(`Thanks for asking. Hi ${email},\npayouts from ${firmName} are paid within 2 days.`);
  await expect(page.getByLabel("Your answer")).toBeFocused();
  await page.getByRole("button", { name: "Send", exact: true }).click();
  await expect(page.getByRole("list", { name: "Messages" }).getByText(`payouts from ${firmName} are paid within 2 days.`)).toBeVisible();

  // The reply is changed, and deleted after a question.
  await savedReplies.click();
  await menu.getByRole("button", { name: "Manage saved replies" }).click();
  await panel.getByRole("button", { name: `Edit ${title}` }).click();
  await expect(panel.getByLabel("Title", { exact: true })).toHaveValue(title);
  await panel.getByLabel("Reply", { exact: true }).fill("Hi {trader}, payouts are paid within 3 days.");
  await panel.getByRole("button", { name: "Save reply" }).click();
  await expect(panel.getByText("Hi {trader}, payouts are paid within 3 days.")).toBeVisible();
  await panel.getByRole("button", { name: `Delete ${title}` }).click();
  await page.getByRole("dialog", { name: /^Delete/ }).getByRole("button", { name: "Delete", exact: true }).click();
  await expect(panel.getByText("No saved replies yet")).toBeVisible();
});
