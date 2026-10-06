"use client";

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MotionConfig } from "motion/react";
import { createContext, useContext, useState } from "react";
import { Toaster } from "sonner";

import type { Branding, OpsSite, Platform } from "@/lib/api/types";

const BrandingContext = createContext<Branding | null>(null);

const PlatformContext = createContext<Platform | null>(null);

const OpsContext = createContext<OpsSite | null>(null);

/** The firm whose portal this is. */
export function useBranding(): Branding {
  const branding = useContext(BrandingContext);
  if (!branding) {
    throw new Error("useBranding must be used inside Providers.");
  }

  return branding;
}

/** Our own platform, where firms sign up. */
export function usePlatform(): Platform {
  const platform = useContext(PlatformContext);
  if (!platform) {
    throw new Error("usePlatform must be used inside PlatformProviders.");
  }

  return platform;
}

/** Our own admin view, where our staff review firms. */
export function useOps(): OpsSite {
  const ops = useContext(OpsContext);
  if (!ops) {
    throw new Error("useOps must be used inside OpsProviders.");
  }

  return ops;
}

// What every site shares: data from the API, motion that stands still for those who asked their system for less, and
// the short notes, such as "Saved", that come and go in a corner.
function QueryProvider({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { retry: 1 } } }));
  return (
    <QueryClientProvider client={queryClient}>
      <MotionConfig reducedMotion="user">
        {children}
        <Toaster
          position="bottom-right"
          gap={10}
          toastOptions={{
            unstyled: true,
            classNames: {
              toast: "flex w-[22rem] max-w-[calc(100vw-2rem)] items-start gap-3 rounded-xl border border-border bg-panel px-4 py-3 text-sm text-foreground shadow-float",
              title: "font-medium",
              description: "text-muted",
              icon: "mt-0.5",
              success: "[&_[data-icon]]:text-profit",
              error: "[&_[data-icon]]:text-loss",
            },
          }}
        />
      </MotionConfig>
    </QueryClientProvider>
  );
}

/** For a firm's portal. */
export function Providers({ branding, children }: { branding: Branding; children: React.ReactNode }) {
  return (
    <BrandingContext.Provider value={branding}>
      <QueryProvider>{children}</QueryProvider>
    </BrandingContext.Provider>
  );
}

/** For the platform's own pages. */
export function PlatformProviders({ platform, children }: { platform: Platform; children: React.ReactNode }) {
  return (
    <PlatformContext.Provider value={platform}>
      <QueryProvider>{children}</QueryProvider>
    </PlatformContext.Provider>
  );
}

/** For our own admin view. */
export function OpsProviders({ ops, children }: { ops: OpsSite; children: React.ReactNode }) {
  return (
    <OpsContext.Provider value={ops}>
      <QueryProvider>{children}</QueryProvider>
    </OpsContext.Provider>
  );
}
