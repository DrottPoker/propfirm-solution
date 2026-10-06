"use client";

import { useEffect, useState } from "react";

import { productName } from "@/lib/config";
import { formatDateTime, formatMoney, formatPercent, timeZoneName } from "@/lib/format";
import { useTradingStore } from "@/lib/store";
import { useTimeZone } from "@/lib/timeZone";

import { KronantMark } from "./KronantMark";

/**
 * Our name, the firm's server, the time in the account's time zone like every other time on screen, and the
 * account's margin figures. The account bar above is the firm's; this is where the terminal says it is ours.
 */
export function StatusBar({ serverName }: { serverName: string }) {
  const account = useTradingStore((s) => s.account);

  return (
    <footer className="flex h-8 shrink-0 items-center gap-6 overflow-x-auto border-t border-border bg-panel px-4 text-xs whitespace-nowrap text-muted">
      <span className="flex items-center gap-1.5 text-foreground">
        <KronantMark className="size-4" />
        {productName}
      </span>
      <span>
        Server: <span className="text-foreground">{serverName}</span>
      </span>
      <Clock />
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

function Clock() {
  const timeZone = useTimeZone();
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
    <span title={`Every time in the terminal is in ${timeZone}, the time zone of the account's trading day.`}>
      Time: <span className="font-mono text-foreground tabular-nums">{now ? formatDateTime(now, timeZone) : "-"}</span> {timeZoneName(timeZone)}
    </span>
  );
}
