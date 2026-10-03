// Small building blocks shared by the portal's pages.

export const fieldClass = "rounded border border-border bg-background px-3 py-2 outline-none focus:border-accent";

export const buttonClass = "rounded bg-accent px-4 py-2 font-medium text-white disabled:opacity-50";

export const secondaryButtonClass = "rounded border border-border px-4 py-2 hover:border-muted disabled:opacity-50";

export function Message({ text }: { text: string }) {
  return <main className="flex flex-1 items-center justify-center p-8 text-muted">{text}</main>;
}

export function ErrorText({ error }: { error: Error | null }) {
  if (!error) {
    return null;
  }

  return (
    <p role="alert" className="text-sm text-loss">
      {error.message}
    </p>
  );
}

export function Panel({ title, actions, children }: { title?: string; actions?: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-4 rounded-lg border border-border bg-panel p-5">
      {(title || actions) && (
        <div className="flex flex-wrap items-center justify-between gap-3">
          {title && <h2 className="font-semibold">{title}</h2>}
          {actions}
        </div>
      )}
      {children}
    </section>
  );
}
