"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Toaster } from "sonner";

import { onSessionEnded } from "@/lib/api/client";
import { meKey } from "@/lib/queries";

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1 } } }));

  // A session that ends while a page is open sends the staff member to log in again.
  useEffect(() => onSessionEnded(() => queryClient.setQueryData(meKey, null)), [queryClient]);

  return (
    <QueryClientProvider client={queryClient}>
      {children}
      <Toaster
        position="bottom-right"
        toastOptions={{
          unstyled: true,
          classNames: {
            toast: "flex w-full items-start gap-3 rounded-xl border border-border bg-raised px-4 py-3 text-sm text-foreground shadow-float",
            title: "font-medium",
            description: "mt-0.5 text-xs text-muted",
            error: "border-loss/40",
          },
        }}
      />
    </QueryClientProvider>
  );
}
