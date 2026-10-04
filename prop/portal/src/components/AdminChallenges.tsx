"use client";

import Link from "next/link";

import { passRateText } from "@/lib/admin";
import type { ChallengeDefinition, ChallengeFigures, ChallengePrice } from "@/lib/api/types";
import { formatMoney } from "@/lib/format";
import { useChallengeFigures, useChallenges, useFirmSettings, usePrices } from "@/lib/queries";

import { PlusIcon } from "./icons";
import { AdminPage, Badge, buttonClass, ErrorText, Message, PageHeader, secondaryButtonClass } from "./ui";

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
        description="What your firm sells. A change applies to challenges started after it. Accounts keep the rules they were bought with."
        actions={
          <Link href="/admin/challenges/edit" className={`${buttonClass} flex items-center gap-2`}>
            <PlusIcon />
            New challenge
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
      <ul className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {(challenges.data ?? []).map((challenge) => (
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
  const first = challenge.evaluation[0];
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
        {first.minTradingDays > 0 && ` · at least ${first.minTradingDays} trading days a phase`}
        {timeLimits.length > 0 ? ` · ${timeLimits.join(" and ")} days to pass` : " · no time limit"}
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
      <div className="mt-auto flex flex-wrap items-center justify-between gap-3">
        <span>
          {price ? (
            <>
              <span className="font-mono text-lg font-medium">{formatMoney(price.amount)}</span> <span className="text-muted">{price.currency} in your shop</span>
            </>
          ) : (
            <span className="text-muted">No price</span>
          )}
        </span>
        <div className="flex gap-2">
          <Link href={`/admin/challenges/edit?from=${encodeURIComponent(challenge.id)}`} className={`${secondaryButtonClass} text-sm`} aria-label={`Copy ${challenge.name}`}>
            Copy
          </Link>
          <Link href={`/admin/challenges/edit?id=${encodeURIComponent(challenge.id)}`} className={`${secondaryButtonClass} text-sm`} aria-label={`Change ${challenge.name}`}>
            Change
          </Link>
        </div>
      </div>
    </li>
  );
}
