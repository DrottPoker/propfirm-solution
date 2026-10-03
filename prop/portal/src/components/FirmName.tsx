"use client";

import Image from "next/image";

import { useBranding } from "@/app/providers";

/** The firm's logo, or its name when it has none. Logos are on the firms' own addresses, so they are not optimized. */
export function FirmName({ size = "md" }: { size?: "md" | "lg" }) {
  const branding = useBranding();
  if (branding.logoUrl) {
    return (
      <Image
        src={branding.logoUrl}
        alt={branding.name}
        width={size === "lg" ? 160 : 112}
        height={size === "lg" ? 40 : 28}
        unoptimized
        className={`${size === "lg" ? "h-10" : "h-7"} w-auto object-contain`}
      />
    );
  }

  return <span className={`font-semibold ${size === "lg" ? "text-xl" : ""}`}>{branding.name}</span>;
}
