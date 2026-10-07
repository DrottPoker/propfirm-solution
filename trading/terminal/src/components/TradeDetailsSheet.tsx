"use client";

import { useState } from "react";

import type { ReceiptClose, ReceiptFill, TradeReceipt } from "@/lib/api/types";
import { closedBecause } from "@/lib/events";
import { formatMoment, formatMoney, formatPrice, formatSignedMoney, formatVolume, timeZoneName } from "@/lib/format";
import { useTradeDetails } from "@/lib/queries";
import { detailsText, filledAtAsk, fillsOf, markupText, stopText } from "@/lib/tradeDetails";
import { useTimeZone } from "@/lib/timeZone";

import { CopyIcon, ProofIcon } from "./icons";
import { Sheet, SheetSection } from "./Sheet";

/**
 * A trade's details (ADR 0053): for the opening and each close, the price the account got and the feed's price behind
 * it with the firm's markup, then the totals and the journal's entries.
 */
export function TradeDetailsSheet({ accountId, positionId }: { accountId: string; positionId: string }) {
  const details = useTradeDetails(accountId, positionId);
  const timeZone = useTimeZone();
  const data = details.data;
  const title = data ? `${data.side} ${formatVolume(data.opened.volume)} ${data.symbol}` : "Trade details";

  return (
    <Sheet label="Trade details" title={title} subtitle={`Position ${positionId} · times in ${timeZoneName(timeZone)}`}>
      {details.isError && <p className="px-5 py-4 text-sm text-loss">Could not load the trade&apos;s details.</p>}
      {details.isPending && <p className="px-5 py-4 text-sm text-muted">Loading...</p>}
      {data && <DetailsBody receipt={data} timeZone={timeZone} />}
    </Sheet>
  );
}

function DetailsBody({ receipt, timeZone }: { receipt: TradeReceipt; timeZone: string }) {
  const [copied, setCopied] = useState(false);
  const entries = fillsOf(receipt)
    .map(({ fill }) => fill.feed?.inputSequence)
    .filter((n): n is number => n !== undefined);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(detailsText(receipt, timeZone));
      setCopied(true);
    } catch {
      // Without clipboard access, the trader can still print the details.
    }
  };

  return (
    <>
      <div className="flex items-baseline justify-between gap-3 border-b border-border px-5 py-4">
        <span className="text-sm text-muted">{receipt.result === null ? "Still open" : "Result after commission"}</span>
        {receipt.result !== null && (
          <span className={`font-mono text-2xl font-semibold tabular-nums ${receipt.result >= 0 ? "text-profit" : "text-loss"}`}>
            {formatSignedMoney(receipt.result)} {receipt.currency}
          </span>
        )}
      </div>

      {receipt.order && (
        <SheetSection title={`${receipt.order.type} order placed`} aside={formatMoment(receipt.order.placedAt, timeZone)}>
          <p className="text-xs text-muted">
            At {formatPrice(receipt.order.price, receipt.digits)}, filled when the price reached it.
          </p>
        </SheetSection>
      )}

      {fillsOf(receipt).map(({ fill, close }, i, fills) => (
        <Fill
          key={fill.at + fill.volume}
          receipt={receipt}
          fill={fill}
          close={close}
          final={close !== null && i === fills.length - 1 && receipt.result !== null}
          timeZone={timeZone}
        />
      ))}

      {receipt.closes.length > 0 && (
        <SheetSection title="Totals">
          <dl className="grid grid-cols-[minmax(0,1fr)_auto] gap-y-1.5">
            <dt className="text-muted">Profit before commission</dt>
            <dd className="text-right font-mono tabular-nums">{formatSignedMoney(receipt.profit ?? 0)}</dd>
            <dt className="text-muted">Commission</dt>
            <dd className="text-right font-mono tabular-nums">{formatSignedMoney(-receipt.commission)}</dd>
            {receipt.result !== null && (
              <>
                <dt className="font-semibold">Result</dt>
                <dd className={`text-right font-mono font-semibold tabular-nums ${receipt.result >= 0 ? "text-profit" : "text-loss"}`}>
                  {formatSignedMoney(receipt.result)} {receipt.currency}
                </dd>
              </>
            )}
          </dl>
        </SheetSection>
      )}

      <div className="flex flex-col gap-3 px-5 py-4">
        <p className="flex gap-2 text-xs leading-relaxed text-muted">
          <ProofIcon className="mt-px size-4 shrink-0 text-accent" />
          <span>
            {entries.length > 0
              ? `Every price here is kept in the platform's journal, ${entries.length === 1 ? "entry" : "entries"} ${entries.map((e) => e.toLocaleString("en-US")).join(" and ")}, and the engine gets the same result when it plays them again.`
              : "The feed's prices behind this trade were not kept with it, since it is older than these details."}
          </span>
        </p>
        <button
          type="button"
          onClick={() => void copy()}
          className="flex h-10 w-fit items-center gap-2 rounded-lg border border-border bg-raised px-4 text-sm font-medium transition duration-150 hover:border-muted active:translate-y-px print:hidden"
        >
          <CopyIcon />
          {copied ? "Copied" : "Copy as text for the firm"}
        </button>
      </div>
    </>
  );
}

// The last close of a closed position closes it, and the others close a part.
function Fill({
  receipt,
  fill,
  close,
  final,
  timeZone,
}: {
  receipt: TradeReceipt;
  fill: ReceiptFill;
  close: ReceiptClose | null;
  final: boolean;
  timeZone: string;
}) {
  const { digits, side } = receipt;
  const opening = close === null;
  const atAsk = filledAtAsk(side, opening);
  const title = close === null ? "Opened" : !final ? "Part closed" : `Closed${closedBecause[close.reason]}`;
  const markup = markupText(fill, atAsk);
  const stop = close === null ? null : stopText(close, side, digits);

  return (
    <SheetSection title={title} aside={formatMoment(fill.at, timeZone)}>
      {opening && !receipt.order && <p className="text-xs text-muted">A market order, filled the moment it reached the trading engine.</p>}
      <dl className="grid grid-cols-[minmax(0,1fr)_auto_auto] gap-x-4 gap-y-1.5">
        <dt />
        <dd className="text-right text-[11px] text-muted">Bid</dd>
        <dd className="text-right text-[11px] text-muted">Ask</dd>
        {fill.feed && (
          <>
            <dt className="text-muted">Price from the feed</dt>
            <dd className="text-right font-mono tabular-nums">{formatPrice(fill.feed.bid, digits)}</dd>
            <dd className="text-right font-mono tabular-nums">{formatPrice(fill.feed.ask, digits)}</dd>
          </>
        )}
        <dt className="font-medium">Your price</dt>
        <dd className={`text-right font-mono tabular-nums ${atAsk ? "text-muted" : "font-semibold text-accent"}`}>{atAsk ? "" : formatPrice(fill.price, digits)}</dd>
        <dd className={`text-right font-mono tabular-nums ${atAsk ? "font-semibold text-accent" : "text-muted"}`}>{atAsk ? formatPrice(fill.price, digits) : ""}</dd>
      </dl>
      {markup && <p className="text-xs text-muted">The firm&apos;s markup: {markup}.</p>}
      {stop && <p className="text-xs leading-relaxed text-muted">{stop}</p>}
      <dl className="grid grid-cols-[minmax(0,1fr)_auto] gap-y-1.5">
        <dt className="text-muted">Volume</dt>
        <dd className="text-right font-mono tabular-nums">{formatVolume(fill.volume)}</dd>
        {close && (
          <>
            <dt className="text-muted">Profit before commission</dt>
            <dd className="text-right font-mono tabular-nums">{formatSignedMoney(close.profit)}</dd>
          </>
        )}
        <dt className="text-muted">Commission</dt>
        <dd className="text-right font-mono tabular-nums">
          {formatMoney(fill.commission)} {receipt.currency}
        </dd>
      </dl>
    </SheetSection>
  );
}
