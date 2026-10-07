"use client";

import { useBranding } from "@/app/providers";
import type { ReceiptClose, ReceiptFill, TradeReceipt } from "@/lib/api/types";
import { formatDateTime, formatLots, formatMoney, formatSignedMoney, timeZoneName } from "@/lib/format";
import { closeReasonLabels, filledAtAsk, fillsOf, markupText, priceText, stopText } from "@/lib/proof";
import { type Role, useTradeDetails } from "@/lib/queries";

import { Sheet } from "./Dialog";
import { FileCheckIcon, PrintIcon } from "./icons";
import { secondaryButtonClass } from "./ui";

/**
 * A trade's details in a panel from the side, as in the terminal (ADR 0053): for the opening and each close, the price
 * the account got and the feed's price behind it with the firm's markup, then the totals and the journal's entries.
 * Printing shows only the panel.
 */
export function TradeDetailsSheet({
  role,
  accountId,
  positionId,
  timeZone,
  onClose,
}: {
  role: Role;
  accountId: string;
  positionId: string;
  timeZone: string;
  onClose: () => void;
}) {
  const details = useTradeDetails(accountId, positionId, role);
  const data = details.data;
  return (
    <Sheet
      open
      print
      onClose={onClose}
      title={data ? `${data.side} ${formatLots(data.opened.volume)} ${data.symbol}` : "Trade details"}
      description={`Trade details for position ${positionId}. Times in ${timeZoneName(timeZone)}.`}
      footer={
        data && (
          <button type="button" onClick={() => window.print()} className={`${secondaryButtonClass} flex items-center gap-1.5 text-sm`}>
            <PrintIcon className="size-4" />
            Print or save as PDF
          </button>
        )
      }
    >
      {details.isError && <p className="text-sm text-loss">{details.error.message}</p>}
      {details.isPending && <p className="text-sm text-muted">Loading...</p>}
      {data && <DetailsBody role={role} receipt={data} timeZone={timeZone} />}
    </Sheet>
  );
}

function DetailsBody({ role, receipt, timeZone }: { role: Role; receipt: TradeReceipt; timeZone: string }) {
  const entries = fillsOf(receipt)
    .map(({ fill }) => fill.feed?.inputSequence)
    .filter((n): n is number => n !== undefined);
  return (
    <>
      <div className="flex items-baseline justify-between gap-3">
        <span className="text-sm text-muted">{receipt.result === null ? "Still open" : "Result after commission"}</span>
        {receipt.result !== null && (
          <span className={`font-mono text-2xl font-semibold tabular-nums ${receipt.result >= 0 ? "text-profit" : "text-loss"}`}>
            {formatSignedMoney(receipt.result)} {receipt.currency}
          </span>
        )}
      </div>

      {receipt.order && (
        <Section title={`${receipt.order.type} order placed`} aside={formatDateTime(receipt.order.placedAt, timeZone)}>
          <p className="text-muted">At {priceText(receipt.order.price, receipt.digits)}, filled when the price reached it.</p>
        </Section>
      )}

      {fillsOf(receipt).map(({ fill, close }, i, fills) => (
        <Fill
          key={`${fill.at}-${i}`}
          role={role}
          receipt={receipt}
          fill={fill}
          close={close}
          final={close !== null && i === fills.length - 1 && receipt.result !== null}
          timeZone={timeZone}
        />
      ))}

      {receipt.closes.length > 0 && (
        <Section title="Totals">
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
        </Section>
      )}

      <p className="flex gap-2 border-t border-border pt-4 text-xs leading-relaxed text-muted">
        <FileCheckIcon className="mt-px size-4 text-accent" />
        <span>
          {entries.length > 0
            ? `Every price here is kept in the trading platform's journal, ${entries.length === 1 ? "entry" : "entries"} ${entries.map((e) => e.toLocaleString("en-US")).join(" and ")}, and the engine gets the same result when it plays them again.`
            : "The feed's prices behind this trade were not kept with it, since it is older than these details."}
        </span>
      </p>
    </>
  );
}

// The last close of a closed position closes it, and the others close a part.
function Fill({
  role,
  receipt,
  fill,
  close,
  final,
  timeZone,
}: {
  role: Role;
  receipt: TradeReceipt;
  fill: ReceiptFill;
  close: ReceiptClose | null;
  final: boolean;
  timeZone: string;
}) {
  const branding = useBranding();
  const { digits, side } = receipt;
  const atAsk = filledAtAsk(side, close === null);
  const reason = close && close.reason !== "Manual" ? `: ${(closeReasonLabels[close.reason] ?? close.reason).toLowerCase()}` : "";
  const title = close === null ? "Opened" : final ? `Closed${reason}` : "Part closed";
  const markup = markupText(fill, atAsk);
  const stop = close === null ? null : stopText(close, side, digits);
  const filled = (shown: boolean) => `text-right font-mono tabular-nums ${shown ? "font-semibold text-accent" : "text-muted"}`;
  return (
    <Section title={title} aside={formatDateTime(fill.at, timeZone)}>
      {close === null && !receipt.order && <p className="text-xs text-muted">A market order, filled the moment it reached the trading engine.</p>}
      <dl className="grid grid-cols-[minmax(0,1fr)_auto_auto] gap-x-4 gap-y-1.5">
        <dt />
        <dd className="text-right text-xs text-muted">Bid</dd>
        <dd className="text-right text-xs text-muted">Ask</dd>
        {fill.feed && (
          <>
            <dt className="text-muted">Price from the feed</dt>
            <dd className="text-right font-mono tabular-nums">{priceText(fill.feed.bid, digits)}</dd>
            <dd className="text-right font-mono tabular-nums">{priceText(fill.feed.ask, digits)}</dd>
          </>
        )}
        <dt className="font-medium">{role === "admin" ? "The trader's price" : "Your price"}</dt>
        <dd className={filled(!atAsk)}>{atAsk ? "" : priceText(fill.price, digits)}</dd>
        <dd className={filled(atAsk)}>{atAsk ? priceText(fill.price, digits) : ""}</dd>
      </dl>
      {markup && (
        <p className="text-xs text-muted">
          {role === "admin" ? "Your" : `${branding.name}'s`} markup: {markup}.
        </p>
      )}
      {!fill.feed && <p className="text-xs text-muted">The feed&apos;s price behind this fill is not known.</p>}
      {stop && <p className="text-xs leading-relaxed text-muted">{stop}</p>}
      <dl className="grid grid-cols-[minmax(0,1fr)_auto] gap-y-1.5">
        <dt className="text-muted">Lots</dt>
        <dd className="text-right font-mono tabular-nums">{formatLots(fill.volume)}</dd>
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
    </Section>
  );
}

/** A part of the details, with a heading and what is on the right of it. */
function Section({ title, aside, children }: { title: string; aside?: string; children: React.ReactNode }) {
  return (
    <section aria-label={title} className="flex flex-col gap-2.5 border-t border-border pt-4 text-sm">
      <div className="flex items-baseline justify-between gap-3">
        <h3 className="font-semibold">{title}</h3>
        {aside && <span className="text-xs text-muted tabular-nums">{aside}</span>}
      </div>
      {children}
    </section>
  );
}
