import { describe, expect, it } from "vitest";

import { payoutMethodForm, payoutMethodLines, payoutMethodOf } from "./payoutMethods";

describe("payout methods", () => {
  it("are read without empty fields, with the ones the firm copies marked", () => {
    expect(payoutMethodLines({ kind: "Bank", accountHolder: "Anna Andersson", accountNumber: "SE45 5000", bankCode: null })).toEqual([
      { label: "Account holder", value: "Anna Andersson", copy: false },
      { label: "IBAN or account number", value: "SE45 5000", copy: true },
    ]);
    expect(payoutMethodLines({ kind: "Crypto", asset: "USDT", network: "TRC20", address: "TXyz" }).map((l) => l.value)).toEqual(["USDT", "TRC20", "TXyz"]);
  });

  it("keeps only the chosen kind's fields, trimmed", () => {
    const form = { ...payoutMethodForm(null), kind: "Crypto" as const, accountHolder: "Anna", asset: " USDT ", network: "TRC20", address: "" };

    expect(payoutMethodOf(form)).toEqual({ kind: "Crypto", asset: "USDT", network: "TRC20", address: null });
  });

  it("starts from the saved method, or a bank transfer", () => {
    expect(payoutMethodForm(null).kind).toBe("Bank");
    expect(payoutMethodForm({ kind: "Other", details: "anna@paypal.test" })).toMatchObject({ kind: "Other", details: "anna@paypal.test", accountHolder: "" });
  });
});
