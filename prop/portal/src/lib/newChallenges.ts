import type { ChallengeDefinition, ChallengeTemplate } from "./api/types";

/** The account sizes firms sell most, to choose several from at once. */
export const challengeSizes = [5_000, 10_000, 25_000, 50_000, 100_000, 200_000] as const;

/** An account size as traders say it: 100K for 100,000, 12.5K for 12,500 and 1M for 1,000,000. As Prop.Api names its templates. */
export function sizeName(balance: number): string {
  if (balance >= 1_000_000 && balance % 100_000 === 0) {
    return `${trim(balance / 1_000_000)}M`;
  }

  if (balance >= 1_000 && balance % 100 === 0) {
    return `${trim(balance / 1_000)}K`;
  }

  return balance.toLocaleString("en-US", { maximumFractionDigits: 2 });
}

function trim(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}

/**
 * One challenge per size from the template, in the firm's currency, named like the template's own: for example
 * one-step-50k, "One-step 50K". The template's rules are percentages, so they fit every size.
 */
export function challengesFromTemplate(template: ChallengeTemplate, sizes: readonly number[], currency: string): ChallengeDefinition[] {
  return sizes.map((size) => {
    const name = sizeName(size);
    return {
      ...template.definition,
      id: `${template.id}-${name.toLowerCase().replace(".", "-")}`,
      name: `${template.name} ${name}`,
      initialBalance: size,
      currency,
    };
  });
}
