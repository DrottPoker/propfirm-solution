"use client";

import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";

import { sessionExpired } from "@/lib/api/client";
import type { AccountDetails, ServerInfo } from "@/lib/api/types";
import { usableTimeZone } from "@/lib/format";
import { digitsLookup, initialInstrument } from "@/lib/instruments";
import { chartMinimum, defaultLayout, fitWidths, layoutLimits, useLayout } from "@/lib/layout";
import { useConnectionNotice } from "@/lib/connection";
import { useInstruments, useMe } from "@/lib/queries";
import { useTradingConnection } from "@/lib/realtime";
import { rememberAccount, rememberServer } from "@/lib/servers";
import { ProfileContext } from "@/lib/profileContext";
import { useSheet } from "@/lib/sheet";
import { useSettings } from "@/lib/settings";
import { isTyping } from "@/lib/shortcuts";
import { useTheme } from "@/lib/theme";
import { useLoadedSettings } from "@/lib/syncedSettings";
import { TimeZoneContext } from "@/lib/timeZone";

import { AccountBar } from "./AccountBar";
import { AccountBanner, PlatformBanner } from "./Banners";
import { BottomPanel } from "./BottomPanel";
import { CandlesIcon, LayersIcon, ListIcon, TradeIcon } from "./icons";
import { KronantMark } from "./KronantMark";
import { OrderPanel } from "./OrderPanel";
import { usePriceAlertWatcher } from "./PriceAlertWatcher";
import { PriceChart } from "./PriceChart";
import { useRuleWarnings } from "./RuleWarnings";
import { Sheets } from "./Sheets";
import { Splitter } from "./Splitter";
import { Tour } from "./Tour";
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
      name={me.data.name}
      server={me.data.server}
    />
  );
}

function TradingTerminal({
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
  useTradingConnection(accountId);
  const instruments = useInstruments(accountId);
  const current = details.find((d) => d.accountId === accountId);
  // Every time on screen is in one zone: by default the one the account's trading day follows, so it agrees with the
  // firm's portal, or the computer's or UTC as the trader chose (ADR 0058).
  const zoneChoice = useSettings((s) => s.timeZone);
  const timeZone = zoneChoice === "utc" ? "UTC" : usableTimeZone(zoneChoice === "computer" ? Intl.DateTimeFormat().resolvedOptions().timeZone : current?.timeZone);
  // The chart draws on a canvas in the theme's colors, so it is drawn anew when the theme changes.
  const theme = useTheme();

  useEffect(() => rememberAccount(accountId), [accountId]);
  // A trade's details or a report belong to the account they were opened on.
  useEffect(() => () => useSheet.getState().close(), []);
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null);
  const [view, setView] = useState<PhoneView>("chart");

  const list = useMemo(() => instruments.data ?? [], [instruments.data]);
  const instrument = list.find((i) => i.symbol === selectedSymbol) ?? initialInstrument(list);
  const digitsOf = useMemo(() => digitsLookup(list), [list]);
  useTradeNotices(digitsOf);
  usePriceAlertWatcher(digitsOf, timeZone);
  useRuleWarnings(accountId, current?.profitTarget ?? null, timeZone, server.profile);
  const connection = useConnectionNotice();
  const offline = connection === "lost" || connection === "unreachable";

  // "/" searches the watchlist and "?" lists the shortcuts (ADR 0058), unless a field is being typed in.
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey || isTyping(e.target)) {
        return;
      }

      if (e.key === "/") {
        e.preventDefault();
        setView("watchlist");
        useLayout.getState().setOpen("watchlist", true);
        requestAnimationFrame(() => document.querySelector<HTMLInputElement>('input[aria-label="Search name or symbol"]')?.focus());
      } else if (e.key === "?") {
        e.preventDefault();
        useSheet.getState().open({ kind: "shortcuts" });
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  if (instruments.isError) {
    return <Message text={`Could not load account ${accountId}.`} />;
  }

  return (
    <ProfileContext value={server.profile}>
      <TimeZoneContext value={timeZone}>
        {/* While the connection is lost, the live figures are dimmed, so none of them is taken for current. */}
        <div className="flex h-full flex-col" data-offline={offline || undefined}>
          <AccountBar accountId={accountId} accounts={accounts} details={details} email={email} name={name} server={server} />
          <AccountBanner details={current} server={server} />
          <PlatformBanner accountId={accountId} serverName={server.name} connection={connection} />
          <Workspace
            view={view}
            watchlist={
              <Watchlist
                accountId={accountId}
                instruments={list}
                selected={instrument?.symbol ?? null}
                onSelect={(symbol) => {
                  setSelectedSymbol(symbol);
                  setView("chart");
                }}
              />
            }
            chart={<PriceChart key={theme} accountId={accountId} instrument={instrument} />}
            orderPanel={<OrderPanel accountId={accountId} instrument={instrument} />}
            positions={<BottomPanel accountId={accountId} digitsOf={digitsOf} />}
          />
          <PhoneTabs view={view} onChange={setView} />
          <div className="hidden lg:contents">
            <StatusBar accountId={accountId} serverName={server.name} />
          </div>
          <Sheets accountId={accountId} digitsOf={digitsOf} server={server} />
          <Tour />
        </div>
      </TimeZoneContext>
    </ProfileContext>
  );
}

/**
 * The parts of the terminal edge to edge, with lines between them that the trader drags (ADR 0058): the watchlist on
 * the left, the chart over the positions in the middle and the order panel on the right, the full height of the window
 * so its buttons and what the order risks are always in view. The positions panel is only as tall as what it shows,
 * up to the height the trader chose, and folds down to its tabs. On a phone one part at a time fills the screen.
 */
function Workspace({
  view,
  watchlist,
  chart,
  orderPanel,
  positions,
}: {
  view: PhoneView;
  watchlist: React.ReactNode;
  chart: React.ReactNode;
  orderPanel: React.ReactNode;
  positions: React.ReactNode;
}) {
  const layout = useLayout();
  const width = useWindowWidth();
  const fitted = fitWidths(layout, width);
  const [middle, setMiddle] = useState<HTMLDivElement | null>(null);
  const middleHeight = useHeight(middle);
  const [chartPane, setChartPane] = useState<HTMLDivElement | null>(null);
  useNotesOver(chartPane);
  // The chart keeps its least height, so the positions never grow over it. Until the middle is measured, the chosen height.
  const bottomMax =
    middleHeight === 0 ? layoutLimits.bottom.max : Math.max(layoutLimits.bottom.min, Math.min(layoutLimits.bottom.max, middleHeight - chartMinimum.height));
  const sizes = {
    "--watchlist-width": `${fitted.watchlist}px`,
    "--order-width": `${fitted.orderPanel}px`,
    "--bottom-height": `${Math.min(layout.bottom, bottomMax)}px`,
  } as React.CSSProperties;

  return (
    <main style={sizes} className="flex min-h-0 flex-1 flex-col bg-panel lg:flex-row">
      {layout.watchlistOpen && (
        <>
          <div className={`${pane("watchlist", view)} lg:w-(--watchlist-width) lg:flex-none`}>{watchlist}</div>
          <div className="hidden lg:contents">
            <Splitter
              orientation="vertical"
              label="Resize the watchlist"
              value={fitted.watchlist}
              {...layoutLimits.watchlist}
              direction={1}
              defaultValue={defaultLayout.watchlist}
              onResize={(size) => layout.resize("watchlist", size)}
              onDone={layout.save}
            />
          </div>
        </>
      )}
      {/* On a phone the watchlist is a tab of its own, also when it is hidden on the computer. */}
      {!layout.watchlistOpen && <div className={`${pane("watchlist", view)} lg:hidden`}>{watchlist}</div>}
      <div ref={setMiddle} className={`${view === "chart" || view === "positions" ? "flex" : "hidden"} min-h-0 min-w-0 flex-1 flex-col lg:flex`}>
        <div ref={setChartPane} className={`${pane("chart", view)} lg:min-h-0 lg:flex-1`}>
          {chart}
        </div>
        {layout.bottomOpen && (
          <div className="hidden lg:contents">
            <Splitter
              orientation="horizontal"
              label="Resize the positions"
              value={Math.min(layout.bottom, bottomMax)}
              min={layoutLimits.bottom.min}
              max={bottomMax}
              direction={-1}
              defaultValue={defaultLayout.bottom}
              onResize={(size) => layout.resize("bottom", size)}
              onDone={layout.save}
            />
          </div>
        )}
        {/* Only as tall as what it shows, up to the chosen height; folded, only its tabs. */}
        <div
          className={`${pane("positions", view)} lg:max-h-(--bottom-height) lg:flex-none lg:*:flex-initial ${layout.bottomOpen ? "" : "lg:border-t lg:border-border"}`}
        >
          {positions}
        </div>
      </div>
      <div className="hidden lg:contents">
        <Splitter
          orientation="vertical"
          label="Resize the order panel"
          value={fitted.orderPanel}
          {...layoutLimits.orderPanel}
          direction={-1}
          defaultValue={defaultLayout.orderPanel}
          onResize={(size) => layout.resize("orderPanel", size)}
          onDone={layout.save}
        />
      </div>
      <div className={`${pane("trade", view)} lg:w-(--order-width) lg:flex-none`}>{orderPanel}</div>
    </main>
  );
}

/**
 * Tells the notes where the chart's bottom left corner is, so they come in over its oldest candles and never over the
 * order panel or the positions.
 */
function useNotesOver(element: HTMLElement | null) {
  useEffect(() => {
    if (!element) {
      return;
    }

    const place = () => {
      const rect = element.getBoundingClientRect();
      const root = document.documentElement.style;
      root.setProperty("--notes-left", `${Math.round(rect.left)}px`);
      // Above the chart's time axis.
      root.setProperty("--notes-bottom", `${Math.round(window.innerHeight - rect.bottom + 28)}px`);
    };
    place();
    const observer = new ResizeObserver(place);
    observer.observe(element);
    window.addEventListener("resize", place);
    return () => {
      observer.disconnect();
      window.removeEventListener("resize", place);
    };
  }, [element]);
}

/** The window's width, kept up to date. */
function useWindowWidth(): number {
  const [width, setWidth] = useState(() => (typeof window === "undefined" ? 1440 : window.innerWidth));
  useEffect(() => {
    const onResize = () => setWidth(window.innerWidth);
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);
  return width;
}

/** The element's height, kept up to date. */
function useHeight(element: HTMLElement | null): number {
  const [height, setHeight] = useState(0);
  useEffect(() => {
    if (!element) {
      return;
    }

    const observer = new ResizeObserver(([entry]) => setHeight(entry.contentRect.height));
    observer.observe(element);
    return () => observer.disconnect();
  }, [element]);
  return height;
}

/** What a phone shows, one at a time: the screen is too narrow for the parts side by side. */
type PhoneView = "chart" | "trade" | "watchlist" | "positions";

const phoneViews: { value: PhoneView; label: string; Icon: (props: { className?: string }) => React.ReactNode }[] = [
  { value: "chart", label: "Chart", Icon: CandlesIcon },
  { value: "trade", label: "Trade", Icon: TradeIcon },
  { value: "watchlist", label: "Watchlist", Icon: ListIcon },
  { value: "positions", label: "Positions", Icon: LayersIcon },
];

// The part's box: on a phone shown only when chosen and filling the screen; from lg up always shown, at the size the
// workspace gives it.
function pane(part: PhoneView, view: PhoneView): string {
  return `${part === view ? "flex" : "hidden"} min-h-0 min-w-0 flex-1 flex-col lg:flex lg:flex-col *:min-h-0 *:flex-1`;
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
