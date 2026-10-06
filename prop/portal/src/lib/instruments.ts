// Instruments as people say them: "Gold" for XAUUSD and "Euro / US dollar" for EURUSD, and the group they are in.

const metals: Record<string, string> = { XAU: "Gold", XAG: "Silver", XPT: "Platinum", XPD: "Palladium" };

const currencies: Record<string, { name: string; sign: string }> = {
  USD: { name: "US dollar", sign: "$" },
  EUR: { name: "Euro", sign: "€" },
  GBP: { name: "British pound", sign: "£" },
  JPY: { name: "Japanese yen", sign: "¥" },
  CHF: { name: "Swiss franc", sign: "Fr" },
  CAD: { name: "Canadian dollar", sign: "C$" },
  AUD: { name: "Australian dollar", sign: "A$" },
  NZD: { name: "New Zealand dollar", sign: "NZ$" },
  SEK: { name: "Swedish krona", sign: "kr" },
  NOK: { name: "Norwegian krone", sign: "kr" },
  DKK: { name: "Danish krone", sign: "kr" },
  SGD: { name: "Singapore dollar", sign: "S$" },
  HKD: { name: "Hong Kong dollar", sign: "HK$" },
  MXN: { name: "Mexican peso", sign: "Mex$" },
  ZAR: { name: "South African rand", sign: "R" },
  TRY: { name: "Turkish lira", sign: "₺" },
  PLN: { name: "Polish zloty", sign: "zł" },
  CNH: { name: "Chinese yuan", sign: "¥" },
};

export type InstrumentGroup = "Forex" | "Metals" | "Other";

/** The order the groups are listed in. */
export const instrumentGroups: InstrumentGroup[] = ["Forex", "Metals", "Other"];

/** The two halves of a six-letter symbol such as EURUSD, or null for another kind of symbol. */
function halves(symbol: string): [string, string] | null {
  return /^[A-Z]{6}$/.test(symbol) ? [symbol.slice(0, 3), symbol.slice(3)] : null;
}

/** Metals for a metal against a currency, Forex for two currencies, and Other for the rest. */
export function instrumentGroup(symbol: string): InstrumentGroup {
  const pair = halves(symbol);
  if (!pair) {
    return "Other";
  }

  if (metals[pair[0]]) {
    return "Metals";
  }

  return currencies[pair[0]] && currencies[pair[1]] ? "Forex" : "Other";
}

/** The instrument's name: the metal's, or the two currencies', or the symbol itself when it is neither. */
export function instrumentName(symbol: string): string {
  const pair = halves(symbol);
  if (!pair) {
    return symbol;
  }

  if (metals[pair[0]]) {
    return metals[pair[0]];
  }

  const [base, quote] = pair.map((code) => currencies[code]);
  return base && quote ? `${base.name} / ${quote.name}` : symbol;
}

/** The short marks for the symbol's two halves, such as "€" and "$", or "Au" for gold, for small coins beside it. */
export function instrumentMarks(symbol: string): [string, string] | null {
  const pair = halves(symbol);
  if (!pair) {
    return null;
  }

  const first = metals[pair[0]] ? { XAU: "Au", XAG: "Ag", XPT: "Pt", XPD: "Pd" }[pair[0]]! : currencies[pair[0]]?.sign;
  const second = currencies[pair[1]]?.sign;
  return first && second ? [first, second] : null;
}
