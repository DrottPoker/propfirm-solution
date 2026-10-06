import type { Metadata } from "next";
import { Familjen_Grotesk, Instrument_Serif, JetBrains_Mono } from "next/font/google";

import { productName } from "@/lib/config";

import "./globals.css";
import { Providers } from "./providers";

// Text: a grotesk from Stockholm whose figures are as wide as each other, as in the firm's portal (ADR 0047).
const text = Familjen_Grotesk({ variable: "--font-text", subsets: ["latin"] });

// Prices, volumes and number columns, where every digit must stand in the same place as the price moves.
const code = JetBrains_Mono({ variable: "--font-code", subsets: ["latin"] });

// Only for "Kronant" in the wordmark.
const display = Instrument_Serif({ variable: "--font-display", subsets: ["latin"], weight: "400" });

export const metadata: Metadata = {
  title: productName,
  description: "Kronant Trader, a simulated trading terminal",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${text.variable} ${code.variable} ${display.variable} h-full antialiased`}>
      <body className="flex h-full flex-col overflow-hidden">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
