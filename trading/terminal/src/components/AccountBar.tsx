"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useCallback, useId, useRef, useState } from "react";

import { accountName, backLink, floorLabel, floorLeftText, floorRisk, initials, suspendedHelp, targetText, type FloorRisk } from "@/lib/account";
import type { AccountDetails, AccountStatus, FloorSnapshot, OwnLimitsSnapshot, ServerInfo } from "@/lib/api/types";
import { formatClock, formatMoney, formatPercent } from "@/lib/format";
import { closeness } from "@/lib/ownLimits";
import { useLogout } from "@/lib/queries";
import { useSheet } from "@/lib/sheet";
import { useTradingStore } from "@/lib/store";

import { ConnectionStatusText, useConnectionStatus } from "./ConnectionStatus";
import { ArrowLeftIcon, ChevronDownIcon, InfoIcon, LogOutIcon, SettingsIcon } from "./icons";
import { RulesMenu } from "./RulesMenu";
import { useDismiss } from "./useDismiss";

const riskColors: Record<FloorRisk, string> = { ok: "text-muted", warning: "text-warning", danger: "text-loss" };

/**
 * The firm, the account as the firm's portal names it, its figures and the distance to every loss limit and to the
 * profit target, so the trader always sees them, and the whole rulebook behind a button. A trader with several
 * accounts, such as one per challenge stage, switches between them here. On a phone the figures take a row of their
 * own under the firm, scrolled sideways.
 */
export function AccountBar({
  accountId,
  accounts,
  details,
  email,
  server,
}: {
  accountId: string;
  accounts: string[];
  details: AccountDetails[];
  email: string;
  server: ServerInfo;
}) {
  const account = useTradingStore((s) => s.account);
  const connection = useConnectionStatus(accountId);
  const router = useRouter();
  const current = details.find((d) => d.accountId === account?.accountId);
  const back = backLink(current, server);

  return (
    <header className="flex shrink-0 flex-wrap items-center gap-x-4 border-b border-border bg-panel px-4 text-sm max-lg:pt-2 lg:h-14 lg:flex-nowrap">
      <span className="flex shrink-0 flex-col justify-center gap-0.5">
        <FirmMark server={server} />
        {back && (
          <a href={back} className="flex items-center gap-1 text-[11px] text-muted transition-colors duration-150 hover:text-foreground">
            <ArrowLeftIcon className="size-3" />
            Back to {server.name}
          </a>
        )}
      </span>

      {/* The connection is in the status bar, which a phone does not show, so a phone says it here when it is not live. */}
      {connection.tone !== "live" && (
        <span className="shrink-0 text-xs lg:hidden">
          <ConnectionStatusText status={connection} />
        </span>
      )}

      <div className="flex h-full min-w-0 flex-1 items-stretch overflow-x-auto max-lg:order-last max-lg:-mx-4 max-lg:mt-2 max-lg:h-12 max-lg:basis-full max-lg:border-t max-lg:border-border max-lg:*:first:border-l-0">
        {account && (
          <>
            <AccountPicker
              accounts={accounts}
              details={details}
              accountId={account.accountId}
              status={account.status}
              own={account.ownLimits}
              onChange={(id) => router.push(`/?account=${encodeURIComponent(id)}`)}
            />
            <Figure label="Balance" value={formatMoney(account.balance)} unit={account.currency} />
            <Figure label="Equity" value={formatMoney(account.equity)} />
            <Figure label="Free margin" value={formatMoney(account.freeMargin)} />
            <Figure label="Margin level" value={formatPercent(account.marginLevelPercent)} />
            {current?.profitTarget != null && account.status !== "Disabled" && (
              <Figure
                label="Profit target"
                value={formatMoney(current.profitTarget)}
                note={targetText(current.profitTarget, account)}
                help="Reach this balance, with every position closed, to pass the stage."
              />
            )}
            {account.status !== "Disabled" && <OwnLossFigure own={account.ownLimits} equity={account.equity} />}
            {account.floors.map((floor) => (
              <FloorFigure key={floor.floorId} floor={floor} ended={account.status === "Disabled"} />
            ))}
          </>
        )}
      </div>

      <div className="flex items-center gap-2 max-lg:ml-auto">
        {account && (
          <RulesMenu
            accountId={account.accountId}
            profitTarget={current?.profitTarget ?? null}
            firmName={server.name}
            detailsUrl={current?.detailsUrl ?? null}
          />
        )}
        <UserMenu email={email} serverName={server.name} />
      </div>
    </header>
  );
}

/** The firm's logo, or its name when it has none, as in its portal. Logos are on the firms' own addresses, so they are not optimized. */
function FirmMark({ server }: { server: ServerInfo }) {
  return server.logoUrl ? (
    <Image src={server.logoUrl} alt={server.name} width={112} height={28} unoptimized loading="eager" className="h-6 w-auto max-w-40 object-contain object-left" />
  ) : (
    <span className="text-base leading-tight font-semibold tracking-tight">{server.name}</span>
  );
}

function AccountPicker({
  accounts,
  details,
  accountId,
  status,
  own,
  onChange,
}: {
  accounts: string[];
  details: AccountDetails[];
  accountId: string;
  status: AccountStatus;
  own: OwnLimitsSnapshot;
  onChange: (accountId: string) => void;
}) {
  const id = useId();
  const nameOf = (a: string) => accountName(a, details.find((d) => d.accountId === a));

  return (
    <div className="flex shrink-0 flex-col justify-center border-l border-border px-3 lg:px-4" title={`Trading account ${accountId}`}>
      {accounts.length > 1 ? (
        <>
          <label htmlFor={id} className="text-[11px] text-muted">
            Account
          </label>
          <span className="relative flex items-center">
            <select
              id={id}
              value={accountId}
              onChange={(e) => onChange(e.target.value)}
              className="cursor-pointer appearance-none bg-transparent pr-5 font-medium outline-none focus-visible:text-accent"
            >
              {accounts.map((a) => (
                <option key={a} value={a}>
                  {nameOf(a)}
                </option>
              ))}
            </select>
            <ChevronDownIcon className="pointer-events-none absolute right-0 size-3.5 text-muted" />
          </span>
        </>
      ) : (
        <>
          <span className="text-[11px] text-muted">Account</span>
          <span className="font-medium">{nameOf(accountId)}</span>
        </>
      )}
      {status === "Disabled" && <span className="text-[11px] font-medium text-loss">Ended</span>}
      {status === "Suspended" && (
        <span className="text-[11px] font-medium text-warning" title={suspendedHelp}>
          Paused
        </span>
      )}
      {status !== "Disabled" && own.lock && (
        <span
          className="text-[11px] font-medium whitespace-nowrap text-accent"
          title="New orders are locked until the next trading day. You can still close positions and change their stops."
        >
          Locked until {formatClock(own.lock.until, own.tradingDay.timeZone)}
        </span>
      )}
    </div>
  );
}

// The trader's own daily loss limit (ADR 0054), beside the firm's: where equity locks the day, and the room left.
function OwnLossFigure({ own, equity }: { own: OwnLimitsSnapshot; equity: number }) {
  if (own.limits.dailyLoss === null || own.lossLevel === null) {
    return null;
  }

  const left = Math.max(0, equity - own.lossLevel);
  return (
    <div
      className="flex shrink-0 flex-col justify-center border-l border-border px-3 lg:px-4"
      title="Your own daily loss limit. If equity reaches it, every position closes and new orders lock until the next trading day. The challenge goes on."
    >
      <span className="flex items-center gap-1 text-[11px] text-muted">
        Your daily limit
        <InfoIcon className="size-3" />
      </span>
      <span className="font-mono text-[13px] whitespace-nowrap tabular-nums">{formatMoney(own.lossLevel)}</span>
      <span className={`font-mono text-[11px] whitespace-nowrap tabular-nums ${riskColors[closeness(left, own.limits.dailyLoss)]}`}>
        {left > 0 ? `${formatMoney(left)} left` : "Reached"}
      </span>
    </div>
  );
}

function Figure({ label, value, unit, note, help }: { label: string; value: string; unit?: string; note?: string; help?: string }) {
  return (
    <div className="flex shrink-0 flex-col justify-center border-l border-border px-3 lg:px-4" title={help}>
      <span className="flex items-center gap-1 text-[11px] text-muted">
        {label}
        {help && <InfoIcon className="size-3" />}
      </span>
      <span className="font-mono text-[13px] whitespace-nowrap tabular-nums">
        {value}
        {unit && (
          <>
            {" "}
            <span className="font-sans text-[11px] text-muted">{unit}</span>
          </>
        )}
      </span>
      {note && <span className="font-mono text-[11px] whitespace-nowrap text-muted tabular-nums">{note}</span>}
    </div>
  );
}

// The headroom comes from the engine: how far equity can fall before the limit is broken. Once trading has ended,
// only a broken limit still says so.
function FloorFigure({ floor, ended }: { floor: FloorSnapshot; ended: boolean }) {
  const broken = floor.headroom <= 0;
  return (
    <div
      className="flex shrink-0 flex-col justify-center border-l border-border px-3 lg:px-4"
      title="Equity must stay above this level. If it falls below, every position is closed and trading on the account ends."
    >
      <span className="flex items-center gap-1 text-[11px] text-muted">
        {floorLabel(floor.floorId)}
        <InfoIcon className="size-3" />
      </span>
      <span className="font-mono text-[13px] whitespace-nowrap tabular-nums">{formatMoney(floor.level)}</span>
      {(!ended || broken) && (
        <span className={`font-mono text-[11px] whitespace-nowrap tabular-nums ${riskColors[floorRisk(floor)]}`}>{floorLeftText(floor)}</span>
      )}
    </div>
  );
}

function UserMenu({ email, serverName }: { email: string; serverName: string }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const panelId = useId();
  const logout = useLogout();
  const router = useRouter();
  const close = useCallback(() => setOpen(false), []);
  useDismiss(open, ref, close);

  return (
    <div ref={ref} className="relative shrink-0">
      <button
        type="button"
        aria-label="User menu"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((o) => !o)}
        className={`flex size-9 items-center justify-center rounded-full bg-raised text-xs font-semibold ring-1 transition duration-150 hover:ring-accent active:translate-y-px ${open ? "ring-accent" : "ring-border"}`}
      >
        {initials(email)}
      </button>
      {open && (
        <div
          id={panelId}
          className="absolute top-11 right-0 z-20 w-64 origin-top-right animate-pop rounded-xl border border-border bg-panel p-1 shadow-float"
        >
          <div className="border-b border-border px-3 py-2.5">
            <p className="truncate font-medium">{email}</p>
            <p className="text-xs text-muted">Server {serverName}</p>
          </div>
          <button
            type="button"
            onClick={() => {
              setOpen(false);
              useSheet.getState().open({ kind: "settings" });
            }}
            className="mt-1 flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground"
          >
            <SettingsIcon />
            Settings
          </button>
          <button
            type="button"
            disabled={logout.isPending}
            onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/login") })}
            className="flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground disabled:opacity-50"
          >
            <LogOutIcon />
            Log out
          </button>
        </div>
      )}
    </div>
  );
}
