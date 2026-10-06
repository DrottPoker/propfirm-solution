"use client";

import confetti from "canvas-confetti";
import { useEffect, useRef } from "react";

/** A check drawn in a ring, for a moment such as a payment that went through. */
export function SuccessMark({ className = "size-16" }: { className?: string }) {
  return (
    <svg viewBox="0 0 52 52" aria-hidden="true" className={`animate-pop ${className}`}>
      <circle cx="26" cy="26" r="24" fill="color-mix(in oklab, var(--profit) 14%, transparent)" stroke="var(--profit)" strokeWidth="2" pathLength={1} strokeDasharray="1" className="animate-draw" />
      <path
        d="M15 27.5 22.5 35 37.5 19"
        fill="none"
        stroke="var(--profit)"
        strokeWidth="3.5"
        strokeLinecap="round"
        strokeLinejoin="round"
        pathLength={1}
        strokeDasharray="1"
        className="animate-draw [animation-delay:350ms]"
      />
    </svg>
  );
}

/**
 * A short burst of confetti in the firm's colors, once, when <code>when</code> first becomes true: for a milestone
 * such as a passed challenge. Nothing moves for those who asked their system for less motion.
 */
export function useConfetti(when: boolean) {
  const fired = useRef(false);
  useEffect(() => {
    if (!when || fired.current) {
      return;
    }

    fired.current = true;
    const style = getComputedStyle(document.documentElement);
    const colors = ["--accent", "--profit", "--foreground"].map((name) => style.getPropertyValue(name).trim()).filter((color) => /^#[0-9a-f]{6}$/i.test(color));
    const burst = (angle: number, x: number) =>
      confetti({ particleCount: 70, angle, spread: 62, startVelocity: 48, origin: { x, y: 0.72 }, colors, ticks: 220, scalar: 0.9, disableForReducedMotion: true });
    burst(60, 0);
    burst(120, 1);
  }, [when]);
}
