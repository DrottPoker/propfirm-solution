import { redirect } from "next/navigation";

import { SandboxBanner } from "@/components/SandboxBanner";
import { Message } from "@/components/ui";
import { getSite } from "@/lib/site";

import { Providers } from "../providers";

// A firm's portal. On the platform's own address and our admin view's there is no firm, so visitors go to the
// sign-up or our admin view instead.
export default async function FirmLayout({ children }: LayoutProps<"/">) {
  const site = await getSite();
  if (site.kind === "platform") {
    redirect("/signup");
  }

  if (site.kind === "ops") {
    redirect("/ops");
  }

  if (site.kind !== "firm") {
    return <Message text={site.kind === "unknown-host" ? "No firm's portal is at this address." : "The portal cannot be reached right now. Try again shortly."} />;
  }

  return (
    <Providers branding={site.branding}>
      <SandboxBanner />
      {children}
    </Providers>
  );
}
