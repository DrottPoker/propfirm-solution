import type { StatusTone } from "./admin";
import type { DiscountCode } from "./api/types";
import { formatMoney } from "./format";

/** What a code takes off, for example "20% off" or "50.00 USD off". */
export function discountText(code: Pick<DiscountCode, "percentOff" | "amountOff" | "currency">): string {
  return code.percentOff != null ? `${code.percentOff}% off` : `${formatMoney(code.amountOff)} ${code.currency ?? ""} off`;
}

/** Whether buyers can use the code now: turned off, ended, used up or active. */
export function discountStatus(code: Pick<DiscountCode, "active" | "expiresAt" | "maxUses" | "uses">, now: number): { label: string; tone: StatusTone } {
  if (!code.active) {
    return { label: "Off", tone: "muted" };
  }

  if (code.expiresAt && Date.parse(code.expiresAt) <= now) {
    return { label: "Ended", tone: "muted" };
  }

  if (code.maxUses != null && code.uses >= code.maxUses) {
    return { label: "Used up", tone: "warning" };
  }

  return { label: "Active", tone: "profit" };
}

/** The end of the day the administrator picked, in their own time zone, as the moment the code stops working. */
export function endOfDay(isoDate: string): string {
  const [year, month, day] = isoDate.split("-").map(Number);
  return new Date(year, month - 1, day, 23, 59, 59).toISOString();
}
