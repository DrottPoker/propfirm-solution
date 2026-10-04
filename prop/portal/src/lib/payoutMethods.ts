import type { PayoutMethod, PayoutMethodKind } from "./api/types";

export const payoutMethodKindLabels: Record<PayoutMethodKind, string> = {
  Bank: "Bank transfer",
  Crypto: "Crypto",
  Other: "Other",
};

/** One field of a payout method as it is read. Copy marks the ones the firm pastes into its payment. */
export type PayoutMethodLine = { label: string; value: string; copy: boolean };

type Field = { key: keyof Omit<PayoutMethod, "kind">; label: string; required: boolean; copy: boolean; placeholder?: string };

/** The fields of each kind, in the order they are asked for and shown. */
export const payoutMethodFields: Record<PayoutMethodKind, Field[]> = {
  Bank: [
    { key: "accountHolder", label: "Account holder", required: true, copy: false },
    { key: "accountNumber", label: "IBAN or account number", required: true, copy: true },
    { key: "bankCode", label: "BIC, SWIFT or routing number", required: false, copy: true },
    { key: "bankName", label: "Bank", required: false, copy: false },
  ],
  Crypto: [
    { key: "asset", label: "Currency", required: true, copy: false, placeholder: "For example USDT" },
    { key: "network", label: "Network", required: true, copy: false, placeholder: "For example TRC20" },
    { key: "address", label: "Wallet address", required: true, copy: true },
  ],
  Other: [{ key: "details", label: "How you want to be paid", required: true, copy: true, placeholder: "For example your PayPal email" }],
};

/** The method's fields as the trader and the firm read them, without those left empty. */
export function payoutMethodLines(method: PayoutMethod): PayoutMethodLine[] {
  return payoutMethodFields[method.kind].flatMap((field) => {
    const value = method[field.key];
    return value ? [{ label: field.label, value, copy: field.copy }] : [];
  });
}

/** The form's values: every field of every kind, so switching kind keeps what was typed. */
export type PayoutMethodForm = { kind: PayoutMethodKind } & Record<Field["key"], string>;

export function payoutMethodForm(method: PayoutMethod | null): PayoutMethodForm {
  return {
    kind: method?.kind ?? "Bank",
    accountHolder: method?.accountHolder ?? "",
    accountNumber: method?.accountNumber ?? "",
    bankCode: method?.bankCode ?? "",
    bankName: method?.bankName ?? "",
    asset: method?.asset ?? "",
    network: method?.network ?? "",
    address: method?.address ?? "",
    details: method?.details ?? "",
  };
}

/** The method the form describes: the fields of its kind only, trimmed, and those left empty as none. */
export function payoutMethodOf(form: PayoutMethodForm): PayoutMethod {
  const method: PayoutMethod = { kind: form.kind };
  for (const field of payoutMethodFields[form.kind]) {
    const value = form[field.key].trim();
    method[field.key] = value.length > 0 ? value : null;
  }

  return method;
}
