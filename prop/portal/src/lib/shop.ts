import type { ChallengeDefinition, ShopItem, ShopPayouts, StageRules } from "./api/types";
import { formatMoney, priceText, wholeAmount } from "./format";

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
  const amount = (challenge: ChallengeDefinition, percent: number) => `${percent}% · ${wholeAmount((challenge.initialBalance * percent) / 100)} ${challenge.currency}`;
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


/** One rule on a challenge's card in the shop: what it is, and its value in short, such as "Daily loss" and "5%". */
export type ShopRule = { key: "target" | "daily" | "max" | "split" | "days" | "time" | "activity"; label: string; value: string };

/** The rules a buyer compares challenges by, in short, for the challenge's card. The table under the cards has them all. */
export function shopRules(challenge: ChallengeDefinition): ShopRule[] {
  const first = challenge.evaluation[0] ?? challenge.funded;
  const targets = challenge.evaluation.map((s) => s.profitTargetPercent).filter((t): t is number => t != null);
  const minDays = challenge.evaluation.map((s) => s.minTradingDays).filter((d) => d > 0);
  const maxDays = challenge.evaluation.map((s) => s.maxDays).filter((d): d is number => d != null);
  const rules: ShopRule[] = [];
  rules.push({
    key: "target",
    label: targets.length > 1 ? "Profit targets" : "Profit target",
    value: challenge.evaluation.length === 0 ? "None, funded at once" : targets.length === 0 ? "None" : targets.map((t) => `${t}%`).join(" then "),
  });
  rules.push({ key: "daily", label: "Daily loss limit", value: `${first.dailyLoss.percent}%` });
  rules.push({ key: "max", label: "Max loss limit", value: `${first.maxLoss.percent}%${first.maxLoss.kind === "Trailing" ? ", trailing" : ""}` });
  if (challenge.funded.profitSplitPercent != null) {
    rules.push({ key: "split", label: "Profit split", value: `${challenge.funded.profitSplitPercent}% to you` });
  }

  if (minDays.length > 0) {
    rules.push({ key: "days", label: "Trading days", value: `At least ${Math.max(...minDays)} a phase` });
  }

  rules.push({ key: "time", label: "Time limit", value: maxDays.length === 0 ? "None" : `${Math.max(...maxDays)} days a phase` });
  return rules;
}

/** A question a buyer asks before buying, answered from the firm's rules. */
export type ShopQuestion = { question: string; answer: string };

/** The questions buyers ask, answered from the rules of the challenges the firm sells, and the firm's name. */
export function shopQuestions(items: ShopItem[], firmName: string): ShopQuestion[] {
  const challenges = items.map((i) => i.challenge);
  const splits = challenges.map((c) => c.funded.profitSplitPercent).filter((s): s is number => s != null);
  const inactivity = challenges.map((c) => c.inactivityDays).filter((d): d is number => d != null);
  const payoutDays = challenges.map((c) => c.funded.minTradingDays).filter((d) => d > 0);
  const timeLimits = challenges.flatMap((c) => c.evaluation.map((s) => s.maxDays)).filter((d): d is number => d != null);
  const questions: ShopQuestion[] = [
    {
      question: "How does it work?",
      answer: `You trade an account with ${firmName}'s money in the browser. Reach the profit target without breaking a loss limit to pass a phase. After the last phase you get a funded account, and from it you ask for payouts.`,
    },
    {
      question: "What happens if I break a loss limit?",
      answer: "The challenge ends at once, and any open positions are closed. Your account page shows exactly when and why. You can start a new challenge whenever you like.",
    },
    {
      question: "When can I ask for a payout?",
      answer:
        `Once your account is funded and in profit${payoutDays.length > 0 ? `, with at least ${Math.min(...payoutDays)} trading days since the last payout` : ""}. ` +
        `${firmName} checks it and sends the money${splits.length > 0 ? `, and you keep ${Math.max(...splits) === Math.min(...splits) ? `${splits[0]}%` : `up to ${Math.max(...splits)}%`} of the profit` : ""}.`,
    },
    {
      question: "Is there a time limit?",
      answer:
        (timeLimits.length === 0 ? "No phase has to be passed within a set time." : `Some phases have to be passed within ${Math.min(...timeLimits)} days or more, as each challenge says.`) +
        (inactivity.length > 0 ? ` A challenge ends if no new trade is opened for ${Math.min(...inactivity)} days.` : ""),
    },
    {
      question: "Where do I trade?",
      answer: "In the Kronant Trader terminal, in your browser on a computer or a phone. You open it from your account in this portal, so there is nothing to install.",
    },
  ];
  return questions;
}

/**
 * What the firm paid out lately, as its shop says it when the firm chose to show it: the total per currency in whole
 * amounts, never rounded up, with how many payouts, and how soon they were paid on average.
 */
export function payoutLines(payouts: ShopPayouts): string[] {
  const totals = payouts.totals.map((t) => priceText(Math.floor(t.amount), t.currency)).join(" + ");
  const lines = [`${totals} paid out to traders in the last 30 days${payouts.count > 1 ? `, in ${payouts.count} payouts` : ""}`];
  const days = payouts.averageDaysToPay;
  if (days != null) {
    lines.push(days < 1 ? "Paid within a day of the request on average" : `Paid ${days} ${days === 1 ? "day" : "days"} after the request on average`);
  }

  return lines;
}
