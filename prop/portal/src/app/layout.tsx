import type { Metadata } from "next";
import { Familjen_Grotesk, Instrument_Serif, JetBrains_Mono } from "next/font/google";

import { kronantIcon, monogramIcon } from "@/lib/icon";
import { getSite } from "@/lib/site";
import { defaultColors, opsColors, platformColors, themeStyle } from "@/lib/theme";

import "./globals.css";

// Text and figures: a grotesk from Stockholm whose figures are as wide as each other, so amounts line up (ADR 0047).
const text = Familjen_Grotesk({ variable: "--font-text", subsets: ["latin"] });

// Page titles and the big moments, such as a passed challenge.
const display = Instrument_Serif({ variable: "--font-display", subsets: ["latin"], weight: "400", style: ["normal", "italic"] });

// Codes, keys and addresses.
const code = JetBrains_Mono({ variable: "--font-code", subsets: ["latin"] });

export async function generateMetadata(): Promise<Metadata> {
  const site = await getSite();
  // The tab's icon is the firm's first letter in its brand color, or our mark on the platform and in our admin view.
  const icon =
    site.kind === "firm"
      ? monogramIcon(site.branding.name, site.branding.colors.accent ?? defaultColors.accent)
      : site.kind === "platform" || site.kind === "ops"
        ? kronantIcon
        : null;
  return {
    title:
      site.kind === "firm"
        ? site.branding.name
        : site.kind === "platform"
          ? site.platform.name
          : site.kind === "ops"
            ? `Admin - ${site.ops.name}`
            : "Portal",
    description: "Challenge accounts and the trading terminal",
    icons: icon ? { icon: [{ url: icon, type: "image/svg+xml" }] } : undefined,
  };
}

// The site is known from the address. A firm's colors are set before anything renders; the platform has Kronant's own,
// and our own admin view Kronant's with a brand color of its own.
export default async function RootLayout({ children }: LayoutProps<"/">) {
  const site = await getSite();
  const colors =
    site.kind === "firm" ? site.branding : site.kind === "ops" ? { colors: opsColors } : site.kind === "platform" ? { colors: platformColors } : null;
  return (
    <html lang="en" className={`${text.variable} ${display.variable} ${code.variable} h-full antialiased`} style={themeStyle(colors) as React.CSSProperties}>
      <body className="flex min-h-full flex-col">{children}</body>
    </html>
  );
}
