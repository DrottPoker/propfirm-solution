"use client";

import { useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState } from "react";

import { floorLabel, floorRisk, initials, type FloorRisk } from "@/lib/account";
import type { FloorSnapshot } from "@/lib/api/types";
import { productName } from "@/lib/config";
import { formatMoney, formatPercent } from "@/lib/format";
import { useLogout } from "@/lib/queries";
import { useTradingStore, type ConnectionState } from "@/lib/store";

import { ChevronDownIcon, InfoIcon, LogoMark, LogOutIcon } from "./icons";

const connectionStyles: Record<ConnectionState, { label: string; className: string }> = {
  connecting: { label: "Connecting", className: "border-warning/30 bg-warning/10 text-warning" },
  connected: { label: "Live", className: "border-profit/30 bg-profit/10 text-profit" },
  reconnecting: { label: "Reconnecting", className: "border-warning/30 bg-warning/10 text-warning" },
  disconnected: { label: "Offline", className: "border-loss/30 bg-loss/10 text-loss" },
};

const riskColors: Record<FloorRisk, string> = { ok: "text-muted", warning: "text-warning", danger: "text-loss" };

/**
 * Account figures and the distance to every equity floor, so the trader always sees the limits. A trader
 * with several accounts, such as one per challenge stage, switches between them here.
 */
export function AccountBar({ accounts, email, serverName }: { accounts: string[]; email: string; serverName: string }) {
  const account = useTradingStore((s) => s.account);
  const connection = connectionStyles[useTradingStore((s) => s.connection)];
  const router = useRouter();

  return (
    <header className="flex h-14 shrink-0 items-center gap-4 border-b border-border bg-panel px-4 text-sm">
      <span className="flex shrink-0 items-center gap-2.5">
        <LogoMark />
        <span className="text-base font-semibold tracking-tight">{productName}</span>
      </span>

      <span
        className={`flex shrink-0 items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium ${connection.className}`}
        title="Connection to the trading service"
      >
        <span className="size-1.5 rounded-full bg-current" />
        {connection.label}
      </span>

      <div className="flex h-full min-w-0 flex-1 items-stretch overflow-x-auto">
        {account && (
          <>
            <AccountPicker
              accounts={accounts}
              accountId={account.accountId}
              disabled={account.status === "Disabled"}
              onChange={(id) => router.push(`/?account=${encodeURIComponent(id)}`)}
            />
            <Figure label="Balance" value={formatMoney(account.balance)} unit={account.currency} />
            <Figure label="Equity" value={formatMoney(account.equity)} />
            <Figure label="Free margin" value={formatMoney(account.freeMargin)} />
            <Figure label="Margin level" value={formatPercent(account.marginLevelPercent)} />
            {account.floors.map((floor) => (
              <FloorFigure key={floor.floorId} floor={floor} />
            ))}
          </>
        )}
      </div>

      <UserMenu email={email} serverName={serverName} />
    </header>
  );
}

function AccountPicker({
  accounts,
  accountId,
  disabled,
  onChange,
}: {
  accounts: string[];
  accountId: string;
  disabled: boolean;
  onChange: (accountId: string) => void;
}) {
  const id = useId();

  return (
    <div className="flex shrink-0 flex-col justify-center border-l border-border px-4">
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
                  {a}
                </option>
              ))}
            </select>
            <ChevronDownIcon className="pointer-events-none absolute right-0 size-3.5 text-muted" />
          </span>
        </>
      ) : (
        <>
          <span className="text-[11px] text-muted">Account</span>
          <span className="font-medium">{accountId}</span>
        </>
      )}
      {disabled && <span className="mt-0.5 w-fit rounded bg-loss/15 px-1.5 text-[11px] font-medium text-loss">Disabled</span>}
    </div>
  );
}

function Figure({ label, value, unit }: { label: string; value: string; unit?: string }) {
  return (
    <div className="flex shrink-0 flex-col justify-center border-l border-border px-4">
      <span className="text-[11px] text-muted">{label}</span>
      <span className="font-mono text-[13px] whitespace-nowrap tabular-nums">
        {value}
        {unit && (
          <>
            {" "}
            <span className="rounded bg-raised px-1 py-px font-sans text-[10px] text-muted">{unit}</span>
          </>
        )}
      </span>
    </div>
  );
}

// The headroom comes from the engine: how far equity can fall before the floor is breached.
function FloorFigure({ floor }: { floor: FloorSnapshot }) {
  return (
    <div
      className="flex shrink-0 flex-col justify-center border-l border-border px-4"
      title="Equity must stay above this level. If it falls below, all positions are closed and the account is disabled."
    >
      <span className="flex items-center gap-1 text-[11px] text-muted">
        {floorLabel(floor.floorId)}
        <InfoIcon className="size-3" />
      </span>
      <span className="font-mono text-[13px] whitespace-nowrap tabular-nums">{formatMoney(floor.level)}</span>
      <span className={`font-mono text-[11px] whitespace-nowrap tabular-nums ${riskColors[floorRisk(floor)]}`}>
        {formatMoney(floor.headroom)} left
      </span>
    </div>
  );
}

function UserMenu({ email, serverName }: { email: string; serverName: string }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const panelId = useId();
  const logout = useLogout();
  const router = useRouter();

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
        className="flex size-9 items-center justify-center rounded-full bg-raised text-xs font-semibold ring-1 ring-border transition hover:ring-accent"
      >
        {initials(email)}
      </button>
      {open && (
        <div id={panelId} className="absolute top-11 right-0 z-20 w-64 rounded-lg border border-border bg-panel p-1 shadow-2xl shadow-black/50">
          <div className="border-b border-border px-3 py-2.5">
            <p className="truncate font-medium">{email}</p>
            <p className="text-xs text-muted">Server {serverName}</p>
          </div>
          <button
            type="button"
            disabled={logout.isPending}
            onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/login") })}
            className="mt-1 flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-muted hover:bg-raised hover:text-foreground disabled:opacity-50"
          >
            <LogOutIcon />
            Log out
          </button>
        </div>
      )}
    </div>
  );
}
