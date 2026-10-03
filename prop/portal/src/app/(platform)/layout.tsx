import { notFound } from "next/navigation";

import { getSite } from "@/lib/site";

import { PlatformProviders } from "../providers";

// Our own pages, where firms sign up. A firm's portal never shows them.
export default async function PlatformLayout({ children }: LayoutProps<"/">) {
  const site = await getSite();
  if (site.kind !== "platform") {
    notFound();
  }

  return <PlatformProviders platform={site.platform}>{children}</PlatformProviders>;
}
