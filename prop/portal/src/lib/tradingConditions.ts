import type { TradingConditionsSymbol, TradingSymbolRequest } from "./api/types";

/** One instrument as the form edits it: whether it is traded, and its conditions as typed. */
export type ConditionRow = {
  symbol: string;
  enabled: boolean;
  leverage: string;
  spreadMarkupPoints: string;
  commissionPerLotPerSide: string;
};

/** Conditions for an instrument the firm turns on, until it changes them. */
export const newInstrumentConditions = { leverage: 30, spreadMarkupPoints: 0, commissionPerLotPerSide: 0 };

export function conditionRows(symbols: TradingConditionsSymbol[]): ConditionRow[] {
  return symbols.map((s) => ({
    symbol: s.symbol,
    enabled: s.enabled,
    leverage: String(s.leverage ?? newInstrumentConditions.leverage),
    spreadMarkupPoints: String(s.spreadMarkupPoints ?? newInstrumentConditions.spreadMarkupPoints),
    commissionPerLotPerSide: (s.commissionPerLotPerSide ?? newInstrumentConditions.commissionPerLotPerSide).toFixed(2),
  }));
}

/** The request for the instruments turned on, or what is wrong with the form. */
export function conditionsRequest(rows: ConditionRow[]): { symbols: TradingSymbolRequest[] } | { problem: string } {
  const enabled = rows.filter((r) => r.enabled);
  if (enabled.length === 0) {
    return { problem: "Choose at least one instrument for your traders." };
  }

  const symbols: TradingSymbolRequest[] = [];
  for (const row of enabled) {
    const leverage = Number(row.leverage);
    const markup = Number(row.spreadMarkupPoints);
    const commission = Number(row.commissionPerLotPerSide.replace(",", "."));
    if (!Number.isInteger(leverage) || leverage < 1) {
      return { problem: `${row.symbol}: write the leverage as a whole number, such as 30 for 1:30.` };
    }

    if (!Number.isInteger(markup) || markup < 0) {
      return { problem: `${row.symbol}: write the spread markup as a whole number of points, 0 or more.` };
    }

    if (!Number.isFinite(commission) || commission < 0 || Math.round(commission * 100) !== commission * 100) {
      return { problem: `${row.symbol}: write the commission as an amount with at most two decimals, 0 or more.` };
    }

    symbols.push({ symbol: row.symbol, leverage, spreadMarkupPoints: markup, commissionPerLotPerSide: commission });
  }

  return { symbols };
}
