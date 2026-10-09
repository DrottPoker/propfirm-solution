"use client";

import { useEffect, useLayoutEffect, useRef, useState } from "react";

import type { InstrumentInfo, PlaceOrderRequest, PointValue, Side } from "@/lib/api/types";
import { formatPrice, formatSignedMoney, formatVolume } from "@/lib/format";
import { useOrderDraft, type GhostLine } from "@/lib/orderDraft";
import { estimatedProfit, stopKindAt, type StopKind } from "@/lib/stops";
import { useTradingStore } from "@/lib/store";
import { pendingType } from "@/lib/ticket";

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
  /** Why the group has nothing to choose now, in place of its items. */
  note?: string;
}

/** What a new order from the menu needs: the ticket's volume, or why no order is taken now. */
export type MenuOrders = { ok: true; volume: number; place: (order: PlaceOrderRequest) => void } | { ok: false; reason: string };

const sides: Side[] = ["Buy", "Sell"];

// The menu keeps this far from the window's edges.
const edge = 8;

const detailColors = { profit: "text-profit", loss: "text-loss", muted: "text-muted" };

/**
 * The menu a right click on the chart opens at a price: the chart view can be reset, a buy and a sell order can wait
 * there at the order panel's volume, as a limit or a stop by where the price is, the order being filled in can get its
 * stop loss or take profit there, a price alert or a horizontal line can go there (ADR 0058), and each open position of
 * the symbol the stop that fits on that side of the price. A right click on a stop line offers to remove
 * that stop instead of moving it to where it already is, a right click on a ghost line of the order being filled in
 * offers to empty that field, and one on an alert's line to remove the alert.
 */
export function PriceMenu({
  instrument,
  price,
  placement,
  line,
  ghost,
  pointValue,
  marketOpen,
  alertId,
  orders,
  onModify,
  onResetChart,
  onAddAlert,
  onRemoveAlert,
  onAddLine,
  onClose,
}: {
  instrument: InstrumentInfo;
  price: number;
  placement: MenuPlacement;
  line: StopLineRef | null;
  ghost: GhostLine["kind"] | null;
  pointValue: PointValue | null | undefined;
  marketOpen: boolean;
  /** The price alert whose line the right click landed on, if any. */
  alertId: string | null;
  orders: MenuOrders;
  onModify: (positionId: string, stopLoss: number | null, takeProfit: number | null) => void;
  onResetChart: () => void;
  onAddAlert: (price: number) => void;
  onRemoveAlert: (id: string) => void;
  onAddLine: (price: number) => void;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const { symbol, digits } = instrument;
  const positions = useTradingStore((s) => s.account?.positions);
  const quote = useTradingStore((s) => s.prices[symbol]);
  const canTrade = useTradingStore((s) => s.connection === "connected" && s.account?.status === "Active");

  useEffect(() => {
    const menu = ref.current;
    const onPointerDown = (e: PointerEvent) => {
      if (!menu?.contains(e.target as Node)) {
        onClose();
      }
    };
    document.addEventListener("pointerdown", onPointerDown, true);
    return () => document.removeEventListener("pointerdown", onPointerDown, true);
  }, [onClose]);

  // Placed once as it opens, inside the window rather than the chart, so a short chart never cuts it off. The choices
  // follow the price, so a group can come or go as the price crosses the level; placed, the menu then only changes at
  // its bottom and never moves the item under the mouse. What does not fit scrolls.
  const [placed, setPlaced] = useState<{ left: number; top: number; maxHeight: number } | null>(null);
  useLayoutEffect(() => {
    const menu = ref.current;
    const area = menu?.parentElement?.getBoundingClientRect();
    if (!menu || !area) {
      return;
    }

    const width = menu.offsetWidth;
    const height = Math.min(menu.offsetHeight, window.innerHeight - 2 * edge);
    const left = area.left + placement.x - (placement.flipX ? width : 0);
    const top = Math.min(Math.max(edge, area.top + placement.y - (placement.flipY ? height : 0)), window.innerHeight - height - edge);
    setPlaced({ left: Math.min(Math.max(edge, left), window.innerWidth - width - edge), top, maxHeight: window.innerHeight - top - edge });
  }, [placement]);

  // The first item takes the focus once the menu is placed and shown, so the arrow keys and Esc work at once.
  useEffect(() => {
    if (placed) {
      ref.current?.querySelector<HTMLButtonElement>("[role=menuitem]")?.focus();
    }
  }, [placed]);

  const requestField = (field: GhostLine["kind"], level: number | null) => useOrderDraft.getState().requestField(symbol, field, level);
  // On a ghost line the choice is to remove it, where the other choices would put it where it already is.
  const orderStop = (kind: StopKind): MenuItem =>
    ghost === kind
      ? { label: `Remove ${stopNames[kind]}`, onSelect: () => requestField(kind, null) }
      : { label: `Use as ${stopNames[kind]}`, onSelect: () => requestField(kind, price) };
  const title = `At ${formatPrice(price, digits)}`;
  // A buy and a sell waiting at the price, each a limit or a stop by which side of its market price it is on.
  const newOrders: MenuItem[] = [];
  if (orders.ok && quote) {
    for (const side of sides) {
      const type = pendingType(side, price, quote, digits);
      if (type) {
        const order: PlaceOrderRequest = {
          orderId: crypto.randomUUID(),
          symbol,
          side,
          type,
          volume: orders.volume,
          price,
          stopLoss: null,
          takeProfit: null,
          trailingStop: false,
        };
        newOrders.push({
          label: `${side} ${type.toLowerCase()}`,
          detail: { text: `${formatVolume(orders.volume)} lots`, tone: "muted" },
          onSelect: () => orders.place(order),
        });
      }
    }
  }

  const groups: MenuGroup[] = [
    { key: "view", label: null, items: [{ label: "Reset chart", detail: { text: "Alt+R", tone: "muted" }, onSelect: onResetChart }] },
    {
      key: "new",
      label: "New order",
      items: newOrders,
      note: orders.ok ? (newOrders.length === 0 ? "That is the market price: use the order panel." : undefined) : orders.reason,
    },
    {
      key: "order",
      label: "Order panel",
      items: [
        ...(ghost === "entry" ? [{ label: "Remove order price", onSelect: () => requestField("entry", null) }] : []),
        orderStop("stopLoss"),
        orderStop("takeProfit"),
      ],
    },
    {
      key: "chart",
      label: "Chart",
      items: [
        alertId
          ? { label: "Remove this alert", onSelect: () => onRemoveAlert(alertId) }
          : { label: "Alert at this price", detail: { text: formatPrice(price, digits), tone: "muted" }, onSelect: () => onAddAlert(price) },
        { label: "Horizontal line here", onSelect: () => onAddLine(price) },
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
        ...(placed ?? { left: 0, top: 0, visibility: "hidden" }),
        transformOrigin: `${placement.flipX ? "right" : "left"} ${placement.flipY ? "bottom" : "top"}`,
      }}
      className={`fixed z-30 min-w-60 overflow-y-auto rounded-xl border border-border bg-panel p-1 text-sm shadow-float ${placed ? "animate-pop" : ""}`}
    >
      {groups.map((group, index) => (
        <div
          key={group.key}
          role="group"
          aria-label={group.label ?? "View"}
          className={index > 0 ? "border-t border-border py-1" : "py-1"}
        >
          {index === 1 && <p className="px-2.5 pt-1 pb-0.5 text-xs font-medium">{title}</p>}
          {group.label && <p className="px-2.5 py-1 text-xs text-muted">{group.label}</p>}
          {group.note && <p className="max-w-64 px-2.5 pb-1 text-xs leading-relaxed text-muted">{group.note}</p>}
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
                <span className={`text-xs ${detailColors[item.detail.tone]}`}>{item.detail.text}</span>
              )}
            </button>
          ))}
        </div>
      ))}
    </div>
  );
}
