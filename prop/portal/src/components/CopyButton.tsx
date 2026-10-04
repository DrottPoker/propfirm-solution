"use client";

import { useState } from "react";

import { CopyIcon } from "./icons";

/** Copies the text, and says so for a moment. */
export function CopyButton({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    await navigator.clipboard.writeText(value);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1_500);
  };

  return (
    <button type="button" onClick={copy} aria-label={`Copy ${label.toLowerCase()}`} title={copied ? "Copied" : "Copy"} className="inline-flex shrink-0 items-center text-muted hover:text-foreground">
      {copied ? <span className="text-xs text-profit">Copied</span> : <CopyIcon className="size-3.5" />}
    </button>
  );
}
