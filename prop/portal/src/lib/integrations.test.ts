import { describe, expect, it } from "vitest";

import type { WebhookDelivery } from "./api/types";
import { deliveryNote, startChallengeExample } from "./integrations";

const delivery: WebhookDelivery = {
  id: "0199a000-0000-7000-8000-000000000201",
  eventType: "webhook.test",
  createdAt: "2026-10-05T08:00:00Z",
  status: "Delivered",
  attempts: 1,
  lastStatus: 200,
  lastError: null,
  deliveredAt: "2026-10-05T08:00:01Z",
  nextAttemptAt: null,
};

describe("integrations", () => {
  it("say how a webhook went", () => {
    expect(deliveryNote(delivery)).toBe("Delivered");
    expect(deliveryNote({ ...delivery, attempts: 3 })).toBe("Delivered after 3 tries");
    expect(deliveryNote({ ...delivery, status: "Pending", attempts: 2, lastStatus: 500 })).toBe("Tried 2 times, answered 500. Tried again soon.");
    expect(deliveryNote({ ...delivery, status: "Pending", attempts: 0, lastStatus: null })).toBe("On its way");
    expect(deliveryNote({ ...delivery, status: "Failed", attempts: 16, lastStatus: null, lastError: "timed out" })).toBe("Given up after 16 tries, timed out");
  });

  it("have an example that starts a challenge", () => {
    expect(startChallengeExample("https://api.example.com/api/firm/v1/", "two-step-100k")).toContain(
      `curl -X POST https://api.example.com/api/firm/v1/accounts \\`,
    );
    expect(startChallengeExample("https://api.example.com/api/firm/v1/", "two-step-100k")).toContain('"challengeId":"two-step-100k"');
  });
});
