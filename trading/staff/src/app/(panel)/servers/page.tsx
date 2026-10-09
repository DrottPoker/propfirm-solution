import type { Metadata } from "next";

import { ServersPage } from "@/components/ServersPage";

export const metadata: Metadata = { title: "Servers" };

export default function Page() {
  return <ServersPage />;
}
