import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";

import { productName } from "@/lib/config";

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

export const metadata: Metadata = {
  title: productName,
  description: "Simulated trading terminal",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}>
      <body className="flex h-full flex-col overflow-hidden">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
