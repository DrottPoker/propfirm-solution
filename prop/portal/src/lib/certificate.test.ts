import { describe, expect, it } from "vitest";

import type { AccountDetails } from "./api/types";
import { certificatesOf, certificateSvg, escapeXml, nameSize } from "./certificate";
import { testDetails } from "./testAccounts";

describe("certificates", () => {
  it("come once every evaluation stage is passed, and for every paid payout", () => {
    const base = testDetails();
    const details = testDetails({
      account: { ...base.account, status: "Active", funded: true, stage: 2 },
      stages: base.stages.map((s, i) => (i < 2 ? { ...s, progress: "Passed", passedAt: `2026-10-0${i + 5}T15:00:00Z` } : { ...s, progress: "Current" })),
      payouts: [
        { ...payout, id: "b", paidAt: "2026-11-20T09:00:00Z", amount: 2_000 },
        { ...payout, id: "a", paidAt: "2026-11-02T09:00:00Z", amount: 6_400 },
        { ...payout, id: "c", status: "Pending", paidAt: null },
      ],
    });

    const certificates = certificatesOf(details);

    expect(certificates.map((c) => [c.headline, c.date, c.fileName])).toEqual([
      ["Passed the Two-step 100K challenge", "6 Oct 2026", "certificate-1001-passed.png"],
      ["Paid out 6,400.00 USD", "2 Nov 2026", "certificate-1001-payout-1.png"],
      ["Paid out 2,000.00 USD", "20 Nov 2026", "certificate-1001-payout-2.png"],
    ]);
  });

  it("are none while the challenge is being passed", () => {
    expect(certificatesOf(testDetails())).toEqual([]);
  });

  it("put the trader's name in the image safely", () => {
    const svg = certificateSvg(
      { key: "passed", title: "Certificate of achievement", headline: "Passed", detail: "#1001", date: "6 Oct 2026", fileName: "c.png" },
      "Firm & Co",
      "Ann <b>",
      "#2563eb",
      null,
    );

    expect(svg).toContain("Ann &lt;b&gt;");
    expect(svg).toContain("Firm &amp; Co");
    expect(escapeXml(`"'`)).toBe("&quot;&apos;");
  });

  it("make a long name smaller, so it fits inside the frame", () => {
    expect(nameSize("Ann Berg")).toBe(84);
    expect(nameSize("visual-funded-1791154968967@e2e.example")).toBeLessThan(60);
    expect(nameSize("x".repeat(200))).toBe(32);
  });
});

const payout: AccountDetails["payouts"][number] = {
  id: "p",
  accountId: "0199a000-0000-7000-8000-000000000001",
  accountNumber: 1001,
  email: "anna@test.example",
  tradingAccountId: "demo-firm-1001-3",
  status: "Paid",
  profit: 8_000,
  profitSplitPercent: 80,
  amount: 6_400,
  currency: "USD",
  requestedAt: "2026-11-01T08:00:00Z",
  withdrawnAt: "2026-11-01T08:00:01Z",
  approvedAt: "2026-11-01T10:00:00Z",
  paidAt: "2026-11-02T09:00:00Z",
  rejectedAt: null,
  failedAt: null,
  reason: null,
  reference: null,
  payTo: null,
  profitReturned: false,
  timeZone: "Europe/Stockholm",
};
