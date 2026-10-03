import type { ChallengeDefinition, StageRules } from "./api/types";

// The challenge editor keeps what is typed as text, and turns it into a definition when saving. Prop.Api checks
// the definition; the editor only refuses what is not a number at all.

export type DailyLossReference = StageRules["dailyLoss"]["reference"];

export type MaxLossKind = StageRules["maxLoss"]["kind"];

export type StageForm = {
  name: string;
  profitTargetPercent: string;
  minTradingDays: string;
  dailyLossPercent: string;
  dailyLossReference: DailyLossReference;
  maxLossPercent: string;
  maxLossKind: MaxLossKind;
  profitSplitPercent: string;
  /** Days to pass the stage, or empty for no time limit. */
  maxDays: string;
};

export type ChallengeForm = {
  id: string;
  name: string;
  currency: string;
  initialBalance: string;
  timeZone: string;
  dayStart: string;
  evaluation: StageForm[];
  funded: StageForm;
  /** Days without a new trade before the challenge ends, or empty for no limit. */
  inactivityDays: string;
};

export const maxEvaluationStages = 3;

export function formOf(definition: ChallengeDefinition): ChallengeForm {
  return {
    id: definition.id,
    name: definition.name,
    currency: definition.currency,
    initialBalance: String(definition.initialBalance),
    timeZone: definition.tradingDay.timeZone,
    dayStart: definition.tradingDay.start.slice(0, 5),
    evaluation: definition.evaluation.map(stageFormOf),
    funded: stageFormOf(definition.funded),
    inactivityDays: definition.inactivityDays == null ? "" : String(definition.inactivityDays),
  };
}

/** A new evaluation stage, like the last one but named after its place. */
export function nextStage(form: ChallengeForm): StageForm {
  const last = form.evaluation[form.evaluation.length - 1];
  return { ...last, name: `Phase ${form.evaluation.length + 1}` };
}

/** The definition to save, or the first field that is not a number. */
export function definitionOf(form: ChallengeForm): { definition: ChallengeDefinition } | { problem: string } {
  const balance = numberOf(form.initialBalance);
  if (balance === null) {
    return { problem: "The account size must be a number." };
  }

  const inactivityDays = optionalWholeNumber(form.inactivityDays);
  if (inactivityDays === undefined) {
    return { problem: "The days without a new trade must be a whole number, or empty for no limit." };
  }

  const stages: StageRules[] = [];
  for (const [index, stage] of [...form.evaluation, form.funded].entries()) {
    const funded = index === form.evaluation.length;
    const rules = stageOf(stage, funded);
    if ("problem" in rules) {
      return { problem: `${stage.name || (funded ? "Funded" : `Stage ${index + 1}`)}: ${rules.problem}` };
    }

    stages.push(rules.stage);
  }

  return {
    definition: {
      id: form.id.trim(),
      name: form.name.trim(),
      currency: form.currency,
      initialBalance: balance,
      tradingDay: { timeZone: form.timeZone.trim(), start: `${form.dayStart || "00:00"}:00` },
      evaluation: stages.slice(0, -1),
      funded: stages[stages.length - 1],
      inactivityDays,
    },
  };
}

function stageFormOf(stage: StageRules): StageForm {
  return {
    name: stage.name,
    profitTargetPercent: stage.profitTargetPercent == null ? "" : String(stage.profitTargetPercent),
    minTradingDays: String(stage.minTradingDays),
    dailyLossPercent: String(stage.dailyLoss.percent),
    dailyLossReference: stage.dailyLoss.reference,
    maxLossPercent: String(stage.maxLoss.percent),
    maxLossKind: stage.maxLoss.kind,
    profitSplitPercent: stage.profitSplitPercent == null ? "" : String(stage.profitSplitPercent),
    maxDays: stage.maxDays == null ? "" : String(stage.maxDays),
  };
}

function stageOf(stage: StageForm, funded: boolean): { stage: StageRules } | { problem: string } {
  const target = funded ? null : numberOf(stage.profitTargetPercent);
  const split = funded ? numberOf(stage.profitSplitPercent) : null;
  const days = numberOf(stage.minTradingDays);
  const daily = numberOf(stage.dailyLossPercent);
  const maxLoss = numberOf(stage.maxLossPercent);
  const maxDays = funded ? null : optionalWholeNumber(stage.maxDays);
  if (!funded && target === null) {
    return { problem: "the profit target must be a number." };
  }

  if (funded && split === null) {
    return { problem: "the profit split must be a number." };
  }

  if (days === null || !Number.isInteger(days)) {
    return { problem: "the trading days must be a whole number." };
  }

  if (daily === null || maxLoss === null) {
    return { problem: "the loss limits must be numbers." };
  }

  if (maxDays === undefined) {
    return { problem: "the time limit must be a whole number of days, or empty for none." };
  }

  return {
    stage: {
      name: stage.name.trim(),
      profitTargetPercent: target,
      minTradingDays: days,
      dailyLoss: { percent: daily, reference: stage.dailyLossReference },
      maxLoss: { percent: maxLoss, kind: stage.maxLossKind },
      profitSplitPercent: split,
      maxDays,
    },
  };
}

/** A whole number, null when empty, or undefined when it is not a whole number. */
function optionalWholeNumber(text: string): number | null | undefined {
  if (text.trim() === "") {
    return null;
  }

  const value = numberOf(text);
  return value !== null && Number.isInteger(value) ? value : undefined;
}

function numberOf(text: string): number | null {
  const trimmed = text.trim().replace(",", ".");
  if (trimmed === "") {
    return null;
  }

  const value = Number(trimmed);
  return Number.isFinite(value) ? value : null;
}
