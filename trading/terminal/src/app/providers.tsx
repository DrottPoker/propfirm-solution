"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Toaster } from "sonner";

import { ErrorIcon, InfoIcon, SuccessIcon } from "@/components/icons";
import { onSessionEnded } from "@/lib/api/client";
import { meKey } from "@/lib/queries";

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1 } } }));

  // A session that ends while the terminal is open logs the trader out, so the terminal sends them to log in again.
  useEffect(() => onSessionEnded(() => queryClient.setQueryData(meKey, null)), [queryClient]);

  return (
    <QueryClientProvider client={queryClient}>
      {children}
      <Notes />
    </QueryClientProvider>
  );
}

/**
 * The short notes about fills, closes and refusals. They come in over the top of the order ticket, below the account
 * bar, so they never cover the positions and their Close buttons.
 */
function Notes() {
  return (
    <Toaster
      position="top-right"
      offset={{ top: "calc(4rem + 1px)", right: "0.5rem" }}
      mobileOffset={{ top: "0.5rem", right: "0.5rem", left: "0.5rem" }}
      gap={8}
      style={{ "--width": "18rem" } as React.CSSProperties}
      icons={{ success: <SuccessIcon className="size-5" />, error: <ErrorIcon className="size-5" />, info: <InfoIcon className="size-5" /> }}
      toastOptions={{
        unstyled: true,
        classNames: {
          toast: "flex w-full items-start gap-3 rounded-xl border border-border bg-raised px-3.5 py-3 text-sm text-foreground shadow-float",
          title: "font-medium",
          description: "mt-0.5 text-xs text-muted",
          icon: "mt-px",
          success: "[&_[data-icon]]:text-profit",
          error: "border-loss/40 [&_[data-icon]]:text-loss",
          info: "[&_[data-icon]]:text-accent",
        },
      }}
    />
  );
}
