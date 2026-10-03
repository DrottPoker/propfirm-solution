"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createContext, useContext, useState } from "react";

import type { Branding } from "@/lib/api/types";

const BrandingContext = createContext<Branding | null>(null);

/** The firm whose portal this is. */
export function useBranding(): Branding {
  const branding = useContext(BrandingContext);
  if (!branding) {
    throw new Error("useBranding must be used inside Providers.");
  }

  return branding;
}

export function Providers({ branding, children }: { branding: Branding; children: React.ReactNode }) {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1 } } }));
  return (
    <BrandingContext.Provider value={branding}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </BrandingContext.Provider>
  );
}
