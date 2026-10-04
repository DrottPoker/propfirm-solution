"use client";

import Link from "next/link";

import type { AccountDetails } from "@/lib/api/types";
import { attentionItems, endingText, hasEnded, type AttentionItem } from "@/lib/dashboard";
import { formatDate, formatMoney } from "@/lib/format";
import { useMyAccounts, useShop } from "@/lib/queries";

import { AccountCard } from "./AccountCard";
import { buttonClass, Message, SectionLabel } from "./ui";

/**
 * The trader's start page: what needs attention, a card for every account that is trading or on its way, and the
 * accounts that have ended.
 */
export function AccountsOverview() {
  const accounts = useMyAccounts();
  const shop = useShop();

  if (accounts.isError) {
    return <Message text="Your accounts cannot be loaded right now. Try again shortly." />;
  }

  if (!accounts.data) {
    return <Message text="Loading..." />;
  }

  const all = accounts.data;
  if (all.length === 0) {
    return (
      <main className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center text-muted">
        <p>You have no challenge yet.</p>
        {shop.data?.open ? (
          <Link href="/buy" className={buttonClass}>
            Buy a challenge
          </Link>
        ) : (
          <p>Your firm opens one for you when you buy it.</p>
        )}
      </main>
    );
  }

  // The newest first, since a trader usually cares about the latest challenge.
  const current = all.filter((a) => !hasEnded(a.account.status)).reverse();
  const ended = all.filter((a) => hasEnded(a.account.status)).reverse();
  const attention = attentionItems(current);
  const trading = current.filter((a) => a.account.status === "Active").length;

  return (
    <main className="mx-auto flex w-full max-w-6xl flex-col gap-8 px-4 py-8 sm:px-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">Your accounts</h1>
        <p className="text-muted">
          {trading === 0 ? "No account is trading right now." : trading === 1 ? "One account is trading right now." : `${trading} accounts are trading right now.`}{" "}
          The figures update every few seconds.
        </p>
      </div>

      {attention.length > 0 && (
        <section aria-label="Needs your attention" className="flex flex-col gap-2">
          {attention.map((item) => (
            <Attention key={item.key} item={item} />
          ))}
        </section>
      )}

      {current.length > 0 && (
        <section aria-labelledby="trading-heading" className="flex flex-col gap-3">
          <SectionLabel id="trading-heading">Trading</SectionLabel>
          <ul className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
            {current.map((details) => (
              <li key={details.account.id} className="flex">
                <AccountCard details={details} />
              </li>
            ))}
          </ul>
        </section>
      )}

      {ended.length > 0 && <EndedAccounts accounts={ended} />}
    </main>
  );
}

const attentionStyles: Record<AttentionItem["tone"], { box: string; icon: string; action: string }> = {
  danger: { box: "border-loss/50 bg-loss/10", icon: "text-loss", action: "text-loss" },
  warning: { box: "border-warning/40 bg-warning/10", icon: "text-warning", action: "text-warning" },
  profit: { box: "border-border bg-panel", icon: "text-profit", action: "text-accent" },
  info: { box: "border-border bg-panel", icon: "text-muted", action: "text-accent" },
};

function Attention({ item }: { item: AttentionItem }) {
  const style = attentionStyles[item.tone];
  return (
    <div role={item.tone === "danger" ? "alert" : "status"} className={`flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg border px-4 py-3 ${style.box}`}>
      <AttentionIcon tone={item.tone} className={style.icon} />
      <p className="min-w-0 flex-1">
        <span className="font-medium">{item.title}</span> <span className="text-muted">{item.detail}</span>
      </p>
      <Link href={`/accounts/${item.accountId}`} className={`font-medium hover:underline ${style.action}`}>
        {item.action}
      </Link>
    </div>
  );
}

function AttentionIcon({ tone, className }: { tone: AttentionItem["tone"]; className: string }) {
  return (
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" className={`shrink-0 ${className}`}>
      {tone === "danger" || tone === "warning" ? (
        <>
          <path d="M12 9v4" />
          <path d="M12 17h.01" />
          <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z" />
        </>
      ) : tone === "profit" ? (
        <>
          <circle cx="12" cy="12" r="9" />
          <path d="m8 12 3 3 5-6" />
        </>
      ) : (
        <>
          <circle cx="12" cy="12" r="9" />
          <path d="M12 8h.01" />
          <path d="M11 12h1v4h1" />
        </>
      )}
    </svg>
  );
}

function EndedAccounts({ accounts }: { accounts: AccountDetails[] }) {
  return (
    <section aria-labelledby="ended-heading" className="flex flex-col gap-3">
      <SectionLabel id="ended-heading">Ended</SectionLabel>
      <div className="overflow-x-auto rounded-lg border border-border bg-panel">
        <table className="w-full min-w-[40rem] text-sm">
          <thead className="text-left text-muted">
            <tr>
              <th scope="col" className="px-5 py-3 font-normal">
                Account
              </th>
              <th scope="col" className="px-5 py-3 font-normal">
                Challenge
              </th>
              <th scope="col" className="px-5 py-3 font-normal">
                Outcome
              </th>
              <th scope="col" className="px-5 py-3 font-normal">
                Ended
              </th>
              <th scope="col" className="px-5 py-3 text-right font-normal">
                Final balance
              </th>
            </tr>
          </thead>
          <tbody>
            {accounts.map((details) => (
              <tr key={details.account.id} className="border-t border-border">
                <td className="px-5 py-3">
                  <Link href={`/accounts/${details.account.id}`} className="text-accent hover:underline">
                    #{details.account.number}
                  </Link>
                </td>
                <td className="px-5 py-3">
                  {details.challenge.name} · {details.account.stageName}
                </td>
                <td className="px-5 py-3">
                  {details.account.status === "Cancelled" ? (
                    <span className="text-muted">Cancelled by the firm</span>
                  ) : (
                    <>
                      <span className={details.breach ? "text-loss" : "text-muted"}>{details.breach ? "Failed" : "Ended"}</span>{" "}
                      <span className="text-muted">· {endingText(details)}</span>
                    </>
                  )}
                </td>
                <td className="px-5 py-3 text-muted">{details.endedAt ? formatDate(details.endedAt) : "-"}</td>
                <td className="px-5 py-3 text-right font-mono tabular-nums">
                  {formatMoney(details.results.balance)} {details.account.currency}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
