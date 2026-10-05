"use client";

import Script from "next/script";
import { useEffect, useRef } from "react";

type Turnstile = {
  render: (element: HTMLElement, options: { sitekey: string; callback: (answer: string) => void; "expired-callback": () => void; "error-callback": () => void }) => string;
  reset: (widgetId: string) => void;
  remove: (widgetId: string) => void;
};

declare global {
  interface Window {
    turnstile?: Turnstile;
  }
}

/**
 * Cloudflare Turnstile's check that a person, not a script, signs a firm up (ADR 0045). Gives each answer to
 * onAnswer, and null when it expires. An answer works once, so each new attempt asks for a new one.
 */
export function RobotCheck({ siteKey, attempt, onAnswer }: { siteKey: string; attempt: number; onAnswer: (answer: string | null) => void }) {
  const element = useRef<HTMLDivElement>(null);
  const widget = useRef<string | null>(null);
  const answer = useRef(onAnswer);

  useEffect(() => {
    answer.current = onAnswer;
  }, [onAnswer]);

  // Each attempt uses up the answer, so the check starts over.
  useEffect(() => {
    if (attempt > 0 && widget.current && window.turnstile) {
      answer.current(null);
      window.turnstile.reset(widget.current);
    }
  }, [attempt]);

  useEffect(
    () => () => {
      if (widget.current && window.turnstile) {
        window.turnstile.remove(widget.current);
        widget.current = null;
      }
    },
    [],
  );

  return (
    <>
      <div ref={element} />
      <Script
        id="turnstile"
        src="https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit"
        onReady={() => {
          if (element.current && window.turnstile && !widget.current) {
            widget.current = window.turnstile.render(element.current, {
              sitekey: siteKey,
              callback: (value) => answer.current(value),
              "expired-callback": () => answer.current(null),
              "error-callback": () => answer.current(null),
            });
          }
        }}
      />
    </>
  );
}
