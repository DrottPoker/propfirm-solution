"use client";

import { useBranding } from "@/app/providers";

import { FirmLogo } from "./FirmLogo";

/** The firm's logo, or its name when it has none. */
export function FirmName({ size = "md" }: { size?: "md" | "lg" }) {
  const branding = useBranding();
  if (branding.logoUrl) {
    return <FirmLogo src={branding.logoUrl} alt={branding.name} className={size === "lg" ? "h-10" : "h-7"} />;
  }

  return <span className={`font-semibold ${size === "lg" ? "text-xl" : ""}`}>{branding.name}</span>;
}
