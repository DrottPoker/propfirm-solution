"use client";

import { useEffect, useRef } from "react";

import type { InstrumentInfo, PointValue } from "@/lib/api/types";
import { formatPrice, formatSignedMoney, formatVolume } from "@/lib/format";
import { useOrderDraft, type GhostLine } from "@/lib/orderDraft";
import { estimatedProfit, stopKindAt, type StopKind } from "@/lib/stops";
import { useTradingStore } from "@/lib/store";

export interface MenuPlacement {
  x: number;
  y: number;
  /** Opens to the left or upwards near the right or bottom edge, so the menu stays inside the chart. */
  flipX: boolean;
  flipY: boolean;
}

/** A position's stop loss or take profit line the right click landed on. */
export interface StopLineRef {
  positionId: string;
  kind: StopKind;
}

const stopNames: Record<StopKind, string> = { stopLoss: "stop loss", takeProfit: "take profit" };

interface MenuItem {
  label: string;
  detail?: { text: string; tone: "profit" | "loss" | "muted" };
  onSelect: () => void;
}

interface MenuGroup {
  key: string;
  /** Groups without a label are shown without a heading. */
  label: string | null;
  items: MenuItem[];
}

const detailColors = { profit: "text-profit", loss: "text-loss", muted: "text-muted" };

/**
 * The menu a right click on the chart opens at a price: the chart view can be reset, the order being filled in can
 * get its stop loss or take profit there, and each open position of the symbol the stop that fits on that side of
 * the price. A right click on a stop line offers to remove that stop instead of moving it to where it already is,
 * and a right click on a ghost line of the order being filled in offers to empty that field.
 */
export function PriceMenu({
  instrument,
  price,
  placement,
  line,
  ghost,
  pointValue,
  marketOpen,
  onModify,
  onResetChart,
  onClose,
}: {
  instrument: InstrumentInfo;
  price: number;
  placement: MenuPlacement;
  line: StopLineRef | null;
  ghost: GhostLine["kind"] | null;
  pointValue: PointValue | null | undefined;
  marketOpen: boolean;
  onModify: (positionId: string, stopLoss: number | null, takeProfit: number | null) => void;
  onResetChart: () => void;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const { symbol, digits } = instrument;
  const positions = useTradingStore((s) => s.account?.positions);
  const quote = useTradingStore((s) => s.prices[symbol]);
  const canTrade = useTradingStore((s) => s.connection === "connected" && s.account?.status === "Active");

  useEffect(() => {
    const menu = ref.current;
    menu?.querySelector<HTMLButtonElement>("[role=menuitem]")?.focus();
    const onPointerDown = (e: PointerEvent) => {
      if (!menu?.contains(e.target as Node)) {
        onClose();
      }
    };
    document.addEventListener("pointerdown", onPointerDown, true);
    return () => document.removeEventListener("pointerdown", onPointerDown, true);
  }, [onClose]);

  const requestField = (field: GhostLine["kind"], level: number | null) => useOrderDraft.getState().requestField(symbol, field, level);
  // On a ghost line the choice is to remove it, where the other choices would put it where it already is.
  const orderStop = (kind: StopKind): MenuItem =>
    ghost === kind
      ? { label: `Remove ${stopNames[kind]}`, onSelect: () => requestField(kind, null) }
      : { label: kind === "stopLoss" ? "Stop loss" : "Take profit", onSelect: () => requestField(kind, price) };
  const title = `At ${formatPrice(price, digits)}`;
  const groups: MenuGroup[] = [
    { key: "view", label: null, items: [{ label: "Reset chart", detail: { text: "Alt+R", tone: "muted" }, onSelect: onResetChart }] },
    {
      key: "order",
      label: "New order",
      items: [
        ...(ghost === "entry" ? [{ label: "Remove order price", onSelect: () => requestField("entry", null) }] : []),
        orderStop("stopLoss"),
        orderStop("takeProfit"),
      ],
    },
  ];

  // A level is a stop loss on one side of the price a position closes at and a take profit on the other.
  // Stops are not moved while the market is closed, since the engine refuses it.
  for (const position of canTrade && marketOpen ? (positions ?? []).filter((p) => p.symbol === symbol) : []) {
    const closePrice = quote ? (position.side === "Buy" ? quote.bid : quote.ask) : position.currentPrice;
    const kind = stopKindAt(position.side, price, closePrice, digits);
    if (!kind) {
      continue;
    }

    const label = `${position.side} ${formatVolume(position.volume)} at ${formatPrice(position.openPrice, digits)}`;
    const onLine = line?.positionId === position.positionId ? line.kind : null;
    if (onLine) {
      groups.push({
        key: position.positionId,
        label,
        items: [
          {
            label: `Remove ${stopNames[onLine]}`,
            onSelect: () =>
              onModify(
                position.positionId,
                onLine === "stopLoss" ? null : position.stopLoss,
                onLine === "takeProfit" ? null : position.takeProfit,
              ),
          },
        ],
      });
      continue;
    }

    const current = kind === "stopLoss" ? position.stopLoss : position.takeProfit;
    const amount = pointValue ? estimatedProfit(position.side, position.volume, position.openPrice, price, digits, pointValue.perLot) : null;
    groups.push({
      key: position.positionId,
      label,
      items: [
        {
          label: `${current == null ? "Set" : "Move"} ${stopNames[kind]} here`,
          detail: amount === null ? undefined : { text: formatSignedMoney(amount), tone: amount >= 0 ? "profit" : "loss" },
          onSelect: () =>
            onModify(
              position.positionId,
              kind === "stopLoss" ? price : position.stopLoss,
              kind === "takeProfit" ? price : position.takeProfit,
            ),
        },
      ],
    });
  }

  const onKeyDown = (e: React.KeyboardEvent) => {
    const items = [...(ref.current?.querySelectorAll<HTMLButtonElement>("[role=menuitem]") ?? [])];
    const index = items.indexOf(document.activeElement as HTMLButtonElement);
    if (e.key === "Escape" || e.key === "Tab") {
      e.preventDefault();
      onClose();
    } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      items[(index + (e.key === "ArrowDown" ? 1 : -1) + items.length) % items.length]?.focus();
    }
  };

  return (
    <div
      ref={ref}
      role="menu"
      aria-label={title}
      onKeyDown={onKeyDown}
      onContextMenu={(e) => e.preventDefault()}
      style={{
        left: placement.x,
        top: placement.y,
        translate: `${placement.flipX ? "-100%" : "0"} ${placement.flipY ? "-100%" : "0"}`,
        transformOrigin: `${placement.flipX ? "right" : "left"} ${placement.flipY ? "bottom" : "top"}`,
      }}
      className="absolute z-30 min-w-60 animate-pop rounded-xl border border-border bg-panel p-1 text-sm shadow-float"
    >
      {groups.map((group, index) => (
        <div
          key={group.key}
          role="group"
          aria-label={group.label ?? "Chart"}
          className={index > 0 ? "border-t border-border py-1" : "py-1"}
        >
          {index === 1 && <p className="px-2.5 pt-1 font-mono text-xs text-muted tabular-nums">{title}</p>}
          {group.label && <p className="px-2.5 py-1 text-[11px] font-medium tracking-wide text-muted uppercase">{group.label}</p>}
          {group.items.map((item) => (
            <button
              key={item.label}
              type="button"
              role="menuitem"
              onClick={() => {
                onClose();
                item.onSelect();
              }}
              className="flex w-full items-center justify-between gap-6 rounded-md px-2.5 py-1.5 text-left transition-colors duration-100 hover:bg-raised focus:bg-raised focus:outline-none"
            >
              <span>{item.label}</span>
              {item.detail && (
                <span className={`font-mono text-xs tabular-nums ${detailColors[item.detail.tone]}`}>{item.detail.text}</span>
              )}
            </button>
          ))}
        </div>
      ))}
    </div>
  );
}
