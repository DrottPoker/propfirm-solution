"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState } from "react";

import { accountName, backLink, floorLabel, floorLeftText, floorRisk, initials, suspendedHelp, targetText, type FloorRisk } from "@/lib/account";
import type { AccountDetails, AccountStatus, FloorSnapshot, ServerInfo } from "@/lib/api/types";
import { formatMoney, formatPercent } from "@/lib/format";
import { useLogout } from "@/lib/queries";
import { useSettings } from "@/lib/settings";
import { playFillSound, unlockSound } from "@/lib/sound";
import { useTradingStore, type ConnectionState } from "@/lib/store";

import { ArrowLeftIcon, ChevronDownIcon, InfoIcon, LogOutIcon, SoundIcon } from "./icons";

// "Live prices", not "Live": the accounts are practice accounts, and "Live" could be read as a real money account.
// The dot pulses while prices stream in.
const connectionStyles: Record<ConnectionState, { label: string; className: string; dot: string }> = {
  connecting: { label: "Connecting", className: "border-warning/30 bg-warning/10 text-warning", dot: "" },
  connected: { label: "Live prices", className: "border-profit/30 bg-profit/10 text-profit", dot: "animate-pulse-dot" },
  reconnecting: { label: "Reconnecting", className: "border-warning/30 bg-warning/10 text-warning", dot: "" },
  disconnected: { label: "Offline", className: "border-loss/30 bg-loss/10 text-loss", dot: "" },
};

const riskColors: Record<FloorRisk, string> = { ok: "text-muted", warning: "text-warning", danger: "text-loss" };

/**
 * The firm, the account as the firm's portal names it, its figures and the distance to every loss limit and to the
 * profit target, so the trader always sees them. A trader with several accounts, such as one per challenge stage,
 * switches between them here. On a phone the figures take a row of their own under the firm, scrolled sideways.
 */
export function AccountBar({
  accounts,
  details,
  email,
  server,
}: {
  accounts: string[];
  details: AccountDetails[];
  email: string;
  server: ServerInfo;
}) {
  const account = useTradingStore((s) => s.account);
  const connection = connectionStyles[useTradingStore((s) => s.connection)];
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

      <span
        className={`flex shrink-0 items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium transition-colors duration-300 ${connection.className}`}
        title="Prices and figures come live from the trading service"
      >
        <span className={`size-1.5 rounded-full bg-current ${connection.dot}`} />
        <span className="max-sm:sr-only">{connection.label}</span>
      </span>

      <div className="flex h-full min-w-0 flex-1 items-stretch overflow-x-auto max-lg:order-last max-lg:-mx-4 max-lg:mt-2 max-lg:h-12 max-lg:basis-full max-lg:border-t max-lg:border-border max-lg:*:first:border-l-0">
        {account && (
          <>
            <AccountPicker
              accounts={accounts}
              details={details}
              accountId={account.accountId}
              status={account.status}
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
            {account.floors.map((floor) => (
              <FloorFigure key={floor.floorId} floor={floor} ended={account.status === "Disabled"} />
            ))}
          </>
        )}
      </div>

      <div className="max-lg:ml-auto">
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
  onChange,
}: {
  accounts: string[];
  details: AccountDetails[];
  accountId: string;
  status: AccountStatus;
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
      {status === "Disabled" && <span className="mt-0.5 w-fit rounded bg-loss/15 px-1.5 text-[11px] font-medium text-loss">Ended</span>}
      {status === "Suspended" && (
        <span className="mt-0.5 w-fit rounded bg-warning/15 px-1.5 text-[11px] font-medium text-warning" title={suspendedHelp}>
          Paused
        </span>
      )}
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
            <span className="rounded bg-raised px-1 py-px font-sans text-[10px] text-muted">{unit}</span>
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
  const sound = useSettings((s) => s.fillSound);
  const changeSetting = useSettings((s) => s.change);

  // Turning the sound on plays it once, so the trader hears what it is, and lets the browser play it later.
  const toggleSound = () => {
    changeSetting("fillSound", !sound);
    if (!sound) {
      unlockSound();
      playFillSound();
    }
  };

  useEffect(() => {
    if (!open) {
      return;
    }

    const onPointerDown = (e: PointerEvent) => {
      if (!ref.current?.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setOpen(false);
      }
    };
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

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
            role="switch"
            aria-checked={sound}
            onClick={toggleSound}
            className="mt-1 flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground"
          >
            <SoundIcon />
            <span className="flex-1">Sound on fills</span>
            <span aria-hidden="true" className={`relative h-4 w-7 rounded-full transition-colors duration-150 ${sound ? "bg-accent" : "bg-border"}`}>
              <span
                className={`absolute top-0.5 left-0.5 size-3 rounded-full bg-foreground shadow transition-transform duration-150 ease-out-soft ${sound ? "translate-x-3" : ""}`}
              />
            </span>
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
