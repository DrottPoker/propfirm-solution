"use client";

import Link from "next/link";
import { useState } from "react";

import { passRateText } from "@/lib/admin";
import type { ChallengeDefinition, ChallengeFigures, ChallengePrice } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { priceCurrencies } from "@/lib/orders";
import { useChallengeFigures, useChallenges, useFirmSettings, usePrices, useSavePrice } from "@/lib/queries";

import { PlusIcon } from "./icons";
import { AdminPage, Badge, buttonClass, ErrorText, fieldClass, Message, PageHeader, secondaryButtonClass } from "./ui";

/** What the firm sells: each challenge with its stages, its price in the portal and how it is doing. */
export function AdminChallenges() {
  const settings = useFirmSettings();
  const challenges = useChallenges(settings.data !== undefined && settings.data.status !== "Provisioning");
  const prices = usePrices();
  const figures = useChallengeFigures();

  if (settings.data?.status === "Provisioning") {
    return <Message text="Your trading server is being set up. Your first challenge comes with it." />;
  }

  return (
    <AdminPage>
      <PageHeader
        title="Challenges"
        description="What your firm sells. Set the price and put a challenge up for sale here. A change of the rules applies to challenges started after it."
        actions={
          <Link href="/admin/challenges/new" className={`${buttonClass} flex items-center gap-2`}>
            <PlusIcon />
            New challenges
          </Link>
        }
      />
      {settings.data && !settings.data.payments.active && (
        <p className="rounded-lg border border-warning/40 bg-warning/10 px-4 py-3 text-sm">
          Your portal sells nothing yet. A challenge with a price that is for sale is sold once you choose how traders pay under{" "}
          <Link href="/admin/checkout" className="underline">
            Checkout
          </Link>
          .
        </p>
      )}
      <ErrorText error={challenges.error ?? prices.error ?? figures.error} />
      {challenges.data && challenges.data.length === 0 && <p className="text-sm text-muted">No challenges yet.</p>}
      {/* The prices come first, since each card starts from the challenge's price. */}
      <ul className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {(prices.data ? (challenges.data ?? []) : []).map((challenge) => (
          <ChallengeCard
            key={challenge.id}
            challenge={challenge}
            price={prices.data?.find((p) => p.challengeId === challenge.id)}
            figures={figures.data?.find((f) => f.challengeId === challenge.id)}
          />
        ))}
      </ul>
    </AdminPage>
  );
}

function ChallengeCard({ challenge, price, figures }: { challenge: ChallengeDefinition; price?: ChallengePrice; figures?: ChallengeFigures }) {
  const forSale = price?.forSale === true;
  const first = challenge.evaluation[0] ?? challenge.funded;
  const timeLimits = challenge.evaluation.map((s) => s.maxDays).filter((d) => d != null);
  return (
    <li className={`flex flex-col gap-4 rounded-lg border p-5 ${forSale ? "border-border bg-panel" : "border-dashed border-border"}`}>
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-0.5">
          <h2 className="font-semibold">{challenge.name}</h2>
          <span className="text-sm text-muted">
            <span className="font-mono">{challenge.id}</span> · {formatMoney(challenge.initialBalance)} {challenge.currency} account
          </span>
        </div>
        <Badge tone={forSale ? "profit" : "muted"}>{forSale ? "For sale" : price ? "Not for sale" : "No price"}</Badge>
      </div>
      <ol aria-label="Stages" className="flex flex-wrap items-center gap-1.5 text-xs">
        {challenge.evaluation.length === 0 && <li className="rounded-full border border-border px-2.5 py-1 text-muted">No evaluation</li>}
        {challenge.evaluation.map((stage) => (
          <li key={stage.name} className="flex items-center gap-1.5">
            <span className="rounded-full border border-border px-2.5 py-1">
              {stage.name} · {stage.profitTargetPercent}% target
            </span>
            <span aria-hidden="true" className="text-muted">
              ›
            </span>
          </li>
        ))}
        <li className="rounded-full border border-profit/40 px-2.5 py-1 text-profit">
          {challenge.funded.name} · {challenge.funded.profitSplitPercent}% split
        </li>
      </ol>
      <p className="text-sm text-muted">
        Daily loss {first.dailyLoss.percent}% · max loss {first.maxLoss.percent}% {first.maxLoss.kind.toLowerCase()}
        {challenge.evaluation.length > 0 && first.minTradingDays > 0 && ` · at least ${first.minTradingDays} trading days a phase`}
        {challenge.evaluation.length === 0 ? " · funded from the start" : timeLimits.length > 0 ? ` · a time limit of ${timeLimits.join(" and ")} days` : " · no time limit"}
        {challenge.inactivityDays != null && ` · ends after ${challenge.inactivityDays} days without a trade`}
      </p>
      {figures && (
        <dl className="grid grid-cols-3 gap-3 border-t border-border pt-4 text-xs">
          <div className="flex flex-col gap-0.5">
            <dt className="text-muted">Trading now</dt>
            <dd className="font-mono text-base">{figures.trading}</dd>
          </div>
          <div className="flex flex-col gap-0.5">
            <dt className="text-muted">Started, 30 days</dt>
            <dd className="font-mono text-base">{figures.startedLast30Days}</dd>
          </div>
          <div className="flex flex-col gap-0.5">
            <dt className="text-muted">Pass rate, 90 days</dt>
            <dd className="font-mono text-base">{passRateText(figures.passRate)}</dd>
          </div>
        </dl>
      )}
      <PriceControl challenge={challenge} price={price} />
      <div className="mt-auto flex flex-wrap justify-end gap-2">
        <Link href={`/admin/challenges/edit?from=${encodeURIComponent(challenge.id)}`} className={`${secondaryButtonClass} text-sm`} aria-label={`Duplicate ${challenge.name}`}>
          Duplicate
        </Link>
        <Link href={`/admin/challenges/edit?id=${encodeURIComponent(challenge.id)}`} className={`${secondaryButtonClass} text-sm`} aria-label={`Change the rules of ${challenge.name}`}>
          Change rules
        </Link>
      </div>
    </li>
  );
}

/**
 * The challenge's price in the shop and whether it is for sale, set on the card. A challenge without a price cannot be
 * for sale, but the firm can still start it for a trader.
 */
export function PriceControl({ challenge, price }: { challenge: ChallengeDefinition; price?: ChallengePrice }) {
  const save = useSavePrice();
  const defaultCurrency = priceCurrencies.some((c) => c === challenge.currency) ? challenge.currency : "USD";
  const [amount, setAmount] = useState(price ? String(price.amount) : "");
  const [currency, setCurrency] = useState(price?.currency ?? defaultCurrency);
  const [forSale, setForSale] = useState(price?.forSale ?? false);
  const [problem, setProblem] = useState<string | null>(null);

  // A saved price that changed, for example one that arrived after the card or was set elsewhere, replaces the fields.
  const savedKey = price ? `${price.amount}|${price.currency}|${price.forSale}` : "";
  const [seen, setSeen] = useState(savedKey);
  if (seen !== savedKey) {
    setSeen(savedKey);
    setAmount(price ? String(price.amount) : "");
    setCurrency(price?.currency ?? defaultCurrency);
    setForSale(price?.forSale ?? false);
  }
  const value = Number(amount.trim().replace(",", "."));
  const hasPrice = amount.trim() !== "";
  const changed = amount !== (price ? String(price.amount) : "") || currency !== (price?.currency ?? currency) || forSale !== (price?.forSale ?? false);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (!(Number.isFinite(value) && value > 0)) {
      setProblem("Write the price as a number, for example 99 or 89.50.");
      return;
    }

    setProblem(null);
    save.mutate({ challengeId: challenge.id, amount: value, currency, forSale });
  };

  return (
    <form onSubmit={submit} className="flex flex-col gap-2 border-t border-border pt-4 text-sm">
      <div className="flex flex-wrap items-center gap-3">
        <span className="flex gap-2">
          <input
            aria-label={`Price of ${challenge.name}`}
            inputMode="decimal"
            value={amount}
            onChange={(e) => {
              save.reset();
              setAmount(e.target.value);
              if (e.target.value.trim() === "") {
                setForSale(false);
              }
            }}
            placeholder="Price"
            className={`${fieldClass} w-28 py-1.5 text-right font-mono`}
          />
          <select aria-label={`Price currency of ${challenge.name}`} value={currency} onChange={(e) => setCurrency(e.target.value)} className={`${fieldClass} py-1.5`}>
            {priceCurrencies.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        </span>
        <label className={`flex items-center gap-2 ${hasPrice ? "" : "text-muted"}`} title={hasPrice ? undefined : "Set a price first."}>
          <input type="checkbox" checked={forSale} disabled={!hasPrice} onChange={(e) => setForSale(e.target.checked)} className="size-4 accent-accent" />
          For sale in your shop
        </label>
        <button type="submit" disabled={!changed || !hasPrice || save.isPending} className={`${buttonClass} ml-auto py-1.5 text-sm`}>
          {save.isPending ? "Saving..." : "Save price"}
        </button>
      </div>
      {save.isSuccess && !changed && (
        <p role="status" className="text-xs text-profit">
          Saved.
        </p>
      )}
      <ErrorText error={problem ? new Error(problem) : save.error} />
    </form>
  );
}
