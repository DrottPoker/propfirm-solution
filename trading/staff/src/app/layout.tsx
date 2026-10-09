import type { Metadata } from "next";
import { Familjen_Grotesk, Instrument_Serif, JetBrains_Mono } from "next/font/google";
import { connection } from "next/server";

import { productName } from "@/lib/config";

import "./globals.css";
import { Providers } from "./providers";

// The same typefaces as the terminal and the portal (ADR 0047): text and figures, code and keys, and the page titles.
const text = Familjen_Grotesk({ variable: "--font-text", subsets: ["latin"] });
const code = JetBrains_Mono({ variable: "--font-code", subsets: ["latin"] });
const display = Instrument_Serif({ variable: "--font-display", subsets: ["latin"], weight: "400" });

export const metadata: Metadata = {
  title: { default: `${productName} staff`, template: `%s, ${productName} staff` },
  description: "Our staff's panel over Kronant Trader",
  robots: { index: false, follow: false },
};

export default async function RootLayout({ children }: LayoutProps<"/">) {
  // Every page is drawn per request, so Next.js can put the nonce from the proxy on its scripts (ADR 0060).
  await connection();
  return (
    <html lang="en" className={`${text.variable} ${code.variable} ${display.variable} antialiased`}>
      <body className="flex min-h-dvh flex-col">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
