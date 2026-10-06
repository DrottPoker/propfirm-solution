"use client";

import Link from "next/link";

import type { AccountDetails } from "@/lib/api/types";
import { attentionItems, endingText, hasEnded, retryOf, type AttentionItem } from "@/lib/dashboard";
import { formatDate, formatMoney } from "@/lib/format";
import { useMe, useMyAccounts, useSendEmailConfirmation, useShop } from "@/lib/queries";

import { AccountCard } from "./AccountCard";
import { AlertIcon, BagIcon, CheckIcon, InfoIcon } from "./icons";
import { LiveDot } from "./Live";
import { VerifyIdentityNotice } from "./TraderIdentity";
import { buttonClass, EmptyState, ErrorText, Loading, Message, PageHeader, SectionLabel, TraderPage } from "./ui";

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
    return <Loading />;
  }

  // The newest first, since a trader usually cares about the latest challenge.
  const all = accounts.data;
  const current = all.filter((a) => !hasEnded(a.account.status)).reverse();
  const ended = all.filter((a) => hasEnded(a.account.status)).reverse();
  const attention = attentionItems(current);
  const trading = current.filter((a) => a.account.status === "Active").length;
  const canBuy = shop.data?.open === true;

  return (
    <TraderPage gap="gap-8">
      <PageHeader
        title="Your accounts"
        description={
          current.length > 0 && (
            <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
              {trading === 0
                ? "No account is trading right now."
                : trading === 1
                  ? "One account is trading right now."
                  : `${trading} accounts are trading right now.`}
              {trading > 0 && <LiveDot label="Figures update live" />}
            </span>
          )
        }
      />

      <ConfirmEmailNotice />
      <VerifyIdentityNotice accounts={current} />

      {current.length === 0 && (
        <section className="rounded-2xl border border-border bg-panel shadow-card">
          <EmptyState
            titleAs="h2"
            icon={<BagIcon className="size-6" />}
            title="You have no active challenge"
            text={
              (ended.length > 0 ? "Your challenges have ended. Start a new one to trade again." : "Buy a challenge to start trading, and it shows up here.") +
              (canBuy ? "" : " Ask your firm for a new challenge.")
            }
            actions={
              canBuy && (
                <Link href="/buy" className={buttonClass}>
                  Buy a challenge
                </Link>
              )
            }
          />
        </section>
      )}

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
          {current.length === 1 ? (
            <AccountCard details={current[0]} featured />
          ) : (
            <ul className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
              {current.map((details) => (
                <li key={details.account.id} className="flex">
                  <AccountCard details={details} />
                </li>
              ))}
            </ul>
          )}
        </section>
      )}

      {ended.length > 0 && <EndedAccounts accounts={ended} />}
    </TraderPage>
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
    <div role={item.tone === "danger" ? "alert" : "status"} className={`flex flex-wrap items-center gap-x-3 gap-y-1 rounded-xl border px-4 py-3 ${style.box}`}>
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
  const Icon = tone === "danger" || tone === "warning" ? AlertIcon : tone === "profit" ? CheckIcon : InfoIcon;
  return (
    <span className={className}>
      <Icon className="size-[18px]" />
    </span>
  );
}

/** A trader who chose the password on an order's page has not confirmed the email yet, which payouts need. */
function ConfirmEmailNotice() {
  const me = useMe("trader");
  const send = useSendEmailConfirmation();
  if (!me.data || me.data.emailConfirmed) {
    return null;
  }

  return (
    <div role="status" className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-xl border border-warning/40 bg-warning/10 px-4 py-3 text-sm">
      <p className="min-w-0 flex-1">
        <span className="font-medium">Confirm your email.</span>{" "}
        <span className="text-muted">Open the link we sent to {me.data.email}. Payouts are paid only once it is confirmed.</span>
      </p>
      {send.isSuccess ? (
        <span className="text-profit">Sent. Check your inbox.</span>
      ) : (
        <button type="button" disabled={send.isPending} onClick={() => send.mutate()} className="font-medium text-warning hover:underline">
          {send.isPending ? "Sending..." : "Send the link again"}
        </button>
      )}
      <ErrorText error={send.error} />
    </div>
  );
}

function Outcome({ details }: { details: AccountDetails }) {
  return details.account.status === "Cancelled" ? (
    <span className="text-muted">Cancelled by the firm</span>
  ) : (
    <>
      <span className={details.breach ? "text-loss" : "text-muted"}>{details.breach ? "Failed" : "Ended"}</span>{" "}
      <span className="text-muted">· {endingText(details)}</span>
    </>
  );
}

/** The accounts that have ended: a table on wider screens, and a card each on a phone, so nothing is cut off. */
function EndedAccounts({ accounts }: { accounts: AccountDetails[] }) {
  return (
    <section aria-labelledby="ended-heading" className="flex flex-col gap-3">
      <SectionLabel id="ended-heading">Ended</SectionLabel>
      <ul className="flex flex-col gap-2 sm:hidden">
        {accounts.map((details) => (
          <li key={details.account.id}>
            <Link
              href={`/accounts/${details.account.id}`}
              className="flex flex-col gap-1 rounded-xl border border-border bg-panel p-4 text-sm transition-colors hover:border-muted"
            >
              <span className="flex items-baseline justify-between gap-3">
                <span className="font-medium">
                  {details.challenge.name} <span className="text-muted">#{details.account.number}</span>
                </span>
                <span className="tabular-nums">
                  {formatMoney(details.results.balance)} {details.account.currency}
                </span>
              </span>
              <span>
                <Outcome details={details} />
              </span>
              <span className="text-xs text-muted">
                {details.account.stageName}
                {details.endedAt && ` · ended ${formatDate(details.endedAt, details.challenge.tradingDay.timeZone)}`}
              </span>
            </Link>
            <RetryLink details={details} className="mt-2 block text-sm" />
          </li>
        ))}
      </ul>
      <div className="hidden overflow-x-auto rounded-xl border border-border bg-panel shadow-card sm:block">
        <table className="w-full text-sm">
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
                  <Outcome details={details} />
                  <RetryLink details={details} className="mt-1 block text-xs" />
                </td>
                <td className="px-5 py-3 text-muted">{details.endedAt ? formatDate(details.endedAt, details.challenge.tradingDay.timeZone) : "-"}</td>
                <td className="px-5 py-3 text-right tabular-nums">
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

// A failed challenge still for sale can be bought again, with the firm's code for retries when it has one.
function RetryLink({ details, className }: { details: AccountDetails; className: string }) {
  const retry = retryOf(details);
  return retry ? (
    <Link href={retry.href} className={`${className} text-accent hover:underline`}>
      {retry.label}
    </Link>
  ) : null;
}
