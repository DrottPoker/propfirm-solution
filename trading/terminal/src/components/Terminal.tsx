"use client";

import { useState } from "react";

import { useInstruments } from "@/lib/queries";
import { useTradingConnection } from "@/lib/realtime";

import { AccountBar } from "./AccountBar";
import { BottomPanel } from "./BottomPanel";
import { OrderPanel } from "./OrderPanel";
import { PriceChart } from "./PriceChart";
import { Watchlist } from "./Watchlist";

export function Terminal({ accountId }: { accountId: string }) {
  useTradingConnection(accountId);
  const instruments = useInstruments(accountId);
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null);

  const list = instruments.data ?? [];
  const instrument = list.find((i) => i.symbol === selectedSymbol) ?? list[0] ?? null;

  if (instruments.isError) {
    return (
      <main className="flex flex-1 items-center justify-center p-8 text-muted">
        Could not load account {accountId}. Is the trading service running?
      </main>
    );
  }

  return (
    <div className="grid h-full grid-rows-[auto_minmax(0,1fr)_16rem] gap-px bg-border">
      <AccountBar />
      <div className="grid min-h-0 grid-cols-[15rem_minmax(0,1fr)_17rem] gap-px">
        <Watchlist instruments={list} selected={instrument?.symbol ?? null} onSelect={setSelectedSymbol} />
        <PriceChart accountId={accountId} instrument={instrument} />
        <OrderPanel accountId={accountId} instrument={instrument} />
      </div>
      <BottomPanel accountId={accountId} instruments={list} />
    </div>
  );
}
