"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";

import { sessionExpired } from "@/lib/api/client";
import type { AccountDetails, ServerInfo } from "@/lib/api/types";
import { usableTimeZone } from "@/lib/format";
import { initialInstrument } from "@/lib/instruments";
import { useInstruments, useMe } from "@/lib/queries";
import { useTradingConnection } from "@/lib/realtime";
import { rememberAccount, rememberServer } from "@/lib/servers";
import { TimeZoneContext } from "@/lib/timeZone";

import { AccountBar } from "./AccountBar";
import { BottomPanel } from "./BottomPanel";
import { EndedNotice } from "./EndedNotice";
import { CandlesIcon, LayersIcon, ListIcon, TradeIcon } from "./icons";
import { OrderPanel } from "./OrderPanel";
import { PriceChart } from "./PriceChart";
import { StatusBar } from "./StatusBar";
import { Watchlist } from "./Watchlist";

/**
 * Sends visitors who are not logged in to the login page, and picks which of the trader's accounts to show. A session
 * that ends while the terminal is open, without the trader logging out, goes to the login page too, which sends a
 * trader of a firm with a portal back there to be logged in again.
 */
export function Terminal({ requestedAccountId }: { requestedAccountId: string | null }) {
  const router = useRouter();
  const me = useMe();
  const hadSession = useRef(false);

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

  if (!me.data) {
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
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null);
  const [view, setView] = useState<PhoneView>("chart");

  const list = instruments.data ?? [];
  const instrument = list.find((i) => i.symbol === selectedSymbol) ?? initialInstrument(list);

  if (instruments.isError) {
    return <Message text={`Could not load account ${accountId}.`} />;
  }

  return (
    <TimeZoneContext value={timeZone}>
      <div className="flex h-full flex-col">
        <AccountBar accounts={accounts} details={details} email={email} server={server} />
        <EndedNotice details={current} server={server} />
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
            <BottomPanel accountId={accountId} instruments={list} />
          </div>
        </main>
        <PhoneTabs view={view} onChange={setView} />
        <div className="hidden lg:contents">
          <StatusBar serverName={server.name} />
        </div>
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

/** The tabs at the bottom of a phone's screen, between the chart, the order ticket, the watchlist and the positions. */
function PhoneTabs({ view, onChange }: { view: PhoneView; onChange: (view: PhoneView) => void }) {
  return (
    <nav aria-label="Terminal" className="grid shrink-0 grid-cols-4 border-t border-border bg-panel pb-[env(safe-area-inset-bottom)] lg:hidden">
      {phoneViews.map(({ value, label, Icon }) => (
        <button
          key={value}
          type="button"
          aria-pressed={value === view}
          onClick={() => onChange(value)}
          className={`flex flex-col items-center gap-1 py-2 text-[11px] ${value === view ? "text-accent" : "text-muted hover:text-foreground"}`}
        >
          <Icon className="size-5" />
          {label}
        </button>
      ))}
    </nav>
  );
}

function Message({ text }: { text: string }) {
  return <main className="flex flex-1 items-center justify-center p-8 text-muted">{text}</main>;
}
