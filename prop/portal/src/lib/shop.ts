import type { ChallengeDefinition, ShopItem, StageRules } from "./api/types";
import { formatMoney } from "./format";

/** One row of a price table: what it is, and its value for each account size. */
export type ShopRow = { label: string; values: string[] };

/** Challenges with the same rules in different account sizes, sizes as columns and rules as rows. */
export type ShopTable = { key: string; title: string; items: ShopItem[]; rows: ShopRow[] };

/**
 * The shop's challenges as price tables: those with the same rules and price currency share a table, the smallest
 * size first, and the tables come in the order of their cheapest challenge.
 */
export function shopTables(items: ShopItem[]): ShopTable[] {
  const groups = new Map<string, ShopItem[]>();
  for (const item of items) {
    const key = rulesKey(item);
    groups.set(key, [...(groups.get(key) ?? []), item]);
  }

  return [...groups.entries()]
    .map(([key, group]) => {
      const sorted = [...group].sort((a, b) => a.challenge.initialBalance - b.challenge.initialBalance);
      return { key, title: titleOf(sorted.map((i) => i.challenge.name)), items: sorted, rows: rowsOf(sorted) };
    })
    .sort((a, b) => Math.min(...a.items.map((i) => i.price)) - Math.min(...b.items.map((i) => i.price)));
}

/** An account size in a column's head, for example "100K" or "2.5K". */
export function sizeLabel(balance: number): string {
  return balance >= 1_000 && balance % 100 === 0 ? `${balance / 1_000}K` : formatMoney(balance);
}

// The rules as percentages, which are the same for every size, and the currency of the price.
function rulesKey(item: ShopItem): string {
  const { evaluation, funded, inactivityDays, tradingDay, currency } = item.challenge;
  return JSON.stringify({ evaluation, funded, inactivityDays: inactivityDays ?? null, tradingDay, currency, priceCurrency: item.currency });
}

// The words the names share, for example "Two-step" for "Two-step 50K" and "Two-step 100K".
function titleOf(names: string[]): string {
  if (names.length === 1) {
    return names[0];
  }

  const words = names.map((n) => n.split(" "));
  const shared: string[] = [];
  for (let i = 0; i < words[0].length && words.every((w) => w[i] === words[0][i]); i++) {
    shared.push(words[0][i]);
  }

  return shared.length > 0 ? shared.join(" ") : names[0];
}

function rowsOf(items: ShopItem[]): ShopRow[] {
  const first = items[0].challenge;
  const stages = [...first.evaluation, first.funded];
  const amount = (challenge: ChallengeDefinition, percent: number) => `${percent}% · ${formatMoney((challenge.initialBalance * percent) / 100)}`;
  const each = (value: (challenge: ChallengeDefinition) => string) => items.map((i) => value(i.challenge));
  const rows: ShopRow[] = [{ label: "Price", values: items.map((i) => `${formatMoney(i.price)} ${i.currency}`) }];

  if (first.evaluation.length === 0) {
    rows.push({ label: "Evaluation", values: each(() => "None, funded at once") });
  }

  first.evaluation.forEach((stage, index) => {
    rows.push({
      label: `${stage.name} target`,
      values: each((c) => {
        const target = c.evaluation[index].profitTargetPercent;
        return target === null ? "None" : amount(c, target);
      }),
    });
  });

  // A rule that is the same in every stage takes one row; otherwise each stage has its own.
  const perStage = (label: string, value: (stage: StageRules, challenge: ChallengeDefinition) => string) => {
    const values = stages.map((s) => value(s, first));
    if (values.every((v) => v === values[0])) {
      rows.push({ label, values: each((c) => value(c.funded, c)) });
    } else {
      stages.forEach((stage, index) => rows.push({ label: `${label}, ${stage.name}`, values: each((c) => value([...c.evaluation, c.funded][index], c)) }));
    }
  };
  perStage("Daily loss limit", (s, c) => amount(c, s.dailyLoss.percent));
  perStage("Max loss limit", (s, c) => `${amount(c, s.maxLoss.percent)}${s.maxLoss.kind === "Trailing" ? ", trailing" : ""}`);

  const minDays = first.evaluation.map((s) => s.minTradingDays);
  if (minDays.some((d) => d > 0)) {
    rows.push({ label: "Minimum trading days", values: each(() => (minDays.every((d) => d === minDays[0]) ? `${minDays[0]}` : minDays.join(" / "))) });
  }

  const maxDays = first.evaluation.map((s) => s.maxDays ?? null);
  if (maxDays.length > 0) {
    rows.push({ label: "Time limit", values: each(() => (maxDays.every((d) => d === null) ? "None" : maxDays.map((d) => (d === null ? "None" : `${d} days`)).join(" / "))) });
  }

  if (first.funded.profitSplitPercent != null) {
    rows.push({ label: "Profit split", values: each((c) => `${c.funded.profitSplitPercent}% to you`) });
  }

  if (first.funded.consistencyPercent != null) {
    rows.push({ label: "Consistency", values: each((c) => `Best day at most ${c.funded.consistencyPercent}% of a payout's profit`) });
  }

  if (first.inactivityDays != null) {
    rows.push({ label: "Inactivity", values: each((c) => `Ends after ${c.inactivityDays} days without a trade`) });
  }

  return rows;
}
