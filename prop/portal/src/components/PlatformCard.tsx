"use client";

import { usePlatform } from "@/app/providers";

/** The platform's own pages have one centered card. */
export function PlatformCard({ title, children }: { title: string; children: React.ReactNode }) {
  const platform = usePlatform();
  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <section className="flex w-full max-w-md flex-col gap-5 rounded-lg border border-border bg-panel p-6">
        <div className="flex flex-col gap-1">
          <span className="text-sm text-muted">{platform.name}</span>
          <h1 className="text-xl font-semibold">{title}</h1>
        </div>
        {children}
      </section>
    </main>
  );
}
