"use client";

import { useApproved } from "@/lib/queries";

import { InfoIcon } from "./icons";

/** Why the firm cannot email, or let log in, someone outside its team before we have approved it, as the service says it (ADR 0043). */
export const teamOnlyText =
  "Until we have approved your firm, it reaches only its administrators: only they get its emails and can log in as its traders. Try the trader's side with your own email address.";

/** Says that the firm reaches only its administrators, until we have approved it. Nothing once we have. */
export function TeamOnlyNote() {
  const approved = useApproved();
  if (approved !== false) {
    return null;
  }

  return (
    <p className="flex items-start gap-2 text-xs text-warning">
      <InfoIcon className="mt-px size-3.5 shrink-0" />
      {teamOnlyText}
    </p>
  );
}
