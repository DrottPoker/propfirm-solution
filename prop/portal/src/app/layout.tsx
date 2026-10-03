import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";

import { getBranding } from "@/lib/branding";
import { themeStyle } from "@/lib/theme";

import "./globals.css";
import { Providers } from "./providers";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export async function generateMetadata(): Promise<Metadata> {
  const portal = await getBranding();
  return {
    title: portal.kind === "found" ? portal.branding.name : "Portal",
    description: "Challenge accounts and the trading terminal",
  };
}

// The firm is known from the address, so its name and colors are set before anything renders.
export default async function RootLayout({ children }: LayoutProps<"/">) {
  const portal = await getBranding();
  const branding = portal.kind === "found" ? portal.branding : null;
  return (
    <html
      lang="en"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
      style={themeStyle(branding) as React.CSSProperties}
    >
      <body className="flex min-h-full flex-col">
        {branding ? (
          <Providers branding={branding}>{children}</Providers>
        ) : (
          <main className="flex flex-1 items-center justify-center p-8 text-muted">
            {portal.kind === "unknown-host" ? "No firm's portal is at this address." : "The portal cannot be reached right now. Try again shortly."}
          </main>
        )}
      </body>
    </html>
  );
}
