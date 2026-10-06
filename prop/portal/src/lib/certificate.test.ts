import { describe, expect, it } from "vitest";

import type { AccountDetails } from "./api/types";
import { certificateColors, certificatesOf, certificateSvg, escapeXml, figureSize, nameSize, shareText } from "./certificate";
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
      {
        key: "passed",
        kind: "passed",
        title: "Certificate of achievement",
        headline: "Passed",
        figure: "Two-step 100K",
        figureLabel: "Passed the challenge",
        detail: "#1001",
        date: "6 Oct 2026",
        fileName: "c.png",
      },
      "Firm & Co",
      "Ann <b>",
      certificateColors({ accent: "#c9a35b" }),
      null,
    );

    expect(svg).toContain("Ann &lt;b&gt;");
    expect(svg).toContain("Firm &amp; Co");
    expect(escapeXml(`"'`)).toBe("&quot;&apos;");
  });

  it("make a long name or figure smaller, so it fits beside the seal", () => {
    expect(nameSize("Ann Berg")).toBe(84);
    expect(nameSize("visual-funded-1791154968967@e2e.example")).toBeLessThan(60);
    expect(nameSize("x".repeat(200))).toBe(32);
    expect(figureSize("6,400.00 USD")).toBe(112);
    expect(figureSize("Two-step 100K Pro Max Challenge")).toBeLessThan(60);
  });

  it("are drawn in the firm's colors, with ours for a color it lacks or that is not plain hex", () => {
    expect(certificateColors({ accent: "#c9a35b", background: "red" })).toEqual({
      background: "#0c0d10",
      panel: "#14161a",
      foreground: "#e4e6ea",
      muted: "#8a9099",
      accent: "#c9a35b",
    });
  });

  it("are shared with a sentence in the trader's words", () => {
    const [passed, payout] = certificatesOf(fundedWithPayout());

    expect(shareText(passed, "Aurora Funded")).toBe("I passed the Two-step 100K challenge with Aurora Funded.");
    expect(shareText(payout, "Aurora Funded")).toBe("I got a payout of 6,400.00 USD from Aurora Funded.");
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

function fundedWithPayout(): AccountDetails {
  const base = testDetails();
  return testDetails({
    account: { ...base.account, status: "Active", funded: true, stage: 2 },
    stages: base.stages.map((s, i) => (i < 2 ? { ...s, progress: "Passed", passedAt: "2026-10-05T15:00:00Z" } : { ...s, progress: "Current" })),
    payouts: [payout],
  });
}
