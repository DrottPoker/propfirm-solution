"use client";

import { useEffect, useState } from "react";

import { formatMoney, formatPercent, formatUtc } from "@/lib/format";
import { useTradingStore } from "@/lib/store";

/** The firm's server, the time in UTC like the chart, and the account's margin figures. */
export function StatusBar({ serverName }: { serverName: string }) {
  const account = useTradingStore((s) => s.account);

  return (
    <footer className="flex h-8 shrink-0 items-center gap-6 overflow-x-auto border-t border-border bg-panel px-4 text-xs whitespace-nowrap text-muted">
      <span>
        Server: <span className="text-foreground">{serverName}</span>
      </span>
      <UtcClock />
      {account && (
        <span className="ml-auto flex gap-6">
          <Figure label="Equity" value={`${formatMoney(account.equity)} ${account.currency}`} />
          <Figure label="Margin" value={formatMoney(account.usedMargin)} />
          <Figure label="Free margin" value={formatMoney(account.freeMargin)} />
          <Figure label="Margin level" value={formatPercent(account.marginLevelPercent)} />
        </span>
      )}
    </footer>
  );
}

function Figure({ label, value }: { label: string; value: string }) {
  return (
    <span>
      {label}: <span className="font-mono text-foreground tabular-nums">{value}</span>
    </span>
  );
}

function UtcClock() {
  const [now, setNow] = useState<Date | null>(null);

  useEffect(() => {
    const tick = () => setNow(new Date());
    const first = setTimeout(tick, 0);
    const timer = setInterval(tick, 1_000);
    return () => {
      clearTimeout(first);
      clearInterval(timer);
    };
  }, []);

  return (
    <span>
      Time: <span className="font-mono text-foreground tabular-nums">{now ? `${formatUtc(now)} UTC` : "-"}</span>
    </span>
  );
}
