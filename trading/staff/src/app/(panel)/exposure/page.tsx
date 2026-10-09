import type { Metadata } from "next";

import { ExposurePage } from "@/components/ExposurePage";

export const metadata: Metadata = { title: "Exposure" };

// One server's exposure with ?server=.
export default async function Page({ searchParams }: PageProps<"/exposure">) {
  const { server } = await searchParams;
  return <ExposurePage server={typeof server === "string" && server.length > 0 ? server : null} />;
}
