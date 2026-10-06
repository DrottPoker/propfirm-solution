import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";

import { monogramIcon } from "@/lib/icon";
import { getSite } from "@/lib/site";
import { defaultColors, opsColors, themeStyle } from "@/lib/theme";

import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export async function generateMetadata(): Promise<Metadata> {
  const site = await getSite();
  // The tab's icon is the firm's first letter in its brand color, or ours on the platform and in our admin view.
  const icon =
    site.kind === "firm"
      ? monogramIcon(site.branding.name, site.branding.colors.accent ?? defaultColors.accent)
      : site.kind === "platform"
        ? monogramIcon(site.platform.name, defaultColors.accent)
        : site.kind === "ops"
          ? monogramIcon(site.ops.name, opsColors.accent ?? defaultColors.accent)
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

// The site is known from the address. A firm's colors are set before anything renders; the platform uses the defaults,
// and our own admin view a brand color of its own.
export default async function RootLayout({ children }: LayoutProps<"/">) {
  const site = await getSite();
  const colors = site.kind === "firm" ? site.branding : site.kind === "ops" ? { colors: opsColors } : null;
  return (
    <html lang="en" className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`} style={themeStyle(colors) as React.CSSProperties}>
      <body className="flex min-h-full flex-col">{children}</body>
    </html>
  );
}
