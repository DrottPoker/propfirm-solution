"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";

import { useInstruments, useMe } from "@/lib/queries";
import { useTradingConnection } from "@/lib/realtime";

import { AccountBar } from "./AccountBar";
import { BottomPanel } from "./BottomPanel";
import { OrderPanel } from "./OrderPanel";
import { PriceChart } from "./PriceChart";
import { StatusBar } from "./StatusBar";
import { Watchlist } from "./Watchlist";

/** Sends visitors who are not logged in to the login page, and picks which of the trader's accounts to show. */
export function Terminal({ requestedAccountId }: { requestedAccountId: string | null }) {
  const router = useRouter();
  const me = useMe();

  useEffect(() => {
    if (me.data === null) {
      router.replace("/login");
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
    <TradingTerminal key={accountId} accountId={accountId} accounts={accounts} email={me.data.email} serverName={me.data.server.name} />
  );
}

function TradingTerminal({
  accountId,
  accounts,
  email,
  serverName,
}: {
  accountId: string;
  accounts: string[];
  email: string;
  serverName: string;
}) {
  useTradingConnection(accountId);
  const instruments = useInstruments(accountId);
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null);

  const list = instruments.data ?? [];
  const instrument = list.find((i) => i.symbol === selectedSymbol) ?? list[0] ?? null;

  if (instruments.isError) {
    return <Message text={`Could not load account ${accountId}.`} />;
  }

  return (
    <div className="flex h-full flex-col">
      <AccountBar accounts={accounts} email={email} serverName={serverName} />
      <main className="grid min-h-0 flex-1 grid-rows-[minmax(0,1fr)_13rem] gap-2 p-2">
        <div className="grid min-h-0 grid-cols-[19rem_minmax(0,1fr)_18rem] gap-2">
          <Watchlist accountId={accountId} instruments={list} selected={instrument?.symbol ?? null} onSelect={setSelectedSymbol} />
          <PriceChart accountId={accountId} instrument={instrument} />
          <OrderPanel accountId={accountId} instrument={instrument} />
        </div>
        <BottomPanel accountId={accountId} instruments={list} />
      </main>
      <StatusBar serverName={serverName} />
    </div>
  );
}

function Message({ text }: { text: string }) {
  return <main className="flex flex-1 items-center justify-center p-8 text-muted">{text}</main>;
}
