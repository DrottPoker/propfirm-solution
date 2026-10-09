"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Toaster } from "sonner";

import { ErrorIcon, InfoIcon, SuccessIcon, WarningIcon } from "@/components/icons";
import { onSessionEnded } from "@/lib/api/client";
import { meKey } from "@/lib/queries";
import { useApplyTheme } from "@/lib/theme";

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1 } } }));

  // A session that ends while the terminal is open logs the trader out, so the terminal sends them to log in again.
  useEffect(() => onSessionEnded(() => queryClient.setQueryData(meKey, null)), [queryClient]);
  useApplyTheme();

  return (
    <QueryClientProvider client={queryClient}>
      {children}
      <Notes />
    </QueryClientProvider>
  );
}

/**
 * The short notes about fills, closes, refusals, alerts and the account's rules. They come in at the bottom left of the
 * chart, over its oldest candles, so they never cover the order panel, the latest price or the positions and their
 * Close buttons (ADR 0058). The workspace tells where the chart is. Several stand one above the other, never on top of
 * each other. On a phone they come in at the top.
 */
function Notes() {
  return (
    <Toaster
      position="bottom-left"
      offset={{ left: "calc(var(--notes-left, 0px) + 0.75rem)", bottom: "calc(var(--notes-bottom, 2rem) + 0.75rem)" }}
      mobileOffset={{ top: "0.5rem", right: "0.5rem", left: "0.5rem" }}
      expand
      visibleToasts={4}
      gap={8}
      style={{ "--width": "18rem" } as React.CSSProperties}
      icons={{
        success: <SuccessIcon className="size-5" />,
        error: <ErrorIcon className="size-5" />,
        warning: <WarningIcon className="size-5" />,
        info: <InfoIcon className="size-5" />,
      }}
      toastOptions={{
        unstyled: true,
        classNames: {
          toast: "flex w-full items-start gap-3 rounded-xl border border-border bg-raised px-3.5 py-3 text-sm text-foreground shadow-float",
          title: "font-medium",
          description: "mt-0.5 text-xs text-muted",
          icon: "mt-px",
          success: "[&_[data-icon]]:text-profit",
          error: "border-loss/40 [&_[data-icon]]:text-loss",
          warning: "border-warning/40 [&_[data-icon]]:text-warning",
          info: "[&_[data-icon]]:text-accent",
        },
      }}
    />
  );
}
