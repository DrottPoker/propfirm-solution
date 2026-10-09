"use client";

import { useEffect, useRef, useState } from "react";

import { maxNoteLength } from "@/lib/drawings";

/**
 * Writes a note on the chart where it was placed: Enter or a press elsewhere saves it, Esc lets it be. An emptied note
 * is removed.
 */
export function NoteInput({
  at,
  text,
  onSave,
  onCancel,
}: {
  at: { x: number; y: number } | null;
  text: string;
  onSave: (text: string) => void;
  onCancel: () => void;
}) {
  const [value, setValue] = useState(text);
  const ref = useRef<HTMLInputElement>(null);
  useEffect(() => ref.current?.focus(), []);

  if (!at) {
    return null;
  }

  return (
    <input
      ref={ref}
      value={value}
      maxLength={maxNoteLength}
      aria-label="Note"
      placeholder="Write a note"
      onChange={(e) => setValue(e.target.value)}
      onBlur={() => onSave(value)}
      onKeyDown={(e) => {
        e.stopPropagation();
        if (e.key === "Enter") {
          onSave(value);
        } else if (e.key === "Escape") {
          onCancel();
        }
      }}
      style={{ left: at.x - 6, top: at.y - 26 }}
      className="absolute z-20 w-56 rounded-md border border-accent bg-panel px-1.5 py-0.5 text-xs text-foreground shadow-float outline-none placeholder:text-muted"
    />
  );
}
