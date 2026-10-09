"use client";

import { useEffect, useState } from "react";

import { productName } from "@/lib/config";
import { formatDateTime, timeZoneName } from "@/lib/format";
import { layoutPresets, presetOf, useLayout } from "@/lib/layout";
import { useSettings } from "@/lib/settings";
import { useTimeZone } from "@/lib/timeZone";

import { ConnectionStatusText, useConnectionStatus } from "./ConnectionStatus";
import { LayoutIcon } from "./icons";
import { KronantMark } from "./KronantMark";
import { Menu } from "./Menu";

/**
 * Our name, the firm's server and whether prices come from it live, the time in the account's time zone like every
 * other time on screen, and the layout. The account bar above is the firm's; this is where the terminal says it is
 * ours. The account's figures are in the account bar only, so nothing is said twice (ADR 0058).
 */
export function StatusBar({ accountId, serverName, children }: { accountId: string; serverName: string; children?: React.ReactNode }) {
  const connection = useConnectionStatus(accountId);

  return (
    <footer className="flex h-8 shrink-0 items-center gap-5 overflow-x-auto border-t border-border bg-panel px-4 text-xs whitespace-nowrap text-muted">
      <span className="flex items-center gap-1.5 text-foreground">
        <KronantMark className="size-4" />
        {productName}
      </span>
      <span title="The firm's server you are logged in to">{serverName}</span>
      <ConnectionStatusText status={connection} />
      <Clock />
      <span className="ml-auto flex items-center gap-1">
        {children}
        <LayoutMenu />
      </span>
    </footer>
  );
}

/** The ready-made layouts, and the parts that can be shown or hidden. */
function LayoutMenu() {
  const layout = useLayout();
  const preset = presetOf(layout);
  type Choice = (typeof layoutPresets)[number]["id"] | "watchlist" | "bottom";
  const items = [
    ...layoutPresets.map((p) => ({ value: p.id as Choice, label: p.name, hint: p.description })),
    { value: "watchlist" as Choice, label: layout.watchlistOpen ? "Hide the watchlist" : "Show the watchlist", action: true },
    { value: "bottom" as Choice, label: layout.bottomOpen ? "Fold the positions down" : "Open the positions", action: true },
  ];

  return (
    <Menu
      label="Layout"
      title="Layout"
      buttonLabel="Layout"
      side="above"
      align="end"
      button={
        <span className="flex items-center gap-1.5">
          <LayoutIcon className="size-3.5" />
          {preset ? layoutPresets.find((p) => p.id === preset)?.name : "Your layout"}
        </span>
      }
      items={items}
      value={preset}
      onChoose={(choice) => {
        if (choice === "watchlist") {
          layout.setOpen("watchlist", !layout.watchlistOpen);
        } else if (choice === "bottom") {
          layout.setOpen("bottom", !layout.bottomOpen);
        } else {
          const chosen = layoutPresets.find((p) => p.id === choice);
          if (chosen) {
            layout.apply(chosen.layout);
            // Active trading also buys and sells from the chart (ADR 0058).
            if (chosen.id === "trading") {
              useSettings.getState().change("chartTrading", true);
            }
          }
        }
      }}
      className="flex h-6 items-center rounded-md px-2 text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground"
    />
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
      <span className="text-foreground">{now ? formatDateTime(now, timeZone) : "-"}</span> {timeZoneName(timeZone)}
    </span>
  );
}
