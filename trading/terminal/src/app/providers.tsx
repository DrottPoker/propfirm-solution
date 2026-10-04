"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useEffect, useState } from "react";

import { onSessionEnded } from "@/lib/api/client";
import { meKey } from "@/lib/queries";

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1 } } }));

  // A session that ends while the terminal is open logs the trader out, so the terminal sends them to log in again.
  useEffect(() => onSessionEnded(() => queryClient.setQueryData(meKey, null)), [queryClient]);

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
