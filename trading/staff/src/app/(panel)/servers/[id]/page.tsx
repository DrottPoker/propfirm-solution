import type { Metadata } from "next";

import { ServerPage } from "@/components/ServerPage";

export async function generateMetadata({ params }: PageProps<"/servers/[id]">): Promise<Metadata> {
  const { id } = await params;
  return { title: decodeURIComponent(id) };
}

// A search for an account opens its server with ?account= and the account's events.
export default async function Page({ params, searchParams }: PageProps<"/servers/[id]">) {
  const { id } = await params;
  const { account } = await searchParams;
  return <ServerPage id={decodeURIComponent(id)} account={typeof account === "string" && account.length > 0 ? account : null} />;
}
