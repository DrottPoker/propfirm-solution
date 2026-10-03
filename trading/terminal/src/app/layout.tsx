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
  const branding = await getBranding();
  return {
    title: branding ? `${branding.displayName} terminal` : "Trading terminal",
    description: "Simulated trading terminal",
  };
}

// The firm is known from the address, so its colors are set before anything renders.
export default async function RootLayout({ children }: LayoutProps<"/">) {
  const branding = await getBranding();
  return (
    <html
      lang="en"
      className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}
      style={themeStyle(branding) as React.CSSProperties}
    >
      <body className="flex h-full flex-col overflow-hidden">
        <Providers branding={branding}>{children}</Providers>
      </body>
    </html>
  );
}
