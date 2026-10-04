import type { WebhookDelivery } from "./api/types";

/** The firm API's calls a firm's own systems use most, with what they do. Paths are after the API's address. */
export const firmApiCalls: { method: string; path: string; text: string }[] = [
  { method: "POST", path: "accounts", text: "Start a challenge for a trader, with the trader's email and the challenge's id." },
  { method: "GET", path: "accounts/{id}", text: "An account: its status, phase, balance and limits." },
  { method: "POST", path: "accounts/{id}/invite", text: "A link that invites the trader to your portal." },
  { method: "GET", path: "payouts?status=Pending", text: "The payouts that wait for your approval." },
  { method: "POST", path: "orders/{id}/mark-paid", text: "An order paid on your own checkout page, which starts its challenge." },
];

/** A ready command that starts a challenge, to paste into a terminal with the firm's key in it. */
export function startChallengeExample(apiUrl: string, challengeId: string): string {
  return [
    `curl -X POST ${apiUrl}accounts \\`,
    '  -H "X-Api-Key: YOUR_KEY" -H "Content-Type: application/json" \\',
    `  -d '{"email":"trader@example.com","challengeId":"${challengeId}"}'`,
  ].join("\n");
}

/** What happened to a webhook, in a few words: delivered, waiting for the next try with the firm's last answer, or given up. */
export function deliveryNote(delivery: WebhookDelivery): string {
  const answer = delivery.lastStatus !== null ? `answered ${delivery.lastStatus}` : delivery.lastError ? delivery.lastError : null;
  switch (delivery.status) {
    case "Delivered":
      return delivery.attempts > 1 ? `Delivered after ${delivery.attempts} tries` : "Delivered";
    case "Failed":
      return `Given up after ${delivery.attempts} tries${answer ? `, ${answer}` : ""}`;
    default:
      return delivery.attempts === 0 ? "On its way" : `Tried ${delivery.attempts} ${delivery.attempts === 1 ? "time" : "times"}${answer ? `, ${answer}` : ""}. Tried again soon.`;
  }
}
