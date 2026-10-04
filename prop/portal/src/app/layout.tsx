import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";

import { getSite } from "@/lib/site";
import { opsColors, themeStyle } from "@/lib/theme";

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
