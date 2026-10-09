"use client";

import Image from "next/image";
import { useRouter } from "next/navigation";
import { useCallback, useId, useRef, useState } from "react";

import {
  accountName,
  backLink,
  floorLabel,
  floorLeftText,
  floorRisk,
  initials,
  nearestRoom,
  statusWord,
  suspendedHelp,
  targetText,
  todayResult,
  type FloorRisk,
} from "@/lib/account";
import type { AccountDetails, AccountSnapshot, FloorSnapshot, ServerInfo } from "@/lib/api/types";
import { formatMoney, formatPercent, formatSignedMoney, formatSignedPercent } from "@/lib/format";
import { closeness } from "@/lib/ownLimits";
import { useAccountSummaries, useLogout } from "@/lib/queries";
import { useProfile } from "@/lib/profileContext";
import { useSheet } from "@/lib/sheet";
import { useTradingStore } from "@/lib/store";

import { ConnectionStatusText, useConnectionStatus } from "./ConnectionStatus";
import { ArrowLeftIcon, CheckIcon, ChevronDownIcon, ExternalIcon, HelpIcon, InfoIcon, KeyboardIcon, LogOutIcon, SettingsIcon } from "./icons";
import { Popover } from "./Popover";
import { RulesMenu } from "./RulesMenu";
import { useTour } from "./Tour";
import { Tooltip } from "./Tooltip";
import { useDismiss } from "./useDismiss";

const riskColors: Record<FloorRisk, string> = { ok: "text-muted", warning: "text-warning", danger: "text-loss" };
const riskBars: Record<FloorRisk, string> = { ok: "bg-profit", warning: "bg-warning", danger: "bg-loss" };
const toneColors = { muted: "text-muted", accent: "text-accent", warning: "text-warning", loss: "text-loss" };
const itemClass = "flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground";

/**
 * The firm and the account, then the three figures a trader needs most (ADR 0058): equity, what the account has made
 * or lost today, and the room left to the nearest loss limit with a meter that turns amber and red as it shrinks. The
 * rest of the figures, every limit and the profit target are behind More, and the rulebook behind its own button. A
 * trader with several accounts, such as one per challenge stage, switches between them in the account menu. On a
 * phone the figures take a row of their own under the firm, scrolled sideways.
 */
export function AccountBar({
  accountId,
  accounts,
  details,
  email,
  name,
  server,
}: {
  accountId: string;
  accounts: string[];
  details: AccountDetails[];
  email: string;
  name: string | null;
  server: ServerInfo;
}) {
  const account = useTradingStore((s) => s.account);
  const connection = useConnectionStatus(accountId);
  const current = details.find((d) => d.accountId === account?.accountId);
  const back = backLink(current, server);

  return (
    <header className="flex shrink-0 flex-wrap items-center gap-x-4 border-b border-border bg-panel px-4 text-sm max-lg:pt-2 lg:h-14 lg:flex-nowrap">
      <span className="flex min-w-0 shrink-0 flex-col justify-center gap-0.5 max-lg:flex-1">
        <FirmMark server={server} />
        {back && (
          <a href={back} className="flex items-center gap-1 text-[11px] text-muted transition-colors duration-150 hover:text-foreground max-lg:hidden">
            <ArrowLeftIcon className="size-3" />
            Back to {server.name}
          </a>
        )}
        {/* A phone has no room for the account beside the figures, so it is under the firm there. */}
        {account && (
          <span className="lg:hidden">
            <AccountMenu accounts={accounts} details={details} account={account} compact />
          </span>
        )}
      </span>

      {/* The connection is in the status bar, which a phone does not show, so a phone says it here when it is not live. */}
      {connection.tone !== "live" && (
        <span className="shrink-0 text-xs lg:hidden">
          <ConnectionStatusText status={connection} />
        </span>
      )}

      {/* On a phone the three figures share a row of their own under the firm, always in view (ADR 0058). */}
      <div data-tour="account" className="flex h-full min-w-0 flex-1 items-stretch max-lg:order-last max-lg:-mx-4 max-lg:mt-2 max-lg:grid max-lg:h-12 max-lg:basis-full max-lg:grid-cols-3 max-lg:divide-x max-lg:divide-border max-lg:border-t max-lg:border-border lg:overflow-x-auto">
        {account && (
          <>
            <span className="flex max-lg:hidden">
              <AccountMenu accounts={accounts} details={details} account={account} />
            </span>
            <Figures account={account} />
          </>
        )}
      </div>

      <div className="flex items-center gap-2 max-lg:ml-auto">
        {account && <MoreFigures account={account} profitTarget={current?.profitTarget ?? null} />}
        {account && (
          <RulesMenu
            accountId={account.accountId}
            profitTarget={current?.profitTarget ?? null}
            firmName={server.name}
            detailsUrl={current?.detailsUrl ?? null}
          />
        )}
        <UserMenu email={email} name={name} serverName={server.name} back={back} />
      </div>
    </header>
  );
}

/** The firm's logo, or its name when it has none, as in its portal. Logos are on the firms' own addresses, so they are not optimized. */
function FirmMark({ server }: { server: ServerInfo }) {
  return server.logoUrl ? (
    <Image
      src={server.logoUrl}
      alt={server.name}
      width={112}
      height={28}
      unoptimized
      loading="eager"
      className="h-6 w-auto max-w-40 object-contain object-left"
    />
  ) : (
    <span className="text-base leading-tight font-semibold tracking-tight">{server.name}</span>
  );
}

/**
 * The account and its state in a word beside its name, and with several accounts a menu of them all with their state
 * and balance, the ended ones last.
 */
function AccountMenu({
  accounts,
  details,
  account,
  compact = false,
}: {
  accounts: string[];
  details: AccountDetails[];
  account: AccountSnapshot;
  /** Under the firm on a phone: the name alone, without the heading. */
  compact?: boolean;
}) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const close = useCallback(() => setOpen(false), [setOpen]);
  const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null);
  const others = accounts.filter((a) => a !== account.accountId);
  const summaries = useAccountSummaries(others, open);
  const nameOf = (id: string) =>
    accountName(
      id,
      details.find((d) => d.accountId === id),
    );
  const state = statusWord(account.status, account.status === "Active" && account.ownLimits.lock !== null);

  const rows = [
    { id: account.accountId, snapshot: account as AccountSnapshot | undefined },
    ...others.map((id, i) => ({ id, snapshot: summaries[i]?.data })),
  ].sort((a, b) => Number(a.snapshot?.status === "Disabled") - Number(b.snapshot?.status === "Disabled"));

  const label = (
    <span className="flex min-w-0 flex-col items-start">
      {!compact && <span className="text-[11px] text-muted">Account</span>}
      <span className="flex max-w-64 items-baseline gap-2">
        <span className="truncate font-medium">{nameOf(account.accountId)}</span>
        {state.word !== "Active" && (
          <span className={`shrink-0 text-[11px] font-medium ${toneColors[state.tone]}`} title={account.status === "Suspended" ? suspendedHelp : undefined}>
            {state.word}
          </span>
        )}
      </span>
    </span>
  );

  if (accounts.length <= 1) {
    return <div className={`flex shrink-0 items-center ${compact ? "text-xs" : "border-l border-border px-3 lg:px-4"}`}>{label}</div>;
  }

  return (
    <div className={`flex shrink-0 items-stretch ${compact ? "text-xs" : "border-l border-border"}`}>
      <button
        ref={setAnchor}
        type="button"
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className={`flex items-center gap-2 text-left transition-colors duration-150 ${compact ? "rounded" : "px-3 hover:bg-raised/60 lg:px-4"}`}
      >
        {label}
        <ChevronDownIcon className={`size-3.5 shrink-0 text-muted transition-transform duration-150 ${open ? "rotate-180" : ""}`} />
      </button>
      {open && (
        <Popover anchor={anchor} label="Your accounts" onClose={close} className="w-80 gap-1 p-1.5">
          <p className="px-2 pt-1 pb-1.5 text-xs text-muted">Your accounts</p>
          {rows.map(({ id, snapshot }) => {
            const s = snapshot ? statusWord(snapshot.status, snapshot.status === "Active" && snapshot.ownLimits.lock !== null) : null;
            const chosen = id === account.accountId;
            return (
              <button
                key={id}
                type="button"
                onClick={() => {
                  close();
                  if (!chosen) {
                    router.push(`/?account=${encodeURIComponent(id)}`);
                  }
                }}
                aria-current={chosen ? "true" : undefined}
                className={`flex items-center gap-3 rounded-lg px-2 py-2 text-left transition-colors duration-150 hover:bg-raised ${chosen ? "bg-raised/60" : ""}`}
              >
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate font-medium">{nameOf(id)}</span>
                  <span className={`text-[11px] ${s ? toneColors[s.tone] : "text-muted"}`}>{s ? s.word : "Loading..."}</span>
                </span>
                {snapshot && (
                  <span className="shrink-0 text-right text-xs tabular-nums">
                    {formatMoney(snapshot.balance)} <span className="text-muted">{snapshot.currency}</span>
                  </span>
                )}
                <CheckIcon className={`size-4 shrink-0 text-accent ${chosen ? "" : "invisible"}`} />
              </button>
            );
          })}
        </Popover>
      )}
    </div>
  );
}

/** Equity, today's result and the room to the nearest limit, each explained on hover, focus or a tap. */
function Figures({ account }: { account: AccountSnapshot }) {
  const today = todayResult(account);
  const room = account.status === "Disabled" ? null : nearestRoom(account);
  return (
    <>
      <Figure label="Equity" help="The balance with the open positions' results now. The loss limits are measured on it.">
        {formatMoney(account.equity)} <span className="text-[11px] text-muted">{account.currency}</span>
      </Figure>
      <Figure label="Today" help="What the account has made or lost since the trading day started, with the open positions' results now.">
        <span className={today.amount > 0 ? "text-profit" : today.amount < 0 ? "text-loss" : ""}>{formatSignedMoney(today.amount)}</span>
        {today.percent !== null && Math.abs(today.percent) >= 0.005 && (
          <span className="ml-1.5 text-[11px] text-muted">{formatSignedPercent(today.percent)}</span>
        )}
      </Figure>
      {room && (
        <Figure
          label={room.name}
          help={`How far equity can fall before the ${room.limit.toLowerCase()} is reached.${room.own ? " This is your own limit: it locks the rest of the day, and the account goes on." : " If it is broken, every position closes and trading on the account ends."}`}
        >
          <span className="flex items-center gap-2.5">
            <span className={room.risk === "ok" ? "" : riskColors[room.risk]}>{formatMoney(room.left)}</span>
            {room.share !== null && (
              <span aria-hidden="true" className="h-1.5 w-16 overflow-hidden rounded-full bg-border">
                <span
                  className={`block h-full rounded-full transition-[width,background-color] duration-300 ${riskBars[room.risk]}`}
                  style={{ width: `${Math.max(3, room.share * 100)}%` }}
                />
              </span>
            )}
          </span>
        </Figure>
      )}
    </>
  );
}

function Figure({ label, help, children }: { label: string; help: string; children: React.ReactNode }) {
  return (
    <div className="flex min-w-0 shrink-0 flex-col justify-center px-3 lg:border-l lg:border-border lg:px-4">
      <Tooltip content={help} focusable className="w-fit rounded">
        <span className="flex items-center gap-1 text-[11px] text-muted">
          {label}
          <InfoIcon className="size-3 opacity-70" />
        </span>
      </Tooltip>
      <span className="live truncate text-[15px] font-medium whitespace-nowrap tabular-nums max-lg:text-sm">{children}</span>
    </div>
  );
}

/**
 * Every figure of the account behind a button: the balance and the margin, each loss limit with the room left to it,
 * the trader's own daily limit and the profit target, each with what it means, so it reads on a touch screen too.
 */
function MoreFigures({ account, profitTarget }: { account: AccountSnapshot; profitTarget: number | null }) {
  const [open, setOpen] = useState(false);
  const close = useCallback(() => setOpen(false), [setOpen]);
  const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null);
  const ended = account.status === "Disabled";
  const own = account.ownLimits;

  return (
    <>
      <button
        ref={setAnchor}
        type="button"
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
        className={`flex h-9 shrink-0 items-center gap-1 rounded-lg px-2.5 text-xs font-medium transition-colors duration-150 hover:bg-raised hover:text-foreground ${open ? "bg-raised text-foreground" : "text-muted"}`}
      >
        More
        <ChevronDownIcon className={`size-3.5 transition-transform duration-150 ${open ? "rotate-180" : ""}`} />
      </button>
      {open && (
        <Popover anchor={anchor} label="The account's figures" onClose={close} className="max-h-[calc(100vh-5rem)] w-[22rem] gap-0 overflow-y-auto p-0">
          <Section title="Account">
            <Row
              label="Balance"
              help="Money in the account, from closed positions and money in or out."
              value={`${formatMoney(account.balance)} ${account.currency}`}
            />
            <Row label="Equity" help="The balance with the open positions' results now." value={formatMoney(account.equity)} />
            <Row label="Used margin" help="What the open positions hold from the equity." value={formatMoney(account.usedMargin)} />
            <Row label="Free margin" help="What is left for new positions." value={formatMoney(account.freeMargin)} />
            <Row
              label="Margin level"
              help="Equity against the used margin. Without open positions there is none."
              value={formatPercent(account.marginLevelPercent)}
            />
          </Section>
          {(account.floors.length > 0 || profitTarget !== null || own.lossLevel !== null) && (
            <Section title="Limits and target">
              {profitTarget !== null && !ended && (
                <Row
                  label="Profit target"
                  help="Reach this balance, with every position closed, to pass the stage."
                  value={formatMoney(profitTarget)}
                  note={targetText(profitTarget, account)}
                />
              )}
              {account.floors.map((floor) => (
                <FloorRow key={floor.floorId} floor={floor} ended={ended} />
              ))}
              {!ended && own.limits.dailyLoss !== null && own.lossLevel !== null && (
                <Row
                  label="Your daily limit"
                  help="Your own daily loss limit. If equity reaches it, every position closes and new orders lock until the next trading day. The account goes on."
                  value={formatMoney(own.lossLevel)}
                  note={account.equity > own.lossLevel ? `${formatMoney(account.equity - own.lossLevel)} left` : "Reached"}
                  noteColor={riskColors[closeness(Math.max(0, account.equity - own.lossLevel), own.limits.dailyLoss)]}
                />
              )}
            </Section>
          )}
        </Popover>
      )}
    </>
  );
}

// The headroom comes from the engine: how far equity can fall before the limit is broken. Once trading has ended,
// only a broken limit still says so.
function FloorRow({ floor, ended }: { floor: FloorSnapshot; ended: boolean }) {
  const broken = floor.headroom <= 0;
  return (
    <Row
      label={floorLabel(floor.floorId)}
      help="Equity must stay above this level. If it falls below, every position is closed and trading on the account ends."
      value={formatMoney(floor.level)}
      note={!ended || broken ? floorLeftText(floor) : undefined}
      noteColor={riskColors[floorRisk(floor)]}
    />
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section aria-label={title} className="flex flex-col border-b border-border px-3.5 py-2.5 last:border-b-0">
      <h3 className="pb-1 text-xs font-semibold">{title}</h3>
      <dl className="flex flex-col">{children}</dl>
    </section>
  );
}

function Row({ label, help, value, note, noteColor = "text-muted" }: { label: string; help: string; value: string; note?: string; noteColor?: string }) {
  return (
    <div className="flex items-start justify-between gap-4 py-1 text-xs">
      <dt>
        <Tooltip content={help} focusable className="rounded">
          <span className="flex items-center gap-1 text-muted">
            {label}
            <InfoIcon className="size-3 opacity-70" />
          </span>
        </Tooltip>
      </dt>
      <dd className="live flex flex-col items-end text-right">
        <span className="text-[13px] tabular-nums">{value}</span>
        {note && <span className={`text-[11px] tabular-nums ${noteColor}`}>{note}</span>}
      </dd>
    </div>
  );
}

function UserMenu({ email, name, serverName, back }: { email: string; name: string | null; serverName: string; back: string | null }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const panelId = useId();
  const logout = useLogout();
  const router = useRouter();
  const close = useCallback(() => setOpen(false), []);
  useDismiss(open, ref, close);
  const { links } = useProfile();
  const pages = [
    { href: links.help, label: `Help at ${serverName}` },
    { href: links.support, label: "Support" },
    { href: links.terms, label: "Terms" },
    { href: links.privacy, label: "Privacy" },
  ].filter((p): p is { href: string; label: string } => p.href !== null);
  const openDialog = (kind: "shortcuts" | "about") => {
    setOpen(false);
    useSheet.getState().open({ kind });
  };

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
        {initials(email, name)}
      </button>
      {open && (
        <div id={panelId} className="absolute top-11 right-0 z-20 w-64 origin-top-right animate-pop rounded-xl border border-border bg-panel p-1 shadow-float">
          <div className="border-b border-border px-3 py-2.5">
            {name && <p className="truncate font-medium">{name}</p>}
            <p className={`truncate ${name ? "text-xs text-muted" : "font-medium"}`}>{email}</p>
            <p className="text-xs text-muted">Server {serverName}</p>
          </div>
          {back && (
            <a href={back} className={`mt-1 ${itemClass}`}>
              <ArrowLeftIcon />
              Back to {serverName}
            </a>
          )}
          <button
            type="button"
            onClick={() => {
              setOpen(false);
              useSheet.getState().open({ kind: "settings" });
            }}
            className={`mt-1 ${itemClass}`}
          >
            <SettingsIcon />
            Settings
          </button>
          <button type="button" onClick={() => openDialog("shortcuts")} className={itemClass}>
            <KeyboardIcon />
            Keyboard shortcuts
          </button>
          <button
            type="button"
            onClick={() => {
              setOpen(false);
              useTour.getState().start();
            }}
            className={itemClass}
          >
            <HelpIcon />
            Take the tour
          </button>
          <button type="button" onClick={() => openDialog("about")} className={itemClass}>
            <InfoIcon />
            About
          </button>
          {/* The firm's own pages, from its terminal profile (ADR 0058). */}
          {pages.length > 0 && (
            <div className="mt-1 border-t border-border pt-1">
              {pages.map((page) => (
                <a key={page.label} href={page.href} target="_blank" rel="noreferrer" className={itemClass}>
                  <ExternalIcon />
                  {page.label}
                </a>
              ))}
            </div>
          )}
          <button
            type="button"
            disabled={logout.isPending}
            onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/login") })}
            className={`mt-1 border-t border-border ${itemClass} disabled:opacity-50`}
          >
            <LogOutIcon />
            Log out
          </button>
        </div>
      )}
    </div>
  );
}
