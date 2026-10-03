import { currencySign } from "@/lib/instruments";

const currencyColors: Record<string, string> = {
  USD: "#15803d",
  EUR: "#1d4ed8",
  GBP: "#7c3aed",
  JPY: "#be123c",
  CHF: "#b91c1c",
  AUD: "#0f766e",
  CAD: "#c2410c",
  NZD: "#334155",
  XAU: "#a16207",
  XAG: "#64748b",
};

/** Two overlapping coins for the base and quote currency, so no flag images are needed. */
export function SymbolIcon({ base, quote }: { base: string; quote: string }) {
  return (
    <span aria-hidden="true" className="relative inline-block h-7 w-9 shrink-0">
      <Coin currency={quote} className="right-0 bottom-0 size-5 text-[9px]" />
      <Coin currency={base} className="top-0 left-0 size-6 text-[11px] ring-2 ring-panel" />
    </span>
  );
}

function Coin({ currency, className }: { currency: string; className: string }) {
  return (
    <span
      className={`absolute flex items-center justify-center rounded-full font-semibold text-white ${className}`}
      style={{ backgroundColor: currencyColors[currency] ?? "#475569" }}
    >
      {currencySign(currency)}
    </span>
  );
}
