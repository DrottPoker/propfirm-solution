import type { Metadata } from "next";

import { EnginePage } from "@/components/EnginePage";

export const metadata: Metadata = { title: "Engine" };

export default function Page() {
  return <EnginePage />;
}
