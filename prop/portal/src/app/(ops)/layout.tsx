import { notFound } from "next/navigation";

import { getSite } from "@/lib/site";

import { OpsProviders } from "../providers";

// Our own admin view, where our staff review firms. Only on its own address.
export default async function OpsLayout({ children }: LayoutProps<"/">) {
  const site = await getSite();
  if (site.kind !== "ops") {
    notFound();
  }

  return <OpsProviders ops={site.ops}>{children}</OpsProviders>;
}
