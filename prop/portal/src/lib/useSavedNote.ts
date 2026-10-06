"use client";

import { useEffect } from "react";
import { toast } from "sonner";

/**
 * Says that something was saved, in a short note in the corner that goes away by itself, each time the mutation
 * succeeds. One way of confirming a save on every page, instead of a line of green text that is easy to miss.
 */
export function useSavedNote(mutation: { isSuccess: boolean; submittedAt: number }, text = "Saved.") {
  const { isSuccess, submittedAt } = mutation;
  useEffect(() => {
    if (isSuccess) {
      toast.success(text);
    }
  }, [isSuccess, submittedAt, text]);
}
