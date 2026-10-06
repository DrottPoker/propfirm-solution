import Link from "next/link";

import { buttonClass, secondaryButtonClass } from "./ui";

/**
 * A page that is not there, or one that failed: who it belongs to, what happened, and the way back. Used by the 404
 * page and the error pages, in the firm's colors on its portal.
 */
export function ErrorScreen({
  brand,
  title,
  text,
  home,
  homeLabel,
  onRetry,
}: {
  brand: React.ReactNode;
  title: string;
  text: string;
  home: string;
  homeLabel: string;
  onRetry?: () => void;
}) {
  return (
    <main className="flex flex-1 items-center justify-center p-6">
      <div className="flex w-full max-w-md flex-col items-center gap-5 rounded-lg border border-border bg-panel px-6 py-10 text-center">
        {brand}
        <div className="flex flex-col gap-2">
          <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
          <p className="text-sm text-muted">{text}</p>
        </div>
        <div className="flex flex-wrap justify-center gap-3">
          {onRetry && (
            <button type="button" onClick={onRetry} className={buttonClass}>
              Try again
            </button>
          )}
          <Link href={home} className={onRetry ? secondaryButtonClass : buttonClass}>
            {homeLabel}
          </Link>
        </div>
      </div>
    </main>
  );
}
