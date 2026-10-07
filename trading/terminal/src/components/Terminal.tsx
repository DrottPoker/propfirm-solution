"use client";

import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";

import { sessionExpired } from "@/lib/api/client";
import type { AccountDetails, ServerInfo } from "@/lib/api/types";
import { usableTimeZone } from "@/lib/format";
import { digitsLookup, initialInstrument } from "@/lib/instruments";
import { useInstruments, useMe } from "@/lib/queries";
import { useTradingConnection } from "@/lib/realtime";
import { rememberAccount, rememberServer } from "@/lib/servers";
import { useSheet } from "@/lib/sheet";
import { useLoadedSettings } from "@/lib/syncedSettings";
import { TimeZoneContext } from "@/lib/timeZone";

import { AccountBar } from "./AccountBar";
import { BottomPanel } from "./BottomPanel";
import { EndedNotice } from "./EndedNotice";
import { CandlesIcon, LayersIcon, ListIcon, TradeIcon } from "./icons";
import { KronantMark } from "./KronantMark";
import { OrderPanel } from "./OrderPanel";
import { OwnLockNotice } from "./OwnLockNotice";
import { PriceAlerts } from "./PriceAlerts";
import { PriceChart } from "./PriceChart";
import { useRuleWarnings } from "./RuleWarnings";
import { Sheets } from "./Sheets";
import { StatusBar } from "./StatusBar";
import { useTradeNotices } from "./TradeNotices";
import { Watchlist } from "./Watchlist";

/**
 * Sends visitors who are not logged in to the login page, and picks which of the trader's accounts to show. A session
 * that ends while the terminal is open, without the trader logging out, goes to the login page too, which sends a
 * trader of a firm with a portal back there to be logged in again. The trader's settings are loaded from their login
 * first, so the terminal opens with their favorites, indicators and drawings from any device (ADR 0052).
 */
export function Terminal({ requestedAccountId }: { requestedAccountId: string | null }) {
  const router = useRouter();
  const me = useMe();
  const hadSession = useRef(false);
  const settingsLoaded = useLoadedSettings(me.data ? `${me.data.server.id}/${me.data.userId}` : null);

  useEffect(() => {
    if (me.data) {
      hadSession.current = true;
      rememberServer(me.data.server.id);
    } else if (me.data === null) {
      router.replace(hadSession.current && sessionExpired() ? "/login?ended=1" : "/login");
    }
  }, [me.data, router]);

  if (me.isError) {
    return <Message text="Could not reach the trading service." />;
  }

  if (!me.data || !settingsLoaded) {
    return <Message text="Loading..." />;
  }

  const accounts = me.data.accounts;
  const accountId = requestedAccountId && accounts.includes(requestedAccountId) ? requestedAccountId : accounts[0];
  if (!accountId) {
    return <Message text="You have no trading account yet." />;
  }

  return (
    <TradingTerminal
      key={accountId}
      accountId={accountId}
      accounts={accounts}
      details={me.data.accountDetails}
      email={me.data.email}
      server={me.data.server}
    />
  );
}

function TradingTerminal({
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
  useTradingConnection(accountId);
  const instruments = useInstruments(accountId);
  const current = details.find((d) => d.accountId === accountId);
  // Every time on screen is in the zone the account's trading day follows, so it agrees with the firm's portal.
  const timeZone = usableTimeZone(current?.timeZone);

  useEffect(() => rememberAccount(accountId), [accountId]);
  // A trade's details or a report belong to the account they were opened on.
  useEffect(() => () => useSheet.getState().close(), []);
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null);
  const [view, setView] = useState<PhoneView>("chart");

  const list = useMemo(() => instruments.data ?? [], [instruments.data]);
  const instrument = list.find((i) => i.symbol === selectedSymbol) ?? initialInstrument(list);
  const digitsOf = useMemo(() => digitsLookup(list), [list]);
  useTradeNotices(digitsOf);
  useRuleWarnings(accountId, current?.profitTarget ?? null, timeZone);

  if (instruments.isError) {
    return <Message text={`Could not load account ${accountId}.`} />;
  }

  return (
    <TimeZoneContext value={timeZone}>
      <div className="flex h-full flex-col">
        <AccountBar accountId={accountId} accounts={accounts} details={details} email={email} server={server} />
        <EndedNotice details={current} server={server} />
        <OwnLockNotice />
        <PriceAlerts accountId={accountId} />
        <main className="flex min-h-0 flex-1 flex-col gap-2 p-2 lg:grid lg:grid-rows-[minmax(0,1fr)_13rem]">
          <div className={`${view === "positions" ? "hidden" : "flex"} min-h-0 flex-1 flex-col lg:grid lg:grid-cols-[19rem_minmax(0,1fr)_18rem] lg:gap-2`}>
            <div className={pane("watchlist", view)}>
              <Watchlist
                accountId={accountId}
                instruments={list}
                selected={instrument?.symbol ?? null}
                onSelect={(symbol) => {
                  setSelectedSymbol(symbol);
                  setView("chart");
                }}
              />
            </div>
            <div className={pane("chart", view)}>
              <PriceChart accountId={accountId} instrument={instrument} />
            </div>
            <div className={pane("trade", view)}>
              <OrderPanel accountId={accountId} instrument={instrument} />
            </div>
          </div>
          <div className={pane("positions", view)}>
            <BottomPanel accountId={accountId} digitsOf={digitsOf} />
          </div>
        </main>
        <PhoneTabs view={view} onChange={setView} />
        <div className="hidden lg:contents">
          <StatusBar accountId={accountId} serverName={server.name} />
        </div>
        <Sheets accountId={accountId} digitsOf={digitsOf} firmName={server.name} />
      </div>
    </TimeZoneContext>
  );
}

/** What a phone shows, one at a time: the screen is too narrow for the parts side by side. */
type PhoneView = "chart" | "trade" | "watchlist" | "positions";

const phoneViews: { value: PhoneView; label: string; Icon: (props: { className?: string }) => React.ReactNode }[] = [
  { value: "chart", label: "Chart", Icon: CandlesIcon },
  { value: "trade", label: "Trade", Icon: TradeIcon },
  { value: "watchlist", label: "Watchlist", Icon: ListIcon },
  { value: "positions", label: "Positions", Icon: LayersIcon },
];

// The part's box: on a phone shown only when chosen, and filled by the part; from lg up it steps aside (display:
// contents), so the parts sit in the grid side by side as on a computer.
function pane(part: PhoneView, view: PhoneView): string {
  return `${part === view ? "flex" : "hidden"} min-h-0 flex-1 flex-col max-lg:*:min-h-0 max-lg:*:flex-1 lg:contents`;
}

/**
 * The tabs at the bottom of a phone's screen, between the chart, the order ticket, the watchlist and the positions.
 * The chosen one is brass, with a short bar along its top edge.
 */
function PhoneTabs({ view, onChange }: { view: PhoneView; onChange: (view: PhoneView) => void }) {
  return (
    <nav aria-label="Terminal" className="grid shrink-0 grid-cols-4 border-t border-border bg-panel pb-[env(safe-area-inset-bottom)] lg:hidden">
      {phoneViews.map(({ value, label, Icon }) => (
        <button
          key={value}
          type="button"
          aria-pressed={value === view}
          onClick={() => onChange(value)}
          className={`relative flex flex-col items-center gap-1 py-2 text-[11px] font-medium transition-colors duration-150 active:translate-y-px ${value === view ? "text-accent" : "text-muted hover:text-foreground"}`}
        >
          {value === view && <span aria-hidden="true" className="absolute inset-x-6 top-0 h-0.5 animate-fade rounded-b-full bg-accent" />}
          <Icon className="size-5" />
          {label}
        </button>
      ))}
    </nav>
  );
}

function Message({ text }: { text: string }) {
  return (
    <main className="flex flex-1 animate-fade flex-col items-center justify-center gap-4 p-8 text-muted">
      <KronantMark className="size-9" />
      {text}
    </main>
  );
}
