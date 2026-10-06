import Link from "next/link";

import { KronantWordmark } from "./KronantMark";

/** The platform's own pages have one centered card, under our wordmark, which leads back to the front page. */
export function PlatformCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <main className="flex flex-1 flex-col items-center justify-center gap-8 p-6">
      <Link href="/" aria-label="Kronant Prop, home">
        <KronantWordmark product="Prop" />
      </Link>
      <section className="stagger flex w-full max-w-md flex-col gap-5 rounded-2xl border border-border bg-panel p-7 shadow-raised">
        <h1 className="font-serif text-3xl leading-tight tracking-tight">{title}</h1>
        {children}
      </section>
    </main>
  );
}
