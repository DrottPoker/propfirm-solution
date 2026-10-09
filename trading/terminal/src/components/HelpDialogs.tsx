"use client";

import { useId, useRef } from "react";

import type { ServerInfo } from "@/lib/api/types";
import packageJson from "../../package.json";
import { useSheet } from "@/lib/sheet";
import { shortcuts } from "@/lib/shortcuts";

import { ConnectionStatusText, useConnectionStatus } from "./ConnectionStatus";
import { CloseIcon } from "./icons";
import { KronantWordmark } from "./KronantMark";
import { useDismiss } from "./useDismiss";

/** The terminal's version, from its package. */
const version = packageJson.version;

/** A small dialog in the middle of the screen, closed with its cross, Esc or a press outside it. */
function Dialog({ title, children }: { title: string; children: React.ReactNode }) {
  const close = useSheet((s) => s.close);
  const ref = useRef<HTMLDivElement>(null);
  const titleId = useId();
  useDismiss(true, ref, close);

  return (
    <div className="fixed inset-0 z-30 flex animate-fade items-center justify-center bg-background/75 p-4 backdrop-blur-sm">
      <div ref={ref} role="dialog" aria-modal="true" aria-labelledby={titleId} className="flex w-full max-w-md animate-pop flex-col rounded-2xl border border-border bg-panel shadow-float">
        <div className="flex items-center justify-between gap-3 border-b border-border px-5 py-3.5">
          <h2 id={titleId} className="text-lg font-semibold">
            {title}
          </h2>
          <button
            type="button"
            aria-label="Close"
            onClick={close}
            className="flex size-8 items-center justify-center rounded-lg text-muted transition-colors duration-150 hover:bg-raised hover:text-foreground"
          >
            <CloseIcon className="size-4" />
          </button>
        </div>
        <div className="px-5 py-4 text-sm">{children}</div>
      </div>
    </div>
  );
}

/** Every keyboard shortcut (ADR 0058). None places an order. */
export function ShortcutsDialog() {
  return (
    <Dialog title="Keyboard shortcuts">
      <dl className="flex flex-col divide-y divide-border">
        {shortcuts.map((s) => (
          <div key={s.what} className="flex items-center justify-between gap-6 py-2">
            <dt className="text-muted">{s.what}</dt>
            <dd className="flex shrink-0 items-center gap-1 text-xs">
              {s.keys.map((key, i) => (
                <span key={key} className="flex items-center gap-1">
                  {i > 0 && <span className="text-muted">{s.keys.length === 2 && s.keys[0] === "1" ? "to" : "+"}</span>}
                  <kbd className="min-w-6 rounded-md border border-border bg-raised px-1.5 py-0.5 text-center font-sans font-medium">{key}</kbd>
                </span>
              ))}
            </dd>
          </div>
        ))}
      </dl>
      <p className="mt-3 text-xs text-muted">No shortcut places an order, so a key pressed by mistake costs nothing.</p>
    </Dialog>
  );
}

/**
 * The terminal's version and maker, how it is connected, and the chart library's attribution, which its licence asks
 * for since its logo is left off the chart (ADR 0058).
 */
export function AboutDialog({ accountId, server }: { accountId: string; server: ServerInfo }) {
  const connection = useConnectionStatus(accountId);
  return (
    <Dialog title="About">
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between gap-3">
          <KronantWordmark name="Kronant Trader" />
          <span className="text-xs text-muted">Version {version}</span>
        </div>
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-xs">
          <dt className="text-muted">Server</dt>
          <dd className="text-right">
            {server.name} <span className="text-muted">({server.id})</span>
          </dd>
          <dt className="text-muted">Connection</dt>
          <dd className="flex justify-end">
            <ConnectionStatusText status={connection} />
          </dd>
          <dt className="text-muted">Made by</dt>
          <dd className="text-right">Ludware</dd>
        </dl>
        <p className="border-t border-border pt-3 text-xs leading-relaxed text-muted">
          Charts by TradingView Lightweight Charts™, Copyright (c) 2025 TradingView, Inc.,{" "}
          <a href="https://www.tradingview.com/" target="_blank" rel="noreferrer" className="text-accent hover:underline">
            tradingview.com
          </a>
          .
        </p>
      </div>
    </Dialog>
  );
}
