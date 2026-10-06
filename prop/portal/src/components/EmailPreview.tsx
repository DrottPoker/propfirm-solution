"use client";

import { useState } from "react";

import { recipientText, type NotificationKind } from "@/lib/notifications";
import { useEmailPreview } from "@/lib/queries";

import { Sheet } from "./Dialog";
import { ErrorText, SegmentedControl, Skeleton } from "./ui";

type View = "email" | "text";

const views: { value: View; label: string }[] = [
  { value: "email", label: "Email" },
  { value: "text", label: "Plain text" },
];

/**
 * A notification email as we would send it for the firm now, with a sample trader and sample figures: who it goes to, its
 * subject, and the email itself, or the plain text that mail programs without HTML show. Full width on a phone.
 */
export function EmailPreview({ email, on, onClose }: { email: NotificationKind | null; on: boolean; onClose: () => void }) {
  return (
    <Sheet
      open={email !== null}
      onClose={onClose}
      title={email?.label ?? "Email"}
      description="As we would send it now, with a sample trader, Alex Example, and sample figures."
    >
      {email && <Preview key={email.kind} kind={email.kind} on={on} />}
    </Sheet>
  );
}

function Preview({ kind, on }: { kind: string; on: boolean }) {
  const preview = useEmailPreview(kind);
  const [view, setView] = useState<View>("email");

  if (preview.error) {
    return <ErrorText error={preview.error} />;
  }

  if (!preview.data) {
    return (
      <div role="status" aria-label="Loading the email" className="flex flex-col gap-4">
        <Skeleton className="h-16 rounded-lg" />
        <Skeleton className="h-7 w-44 rounded-lg" />
        <Skeleton className="h-96 rounded-lg" />
      </div>
    );
  }

  const { subject, audience, html, text } = preview.data;
  return (
    <>
      <div className="flex flex-col gap-1 rounded-lg border border-border bg-background/40 p-3.5 text-sm">
        <p className="text-muted">{recipientText(audience)}</p>
        <p className="font-medium">
          <span className="sr-only">Subject: </span>
          {subject}
        </p>
      </div>
      {!on && <p className="text-sm text-muted">You have turned this email off, so we do not send it now.</p>}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <SegmentedControl label="Show the email as" options={views} value={view} onChange={setView} />
        <span className="text-xs text-muted">Links do not open in the preview.</span>
      </div>
      {view === "email" ? (
        // No scripts, forms or links run in the frame. Links target a new tab, which the sandbox blocks, so a click
        // does not load the portal inside the preview.
        <iframe
          title={`Preview of the email "${subject}"`}
          sandbox=""
          srcDoc={html.replace("<head>", '<head><base target="_blank">')}
          className="h-[min(36rem,70dvh)] w-full shrink-0 rounded-lg border border-border bg-white"
        />
      ) : (
        <pre className="whitespace-pre-wrap break-words rounded-lg border border-border bg-background p-3.5 font-sans text-sm">{text}</pre>
      )}
    </>
  );
}
