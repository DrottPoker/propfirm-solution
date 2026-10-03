"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createContext, useContext, useState } from "react";

import type { Branding } from "@/lib/api/types";

const BrandingContext = createContext<Branding | null>(null);

/** The firm's name and logo, or null for the default look. */
export function useBranding(): Branding | null {
  return useContext(BrandingContext);
}

export function Providers({ branding, children }: { branding: Branding | null; children: React.ReactNode }) {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1 } } }));
  return (
    <BrandingContext.Provider value={branding}>
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    </BrandingContext.Provider>
  );
}
