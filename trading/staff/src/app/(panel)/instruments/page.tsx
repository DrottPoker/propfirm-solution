import type { Metadata } from "next";

import { InstrumentsPage } from "@/components/InstrumentsPage";

export const metadata: Metadata = { title: "Instruments" };

export default function Page() {
  return <InstrumentsPage />;
}
