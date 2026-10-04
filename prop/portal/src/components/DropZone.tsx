"use client";

import { useState } from "react";

import { UploadIcon } from "./icons";

/** A place to drop a file on, or to click and choose one, for the logo and the application's documents. */
export function DropZone({
  label,
  title,
  hint,
  accept,
  busy,
  busyText = "Uploading...",
  onFile,
}: {
  /** The file input's name for screen readers and tests. */
  label: string;
  /** What to drop, for example "Drop your logo here, or ". The words "choose a file" follow. */
  title: string;
  hint: string;
  accept: string;
  busy: boolean;
  busyText?: string;
  onFile: (file: File) => void;
}) {
  const [dragging, setDragging] = useState(false);
  const send = (file: File | undefined) => {
    if (file && !busy) {
      onFile(file);
    }
  };

  return (
    <label
      onDragOver={(e) => {
        e.preventDefault();
        setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(e) => {
        e.preventDefault();
        setDragging(false);
        send(e.dataTransfer.files[0]);
      }}
      className={`flex cursor-pointer flex-col items-center gap-1.5 rounded-lg border border-dashed px-4 py-5 text-center text-sm ${dragging ? "border-accent bg-accent/5" : "border-border bg-background"}`}
    >
      <UploadIcon className="size-5 text-muted" />
      <span className="font-medium">
        {busy ? busyText : title}
        {!busy && <span className="text-accent">choose a file</span>}
      </span>
      <span className="text-xs text-muted">{hint}</span>
      <input
        type="file"
        aria-label={label}
        accept={accept}
        disabled={busy}
        onChange={(e) => {
          send(e.target.files?.[0]);
          e.target.value = "";
        }}
        className="sr-only"
      />
    </label>
  );
}
